using HRM.Web.Data;
using HRM.Web.Models;
using HRM.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRM.Web.Controllers;

public record EmployeeDto(
    string FullName, string? Email, string? Phone, Gender Gender, DateOnly? DateOfBirth, string? NationalId,
    string? Address, int? DepartmentId, int? DesignationId, int? ShiftId, int? ReportsToId,
    DateOnly JoiningDate, DateOnly? ConfirmationDate, DateOnly? LastWorkingDate, EmploymentStatus Status,
    decimal OpeningEarnLeave, string? WeeklyOffDays);

/// <summary>Employee information.</summary>
[ApiController]
[Route("api/employees")]
public class EmployeesController : ControllerBase
{
    private readonly HrmDbContext _db;
    public EmployeesController(HrmDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List(string? search, int? departmentId, bool activeOnly = false)
    {
        var q = _db.Employees.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(e => e.FullName.Contains(search) || e.EmployeeCode.Contains(search) || (e.Email != null && e.Email.Contains(search)));
        if (departmentId.HasValue) q = q.Where(e => e.DepartmentId == departmentId);
        if (activeOnly) q = q.Where(e => e.IsActive);

        var list = await q.OrderBy(e => e.EmployeeCode).Select(e => new
        {
            e.Id, e.EmployeeCode, e.FullName, e.Email, e.Phone, e.Gender, e.DateOfBirth, e.NationalId, e.Address,
            e.DepartmentId, department = e.Department != null ? e.Department.Name : null,
            e.DesignationId, designation = e.Designation != null ? e.Designation.Name : null,
            e.ShiftId, e.ReportsToId, e.JoiningDate, e.ConfirmationDate, e.LastWorkingDate,
            e.Status, e.IsActive, e.WeeklyOffDays, e.OpeningEarnLeave
        }).ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var e = await _db.Employees.FindAsync(id);
        return e == null ? NotFound() : Ok(e);
    }

    [HttpPost]
    public async Task<IActionResult> Create(EmployeeDto dto)
    {
        var e = new Employee();
        Apply(e, dto);
        var n = await _db.Employees.CountAsync() + 1;
        var code = $"EMP-{n:00000}";
        while (await _db.Employees.AnyAsync(x => x.EmployeeCode == code)) code = $"EMP-{++n:00000}";
        e.EmployeeCode = code;
        _db.Employees.Add(e);
        await _db.SaveChangesAsync();
        return Ok(e);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, EmployeeDto dto)
    {
        var e = await _db.Employees.FindAsync(id);
        if (e == null) return NotFound();
        Apply(e, dto);
        await _db.SaveChangesAsync();
        return Ok(e);
    }

    private static void Apply(Employee e, EmployeeDto d)
    {
        if (string.IsNullOrWhiteSpace(d.FullName)) throw new InvalidOperationException("Full name is required.");
        e.FullName = d.FullName.Trim();
        e.Email = d.Email;
        e.Phone = d.Phone;
        e.Gender = d.Gender;
        e.DateOfBirth = d.DateOfBirth;
        e.NationalId = d.NationalId;
        e.Address = d.Address;
        e.DepartmentId = d.DepartmentId;
        e.DesignationId = d.DesignationId;
        e.ShiftId = d.ShiftId;
        e.ReportsToId = d.ReportsToId;
        e.JoiningDate = d.JoiningDate;
        e.ConfirmationDate = d.ConfirmationDate;
        e.LastWorkingDate = d.LastWorkingDate;
        e.Status = d.Status;
        e.OpeningEarnLeave = d.OpeningEarnLeave;
        e.IsActive = d.Status is not (EmploymentStatus.Resigned or EmploymentStatus.Terminated);
        if (!string.IsNullOrWhiteSpace(d.WeeklyOffDays)) e.WeeklyOffDays = d.WeeklyOffDays;
    }
}

/// <summary>Weekly off day calculation.</summary>
[ApiController]
[Route("api/weeklyoff")]
public class WeeklyOffController : ControllerBase
{
    private readonly HrmDbContext _db;
    public WeeklyOffController(HrmDbContext db) => _db = db;

    public record WeeklyOffDto(List<string> Days);

    /// <summary>The employee's weekly off dates between two dates, with the total count.</summary>
    [HttpGet("{employeeId:int}")]
    public async Task<IActionResult> Calculate(int employeeId, DateOnly from, DateOnly to)
    {
        var e = await _db.Employees.FindAsync(employeeId);
        if (e == null) return NotFound();
        if (to < from) return BadRequest(new { message = "To date is before from date." });
        if (to.DayNumber - from.DayNumber > 366) return BadRequest(new { message = "Range is limited to one year." });

        var off = DateRules.ParseOffDays(e.WeeklyOffDays);
        var dates = DateRules.Range(DateRules.Max(from, e.JoiningDate), to)
            .Where(d => off.Contains(d.DayOfWeek)).ToList();
        return Ok(new
        {
            employeeId = e.Id,
            weeklyOffDays = off.OrderBy(d => (int)d).Select(d => d.ToString()),
            count = dates.Count,
            dates
        });
    }

