namespace HRM.Web.Models;

public enum Gender { Male, Female, Other }
public enum EmploymentStatus { Probation, Confirmed, Resigned, Terminated }
public enum CircularStatus { Draft, Published, Closed }

/// <summary>Recruitment pipeline stages, in order.</summary>
public enum ApplicationStatus
{
    Applied,          // CV dropped
    Shortlisted,
    Interviewed,
    MeritListed,
    Approved,         // candidate approval
    Selected,         // final selection
    Rejected,
    Appointed,        // appointment letter issued
    JoiningSubmitted, // joining letter received
    Joined            // added as employee
}

public enum LeaveType { Casual, Sick, Earned, Maternity, Paternity, Unpaid }
public enum LeaveStatus { Pending, Approved, Rejected, Cancelled }
public enum AttendanceStatus { Present, Late, HalfDay, Absent, OnLeave, WeeklyOff, Holiday }
public enum PayrollStatus { Draft, Finalized, Paid }
public enum ReviewStatus { Draft, Submitted }
