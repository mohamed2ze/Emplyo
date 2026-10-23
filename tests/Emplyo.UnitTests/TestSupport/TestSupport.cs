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

public record NotificationCall(string? UserId, string? Role, string Title, string Message, string? Url, NotificationType Type);

public class FakeNotifications : INotificationService
{
    public List<NotificationCall> Calls { get; } = new();

    public Task NotifyUserAsync(string userId, string title, string message, string? url = null,
        NotificationType type = NotificationType.General, CancellationToken ct = default)
    {
        Calls.Add(new NotificationCall(userId, null, title, message, url, type));
        return Task.CompletedTask;
    }

    public Task NotifyRoleAsync(string role, string title, string message, string? url = null,
        NotificationType type = NotificationType.General, CancellationToken ct = default)
    {
        Calls.Add(new NotificationCall(null, role, title, message, url, type));
        return Task.CompletedTask;
    }
}

public class FakeSettings : IAppSettingsService
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    public FakeSettings With(string key, string value)
    {
        _values[key] = value;
        return this;
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        if (_values.TryGetValue(key, out var value)) return Task.FromResult<string?>(value);
        return Task.FromResult<string?>(key switch
        {
            "Organization.TimeZone" => "UTC",
            "Organization.Name" => "Emplyo",
            "Attendance.WorkDayStart" => "09:00",
            _ => null
        });
    }

    public async Task<string> GetAsync(string key, string defaultValue, CancellationToken ct = default)
        => await GetAsync(key, ct) ?? defaultValue;

    public Task SetAsync(string key, string value, string? description = null, CancellationToken ct = default)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task<TimeZoneInfo> GetOrganizationTimeZoneAsync(CancellationToken ct = default)
        => Task.FromResult(TimeZone);

    public Task<IReadOnlyList<(string Key, string Value, string? Description)>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<(string, string, string?)>>(
            _values.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => (kv.Key, kv.Value, (string?)null)).ToList());
}

public class FakeFileStorage : IFileStorageService
{
    private int _counter;

    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
    public List<string> Deleted { get; } = new();
    public List<string> SavedOriginalNames { get; } = new();
    public bool ThrowOnDelete { get; set; }

    public async Task<string> SaveAsync(Stream content, string originalFileName, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var storedName = $"stored-{++_counter}{Path.GetExtension(originalFileName).ToLowerInvariant()}";
        Files[storedName] = buffer.ToArray();
        SavedOriginalNames.Add(originalFileName);
        return storedName;
    }

    public Stream OpenRead(string storedFileName)
    {
        var name = Path.GetFileName(storedFileName);
        if (!Files.TryGetValue(name, out var bytes))
            throw new FileNotFoundException("Stored document was not found.", name);
        return new MemoryStream(bytes, writable: false);
    }

    public void Delete(string storedFileName)
    {
        if (ThrowOnDelete) throw new IOException("Simulated storage failure.");
        var name = Path.GetFileName(storedFileName);
        Deleted.Add(name);
        Files.Remove(name);
    }

    public bool Exists(string storedFileName)
        => Files.ContainsKey(Path.GetFileName(storedFileName));
}

public class FakeIdentityService : IIdentityService
{
    public Dictionary<string, AppUserInfo> Users { get; } = new(StringComparer.Ordinal);
    public int CreateUserCalls { get; private set; }
    public bool ResetPasswordResult { get; set; } = true;

    public AppUserInfo AddUser(string id, string email, string displayName, bool isActive, params string[] roles)
    {
        var user = new AppUserInfo
        {
            Id = id,
            Email = email,
            UserName = email,
            DisplayName = displayName,
            IsActive = isActive,
            Roles = roles.ToList()
        };
        Users[id] = user;
        return user;
    }

