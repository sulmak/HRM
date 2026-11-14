using System.ComponentModel.DataAnnotations;

namespace HRM.Web.Models;

public class Employee
{
    public int Id { get; set; }
    [Required, MaxLength(30)] public string EmployeeCode { get; set; } = "";
    [Required, MaxLength(150)] public string FullName { get; set; } = "";
    [MaxLength(150)] public string? Email { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    public Gender Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    [MaxLength(30)] public string? NationalId { get; set; }
    [MaxLength(300)] public string? Address { get; set; }

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public int? DesignationId { get; set; }
    public Designation? Designation { get; set; }
    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }
    public int? ReportsToId { get; set; }

    public DateOnly JoiningDate { get; set; }
    public DateOnly? ConfirmationDate { get; set; }
    public DateOnly? LastWorkingDate { get; set; }
    public EmploymentStatus Status { get; set; } = EmploymentStatus.Probation;
    public bool IsActive { get; set; } = true;

    /// <summary>Comma separated day names, e.g. "Friday" or "Friday,Saturday".</summary>
    [MaxLength(100)] public string WeeklyOffDays { get; set; } = "Friday";

    /// <summary>Earned-leave balance brought forward from a legacy system.</summary>
    public decimal OpeningEarnLeave { get; set; }

    public int? JobApplicationId { get; set; }
}

public class SalaryStructure
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public decimal Basic { get; set; }
    public decimal HouseRent { get; set; }
    public decimal Medical { get; set; }
    public decimal Conveyance { get; set; }
    public decimal OtherAllowance { get; set; }
    public decimal ProvidentFundPercent { get; set; }   // % of basic, deducted
    public decimal MonthlyTax { get; set; }              // fixed monthly tax deduction
    public DateOnly EffectiveFrom { get; set; }

    public decimal Gross => Basic + HouseRent + Medical + Conveyance + OtherAllowance;
}
