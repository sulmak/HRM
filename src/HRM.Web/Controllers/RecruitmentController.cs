using HRM.Web.Data;
using HRM.Web.Models;
using HRM.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRM.Web.Controllers;

public class ApplyForm
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public Gender Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public decimal ExpectedSalary { get; set; }
    public IFormFile? Cv { get; set; }
}

public record ShortlistDto(List<int> Ids);

/// <summary>Employee selection process.</summary>
[ApiController]
[Route("api/recruitment")]
public class RecruitmentController : ControllerBase
{
    private static readonly string[] AllowedCvExtensions = { ".pdf", ".doc", ".docx" };
    private const long MaxCvBytes = 5 * 1024 * 1024;

    private readonly HrmDbContext _db;
    private readonly RecruitmentService _svc;
    private readonly IWebHostEnvironment _env;

    public RecruitmentController(HrmDbContext db, RecruitmentService svc, IWebHostEnvironment env)
    {
        _db = db;
        _svc = svc;
        _env = env;
    }

    // ---- i. circular --------------------------------------------------------------------

    [HttpGet("circulars")]
    public async Task<IActionResult> Circulars(CircularStatus? status)
    {
        var q = _db.JobCirculars.AsQueryable();
        if (status.HasValue) q = q.Where(c => c.Status == status);
        return Ok(await q.OrderByDescending(c => c.Id).Select(c => new
        {
            c.Id, c.Title, c.DepartmentId, department = c.Department != null ? c.Department.Name : null,
            c.DesignationId, designation = c.Designation != null ? c.Designation.Name : null,
            c.Vacancies, c.Location, c.Description, c.Requirements, c.SalaryRange,
            c.PublishDate, c.Deadline, c.Status,
            applications = c.Applications.Count,
            filled = c.Applications.Count(a => a.Status == ApplicationStatus.Selected || a.Status == ApplicationStatus.Appointed ||
                                               a.Status == ApplicationStatus.JoiningSubmitted || a.Status == ApplicationStatus.Joined)
        }).ToListAsync());
    }

    [HttpPost("circulars")]
    public async Task<IActionResult> CreateCircular(CircularDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest(new { message = "Title is required." });
        if (dto.Vacancies < 1) return BadRequest(new { message = "At least one vacancy is required." });
        var c = new JobCircular();
        Map(c, dto);
        _db.JobCirculars.Add(c);
        await _db.SaveChangesAsync();
        return Ok(c);
    }

    [HttpPut("circulars/{id:int}")]
    public async Task<IActionResult> UpdateCircular(int id, CircularDto dto)
    {
        var c = await _db.JobCirculars.FindAsync(id);
        if (c == null) return NotFound();
        if (c.Status != CircularStatus.Draft) return BadRequest(new { message = "Only draft circulars can be edited." });
        Map(c, dto);
        await _db.SaveChangesAsync();
        return Ok(c);
    }

    private static void Map(JobCircular c, CircularDto d)
    {
        c.Title = d.Title.Trim();
        c.DepartmentId = d.DepartmentId;
        c.DesignationId = d.DesignationId;
        c.Vacancies = d.Vacancies;
        c.Location = d.Location;
        c.Description = d.Description;
        c.Requirements = d.Requirements;
        c.SalaryRange = d.SalaryRange;
        c.Deadline = d.Deadline;
    }

    [HttpPost("circulars/{id:int}/publish")]
    public async Task<IActionResult> Publish(int id) => Ok(await _svc.PublishCircularAsync(id));

    [HttpPost("circulars/{id:int}/close")]
    public async Task<IActionResult> Close(int id) => Ok(await _svc.CloseCircularAsync(id));

    // ---- ii. CV drop --------------------------------------------------------------------

