using HRM.Web.Data;
using HRM.Web.Models;
using HRM.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HRM.Web.Controllers;

public record CycleDto(string Name, DateOnly StartDate, DateOnly EndDate);
public record KpiDto(string Name, string? Description, decimal Weight);
public record ReviewItemDto(string KpiName, decimal Weight, decimal Score);
public record ReviewDto(int EmployeeId, int ReviewCycleId, string? ReviewerName, string? Comments, List<ReviewItemDto> Items);

/// <summary>Employee performance measure system: KPI scoring blended with attendance discipline.</summary>
[ApiController]
[Route("api/performance")]
public class PerformanceController : ControllerBase
{
    private readonly HrmDbContext _db;
    private readonly PerformanceOptions _opt;

    public PerformanceController(HrmDbContext db, IOptions<PerformanceOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
    }

    // ---- cycles & KPI templates ---------------------------------------------------------

    [HttpGet("cycles")]
    public async Task<IActionResult> Cycles() => Ok(await _db.ReviewCycles.OrderByDescending(c => c.StartDate).ToListAsync());

    [HttpPost("cycles")]
    public async Task<IActionResult> CreateCycle(CycleDto dto)
    {
        if (dto.EndDate < dto.StartDate) return BadRequest(new { message = "End date is before start date." });
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Name is required." });
        var c = new ReviewCycle { Name = dto.Name.Trim(), StartDate = dto.StartDate, EndDate = dto.EndDate };
        _db.ReviewCycles.Add(c);
        await _db.SaveChangesAsync();
        return Ok(c);
    }

    [HttpPost("cycles/{id:int}/close")]
    public async Task<IActionResult> CloseCycle(int id)
    {
        var c = await _db.ReviewCycles.FindAsync(id);
        if (c == null) return NotFound();
        c.IsOpen = false;
        await _db.SaveChangesAsync();
        return Ok(c);
    }

    [HttpGet("kpis")]
    public async Task<IActionResult> Kpis() => Ok(await _db.KpiTemplates.OrderByDescending(k => k.Weight).ToListAsync());

