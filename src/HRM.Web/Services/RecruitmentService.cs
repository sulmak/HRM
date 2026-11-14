using HRM.Web.Data;
using HRM.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HRM.Web.Services;

public record CircularDto(string Title, int? DepartmentId, int? DesignationId, int Vacancies, string? Location,
    string? Description, string? Requirements, string? SalaryRange, DateOnly Deadline);
public record InterviewDto(DateTime ScheduledAt, int Round, string? Panel, string? Venue);
public record InterviewResultDto(decimal Score, string? Feedback);
public record ApprovalDto(bool Approve, string? Remarks);
public record DecisionDto(bool Selected, string? Remarks);
public record AppointmentDto(decimal OfferedGrossSalary, DateOnly JoiningDate, int? DepartmentId, int? DesignationId);
public record JoiningDto(DateOnly ActualJoiningDate, string? Remarks);

/// <summary>
/// Employee selection workflow:
/// circular -> CV drop -> shortlist -> interview -> merit list -> candidate approval ->
/// final selection / rejection -> appointment letter -> joining letter -> employee.
/// </summary>
public class RecruitmentService
{
    private readonly HrmDbContext _db;
    public RecruitmentService(HrmDbContext db) => _db = db;

    // ---- i. circular -------------------------------------------------------------------------

    public async Task<JobCircular> PublishCircularAsync(int id)
    {
        var c = await GetCircularAsync(id);
        if (c.Status != CircularStatus.Draft) throw new InvalidOperationException("Only draft circulars can be published.");
        if (c.Deadline < DateOnly.FromDateTime(DateTime.Today)) throw new InvalidOperationException("Deadline is in the past.");
        c.Status = CircularStatus.Published;
        c.PublishDate = DateOnly.FromDateTime(DateTime.Today);
        await _db.SaveChangesAsync();
        return c;
    }

    public async Task<JobCircular> CloseCircularAsync(int id)
    {
        var c = await GetCircularAsync(id);
        if (c.Status != CircularStatus.Published) throw new InvalidOperationException("Only published circulars can be closed.");
        c.Status = CircularStatus.Closed;
        await _db.SaveChangesAsync();
        return c;
    }

    // ---- ii. CV drop -------------------------------------------------------------------------

    public async Task<JobApplication> ApplyAsync(int circularId, JobApplication app)
    {
        var c = await GetCircularAsync(circularId);
        if (c.Status != CircularStatus.Published) throw new InvalidOperationException("This circular is not open for applications.");
        if (c.Deadline < DateOnly.FromDateTime(DateTime.Today)) throw new InvalidOperationException("The application deadline has passed.");
        if (await _db.JobApplications.AnyAsync(a => a.JobCircularId == circularId && a.Email == app.Email))
            throw new InvalidOperationException("An application with this email already exists for this circular.");

        app.JobCircularId = circularId;
        app.Status = ApplicationStatus.Applied;
        app.AppliedOn = DateTime.UtcNow;
        _db.JobApplications.Add(app);
        await _db.SaveChangesAsync();
        return app;
    }

    public async Task ShortlistAsync(IEnumerable<int> ids)
    {
        var apps = await _db.JobApplications.Where(a => ids.Contains(a.Id)).ToListAsync();
        foreach (var a in apps)
        {
            Require(a, ApplicationStatus.Applied);
            a.Status = ApplicationStatus.Shortlisted;
        }
        await _db.SaveChangesAsync();
    }

    // ---- iii. interview ----------------------------------------------------------------------

    public async Task<Interview> ScheduleInterviewAsync(int applicationId, InterviewDto dto)
    {
        var a = await GetApplicationAsync(applicationId);
        Require(a, ApplicationStatus.Shortlisted, ApplicationStatus.Interviewed);
        if (await _db.Interviews.AnyAsync(i => i.JobApplicationId == a.Id && !i.IsCompleted))
            throw new InvalidOperationException("Record the result of the pending interview first.");

        var round = dto.Round > 0 ? dto.Round : (await _db.Interviews.CountAsync(i => i.JobApplicationId == a.Id)) + 1;
        var iv = new Interview
        {
            JobApplicationId = a.Id, Round = round, ScheduledAt = dto.ScheduledAt, Panel = dto.Panel, Venue = dto.Venue
        };
        _db.Interviews.Add(iv);
        await _db.SaveChangesAsync();
        return iv;
    }

