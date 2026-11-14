using System.ComponentModel.DataAnnotations;

namespace HRM.Web.Models;

public class LeavePolicy
{
    public int Id { get; set; }
    public LeaveType LeaveType { get; set; }
    /// <summary>Yearly entitlement in days. 0 means unlimited (Unpaid) or accrual based (Earned).</summary>
    public decimal DaysPerYear { get; set; }
    /// <summary>Earned leave only: 1 day of leave per this many days worked.</summary>
    public int WorkDaysPerLeave { get; set; }
    /// <summary>Earned leave only: cap on accumulated balance (0 = no cap).</summary>
    public decimal MaxAccumulate { get; set; }
    /// <summary>Months of service needed before the leave can be taken.</summary>
    public int EligibleAfterMonths { get; set; }
    /// <summary>Prorate the entitlement by remaining months in the joining year.</summary>
    public bool ProRateFirstYear { get; set; }
    [MaxLength(300)] public string? Description { get; set; }
}

public class LeaveApplication
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public LeaveType LeaveType { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    /// <summary>Counted leave days (weekly offs and holidays excluded where applicable).</summary>
    public decimal Days { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public DateTime AppliedOn { get; set; } = DateTime.UtcNow;
    public DateTime? DecisionOn { get; set; }
    [MaxLength(500)] public string? DecisionRemarks { get; set; }
}

/// <summary>Raw punch / manual attendance entry. One per employee per day.</summary>
public class AttendanceLog
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly? InTime { get; set; }
    public TimeOnly? OutTime { get; set; }
    [MaxLength(30)] public string Source { get; set; } = "Manual";
    /// <summary>The shift worked that day (rotating shifts). Null = the employee's default shift.</summary>
    public int? ShiftId { get; set; }
    [MaxLength(300)] public string? Remarks { get; set; }
}

/// <summary>Result of attendance processing. One per employee per day.</summary>
public class AttendanceDaily
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly Date { get; set; }
    public AttendanceStatus Status { get; set; }
    public TimeOnly? InTime { get; set; }
    public TimeOnly? OutTime { get; set; }
    public int LateMinutes { get; set; }
    public decimal WorkedHours { get; set; }
    public decimal OvertimeHours { get; set; }
    /// <summary>The shift this day was processed against; payroll reads late/overtime rules from it.</summary>
    public int? ShiftId { get; set; }
    public DateTime ProcessedOn { get; set; } = DateTime.UtcNow;
}
