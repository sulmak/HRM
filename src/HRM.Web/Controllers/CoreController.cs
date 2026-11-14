using HRM.Web.Data;
using HRM.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRM.Web.Controllers;

public record NameDto(string Name);
public record HolidayDto(DateOnly Date, string Name, bool IsOptional);

[ApiController]
[Route("api/lookups")]
public class LookupsController : ControllerBase
{
    private readonly HrmDbContext _db;
    public LookupsController(HrmDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(new
    {
        departments = await _db.Departments.OrderBy(d => d.Name).ToListAsync(),
        designations = await _db.Designations.OrderBy(d => d.Name).ToListAsync(),
        shifts = await _db.Shifts.OrderBy(s => s.Name).ToListAsync()
    });

    [HttpPost("departments")]
    public async Task<IActionResult> AddDepartment(NameDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Name is required." });
        var d = new Department { Name = dto.Name.Trim() };
        _db.Departments.Add(d);
        await _db.SaveChangesAsync();
        return Ok(d);
    }

    [HttpPost("designations")]
    public async Task<IActionResult> AddDesignation(NameDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Name is required." });
        var d = new Designation { Name = dto.Name.Trim() };
        _db.Designations.Add(d);
        await _db.SaveChangesAsync();
        return Ok(d);
    }

    [HttpGet("holidays")]
    public async Task<IActionResult> Holidays(int? year)
    {
        var y = year ?? DateTime.Today.Year;
        var from = new DateOnly(y, 1, 1);
        var to = new DateOnly(y, 12, 31);
        return Ok(await _db.Holidays.Where(h => h.Date >= from && h.Date <= to).OrderBy(h => h.Date).ToListAsync());
    }

    [HttpPost("holidays")]
    public async Task<IActionResult> AddHoliday(HolidayDto dto)
    {
        if (await _db.Holidays.AnyAsync(h => h.Date == dto.Date))
            return BadRequest(new { message = "A holiday already exists on this date." });
        var h = new Holiday { Date = dto.Date, Name = dto.Name, IsOptional = dto.IsOptional };
        _db.Holidays.Add(h);
        await _db.SaveChangesAsync();
        return Ok(h);
    }

    [HttpDelete("holidays/{id:int}")]
    public async Task<IActionResult> DeleteHoliday(int id)
    {
        var h = await _db.Holidays.FindAsync(id);
        if (h == null) return NotFound();
        _db.Holidays.Remove(h);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly HrmDbContext _db;
    public DashboardController(HrmDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var todays = await _db.AttendanceDailies.Where(a => a.Date == today).Select(a => a.Status).ToListAsync();

        return Ok(new
        {
            employees = await _db.Employees.CountAsync(e => e.IsActive),
            openCirculars = await _db.JobCirculars.CountAsync(c => c.Status == CircularStatus.Published),
            candidatesInPipeline = await _db.JobApplications.CountAsync(a =>
                a.Status != ApplicationStatus.Rejected && a.Status != ApplicationStatus.Joined),
            pendingLeaves = await _db.LeaveApplications.CountAsync(l => l.Status == LeaveStatus.Pending),
            todayPresent = todays.Count(s => s is AttendanceStatus.Present or AttendanceStatus.Late or AttendanceStatus.HalfDay),
            todayAbsent = todays.Count(s => s == AttendanceStatus.Absent),
            todayOnLeave = todays.Count(s => s == AttendanceStatus.OnLeave),
            upcomingHolidays = await _db.Holidays.Where(h => h.Date >= today).OrderBy(h => h.Date).Take(4).ToListAsync(),
            latestPayroll = await _db.PayrollRuns.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
                .Select(r => new { r.Id, r.Year, r.Month, r.Status }).FirstOrDefaultAsync()
        });
    }
}
