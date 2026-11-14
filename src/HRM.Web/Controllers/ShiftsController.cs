using HRM.Web.Data;
using HRM.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRM.Web.Controllers;

public record ShiftDto(string Name, TimeOnly StartTime, TimeOnly EndTime, int GraceMinutes, decimal HalfDayHours);
public record AssignShiftDto(int ShiftId, List<int> EmployeeIds);

/// <summary>Shift setup and shift change (assign a shift to one or many employees).</summary>
[ApiController]
[Route("api/shifts")]
public class ShiftsController : ControllerBase
{
    private readonly HrmDbContext _db;
    public ShiftsController(HrmDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await _db.Shifts.OrderBy(s => s.Name).Select(s => new
        {
            s.Id, s.Name, s.StartTime, s.EndTime, s.GraceMinutes, s.HalfDayHours,
            employees = _db.Employees.Count(e => e.ShiftId == s.Id && e.IsActive)
        }).ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create(ShiftDto dto)
    {
        Validate(dto);
        var s = new Shift();
        Map(s, dto);
        _db.Shifts.Add(s);
        await _db.SaveChangesAsync();
        return Ok(s);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ShiftDto dto)
    {
        Validate(dto);
        var s = await _db.Shifts.FindAsync(id);
        if (s == null) return NotFound();
        Map(s, dto);
        await _db.SaveChangesAsync();
        return Ok(s);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var s = await _db.Shifts.FindAsync(id);
        if (s == null) return NotFound();
        if (await _db.Employees.AnyAsync(e => e.ShiftId == id))
            return BadRequest(new { message = "Employees are still assigned to this shift. Move them to another shift first." });
        _db.Shifts.Remove(s);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Shift change: moves the selected employees to a shift.</summary>
    [HttpPost("assign")]
    public async Task<IActionResult> Assign(AssignShiftDto dto)
    {
        if (dto.EmployeeIds == null || dto.EmployeeIds.Count == 0)
            return BadRequest(new { message = "Select at least one employee." });
        if (!await _db.Shifts.AnyAsync(s => s.Id == dto.ShiftId))
            return NotFound(new { message = "Shift not found." });

        var employees = await _db.Employees.Where(e => dto.EmployeeIds.Contains(e.Id)).ToListAsync();
        var changed = 0;
        foreach (var e in employees)
        {
            if (e.ShiftId == dto.ShiftId) continue;
            e.ShiftId = dto.ShiftId;
            changed++;
        }
        await _db.SaveChangesAsync();
        return Ok(new
        {
            changed,
            unchanged = employees.Count - changed,
            note = "Attendance processing uses the employee's current shift, so re-run it for any dates already processed."
        });
    }

    private static void Validate(ShiftDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("Shift name is required.");
        if (dto.StartTime == dto.EndTime) throw new InvalidOperationException("Start and end time cannot be the same.");
        if (dto.GraceMinutes < 0 || dto.GraceMinutes > 180) throw new InvalidOperationException("Grace minutes must be between 0 and 180.");
        if (dto.HalfDayHours < 0 || dto.HalfDayHours > 12) throw new InvalidOperationException("Half-day hours must be between 0 and 12.");
    }

    private static void Map(Shift s, ShiftDto d)
    {
        s.Name = d.Name.Trim();
        s.StartTime = d.StartTime;
        s.EndTime = d.EndTime;
        s.GraceMinutes = d.GraceMinutes;
        s.HalfDayHours = d.HalfDayHours;
    }
}
