using Emplyo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Employee> Employees { get; }
    DbSet<Department> Departments { get; }
    DbSet<Position> Positions { get; }
    DbSet<LeaveType> LeaveTypes { get; }
    DbSet<LeaveBalance> LeaveBalances { get; }
    DbSet<LeaveRequest> LeaveRequests { get; }
    DbSet<AttendanceRecord> AttendanceRecords { get; }
    DbSet<Holiday> Holidays { get; }
    DbSet<Announcement> Announcements { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<EmployeeDocument> EmployeeDocuments { get; }
    DbSet<OnboardingTask> OnboardingTasks { get; }
    DbSet<OffboardingCase> OffboardingCases { get; }
    DbSet<OffboardingTask> OffboardingTasks { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<AppSetting> AppSettings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
