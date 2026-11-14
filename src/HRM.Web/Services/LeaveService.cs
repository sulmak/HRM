using HRM.Web.Data;
using HRM.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HRM.Web.Services;

public record LeaveBalanceDto(
    LeaveType LeaveType,
    decimal Entitled,
    decimal Used,
    decimal Balance,
    decimal AccruingThisYear,
    bool Eligible,
    string? Note);

public record LeaveApplyDto(int EmployeeId, LeaveType LeaveType, DateOnly FromDate, DateOnly ToDate, string? Reason);

/// <summary>Leave rules: casual, sick, earned and other leave, with weekly-off / holiday aware day counting.</summary>
public class LeaveService
{
    private readonly HrmDbContext _db;
    public LeaveService(HrmDbContext db) => _db = db;

    /// <summary>
    /// Counts the leave days in a range. Weekly off days and public holidays inside the range are not
    /// counted, except for maternity/paternity leave which runs on calendar days.
    /// </summary>
    public async Task<decimal> CountLeaveDaysAsync(Employee emp, LeaveType type, DateOnly from, DateOnly to)
    {
        if (to < from) return 0;
        if (type is LeaveType.Maternity or LeaveType.Paternity)
            return to.DayNumber - from.DayNumber + 1;

        var off = DateRules.ParseOffDays(emp.WeeklyOffDays);
        var holidays = (await _db.Holidays
                .Where(h => h.Date >= from && h.Date <= to && !h.IsOptional)
                .Select(h => h.Date).ToListAsync())
            .ToHashSet();

        decimal days = 0;
        foreach (var d in DateRules.Range(from, to))
        {
            if (off.Contains(d.DayOfWeek) || holidays.Contains(d)) continue;
            days++;
        }
        return days;
    }

    public async Task<List<LeaveBalanceDto>> GetBalancesAsync(int employeeId, int year)
    {
        var emp = await _db.Employees.FindAsync(employeeId)
                  ?? throw new KeyNotFoundException("Employee not found");
        var policies = await _db.LeavePolicies.OrderBy(p => p.LeaveType).ToListAsync();
        var asOf = new DateOnly(year, 12, 31);
        var result = new List<LeaveBalanceDto>();

        foreach (var p in policies)
        {
            if (p.LeaveType == LeaveType.Maternity && emp.Gender != Gender.Female) continue;
            if (p.LeaveType == LeaveType.Paternity && emp.Gender != Gender.Male) continue;

            var eligibleFrom = emp.JoiningDate.AddMonths(p.EligibleAfterMonths);
            var eligible = asOf >= eligibleFrom;

            if (p.LeaveType == LeaveType.Earned)
            {
                result.Add(await EarnedBalanceAsync(emp, p, year, eligible, eligibleFrom));
                continue;
            }

            var used = await UsedAsync(emp.Id, p.LeaveType, year, cumulative: false);

            if (p.LeaveType == LeaveType.Unpaid)
            {
                result.Add(new LeaveBalanceDto(p.LeaveType, 0, used, 0, 0, true, "No limit; deducted from salary"));
                continue;
            }

            var entitled = p.DaysPerYear;
            string? note = null;
            if (p.ProRateFirstYear && emp.JoiningDate.Year == year)
            {
                var monthsLeft = 12 - emp.JoiningDate.Month + 1;
                entitled = Math.Round(p.DaysPerYear * monthsLeft / 12m, MidpointRounding.AwayFromZero);
                note = $"Prorated for {monthsLeft} month(s) in joining year";
            }
            if (!eligible) note = $"Eligible from {eligibleFrom:yyyy-MM-dd}";

            result.Add(new LeaveBalanceDto(p.LeaveType, entitled, used, Math.Max(0, entitled - used), 0, eligible, note));
        }
        return result;
    }

    /// <summary>
    /// Earned leave: 1 day per <c>WorkDaysPerLeave</c> days worked in each completed year, plus any opening
    /// balance, capped at <c>MaxAccumulate</c>, minus everything already taken.
    /// </summary>
    private async Task<LeaveBalanceDto> EarnedBalanceAsync(Employee emp, LeavePolicy p, int year, bool eligible, DateOnly eligibleFrom)
    {
        var perLeave = p.WorkDaysPerLeave <= 0 ? 18 : p.WorkDaysPerLeave;
        decimal accrued = emp.OpeningEarnLeave;

        for (var y = emp.JoiningDate.Year; y < year; y++)
            accrued += Math.Floor(await WorkedDaysAsync(emp.Id, y) / perLeave);

        if (p.MaxAccumulate > 0) accrued = Math.Min(accrued, p.MaxAccumulate);

        var used = await UsedAsync(emp.Id, LeaveType.Earned, year, cumulative: true);
        var thisYear = Math.Floor(await WorkedDaysAsync(emp.Id, year) / perLeave);

        var note = eligible
            ? "Accrued from previous years; this year's accrual becomes available next year"
            : $"Eligible from {eligibleFrom:yyyy-MM-dd}";

        return new LeaveBalanceDto(LeaveType.Earned, accrued, used, Math.Max(0, accrued - used), thisYear, eligible, note);
    }