    public Task<CreateUserResult> CreateUserAsync(string email, string displayName, string password,
        IReadOnlyCollection<string> roles, int? employeeId, CancellationToken ct = default)
    {
        CreateUserCalls++;
        if (Users.Values.Any(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(new CreateUserResult { Succeeded = false, Errors = new[] { "A user with this email already exists." } });

        var id = Guid.NewGuid().ToString("N");
        AddUser(id, email, displayName, true, roles.ToArray());
        Users[id].EmployeeId = employeeId;
        return Task.FromResult(new CreateUserResult { Succeeded = true, UserId = id });
    }

    public Task<AppUserInfo?> FindByIdAsync(string userId, CancellationToken ct = default)
        => Task.FromResult(Users.TryGetValue(userId, out var user) ? user : null);

    public Task<IReadOnlyList<AppUserInfo>> SearchUsersAsync(string? query, CancellationToken ct = default)
    {
        var users = Users.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            users = users.Where(u => (u.Email?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || u.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        return Task.FromResult<IReadOnlyList<AppUserInfo>>(users.OrderBy(u => u.DisplayName).ToList());
    }

    public Task<IReadOnlyList<AppUserInfo>> GetUsersInRoleAsync(string role, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AppUserInfo>>(
            Users.Values.Where(u => u.IsActive && u.Roles.Contains(role)).ToList());

    public Task SetRolesAsync(string userId, IReadOnlyCollection<string> roles, CancellationToken ct = default)
    {
        if (Users.TryGetValue(userId, out var user)) user.Roles = roles.ToList();
        return Task.CompletedTask;
    }

    public Task<bool> ResetPasswordAsync(string userId, string newPassword, CancellationToken ct = default)
        => Task.FromResult(ResetPasswordResult && Users.ContainsKey(userId));

    public Task SetActiveAsync(string userId, bool isActive, CancellationToken ct = default)
    {
        if (Users.TryGetValue(userId, out var user)) user.IsActive = isActive;
        return Task.CompletedTask;
    }

    public Task RecordLoginAsync(string userId, CancellationToken ct = default)
    {
        if (Users.TryGetValue(userId, out var user)) user.LastLoginUtc = DateTime.UtcNow;
        return Task.CompletedTask;
    }
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

    public static async Task<LeaveType> InactiveLeaveTypeAsync(AppDbContext db, string name = "Old Leave", decimal entitlement = 5)
    {
        var type = new LeaveType { Name = name, IsPaid = true, DefaultAnnualEntitlement = entitlement, IsActive = false };
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

    public static async Task<Department> DepartmentAsync(AppDbContext db, string name = "Engineering", bool isActive = true)
    {
        var department = new Department { Name = name, IsActive = isActive };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        return department;
    }

    public static async Task<Position> PositionAsync(AppDbContext db, string title = "Software Engineer", int? departmentId = null, bool isActive = true)
    {
        var position = new Position { Title = title, DepartmentId = departmentId, IsActive = isActive };
        db.Positions.Add(position);
        await db.SaveChangesAsync();
        return position;
    }

    public static async Task<LeaveBalance> BalanceAsync(AppDbContext db, int employeeId, int leaveTypeId, int year,
        decimal entitlement, decimal used = 0m)
    {
        var balance = new LeaveBalance
        {
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            Year = year,
            Entitlement = entitlement,
            Used = used
        };
        db.LeaveBalances.Add(balance);
        await db.SaveChangesAsync();
        return balance;
    }

    public static async Task<Holiday> HolidayAsync(AppDbContext db, DateOnly date, string name = "Holiday", bool recurring = false)
    {
        var holiday = new Holiday { Name = name, Date = date, IsRecurringAnnually = recurring };
        db.Holidays.Add(holiday);
        await db.SaveChangesAsync();
        return holiday;
    }

    public static async Task<LeaveRequest> LeaveRequestAsync(AppDbContext db, int employeeId, int leaveTypeId,
        DateOnly start, DateOnly end, LeaveRequestStatus status, decimal totalDays = 1m)
    {
        var request = new LeaveRequest
        {
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            StartDate = start,
            EndDate = end,
            TotalDays = totalDays,
            Status = status,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }
}
