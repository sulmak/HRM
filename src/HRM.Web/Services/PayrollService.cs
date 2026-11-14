using HRM.Web.Data;
using HRM.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HRM.Web.Services;

public record PayrollProcessResult(int RunId, int Year, int Month, int PayslipCount, decimal TotalNetPay, List<string> Warnings);

/// <summary>One shift's share of an employee's month: days, late, absence and overtime worked in that shift.</summary>
public record ShiftLine(int? ShiftId, string ShiftName, decimal ShiftHours, decimal WorkedDays, int LateDays,
    decimal AbsentDays, decimal OvertimeHours, decimal OvertimePay);

public class PayrollService
{
    private readonly HrmDbContext _db;
    private readonly LeaveService _leave;
    private readonly PayrollOptions _opt;

    public PayrollService(HrmDbContext db, LeaveService leave, IOptions<PayrollOptions> opt)
    {
        _db = db;
        _leave = leave;
        _opt = opt.Value;
    }

    /// <summary>
    /// Monthly payroll, driven by the processed attendance and the shift worked on each day:
    /// gross salary from the salary structure (prorated for joining/leaving mid month), less absence, unpaid leave,
    /// late-arrival, provident fund and tax. Overtime is paid per shift: each day's overtime hours are valued at
    /// basic / working days per month / that shift's hours, times the overtime multiplier.
    /// Run attendance processing for the month first.
    /// </summary>
    public async Task<PayrollProcessResult> ProcessAsync(int year, int month)
    {
        if (month < 1 || month > 12) throw new InvalidOperationException("Month must be 1-12.");
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var dim = end.Day;

        var run = await _db.PayrollRuns.Include(r => r.Payslips).FirstOrDefaultAsync(r => r.Year == year && r.Month == month);
        if (run != null && run.Status != PayrollStatus.Draft)
            throw new InvalidOperationException($"Payroll for {year}-{month:00} is already {run.Status}.");
        if (run == null)
        {
            run = new PayrollRun { Year = year, Month = month };
            _db.PayrollRuns.Add(run);
        }
        else
        {
            _db.Payslips.RemoveRange(run.Payslips);
            run.Payslips.Clear();
            run.ProcessedOn = DateTime.UtcNow;
        }

        var warnings = new List<string>();
        var employees = await _db.Employees
            .Where(e => e.JoiningDate <= end && (e.LastWorkingDate == null || e.LastWorkingDate >= start))
            .OrderBy(e => e.EmployeeCode).ToListAsync();
        var ids = employees.Select(e => e.Id).ToList();

        var structures = (await _db.SalaryStructures
                .Where(s => ids.Contains(s.EmployeeId) && s.EffectiveFrom <= end).ToListAsync())
            .GroupBy(s => s.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.EffectiveFrom).First());
        var dailies = (await _db.AttendanceDailies
                .Where(a => ids.Contains(a.EmployeeId) && a.Date >= start && a.Date <= end).ToListAsync())
            .GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var unpaidLeaves = await _db.LeaveApplications
            .Where(l => ids.Contains(l.EmployeeId) && l.Status == LeaveStatus.Approved &&
                        l.LeaveType == LeaveType.Unpaid && l.FromDate <= end && l.ToDate >= start).ToListAsync();
        var shifts = await _db.Shifts.ToDictionaryAsync(s => s.Id);
        var fallbackShift = shifts.Values.OrderBy(s => s.Id).FirstOrDefault();