    /// <summary>Days worked in a year: present, late and on-leave count fully, half days count half.</summary>
    public async Task<decimal> WorkedDaysAsync(int employeeId, int year)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var statuses = await _db.AttendanceDailies
            .Where(a => a.EmployeeId == employeeId && a.Date >= from && a.Date <= to)
            .Select(a => a.Status).ToListAsync();

        decimal worked = 0;
        foreach (var s in statuses)
        {
            worked += s switch
            {
                AttendanceStatus.Present or AttendanceStatus.Late or AttendanceStatus.OnLeave => 1m,
                AttendanceStatus.HalfDay => 0.5m,
                _ => 0m
            };
        }
        return worked;
    }

    private async Task<decimal> UsedAsync(int employeeId, LeaveType type, int year, bool cumulative)
    {
        var q = _db.LeaveApplications.Where(l =>
            l.EmployeeId == employeeId && l.LeaveType == type && l.Status == LeaveStatus.Approved);
        q = cumulative ? q.Where(l => l.FromDate.Year <= year) : q.Where(l => l.FromDate.Year == year);
        return await q.SumAsync(l => (decimal?)l.Days) ?? 0;
    }

    public async Task<LeaveApplication> ApplyAsync(LeaveApplyDto dto)
    {
        var emp = await _db.Employees.FindAsync(dto.EmployeeId)
                  ?? throw new KeyNotFoundException("Employee not found");
        if (dto.ToDate < dto.FromDate) throw new InvalidOperationException("To date is before from date.");
        if (dto.FromDate.Year != dto.ToDate.Year) throw new InvalidOperationException("Split leave that crosses a year end into two applications.");

        var overlap = await _db.LeaveApplications.AnyAsync(l =>
            l.EmployeeId == dto.EmployeeId &&
            (l.Status == LeaveStatus.Pending || l.Status == LeaveStatus.Approved) &&
            l.FromDate <= dto.ToDate && l.ToDate >= dto.FromDate);
        if (overlap) throw new InvalidOperationException("This employee already has leave in the selected period.");

        var days = await CountLeaveDaysAsync(emp, dto.LeaveType, dto.FromDate, dto.ToDate);
        if (days <= 0) throw new InvalidOperationException("The selected period has no working days.");

        await EnsureBalanceAsync(emp, dto.LeaveType, dto.FromDate.Year, days);

        var app = new LeaveApplication
        {
            EmployeeId = emp.Id,
            LeaveType = dto.LeaveType,
            FromDate = dto.FromDate,
            ToDate = dto.ToDate,
            Days = days,
            Reason = dto.Reason
        };
        _db.LeaveApplications.Add(app);
        await _db.SaveChangesAsync();
        return app;
    }

    public async Task<LeaveApplication> DecideAsync(int id, bool approve, string? remarks)
    {
        var app = await _db.LeaveApplications.Include(l => l.Employee).FirstOrDefaultAsync(l => l.Id == id)
                  ?? throw new KeyNotFoundException("Leave application not found");
        if (app.Status != LeaveStatus.Pending) throw new InvalidOperationException("Only pending applications can be decided.");

        if (approve) await EnsureBalanceAsync(app.Employee!, app.LeaveType, app.FromDate.Year, app.Days);

        app.Status = approve ? LeaveStatus.Approved : LeaveStatus.Rejected;
        app.DecisionOn = DateTime.UtcNow;
        app.DecisionRemarks = remarks;
        await _db.SaveChangesAsync();
        return app;
    }

    private async Task EnsureBalanceAsync(Employee emp, LeaveType type, int year, decimal days)
    {
        if (type == LeaveType.Unpaid) return;
        var balance = (await GetBalancesAsync(emp.Id, year)).FirstOrDefault(b => b.LeaveType == type)
                      ?? throw new InvalidOperationException($"{type} leave does not apply to this employee.");
        if (!balance.Eligible) throw new InvalidOperationException($"Not yet eligible for {type} leave. {balance.Note}");
        if (balance.Balance < days)
            throw new InvalidOperationException($"Insufficient {type} leave: {balance.Balance} day(s) left, {days} requested.");
    }
}
