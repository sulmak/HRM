using System.ComponentModel.DataAnnotations;

namespace HRM.Web.Models;

public class PayrollRun
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public PayrollStatus Status { get; set; } = PayrollStatus.Draft;
    public DateTime ProcessedOn { get; set; } = DateTime.UtcNow;
    public DateTime? FinalizedOn { get; set; }
    public DateTime? PaidOn { get; set; }
    public List<Payslip> Payslips { get; set; } = new();
}

public class Payslip
{
    public int Id { get; set; }
    public int PayrollRunId { get; set; }
    public PayrollRun? PayrollRun { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int DaysInMonth { get; set; }
    public decimal PayableDays { get; set; }
    public decimal AbsentDays { get; set; }
    public decimal UnpaidLeaveDays { get; set; }
    public int LateDays { get; set; }
    public decimal OvertimeHours { get; set; }

    public decimal Basic { get; set; }
    public decimal HouseRent { get; set; }
    public decimal Medical { get; set; }
    public decimal Conveyance { get; set; }
    public decimal OtherAllowance { get; set; }
    public decimal Gross { get; set; }
    public decimal OvertimePay { get; set; }
    public decimal TotalEarnings { get; set; }

    public decimal AbsentDeduction { get; set; }
    public decimal LateDeduction { get; set; }
    public decimal ProvidentFund { get; set; }
    public decimal Tax { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetPay { get; set; }
}

public class ReviewCycle
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsOpen { get; set; } = true;
}

public class KpiTemplate
{
    public int Id { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    [MaxLength(300)] public string? Description { get; set; }
    /// <summary>Percent weight inside the KPI part of the score.</summary>
    public decimal Weight { get; set; }
}

public class PerformanceReview
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public int ReviewCycleId { get; set; }
    public ReviewCycle? ReviewCycle { get; set; }
    [MaxLength(150)] public string? ReviewerName { get; set; }
    public decimal KpiScore { get; set; }          // 0-100
    public decimal AttendanceScore { get; set; }   // 0-100, derived from processed attendance
    public decimal OverallScore { get; set; }      // 0-100
    [MaxLength(40)] public string Rating { get; set; } = "";
    [MaxLength(1000)] public string? Comments { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Draft;
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public List<PerformanceReviewItem> Items { get; set; } = new();
}

public class PerformanceReviewItem
{
    public int Id { get; set; }
    public int PerformanceReviewId { get; set; }
    public PerformanceReview? PerformanceReview { get; set; }
    [Required, MaxLength(150)] public string KpiName { get; set; } = "";
    public decimal Weight { get; set; }
    /// <summary>1-5</summary>
    public decimal Score { get; set; }
}
