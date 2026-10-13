namespace Emplyo.Application.Common.Interfaces;

public interface ICurrentUserService
{
    string? UserId { get; }
    string DisplayName { get; }
    int? EmployeeId { get; }
    bool IsAuthenticated { get; }
    IReadOnlyCollection<string> Roles { get; }
}

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}

public interface IAppSettingsService
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task<string> GetAsync(string key, string defaultValue, CancellationToken ct = default);
    Task SetAsync(string key, string value, string? description = null, CancellationToken ct = default);
    Task<TimeZoneInfo> GetOrganizationTimeZoneAsync(CancellationToken ct = default);
    Task<IReadOnlyList<(string Key, string Value, string? Description)>> GetAllAsync(CancellationToken ct = default);
}