    [HttpPost("kpis")]
    public async Task<IActionResult> AddKpi(KpiDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Weight <= 0) return BadRequest(new { message = "Name and a positive weight are required." });
        var k = new KpiTemplate { Name = dto.Name.Trim(), Description = dto.Description, Weight = dto.Weight };
        _db.KpiTemplates.Add(k);
        await _db.SaveChangesAsync();
        return Ok(k);
    }

    [HttpDelete("kpis/{id:int}")]
    public async Task<IActionResult> DeleteKpi(int id)
    {
        var k = await _db.KpiTemplates.FindAsync(id);
        if (k == null) return NotFound();
        _db.KpiTemplates.Remove(k);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---- reviews ------------------------------------------------------------------------

    /// <summary>Reviews ordered by overall score (highest first), so the list doubles as a ranking.</summary>
    [HttpGet("reviews")]
    public async Task<IActionResult> Reviews(int? cycleId, int? employeeId)
    {
        var q = _db.PerformanceReviews.AsQueryable();
        if (cycleId.HasValue) q = q.Where(r => r.ReviewCycleId == cycleId);
        if (employeeId.HasValue) q = q.Where(r => r.EmployeeId == employeeId);
        return Ok(await q.OrderByDescending(r => r.OverallScore).Select(r => new
        {
            r.Id, r.EmployeeId, employeeCode = r.Employee!.EmployeeCode, employeeName = r.Employee.FullName,
            department = r.Employee.Department != null ? r.Employee.Department.Name : null,
            r.ReviewCycleId, cycle = r.ReviewCycle!.Name,
            r.KpiScore, r.AttendanceScore, r.OverallScore, r.Rating, r.Status, r.ReviewerName
        }).ToListAsync());
    }

    [HttpGet("reviews/{id:int}")]
    public async Task<IActionResult> Review(int id)
    {
        var r = await _db.PerformanceReviews.Include(x => x.Items).Include(x => x.Employee).Include(x => x.ReviewCycle)
            .FirstOrDefaultAsync(x => x.Id == id);
        return r == null ? NotFound() : Ok(r);
    }

    /// <summary>Creates or replaces the draft review for an employee in a cycle and calculates the scores.</summary>
    [HttpPost("reviews")]
    public async Task<IActionResult> SaveReview(ReviewDto dto)
    {
        if (dto.Items == null || dto.Items.Count == 0) return BadRequest(new { message = "Add at least one KPI score." });
        if (dto.Items.Any(i => i.Score < 1 || i.Score > 5)) return BadRequest(new { message = "Scores must be between 1 and 5." });
        if (dto.Items.Any(i => i.Weight <= 0)) return BadRequest(new { message = "Weights must be positive." });

        var cycle = await _db.ReviewCycles.FindAsync(dto.ReviewCycleId);
        if (cycle == null) return NotFound(new { message = "Review cycle not found." });
        if (!cycle.IsOpen) return BadRequest(new { message = "This review cycle is closed." });
        if (!await _db.Employees.AnyAsync(e => e.Id == dto.EmployeeId)) return NotFound(new { message = "Employee not found." });

        var review = await _db.PerformanceReviews.Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.EmployeeId == dto.EmployeeId && r.ReviewCycleId == dto.ReviewCycleId);
        if (review != null && review.Status == ReviewStatus.Submitted)
            return BadRequest(new { message = "A submitted review cannot be changed." });
        if (review == null)
        {
            review = new PerformanceReview { EmployeeId = dto.EmployeeId, ReviewCycleId = dto.ReviewCycleId };
            _db.PerformanceReviews.Add(review);
        }
        else
        {
            _db.PerformanceReviewItems.RemoveRange(review.Items);
            review.Items.Clear();
        }

        foreach (var i in dto.Items)
            review.Items.Add(new PerformanceReviewItem { KpiName = i.KpiName, Weight = i.Weight, Score = i.Score });

        var totalWeight = dto.Items.Sum(i => i.Weight);
        review.KpiScore = Math.Round(dto.Items.Sum(i => i.Weight * i.Score / 5m) / totalWeight * 100m, 2);

        var (attScore, hasAttendance) = await AttendanceScoreAsync(dto.EmployeeId, cycle);
        review.AttendanceScore = attScore;

        var kpiW = _opt.KpiWeightPercent;
        var attW = hasAttendance ? _opt.AttendanceWeightPercent : 0m;
        review.OverallScore = Math.Round((review.KpiScore * kpiW + review.AttendanceScore * attW) / (kpiW + attW), 2);
        review.Rating = RatingFor(review.OverallScore);
        review.ReviewerName = dto.ReviewerName;
        review.Comments = dto.Comments;

        await _db.SaveChangesAsync();
        return Ok(new
        {
            review.Id, review.KpiScore, review.AttendanceScore, review.OverallScore, review.Rating,
            attendanceDataAvailable = hasAttendance
        });
    }

    [HttpPost("reviews/{id:int}/submit")]
    public async Task<IActionResult> Submit(int id)
    {
        var r = await _db.PerformanceReviews.FindAsync(id);
        if (r == null) return NotFound();
        r.Status = ReviewStatus.Submitted;
        await _db.SaveChangesAsync();
        return Ok(r);
    }

    /// <summary>
    /// Attendance discipline score: present = 1, late = 0.75, half day = 0.5, absent = 0, over the days the
    /// employee was expected to work. Needs processed attendance inside the cycle.
    /// </summary>
    private async Task<(decimal score, bool hasData)> AttendanceScoreAsync(int employeeId, ReviewCycle cycle)
    {
        var statuses = await _db.AttendanceDailies
            .Where(a => a.EmployeeId == employeeId && a.Date >= cycle.StartDate && a.Date <= cycle.EndDate)
            .Select(a => a.Status).ToListAsync();

        var expected = statuses.Count(s => s is AttendanceStatus.Present or AttendanceStatus.Late or AttendanceStatus.HalfDay or AttendanceStatus.Absent);
        if (expected == 0) return (0m, false);

        var points = statuses.Sum(s => s switch
        {
            AttendanceStatus.Present => 1m,
            AttendanceStatus.Late => 0.75m,
            AttendanceStatus.HalfDay => 0.5m,
            _ => 0m
        });
        return (Math.Round(points / expected * 100m, 2), true);
    }

    private static string RatingFor(decimal score) => score switch
    {
        >= 90 => "Outstanding",
        >= 75 => "Exceeds expectations",
        >= 60 => "Meets expectations",
        >= 40 => "Needs improvement",
        _ => "Unsatisfactory"
    };
}
