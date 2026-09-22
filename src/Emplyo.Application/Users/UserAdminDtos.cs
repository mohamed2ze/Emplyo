using System.ComponentModel.DataAnnotations;
using Emplyo.Application.Common.Models;

namespace Emplyo.Application.Users;

public class UserListItemDto
{
    public string Id { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    public bool IsActive { get; set; }
    public int? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public DateTime? LastLoginUtc { get; set; }
}

public class CreateUserDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(120)]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 12)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "Employee";

    [Display(Name = "Link employee")]
    public int? EmployeeId { get; set; }
}

public class EditUserDto
{
    [Required]
    public string Role { get; set; } = "Employee";

    [Display(Name = "Link employee")]
    public int? EmployeeId { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public class ResetUserPasswordDto
{
    [Required, StringLength(100, MinimumLength = 12)]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;
}

public interface IUserAdminService
{
    Task<PagedResult<UserListItemDto>> GetPagedAsync(string? search, string? role, int page, int pageSize, CancellationToken ct = default);
    Task<UserListItemDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<(bool Success, string? UserId, IReadOnlyList<string> Errors)> CreateAsync(CreateUserDto dto, CancellationToken ct = default);
    Task UpdateAsync(string id, EditUserDto dto, CancellationToken ct = default);
    Task ResetPasswordAsync(string id, ResetUserPasswordDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<(int Id, string Name, string Number)>> GetLinkableEmployeesAsync(string? excludeUserId = null, CancellationToken ct = default);
}
