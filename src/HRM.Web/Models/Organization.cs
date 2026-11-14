using System.ComponentModel.DataAnnotations;

namespace HRM.Web.Models;

public class Department
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
}

public class Designation
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
}

public class Shift
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    /// <summary>Minutes after StartTime before an arrival counts as late.</summary>
    public int GraceMinutes { get; set; } = 10;
    /// <summary>Worked hours below this on a working day make it a half day.</summary>
    public decimal HalfDayHours { get; set; } = 4;
}

public class Holiday
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    public bool IsOptional { get; set; }
}
