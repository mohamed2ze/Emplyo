namespace Emplyo.Application.Common.Interfaces;

public class AppUserInfo
{
    public string Id { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? UserName { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int? EmployeeId { get; set; }
    public DateTime? LastLoginUtc { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}

public class CreateUserResult
{
    public bool Succeeded { get; init; }
    public string? UserId { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public interface IIdentityService
{
    Task<CreateUserResult> CreateUserAsync(string email, string displayName, string password,
        IReadOnlyCollection<string> roles, int? employeeId, CancellationToken ct = default);

    Task<AppUserInfo?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<AppUserInfo?> FindByIdAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<AppUserInfo>> SearchUsersAsync(string? query, CancellationToken ct = default);
    Task<IReadOnlyList<AppUserInfo>> GetUsersInRoleAsync(string role, CancellationToken ct = default);

    Task SetRolesAsync(string userId, IReadOnlyCollection<string> roles, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct = default);
    Task<bool> ResetPasswordAsync(string userId, string newPassword, CancellationToken ct = default);
    Task SetActiveAsync(string userId, bool isActive, CancellationToken ct = default);
    Task<bool> AddToRoleAsync(string userId, string role, CancellationToken ct = default);
    Task<bool> HasAnyUserAsync(CancellationToken ct = default);
    Task<string?> GeneratePasswordResetTokenAsync(string userId, CancellationToken ct = default);
    Task<bool> ResetPasswordWithTokenAsync(string userId, string token, string newPassword, CancellationToken ct = default);
    Task RecordLoginAsync(string userId, CancellationToken ct = default);
}

public interface IAppEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
    bool IsEnabled { get; }
}