    [HttpPut("{employeeId:int}")]
    public async Task<IActionResult> Set(int employeeId, WeeklyOffDto dto)
    {
        var e = await _db.Employees.FindAsync(employeeId);
        if (e == null) return NotFound();
        var joined = DateRules.JoinOffDays(dto.Days);
        if (joined.Split(',', StringSplitOptions.RemoveEmptyEntries).Length >= 7)
            return BadRequest(new { message = "At least one working day is required." });
        e.WeeklyOffDays = joined;
        await _db.SaveChangesAsync();
        return Ok(new { e.Id, e.WeeklyOffDays, note = "Re-run attendance processing for periods already processed." });
    }

    /// <summary>Weekly off count for every active employee in a month.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var emps = await _db.Employees.Where(e => e.IsActive).OrderBy(e => e.EmployeeCode).ToListAsync();
        return Ok(emps.Select(e =>
        {
            var off = DateRules.ParseOffDays(e.WeeklyOffDays);
            var count = DateRules.Range(DateRules.Max(from, e.JoiningDate), to).Count(d => off.Contains(d.DayOfWeek));
            return new { employeeId = e.Id, e.EmployeeCode, e.FullName, e.WeeklyOffDays, count };
        }));
    }
}

/// <summary>Employee calendar: holidays, weekly offs, leave, attendance, birthdays and work anniversaries.</summary>
[ApiController]
[Route("api/calendar")]
public class CalendarController : ControllerBase
{
    private readonly HrmDbContext _db;
    public CalendarController(HrmDbContext db) => _db = db;

    public record CalendarEvent(DateOnly Date, string Type, string Title, string? Detail);

    /// <summary>With employeeId: that employee's calendar. Without: company-wide calendar.</summary>
    [HttpGet]
    public async Task<IActionResult> Month(int year, int month, int? employeeId)
    {
        if (month < 1 || month > 12) return BadRequest(new { message = "Month must be 1-12." });
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var events = new List<CalendarEvent>();

        var holidays = await _db.Holidays.Where(h => h.Date >= from && h.Date <= to).ToListAsync();
        events.AddRange(holidays.Select(h => new CalendarEvent(h.Date, "Holiday", h.Name, h.IsOptional ? "Optional" : null)));
        var holidaySet = holidays.Where(h => !h.IsOptional).Select(h => h.Date).ToHashSet();

        var empQuery = _db.Employees.Where(e => e.IsActive);
        if (employeeId.HasValue) empQuery = empQuery.Where(e => e.Id == employeeId.Value);
        var emps = await empQuery.ToListAsync();
        if (employeeId.HasValue && emps.Count == 0) return NotFound();
        var ids = emps.Select(e => e.Id).ToList();

        var leaves = await _db.LeaveApplications.Include(l => l.Employee)
            .Where(l => ids.Contains(l.EmployeeId) && l.Status != LeaveStatus.Rejected && l.Status != LeaveStatus.Cancelled &&
                        l.FromDate <= to && l.ToDate >= from).ToListAsync();

        foreach (var emp in emps)
        {
            var off = DateRules.ParseOffDays(emp.WeeklyOffDays);
            if (employeeId.HasValue)
            {
                foreach (var d in DateRules.Range(DateRules.Max(from, emp.JoiningDate), to).Where(d => off.Contains(d.DayOfWeek)))
                    events.Add(new CalendarEvent(d, "WeeklyOff", "Weekly off", null));
            }

            if (emp.DateOfBirth is { } dob && dob.Month == month)
                events.Add(new CalendarEvent(new DateOnly(year, month, Math.Min(dob.Day, DateTime.DaysInMonth(year, month))),
                    "Birthday", $"{emp.FullName}'s birthday", null));
            if (emp.JoiningDate.Month == month && emp.JoiningDate.Year < year)
                events.Add(new CalendarEvent(new DateOnly(year, month, Math.Min(emp.JoiningDate.Day, DateTime.DaysInMonth(year, month))),
                    "Anniversary", $"{emp.FullName}: {year - emp.JoiningDate.Year} year(s) with the company", null));
        }

        foreach (var l in leaves)
            foreach (var d in DateRules.Range(DateRules.Max(from, l.FromDate), DateRules.Min(to, l.ToDate)))
            {
                var emp = l.Employee!;
                if (DateRules.ParseOffDays(emp.WeeklyOffDays).Contains(d.DayOfWeek) || holidaySet.Contains(d)) continue;
                var who = employeeId.HasValue ? "" : emp.FullName + ": ";
                events.Add(new CalendarEvent(d, "Leave", $"{who}{l.LeaveType} leave", l.Status.ToString()));
            }

        if (employeeId.HasValue)
        {
            var dailies = await _db.AttendanceDailies
                .Where(a => a.EmployeeId == employeeId.Value && a.Date >= from && a.Date <= to).ToListAsync();
            foreach (var a in dailies.Where(a => a.Status is not (AttendanceStatus.WeeklyOff or AttendanceStatus.Holiday or AttendanceStatus.OnLeave)))
            {
                string? detail = null;
                if (a.InTime.HasValue)
                {
                    var outText = a.OutTime.HasValue ? a.OutTime.Value.ToString("HH:mm") : "?";
                    detail = a.InTime.Value.ToString("HH:mm") + " - " + outText;
                }
                events.Add(new CalendarEvent(a.Date, "Attendance", a.Status.ToString(), detail));
            }
        }

        return Ok(events.OrderBy(e => e.Date).ThenBy(e => e.Type));
    }
}
