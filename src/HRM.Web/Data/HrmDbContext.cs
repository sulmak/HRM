using HRM.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HRM.Web.Data;

public class HrmDbContext : DbContext
{
    public HrmDbContext(DbContextOptions<HrmDbContext> options) : base(options) { }

    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Designation> Designations => Set<Designation>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<SalaryStructure> SalaryStructures => Set<SalaryStructure>();

    public DbSet<JobCircular> JobCirculars => Set<JobCircular>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<Interview> Interviews => Set<Interview>();
    public DbSet<AppointmentLetter> AppointmentLetters => Set<AppointmentLetter>();
    public DbSet<JoiningLetter> JoiningLetters => Set<JoiningLetter>();

    public DbSet<LeavePolicy> LeavePolicies => Set<LeavePolicy>();
    public DbSet<LeaveApplication> LeaveApplications => Set<LeaveApplication>();
    public DbSet<AttendanceLog> AttendanceLogs => Set<AttendanceLog>();
    public DbSet<AttendanceDaily> AttendanceDailies => Set<AttendanceDaily>();

    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<Payslip> Payslips => Set<Payslip>();

    public DbSet<ReviewCycle> ReviewCycles => Set<ReviewCycle>();
    public DbSet<KpiTemplate> KpiTemplates => Set<KpiTemplate>();
    public DbSet<PerformanceReview> PerformanceReviews => Set<PerformanceReview>();
    public DbSet<PerformanceReviewItem> PerformanceReviewItems => Set<PerformanceReviewItem>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Employee>().HasIndex(e => e.EmployeeCode).IsUnique();
        m.Entity<Holiday>().HasIndex(h => h.Date).IsUnique();
        m.Entity<LeavePolicy>().HasIndex(p => p.LeaveType).IsUnique();
        m.Entity<AttendanceLog>().HasIndex(a => new { a.EmployeeId, a.Date }).IsUnique();
        m.Entity<AttendanceDaily>().HasIndex(a => new { a.EmployeeId, a.Date }).IsUnique();
        m.Entity<PayrollRun>().HasIndex(r => new { r.Year, r.Month }).IsUnique();
        m.Entity<Payslip>().HasIndex(p => new { p.PayrollRunId, p.EmployeeId }).IsUnique();
        m.Entity<PerformanceReview>().HasIndex(r => new { r.EmployeeId, r.ReviewCycleId }).IsUnique();
        m.Entity<AppointmentLetter>().HasIndex(a => a.JobApplicationId).IsUnique();
        m.Entity<JoiningLetter>().HasIndex(a => a.JobApplicationId).IsUnique();

        // Avoid SQL Server "multiple cascade paths" errors; children are removed explicitly in code.
        foreach (var fk in m.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
        {
            if (fk.DeleteBehavior == DeleteBehavior.Cascade)
                fk.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
