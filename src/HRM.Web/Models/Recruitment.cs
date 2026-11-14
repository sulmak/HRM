using System.ComponentModel.DataAnnotations;

namespace HRM.Web.Models;

public class JobCircular
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public int? DesignationId { get; set; }
    public Designation? Designation { get; set; }
    public int Vacancies { get; set; } = 1;
    [MaxLength(200)] public string? Location { get; set; }
    public string? Description { get; set; }
    public string? Requirements { get; set; }
    [MaxLength(100)] public string? SalaryRange { get; set; }
    public DateOnly? PublishDate { get; set; }
    public DateOnly Deadline { get; set; }
    public CircularStatus Status { get; set; } = CircularStatus.Draft;
    public List<JobApplication> Applications { get; set; } = new();
}

public class JobApplication
{
    public int Id { get; set; }
    public int JobCircularId { get; set; }
    public JobCircular? JobCircular { get; set; }

    [Required, MaxLength(150)] public string FullName { get; set; } = "";
    [Required, MaxLength(150)] public string Email { get; set; } = "";
    [MaxLength(30)] public string? Phone { get; set; }
    public Gender Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    [MaxLength(300)] public string? Address { get; set; }
    public decimal ExpectedSalary { get; set; }

    [MaxLength(300)] public string? CvFilePath { get; set; }
    [MaxLength(200)] public string? CvFileName { get; set; }
    public DateTime AppliedOn { get; set; } = DateTime.UtcNow;

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Applied;
    public decimal? TotalScore { get; set; }
    public int? MeritRank { get; set; }

    [MaxLength(500)] public string? ApprovalRemarks { get; set; }
    public DateTime? ApprovedOn { get; set; }
    [MaxLength(500)] public string? DecisionRemarks { get; set; }
    public DateTime? DecisionOn { get; set; }

    public List<Interview> Interviews { get; set; } = new();
    public AppointmentLetter? Appointment { get; set; }
    public JoiningLetter? Joining { get; set; }
}

public class Interview
{
    public int Id { get; set; }
    public int JobApplicationId { get; set; }
    public JobApplication? JobApplication { get; set; }
    public int Round { get; set; } = 1;
    public DateTime ScheduledAt { get; set; }
    [MaxLength(200)] public string? Panel { get; set; }
    [MaxLength(200)] public string? Venue { get; set; }
    public bool IsCompleted { get; set; }
    /// <summary>0-100</summary>
    public decimal? Score { get; set; }
    [MaxLength(1000)] public string? Feedback { get; set; }
}

public class AppointmentLetter
{
    public int Id { get; set; }
    public int JobApplicationId { get; set; }
    public JobApplication? JobApplication { get; set; }
    [MaxLength(50)] public string ReferenceNo { get; set; } = "";
    public DateOnly IssueDate { get; set; }
    public int? DepartmentId { get; set; }
    public int? DesignationId { get; set; }
    public decimal OfferedGrossSalary { get; set; }
    public DateOnly JoiningDate { get; set; }
    public string Body { get; set; } = "";
}

public class JoiningLetter
{
    public int Id { get; set; }
    public int JobApplicationId { get; set; }
    public JobApplication? JobApplication { get; set; }
    public DateOnly ActualJoiningDate { get; set; }
    public DateTime SubmittedOn { get; set; } = DateTime.UtcNow;
    [MaxLength(500)] public string? Remarks { get; set; }
    public int? EmployeeId { get; set; }
}