    public async Task<Interview> RecordInterviewAsync(int interviewId, InterviewResultDto dto)
    {
        if (dto.Score < 0 || dto.Score > 100) throw new InvalidOperationException("Score must be between 0 and 100.");
        var iv = await _db.Interviews.FindAsync(interviewId) ?? throw new KeyNotFoundException("Interview not found");
        var a = await GetApplicationAsync(iv.JobApplicationId);
        Require(a, ApplicationStatus.Shortlisted, ApplicationStatus.Interviewed);

        iv.IsCompleted = true;
        iv.Score = dto.Score;
        iv.Feedback = dto.Feedback;
        await _db.SaveChangesAsync();

        var scores = await _db.Interviews
            .Where(i => i.JobApplicationId == a.Id && i.IsCompleted && i.Score != null)
            .Select(i => i.Score!.Value).ToListAsync();
        a.TotalScore = Math.Round(scores.Average(), 2);
        a.Status = ApplicationStatus.Interviewed;
        await _db.SaveChangesAsync();
        return iv;
    }

    // ---- iv. merit list + candidate approval -------------------------------------------------

    /// <summary>Ranks every interviewed candidate of a circular by average interview score.</summary>
    public async Task<List<JobApplication>> BuildMeritListAsync(int circularId)
    {
        var apps = await _db.JobApplications
            .Where(a => a.JobCircularId == circularId &&
                        (a.Status == ApplicationStatus.Interviewed || a.Status == ApplicationStatus.MeritListed))
            .OrderByDescending(a => a.TotalScore).ThenBy(a => a.AppliedOn).ToListAsync();
        if (apps.Count == 0) throw new InvalidOperationException("No interviewed candidates to rank.");

        var rank = 1;
        foreach (var a in apps)
        {
            a.MeritRank = rank++;
            a.Status = ApplicationStatus.MeritListed;
        }
        await _db.SaveChangesAsync();
        return apps;
    }

    public async Task<JobApplication> ApproveCandidateAsync(int applicationId, ApprovalDto dto)
    {
        var a = await GetApplicationAsync(applicationId);
        Require(a, ApplicationStatus.MeritListed);
        a.Status = dto.Approve ? ApplicationStatus.Approved : ApplicationStatus.Rejected;
        a.ApprovalRemarks = dto.Remarks;
        a.ApprovedOn = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return a;
    }

    // ---- v. final selection / rejection ------------------------------------------------------

    public async Task<JobApplication> FinalDecisionAsync(int applicationId, DecisionDto dto)
    {
        var a = await GetApplicationAsync(applicationId);
        Require(a, ApplicationStatus.Approved);

        if (dto.Selected)
        {
            var c = await GetCircularAsync(a.JobCircularId);
            var taken = await _db.JobApplications.CountAsync(x =>
                x.JobCircularId == c.Id &&
                (x.Status == ApplicationStatus.Selected || x.Status == ApplicationStatus.Appointed ||
                 x.Status == ApplicationStatus.JoiningSubmitted || x.Status == ApplicationStatus.Joined));
            if (taken >= c.Vacancies)
                throw new InvalidOperationException($"All {c.Vacancies} vacancy/vacancies are already filled.");
        }

        a.Status = dto.Selected ? ApplicationStatus.Selected : ApplicationStatus.Rejected;
        a.DecisionRemarks = dto.Remarks;
        a.DecisionOn = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return a;
    }

    // ---- vi. appointment letter --------------------------------------------------------------

    public async Task<AppointmentLetter> IssueAppointmentAsync(int applicationId, AppointmentDto dto)
    {
        var a = await GetApplicationAsync(applicationId);
        Require(a, ApplicationStatus.Selected);
        if (dto.OfferedGrossSalary <= 0) throw new InvalidOperationException("Offered salary must be greater than zero.");

        var c = await GetCircularAsync(a.JobCircularId);
        var deptId = dto.DepartmentId ?? c.DepartmentId;
        var desigId = dto.DesignationId ?? c.DesignationId;
        var dept = deptId.HasValue ? (await _db.Departments.FindAsync(deptId.Value))?.Name : null;
        var desig = desigId.HasValue ? (await _db.Designations.FindAsync(desigId.Value))?.Name : null;
        var today = DateOnly.FromDateTime(DateTime.Today);

        var letter = new AppointmentLetter
        {
            JobApplicationId = a.Id,
            ReferenceNo = $"APT-{today.Year}-{a.Id:0000}",
            IssueDate = today,
            DepartmentId = deptId,
            DesignationId = desigId,
            OfferedGrossSalary = dto.OfferedGrossSalary,
            JoiningDate = dto.JoiningDate,
            Body =
$@"Date: {today:dd MMM yyyy}
Ref: APT-{today.Year}-{a.Id:0000}

Dear {a.FullName},

We are pleased to offer you the position of {desig ?? c.Title}{(dept != null ? " in the " + dept + " department" : "")}.

Gross monthly salary: {dto.OfferedGrossSalary:N2}
Date of joining: {dto.JoiningDate:dd MMM yyyy}

Please sign and return the joining letter on or before your joining date. This offer is subject to verification of your documents and the company's policies.

Human Resources"
        };
        _db.AppointmentLetters.Add(letter);
        a.Status = ApplicationStatus.Appointed;
        await _db.SaveChangesAsync();
        return letter;
    }

