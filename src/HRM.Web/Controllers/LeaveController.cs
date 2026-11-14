using HRM.Web.Data;
using HRM.Web.Models;
using HRM.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRM.Web.Controllers;

public record LeaveDecisionDto(bool Approve, string? Remarks);
public record LeavePolicyDto(decimal DaysPerYear, int WorkDaysPerLeave, decimal MaxAccumulate, int EligibleAfterMonths, bool ProRateFirstYear);

/// <summary>Leave calculation: casual, earned and other leave.</summary>
[ApiController]
[Route("api/leave")]
public class LeaveController : ControllerBase
{
    private readonly HrmDbContext _db;
    private readonly LeaveService _svc;

    public LeaveController(HrmDbContext db, LeaveService svc)
    {
        _db = db;
        _svc = svc;
    }

    [HttpGet("policies")]
    public async Task<IActionResult> Policies() => Ok(await _db.LeavePolicies.OrderBy(p => p.LeaveType).ToListAsync());

    [HttpPut("policies/{id:int}")]
    public async Task<IActionResult> UpdatePolicy(int id, LeavePolicyDto dto)
    {
        var p = await _db.LeavePolicies.FindAsync(id);
        if (p == null) return NotFound();
        if (dto.DaysPerYear < 0 || dto.MaxAccumulate < 0 || dto.WorkDaysPerLeave < 0 || dto.EligibleAfterMonths < 0)
            return BadRequest(new { message = "Values cannot be negative." });
        p.DaysPerYear = dto.DaysPerYear;
        p.WorkDaysPerLeave = dto.WorkDaysPerLeave;
        p.MaxAccumulate = dto.MaxAccumulate;
        p.EligibleAfterMonths = dto.EligibleAfterMonths;
        p.ProRateFirstYear = dto.ProRateFirstYear;
        await _db.SaveChangesAsync();
        return Ok(p);
    }

    /// <summary>Entitlement, used and remaining days per leave type (casual, sick, earned, ...).</summary>
    [HttpGet("balances")]
    public async Task<IActionResult> Balances(int employeeId, int? year) =>
        Ok(await _svc.GetBalancesAsync(employeeId, year ?? DateTime.Today.Year));

    /// <summary>Preview how many leave days a date range will consume.</summary>
    [HttpGet("calculate")]
    public async Task<IActionResult> Calculate(int employeeId, LeaveType type, DateOnly from, DateOnly to)
    {
        var emp = await _db.Employees.FindAsync(employeeId);
        if (emp == null) return NotFound();
        return Ok(new { days = await _svc.CountLeaveDaysAsync(emp, type, from, to) });
    }

    [HttpGet("applications")]
    public async Task<IActionResult> Applications(int? employeeId, LeaveStatus? status)
    {
        var q = _db.LeaveApplications.AsQueryable();
        if (employeeId.HasValue) q = q.Where(l => l.EmployeeId == employeeId);
        if (status.HasValue) q = q.Where(l => l.Status == status);
        return Ok(await q.OrderByDescending(l => l.AppliedOn).Select(l => new
        {
            l.Id, l.EmployeeId, employeeCode = l.Employee!.EmployeeCode, employeeName = l.Employee.FullName,
            l.LeaveType, l.FromDate, l.ToDate, l.Days, l.Reason, l.Status, l.AppliedOn, l.DecisionOn, l.DecisionRemarks
        }).ToListAsync());
    }

    [HttpPost("apply")]
    public async Task<IActionResult> Apply(LeaveApplyDto dto) => Ok(await _svc.ApplyAsync(dto));

    [HttpPost("applications/{id:int}/decision")]
    public async Task<IActionResult> Decide(int id, LeaveDecisionDto dto) => Ok(await _svc.DecideAsync(id, dto.Approve, dto.Remarks));

    [HttpPost("applications/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var l = await _db.LeaveApplications.FindAsync(id);
        if (l == null) return NotFound();
        if (l.Status is LeaveStatus.Rejected or LeaveStatus.Cancelled)
            return BadRequest(new { message = "This application is already closed." });
        if (l.Status == LeaveStatus.Approved && l.FromDate <= DateOnly.FromDateTime(DateTime.Today))
            return BadRequest(new { message = "Leave that has already started cannot be cancelled." });
        l.Status = LeaveStatus.Cancelled;
        await _db.SaveChangesAsync();
        return Ok(l);
    }
}
