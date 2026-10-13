using Emplyo.Application.Common.Interfaces;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Emplyo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.UnitTests.TestSupport;

public static class TestDb
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"emplyo-unit-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }
}

public class FakeClock : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);
}

public class FakeCurrentUser : ICurrentUserService
{
    public string? UserId { get; set; } = "user-1";
    public string DisplayName { get; set; } = "Test User";
    public int? EmployeeId { get; set; }
    public bool IsAuthenticated { get; set; } = true;
    public string[] RoleList { get; set; } = Array.Empty<string>();
    public IReadOnlyCollection<string> Roles => RoleList;
}

public class FakeAudit : IAuditService
{
    public List<string> Recorded { get; } = new();
    public Task RecordAsync(Emplyo.Application.Common.Models.AuditEntry entry, CancellationToken ct = default)
    {
        Recorded.Add(entry.Action);
        return Task.CompletedTask;
    }
}

public class FakeNotifications : INotificationService
{
    public Task NotifyUserAsync(string userId, string title, string message, string? url = null, NotificationType type = NotificationType.General, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task NotifyRoleAsync(string role, string title, string message, string? url = null, NotificationType type = NotificationType.General, CancellationToken ct = default)
        => Task.CompletedTask;
}

public class FakeSettings : IAppSettingsService
{
    public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task<string> GetAsync(string key, string defaultValue, CancellationToken ct = default) => Task.FromResult(defaultValue);
    public Task SetAsync(string key, string value, string? description = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task<TimeZoneInfo> GetOrganizationTimeZoneAsync(CancellationToken ct = default) => Task.FromResult(TimeZoneInfo.Utc);
    public Task<IReadOnlyList<(string Key, string Value, string? Description)>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<(string, string, string?)>>(Array.Empty<(string, string, string?)>());
}

public static class Seed
{
    public static async Task<LeaveType> LeaveTypeAsync(AppDbContext db, string name = "Annual Leave", decimal entitlement = 20, bool isPaid = true)
    {
        var type = new LeaveType { Name = name, IsPaid = isPaid, DefaultAnnualEntitlement = entitlement, IsActive = true };
        db.LeaveTypes.Add(type);
        await db.SaveChangesAsync();
        return type;
    }

    public static async Task<Employee> EmployeeAsync(AppDbContext db, string number = "EMP-0001", string email = "e1@test.local",
        EmploymentStatus status = EmploymentStatus.Active)
    {
        var employee = new Employee
        {
            EmployeeNumber = number,
            FirstName = "Test",
            LastName = "Employee",
            WorkEmail = email,
            EmploymentStatus = status,
            HireDate = new DateOnly(2024, 1, 1)
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee;
    }
}
