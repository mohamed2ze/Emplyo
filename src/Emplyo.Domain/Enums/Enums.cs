namespace Emplyo.Domain.Enums;

public enum EmploymentStatus
{
    Pending = 0,
    Active = 1,
    OnLeave = 2,
    Suspended = 3,
    Terminated = 4
}

public enum EmploymentType
{
    FullTime = 0,
    PartTime = 1,
    Contract = 2,
    Intern = 3
}

public enum LeaveRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3
}

public enum AttendanceStatus
{
    Present = 0,
    Late = 1,
    Absent = 2,
    HalfDay = 3,
    Remote = 4,
    Holiday = 5,
    OnLeave = 6
}

public enum DocumentCategory
{
    Contract = 0,
    Identification = 1,
    Certificate = 2,
    Policy = 3,
    Payroll = 4,
    Other = 5
}

public enum TaskItemStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3
}

public enum AnnouncementAudience
{
    Everyone = 0,
    Administrators = 1,
    HrManagers = 2,
    Employees = 3,
    Department = 4
}

public enum NotificationType
{
    General = 0,
    LeaveSubmitted = 1,
    LeaveApproved = 2,
    LeaveRejected = 3,
    LeaveCancelled = 4,
    Onboarding = 5,
    Offboarding = 6,
    Announcement = 7
}

public enum AuditOutcome
{
    Success = 0,
    Failure = 1
}

public enum OffboardingStatus
{
    Initiated = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3
}
