using HRM.Web.Data;
using HRM.Web.Models;
using HRM.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRM.Web.Controllers;

public record BulkRowDto(int EmployeeId, TimeOnly? InTime, TimeOnly? OutTime, string? Remarks, int? ShiftId = null);
public record BulkEntryDto(DateOnly Date, List<BulkRowDto> Rows);
public record DevicePunchBody(List<PunchDto> Punches);

/// <summary>Attendance entry and attendance process.</summary>
[ApiController]
[Route("api/attendance")]
public class AttendanceController : ControllerBase
{
    private readonly HrmDbContext _db;
    private readonly AttendanceService _svc;
    private readonly IConfiguration _config;

    public AttendanceController(HrmDbContext db, AttendanceService svc, IConfiguration config)
    {
        _db = db;
        _svc = svc;
        _config = config;
    }

    /// <summary>
    /// For an attendance device or its middleware to push punches. Employee code is the HRM code (EMP-00003)
    /// or the device user id (3). If Device:ApiKey is set in appsettings, send it in the X-Api-Key header.
    /// </summary>
    [HttpPost("device/punches")]
    public async Task<IActionResult> DevicePunches(DevicePunchBody body, [FromHeader(Name = "X-Api-Key")] string? apiKey)
    {
        var required = _config["Device:ApiKey"];
        if (!string.IsNullOrEmpty(required) && apiKey != required)
            return Unauthorized(new { message = "Invalid API key." });
        return Ok(await _svc.ImportPunchesAsync(body.Punches ?? new List<PunchDto>(), new List<string>()));
    }

    /// <summary>Imports a punch log exported from the device (CSV / TXT).</summary>
    [HttpPost("device/import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> DeviceImport([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "Choose a punch file first." });
        string text;
        using (var reader = new StreamReader(file.OpenReadStream()))
            text = await reader.ReadToEndAsync();

        var problems = new List<string>();
        var punches = AttendanceService.ParsePunchFile(text, problems);
        return Ok(await _svc.ImportPunchesAsync(punches, problems));
    }

    /// <summary>Entry sheet for one day: every employee with their saved in/out time (if any).</summary>
    [HttpGet("entries")]
    public async Task<IActionResult> Entries(DateOnly date)
    {
        var emps = await _db.Employees
            .Where(e => e.JoiningDate <= date && (e.LastWorkingDate == null || e.LastWorkingDate >= date))
            .OrderBy(e => e.EmployeeCode)
            .Select(e => new { e.Id, e.EmployeeCode, e.FullName }).ToListAsync();
        var logs = await _db.AttendanceLogs.Where(l => l.Date == date).ToListAsync();

        return Ok(emps.Select(e =>
        {
            var l = logs.FirstOrDefault(x => x.EmployeeId == e.Id);
            return new { employeeId = e.Id, e.EmployeeCode, e.FullName, inTime = l?.InTime, outTime = l?.OutTime, remarks = l?.Remarks, shiftId = l?.ShiftId };
        }));
    }

    [HttpPost("entry")]
    public async Task<IActionResult> Entry(AttendanceEntryDto dto) => Ok(await _svc.SaveEntryAsync(dto));

    /// <summary>Saves a whole day at once. Rows without an in time clear that employee's entry.</summary>
    [HttpPost("entry/bulk")]
    public async Task<IActionResult> Bulk(BulkEntryDto dto)
    {
        var saved = 0;
        foreach (var r in dto.Rows)
        {
            if (r.InTime.HasValue)
            {
                await _svc.SaveEntryAsync(new AttendanceEntryDto(r.EmployeeId, dto.Date, r.InTime, r.OutTime, r.Remarks, r.ShiftId), "Bulk");
                saved++;
            }
            else if (await _svc.RemoveEntryAsync(r.EmployeeId, dto.Date))
            {
                saved++;
            }
        }
        return Ok(new { saved });
    }

    /// <summary>Attendance process for a date range (all employees or one).</summary>
    [HttpPost("process")]
    public async Task<IActionResult> Process(AttendanceProcessDto dto) => Ok(await _svc.ProcessAsync(dto));

    [HttpGet("daily")]
    public async Task<IActionResult> Daily(DateOnly from, DateOnly to, int? employeeId)
    {
        if (to < from) return BadRequest(new { message = "To date is before from date." });
        var q = _db.AttendanceDailies.Where(a => a.Date >= from && a.Date <= to);
        if (employeeId.HasValue) q = q.Where(a => a.EmployeeId == employeeId);
        return Ok(await q.OrderBy(a => a.Date).ThenBy(a => a.Employee!.EmployeeCode).Select(a => new
        {
            a.Id, a.EmployeeId, employeeCode = a.Employee!.EmployeeCode, employeeName = a.Employee.FullName,
            a.Date, a.Status, a.InTime, a.OutTime, a.LateMinutes, a.WorkedHours, a.OvertimeHours, a.ShiftId
        }).Take(2000).ToListAsync());
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var rows = await _db.AttendanceDailies.Where(a => a.Date >= from && a.Date <= to)
            .Select(a => new
            {
                a.EmployeeId,
                EmployeeCode = a.Employee!.EmployeeCode,
                FullName = a.Employee.FullName,
                a.Status,
                a.OvertimeHours
            })
            .ToListAsync();

        return Ok(rows.GroupBy(r => new { r.EmployeeId, r.EmployeeCode, r.FullName }).OrderBy(g => g.Key.EmployeeCode).Select(g => new
        {
            g.Key.EmployeeId, g.Key.EmployeeCode, g.Key.FullName,
            present = g.Count(x => x.Status == AttendanceStatus.Present),
            late = g.Count(x => x.Status == AttendanceStatus.Late),
            halfDay = g.Count(x => x.Status == AttendanceStatus.HalfDay),
            absent = g.Count(x => x.Status == AttendanceStatus.Absent),
            onLeave = g.Count(x => x.Status == AttendanceStatus.OnLeave),
            weeklyOff = g.Count(x => x.Status == AttendanceStatus.WeeklyOff),
            holiday = g.Count(x => x.Status == AttendanceStatus.Holiday),
            overtimeHours = g.Sum(x => x.OvertimeHours)
        }));
    }
}
