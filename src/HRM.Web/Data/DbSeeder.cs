using HRM.Web.Models;

namespace HRM.Web.Data;

public static class DbSeeder
{
    public static void Seed(HrmDbContext db)
    {
        if (db.Departments.Any()) return;

        db.Departments.AddRange(
            new Department { Name = "Human Resources" },
            new Department { Name = "Engineering" },
            new Department { Name = "Accounts & Finance" },
            new Department { Name = "Sales & Marketing" },
            new Department { Name = "Operations" });

        db.Designations.AddRange(
            new Designation { Name = "Manager" },
            new Designation { Name = "Senior Executive" },
            new Designation { Name = "Executive" },
            new Designation { Name = "Software Engineer" },
            new Designation { Name = "Accountant" });

        db.Shifts.Add(new Shift
        {
            Name = "General (09:00 - 18:00)",
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(18, 0),
            GraceMinutes = 10,
            HalfDayHours = 4
        });

        // Fixed-date national holidays. Add movable (Eid, Puja etc.) holidays from the UI/API each year.
        var year = DateTime.Today.Year;
        db.Holidays.AddRange(
            new Holiday { Date = new DateOnly(year, 2, 21), Name = "International Mother Language Day" },
            new Holiday { Date = new DateOnly(year, 3, 26), Name = "Independence Day" },
            new Holiday { Date = new DateOnly(year, 4, 14), Name = "Pohela Boishakh" },
            new Holiday { Date = new DateOnly(year, 5, 1), Name = "May Day" },
            new Holiday { Date = new DateOnly(year, 12, 16), Name = "Victory Day" },
            new Holiday { Date = new DateOnly(year, 12, 25), Name = "Christmas Day" });

        // Defaults follow the common Bangladesh Labour Act style rules; edit them via /api/leave/policies.
        db.LeavePolicies.AddRange(
            new LeavePolicy { LeaveType = LeaveType.Casual, DaysPerYear = 10, ProRateFirstYear = true, Description = "Casual leave, 10 days a year" },
            new LeavePolicy { LeaveType = LeaveType.Sick, DaysPerYear = 14, ProRateFirstYear = true, Description = "Sick leave, 14 days a year" },
            new LeavePolicy { LeaveType = LeaveType.Earned, WorkDaysPerLeave = 18, MaxAccumulate = 60, EligibleAfterMonths = 12, Description = "Earned leave, 1 day per 18 days worked, usable after 12 months" },
            new LeavePolicy { LeaveType = LeaveType.Maternity, DaysPerYear = 112, EligibleAfterMonths = 6, Description = "Maternity leave, 112 days (female staff)" },
            new LeavePolicy { LeaveType = LeaveType.Paternity, DaysPerYear = 7, Description = "Paternity leave, 7 days (male staff)" },
            new LeavePolicy { LeaveType = LeaveType.Unpaid, DaysPerYear = 0, Description = "Leave without pay (no limit, deducted in payroll)" });

        db.KpiTemplates.AddRange(
            new KpiTemplate { Name = "Quality of work", Weight = 30, Description = "Accuracy and completeness of output" },
            new KpiTemplate { Name = "Productivity", Weight = 25, Description = "Volume and timeliness of delivery" },
            new KpiTemplate { Name = "Teamwork & communication", Weight = 20 },
            new KpiTemplate { Name = "Initiative & problem solving", Weight = 15 },
            new KpiTemplate { Name = "Discipline & compliance", Weight = 10 });

        db.ReviewCycles.Add(new ReviewCycle
        {
            Name = $"{year} Annual Review",
            StartDate = new DateOnly(year, 1, 1),
            EndDate = new DateOnly(year, 12, 31)
        });

        db.SaveChanges();

        var hr = db.Departments.First(d => d.Name == "Human Resources");
        var eng = db.Departments.First(d => d.Name == "Engineering");
        var mgr = db.Designations.First(d => d.Name == "Manager");
        var swe = db.Designations.First(d => d.Name == "Software Engineer");
        var shift = db.Shifts.First();

        var e1 = new Employee
        {
            EmployeeCode = "EMP-00001", FullName = "Rahim Uddin", Email = "rahim@example.com", Gender = Gender.Male,
            DepartmentId = hr.Id, DesignationId = mgr.Id, ShiftId = shift.Id,
            JoiningDate = new DateOnly(year - 3, 1, 1), Status = EmploymentStatus.Confirmed, WeeklyOffDays = "Friday"
        };
        var e2 = new Employee
        {
            EmployeeCode = "EMP-00002", FullName = "Nusrat Jahan", Email = "nusrat@example.com", Gender = Gender.Female,
            DepartmentId = eng.Id, DesignationId = swe.Id, ShiftId = shift.Id,
            JoiningDate = new DateOnly(year - 1, 3, 1), Status = EmploymentStatus.Confirmed, WeeklyOffDays = "Friday,Saturday"
        };
        db.Employees.AddRange(e1, e2);
        db.SaveChanges();

        db.SalaryStructures.AddRange(
            new SalaryStructure { EmployeeId = e1.Id, Basic = 42000, HouseRent = 21000, Medical = 4000, Conveyance = 3000, ProvidentFundPercent = 5, MonthlyTax = 1500, EffectiveFrom = e1.JoiningDate },
            new SalaryStructure { EmployeeId = e2.Id, Basic = 30000, HouseRent = 15000, Medical = 3000, Conveyance = 2000, ProvidentFundPercent = 5, MonthlyTax = 0, EffectiveFrom = e2.JoiningDate });
        db.SaveChanges();
    }
}