        decimal totalNet = 0;
        foreach (var emp in employees)
        {
            if (!structures.TryGetValue(emp.Id, out var s))
            {
                warnings.Add($"{emp.EmployeeCode} {emp.FullName}: no salary structure, skipped.");
                continue;
            }
            if (!dailies.TryGetValue(emp.Id, out var ds) || ds.Count == 0)
            {
                warnings.Add($"{emp.EmployeeCode} {emp.FullName}: attendance not processed for the month, skipped.");
                continue;
            }

            // Days outside the employment period are unpaid.
            var firstDay = DateRules.Max(start, emp.JoiningDate);
            var lastDay = emp.LastWorkingDate.HasValue ? DateRules.Min(end, emp.LastWorkingDate.Value) : end;
            var notEmployed = (firstDay.DayNumber - start.DayNumber) + (end.DayNumber - lastDay.DayNumber);
            var payableDays = dim - notEmployed;
            var factor = (decimal)payableDays / dim;

            var absent = ds.Count(d => d.Status == AttendanceStatus.Absent) + 0.5m * ds.Count(d => d.Status == AttendanceStatus.HalfDay);
            var lateDays = ds.Count(d => d.Status == AttendanceStatus.Late);

            // Shift-wise part: overtime hours and pay come from the shift each day was worked in.
            var lines = BuildShiftLines(ds, emp, s, shifts, fallbackShift, _opt, out var unassigned);
            if (unassigned > 0)
                warnings.Add($"{emp.EmployeeCode} {emp.FullName}: {unassigned} day(s) have no recorded shift, so the employee's current shift was used. Re-run attendance processing to record shifts.");
            var overtime = lines.Sum(l => l.OvertimeHours);
            var otPay = R(lines.Sum(l => l.OvertimePay));

            decimal unpaid = 0;
            foreach (var l in unpaidLeaves.Where(x => x.EmployeeId == emp.Id))
                unpaid += await _leave.CountLeaveDaysAsync(emp, LeaveType.Unpaid,
                    DateRules.Max(l.FromDate, start), DateRules.Min(l.ToDate, end));

            var basic = R(s.Basic * factor);
            var house = R(s.HouseRent * factor);
            var medical = R(s.Medical * factor);
            var conv = R(s.Conveyance * factor);
            var other = R(s.OtherAllowance * factor);
            var gross = basic + house + medical + conv + other;

            var perDay = s.Gross / dim;
            var lateDeductDays = _opt.LateDaysPerAbsent > 0 ? Math.Floor((decimal)lateDays / _opt.LateDaysPerAbsent) : 0;
            var absentDed = R(perDay * (absent + unpaid));
            var lateDed = R(perDay * lateDeductDays);
            var pf = R(basic * s.ProvidentFundPercent / 100m);
            var tax = R(s.MonthlyTax * factor);

            var earnings = gross + otPay;
            var deductions = absentDed + lateDed + pf + tax;

            run.Payslips.Add(new Payslip
            {
                EmployeeId = emp.Id,
                DaysInMonth = dim,
                PayableDays = payableDays,
                AbsentDays = absent,
                UnpaidLeaveDays = unpaid,
                LateDays = lateDays,
                OvertimeHours = overtime,
                Basic = basic, HouseRent = house, Medical = medical, Conveyance = conv, OtherAllowance = other,
                Gross = gross,
                OvertimePay = otPay,
                TotalEarnings = earnings,
                AbsentDeduction = absentDed,
                LateDeduction = lateDed,
                ProvidentFund = pf,
                Tax = tax,
                TotalDeductions = deductions,
                NetPay = Math.Max(0, earnings - deductions)
            });
            totalNet += Math.Max(0, earnings - deductions);
        }

