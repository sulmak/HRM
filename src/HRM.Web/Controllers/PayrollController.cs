using HRM.Web.Data;
using HRM.Web.Models;
using HRM.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRM.Web.Controllers;

public record SalaryStructureDto(decimal Basic, decimal HouseRent, decimal Medical, decimal Conveyance,
    decimal OtherAllowance, decimal ProvidentFundPercent, decimal MonthlyTax, DateOnly EffectiveFrom);
public record PayrollProcessDto(int Year, int Month);

/// <summary>Salary process: salary structures and the monthly payroll run.</summary>
[ApiController]
[Route("api/payroll")]
public class PayrollController : ControllerBase
{
    private readonly HrmDbContext _db;
    private readonly PayrollService _svc;

    public PayrollController(HrmDbContext db, PayrollService svc)
    {
        _db = db;
        _svc = svc;
    }

    [HttpGet("structures/{employeeId:int}")]
    public async Task<IActionResult> Structure(int employeeId)
    {
        var list = await _db.SalaryStructures.Where(s => s.EmployeeId == employeeId)
            .OrderByDescending(s => s.EffectiveFrom).ToListAsync();
        return Ok(list.Select(s => new
        {
            s.Id, s.EmployeeId, s.Basic, s.HouseRent, s.Medical, s.Conveyance, s.OtherAllowance,
            s.ProvidentFundPercent, s.MonthlyTax, s.EffectiveFrom, gross = s.Gross
        }));
    }

    /// <summary>Adds a new salary structure effective from a date (older ones stay as history).</summary>
    [HttpPut("structures/{employeeId:int}")]
    public async Task<IActionResult> SetStructure(int employeeId, SalaryStructureDto dto)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId)) return NotFound();
        if (dto.Basic <= 0) return BadRequest(new { message = "Basic salary must be greater than zero." });
        if (new[] { dto.HouseRent, dto.Medical, dto.Conveyance, dto.OtherAllowance, dto.ProvidentFundPercent, dto.MonthlyTax }.Any(v => v < 0))
            return BadRequest(new { message = "Amounts cannot be negative." });

        var s = await _db.SalaryStructures.FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.EffectiveFrom == dto.EffectiveFrom);
        if (s == null)
        {
            s = new SalaryStructure { EmployeeId = employeeId, EffectiveFrom = dto.EffectiveFrom };
            _db.SalaryStructures.Add(s);
        }
        s.Basic = dto.Basic;
        s.HouseRent = dto.HouseRent;
        s.Medical = dto.Medical;
        s.Conveyance = dto.Conveyance;
        s.OtherAllowance = dto.OtherAllowance;
        s.ProvidentFundPercent = dto.ProvidentFundPercent;
        s.MonthlyTax = dto.MonthlyTax;
        await _db.SaveChangesAsync();
        return Ok(new { s.Id, gross = s.Gross });
    }

    [HttpPost("process")]
    public async Task<IActionResult> Process(PayrollProcessDto dto) => Ok(await _svc.ProcessAsync(dto.Year, dto.Month));

    [HttpGet("runs")]
    public async Task<IActionResult> Runs() =>
        Ok(await _db.PayrollRuns.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).Select(r => new
        {
            r.Id, r.Year, r.Month, r.Status, r.ProcessedOn, r.FinalizedOn, r.PaidOn,
            employees = r.Payslips.Count,
            totalGross = r.Payslips.Sum(p => (decimal?)p.Gross) ?? 0,
            totalNet = r.Payslips.Sum(p => (decimal?)p.NetPay) ?? 0
        }).ToListAsync());

    [HttpGet("runs/{id:int}/payslips")]
    public async Task<IActionResult> Payslips(int id) =>
        Ok(await _db.Payslips.Where(p => p.PayrollRunId == id).OrderBy(p => p.Employee!.EmployeeCode).Select(p => new
        {
            p.Id, p.EmployeeId, employeeCode = p.Employee!.EmployeeCode, employeeName = p.Employee.FullName,
            p.DaysInMonth, p.PayableDays, p.AbsentDays, p.UnpaidLeaveDays, p.LateDays, p.OvertimeHours,
            p.Gross, p.OvertimePay, p.TotalEarnings, p.AbsentDeduction, p.LateDeduction,
            p.ProvidentFund, p.Tax, p.TotalDeductions, p.NetPay
        }).ToListAsync());

    [HttpGet("payslips/{id:int}")]
    public async Task<IActionResult> Payslip(int id)
    {
        var p = await _db.Payslips.Include(x => x.PayrollRun)
            .Include(x => x.Employee).ThenInclude(e => e!.Department)
            .Include(x => x.Employee).ThenInclude(e => e!.Designation)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        var shiftBreakdown = await _svc.GetShiftBreakdownAsync(id);
        return Ok(new
        {
            shiftBreakdown,
            p.Id, period = $"{p.PayrollRun!.Year}-{p.PayrollRun.Month:00}", runStatus = p.PayrollRun.Status,
            employeeCode = p.Employee!.EmployeeCode, employeeName = p.Employee.FullName,
            department = p.Employee.Department?.Name, designation = p.Employee.Designation?.Name,
            p.Employee.JoiningDate,
            p.DaysInMonth, p.PayableDays, p.AbsentDays, p.UnpaidLeaveDays, p.LateDays, p.OvertimeHours,
            p.Basic, p.HouseRent, p.Medical, p.Conveyance, p.OtherAllowance, p.Gross, p.OvertimePay, p.TotalEarnings,
            p.AbsentDeduction, p.LateDeduction, p.ProvidentFund, p.Tax, p.TotalDeductions, p.NetPay
        });
    }

    [HttpPost("runs/{id:int}/finalize")]
    public async Task<IActionResult> Finalize(int id) => Ok(await _svc.FinalizeAsync(id));

    [HttpPost("runs/{id:int}/pay")]
    public async Task<IActionResult> Pay(int id) => Ok(await _svc.MarkPaidAsync(id));
}
