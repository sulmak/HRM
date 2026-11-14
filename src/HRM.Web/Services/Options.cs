namespace HRM.Web.Services;

public class PayrollOptions
{
    /// <summary>Every N late arrivals cost one day's pay.</summary>
    public int LateDaysPerAbsent { get; set; } = 3;
    public decimal OvertimeMultiplier { get; set; } = 2m;
    /// <summary>Hourly overtime rate for a day = basic / this / that day's shift hours.</summary>
    public decimal WorkingDaysPerMonth { get; set; } = 26m;
}

public class PerformanceOptions
{
    public decimal KpiWeightPercent { get; set; } = 80m;
    public decimal AttendanceWeightPercent { get; set; } = 20m;
}