        await _db.SaveChangesAsync();
        return new PayrollProcessResult(run.Id, year, month, run.Payslips.Count, totalNet, warnings);
    }

    /// <summary>
    /// Groups an employee's processed days by the shift they were worked in. Days with no recorded shift
    /// (processed before shifts were tracked) fall back to the employee's current shift and are counted in
    /// <paramref name="unassigned"/>.
    /// </summary>
    public static List<ShiftLine> BuildShiftLines(List<AttendanceDaily> days, Employee emp, SalaryStructure s,
        Dictionary<int, Shift> shifts, Shift? fallback, PayrollOptions opt, out int unassigned)
    {
        unassigned = 0;
        var workDays = opt.WorkingDaysPerMonth > 0 ? opt.WorkingDaysPerMonth : 26m;

        Shift? own = fallback;
        if (emp.ShiftId.HasValue && shifts.TryGetValue(emp.ShiftId.Value, out var empShift)) own = empShift;

        var rows = new List<(Shift? Shift, AttendanceDaily Day)>();
        foreach (var d in days)
        {
            var relevant = d.Status is AttendanceStatus.Present or AttendanceStatus.Late
                or AttendanceStatus.HalfDay or AttendanceStatus.Absent;
            if (!relevant) continue;

            Shift? sh = null;
            if (d.ShiftId.HasValue && shifts.TryGetValue(d.ShiftId.Value, out var found)) sh = found;
            if (sh == null)
            {
                sh = own;
                unassigned++;
            }
            rows.Add((sh, d));
        }

        var lines = new List<ShiftLine>();
        foreach (var g in rows.GroupBy(r => r.Shift?.Id ?? 0))
        {
            var sh = g.First().Shift;
            var hours = sh == null ? 0m : (decimal)(sh.EndTime - sh.StartTime).TotalHours;
            var hourly = hours > 0 ? s.Basic / workDays / hours : 0m;

            decimal worked = 0, absent = 0, overtime = 0;
            var late = 0;
            foreach (var (_, day) in g)
            {
                if (day.Status == AttendanceStatus.Present) worked += 1;
                else if (day.Status == AttendanceStatus.Late) { worked += 1; late++; }
                else if (day.Status == AttendanceStatus.HalfDay) { worked += 0.5m; absent += 0.5m; }
                else if (day.Status == AttendanceStatus.Absent) absent += 1;
                overtime += day.OvertimeHours;
            }

            lines.Add(new ShiftLine(sh?.Id, sh?.Name ?? "No shift", Math.Round(hours, 2), worked, late, absent, overtime,
                Math.Round(overtime * hourly * opt.OvertimeMultiplier, 2, MidpointRounding.AwayFromZero)));
        }
        return lines.OrderBy(l => l.ShiftName).ToList();
    }

    /// <summary>The shift-wise breakdown behind a stored payslip.</summary>
    public async Task<List<ShiftLine>> GetShiftBreakdownAsync(int payslipId)
    {
        var p = await _db.Payslips.Include(x => x.PayrollRun).Include(x => x.Employee)
                    .FirstOrDefaultAsync(x => x.Id == payslipId)
                ?? throw new KeyNotFoundException("Payslip not found");

        var start = new DateOnly(p.PayrollRun!.Year, p.PayrollRun.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var structure = await _db.SalaryStructures
            .Where(x => x.EmployeeId == p.EmployeeId && x.EffectiveFrom <= end)
            .OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync();
        if (structure == null) return new List<ShiftLine>();

        var days = await _db.AttendanceDailies
            .Where(a => a.EmployeeId == p.EmployeeId && a.Date >= start && a.Date <= end).ToListAsync();
        var shifts = await _db.Shifts.ToDictionaryAsync(x => x.Id);
        return BuildShiftLines(days, p.Employee!, structure, shifts, shifts.Values.OrderBy(x => x.Id).FirstOrDefault(), _opt, out _);
    }

    public async Task<PayrollRun> FinalizeAsync(int runId)
    {
        var run = await _db.PayrollRuns.FindAsync(runId) ?? throw new KeyNotFoundException("Payroll run not found");
        if (run.Status != PayrollStatus.Draft) throw new InvalidOperationException("Only a draft payroll can be finalized.");
        run.Status = PayrollStatus.Finalized;
        run.FinalizedOn = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return run;
    }

    public async Task<PayrollRun> MarkPaidAsync(int runId)
    {
        var run = await _db.PayrollRuns.FindAsync(runId) ?? throw new KeyNotFoundException("Payroll run not found");
        if (run.Status != PayrollStatus.Finalized) throw new InvalidOperationException("Finalize the payroll before marking it paid.");
        run.Status = PayrollStatus.Paid;
        run.PaidOn = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return run;
    }

    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