    [HttpPost("circulars/{id:int}/apply")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Apply(int id, [FromForm] ApplyForm form)
    {
        if (string.IsNullOrWhiteSpace(form.FullName) || string.IsNullOrWhiteSpace(form.Email))
            return BadRequest(new { message = "Name and email are required." });

        string? path = null, originalName = null;
        if (form.Cv is { Length: > 0 })
        {
            var ext = Path.GetExtension(form.Cv.FileName).ToLowerInvariant();
            if (!AllowedCvExtensions.Contains(ext)) return BadRequest(new { message = "CV must be a PDF or Word file." });
            if (form.Cv.Length > MaxCvBytes) return BadRequest(new { message = "CV must be 5 MB or smaller." });

            var dir = Path.Combine(_env.ContentRootPath, "App_Data", "cv");
            Directory.CreateDirectory(dir);
            path = $"{Guid.NewGuid():N}{ext}";
            await using var fs = System.IO.File.Create(Path.Combine(dir, path));
            await form.Cv.CopyToAsync(fs);
            originalName = Path.GetFileName(form.Cv.FileName);
        }

        var app = new JobApplication
        {
            FullName = form.FullName.Trim(),
            Email = form.Email.Trim(),
            Phone = form.Phone,
            Gender = form.Gender,
            DateOfBirth = form.DateOfBirth,
            Address = form.Address,
            ExpectedSalary = form.ExpectedSalary,
            CvFilePath = path,
            CvFileName = originalName
        };
        try
        {
            await _svc.ApplyAsync(id, app);
        }
        catch
        {
            if (path != null) System.IO.File.Delete(Path.Combine(_env.ContentRootPath, "App_Data", "cv", path));
            throw;
        }
        return Ok(new { app.Id, app.FullName, app.Status });
    }

    [HttpGet("circulars/{id:int}/applications")]
    public async Task<IActionResult> Applications(int id)
    {
        var list = await _db.JobApplications.Where(a => a.JobCircularId == id)
            .OrderBy(a => a.MeritRank ?? int.MaxValue).ThenByDescending(a => a.AppliedOn)
            .Select(a => new
            {
                a.Id, a.FullName, a.Email, a.Phone, a.Gender, a.ExpectedSalary, a.CvFileName,
                hasCv = a.CvFilePath != null, a.AppliedOn, a.Status, a.TotalScore, a.MeritRank,
                a.ApprovalRemarks, a.DecisionRemarks,
                interviews = a.Interviews.OrderBy(i => i.Round).Select(i => new
                {
                    i.Id, i.Round, i.ScheduledAt, i.Panel, i.Venue, i.IsCompleted, i.Score, i.Feedback
                }).ToList(),
                hasAppointment = a.Appointment != null,
                hasJoining = a.Joining != null
            }).ToListAsync();
        return Ok(list);
    }

    [HttpGet("applications/{id:int}/cv")]
    public async Task<IActionResult> DownloadCv(int id)
    {
        var a = await _db.JobApplications.FindAsync(id);
        if (a?.CvFilePath == null) return NotFound();
        var full = Path.Combine(_env.ContentRootPath, "App_Data", "cv", Path.GetFileName(a.CvFilePath));
        if (!System.IO.File.Exists(full)) return NotFound();
        var type = Path.GetExtension(full).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/msword"
        };
        return PhysicalFile(full, type, a.CvFileName ?? Path.GetFileName(full));
    }

    [HttpPost("applications/shortlist")]
    public async Task<IActionResult> Shortlist(ShortlistDto dto)
    {
        await _svc.ShortlistAsync(dto.Ids);
        return NoContent();
    }

    // ---- iii. interview -----------------------------------------------------------------

    [HttpPost("applications/{id:int}/interviews")]
    public async Task<IActionResult> Schedule(int id, InterviewDto dto) => Ok(await _svc.ScheduleInterviewAsync(id, dto));

    [HttpPost("interviews/{id:int}/result")]
    public async Task<IActionResult> Result(int id, InterviewResultDto dto) => Ok(await _svc.RecordInterviewAsync(id, dto));

    // ---- iv. merit list + candidate approval --------------------------------------------

    [HttpPost("circulars/{id:int}/merit-list")]
    public async Task<IActionResult> MeritList(int id)
    {
        var list = await _svc.BuildMeritListAsync(id);
        return Ok(list.Select(a => new { a.Id, a.FullName, a.TotalScore, a.MeritRank }));
    }

    [HttpPost("applications/{id:int}/approval")]
    public async Task<IActionResult> Approval(int id, ApprovalDto dto) => Ok(await _svc.ApproveCandidateAsync(id, dto));

    // ---- v. final selection / rejection -------------------------------------------------

    [HttpPost("applications/{id:int}/decision")]
    public async Task<IActionResult> Decision(int id, DecisionDto dto) => Ok(await _svc.FinalDecisionAsync(id, dto));

    // ---- vi. appointment letter ---------------------------------------------------------

    [HttpPost("applications/{id:int}/appointment")]
    public async Task<IActionResult> Appointment(int id, AppointmentDto dto) => Ok(await _svc.IssueAppointmentAsync(id, dto));

    [HttpGet("applications/{id:int}/appointment")]
    public async Task<IActionResult> GetAppointment(int id)
    {
        var l = await _db.AppointmentLetters.FirstOrDefaultAsync(x => x.JobApplicationId == id);
        return l == null ? NotFound() : Ok(l);
    }

    // ---- vii. joining letter ------------------------------------------------------------

    [HttpPost("applications/{id:int}/joining")]
    public async Task<IActionResult> Joining(int id, JoiningDto dto) => Ok(await _svc.SubmitJoiningAsync(id, dto));

    // ---- viii. add as employee ----------------------------------------------------------

    [HttpPost("applications/{id:int}/add-employee")]
    public async Task<IActionResult> AddEmployee(int id)
    {
        var e = await _svc.AddAsEmployeeAsync(id);
        return Ok(new { e.Id, e.EmployeeCode, e.FullName });
    }
}