    // ---- vii. joining letter -----------------------------------------------------------------

    public async Task<JoiningLetter> SubmitJoiningAsync(int applicationId, JoiningDto dto)
    {
        var a = await GetApplicationAsync(applicationId);
        Require(a, ApplicationStatus.Appointed);
        var j = new JoiningLetter { JobApplicationId = a.Id, ActualJoiningDate = dto.ActualJoiningDate, Remarks = dto.Remarks };
        _db.JoiningLetters.Add(j);
        a.Status = ApplicationStatus.JoiningSubmitted;
        await _db.SaveChangesAsync();
        return j;
    }

    // ---- viii. add as employee ---------------------------------------------------------------

    public async Task<Employee> AddAsEmployeeAsync(int applicationId)
    {
        var a = await _db.JobApplications
            .Include(x => x.Appointment).Include(x => x.Joining)
            .FirstOrDefaultAsync(x => x.Id == applicationId) ?? throw new KeyNotFoundException("Application not found");
        Require(a, ApplicationStatus.JoiningSubmitted);
        var letter = a.Appointment ?? throw new InvalidOperationException("Appointment letter missing.");
        var joining = a.Joining ?? throw new InvalidOperationException("Joining letter missing.");

        var shiftId = (await _db.Shifts.OrderBy(s => s.Id).FirstOrDefaultAsync())?.Id;
        var emp = new Employee
        {
            EmployeeCode = await NextCodeAsync(),
            FullName = a.FullName,
            Email = a.Email,
            Phone = a.Phone,
            Gender = a.Gender,
            DateOfBirth = a.DateOfBirth,
            Address = a.Address,
            DepartmentId = letter.DepartmentId,
            DesignationId = letter.DesignationId,
            ShiftId = shiftId,
            JoiningDate = joining.ActualJoiningDate,
            Status = EmploymentStatus.Probation,
            JobApplicationId = a.Id
        };
        _db.Employees.Add(emp);
        await _db.SaveChangesAsync();

        // Default split of the offered gross; adjust later on the Employees page.
        var g = letter.OfferedGrossSalary;
        _db.SalaryStructures.Add(new SalaryStructure
        {
            EmployeeId = emp.Id,
            Basic = Math.Round(g * 0.60m, 2),
            HouseRent = Math.Round(g * 0.20m, 2),
            Medical = Math.Round(g * 0.10m, 2),
            Conveyance = Math.Round(g * 0.10m, 2),
            EffectiveFrom = joining.ActualJoiningDate
        });
        joining.EmployeeId = emp.Id;
        a.Status = ApplicationStatus.Joined;
        await _db.SaveChangesAsync();
        return emp;
    }

    // ---- helpers -----------------------------------------------------------------------------

    private async Task<string> NextCodeAsync()
    {
        var n = await _db.Employees.CountAsync() + 1;
        var code = $"EMP-{n:00000}";
        while (await _db.Employees.AnyAsync(e => e.EmployeeCode == code))
            code = $"EMP-{++n:00000}";
        return code;
    }

    private async Task<JobCircular> GetCircularAsync(int id) =>
        await _db.JobCirculars.FindAsync(id) ?? throw new KeyNotFoundException("Circular not found");

    private async Task<JobApplication> GetApplicationAsync(int id) =>
        await _db.JobApplications.FindAsync(id) ?? throw new KeyNotFoundException("Application not found");

    private static void Require(JobApplication a, params ApplicationStatus[] allowed)
    {
        if (!allowed.Contains(a.Status))
            throw new InvalidOperationException(
                $"Candidate {a.FullName} is '{a.Status}'; this step needs: {string.Join(" / ", allowed)}.");
    }
}
