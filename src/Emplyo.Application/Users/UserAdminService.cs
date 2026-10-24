using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Common.Models;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Application.Users;

public class UserAdminService : IUserAdminService
{
    public static readonly string[] AssignableRoles = { "Administrator", "HRManager", "Employee" };

    private readonly IIdentityService _identity;
    private readonly IApplicationDbContext _db;
    private readonly IAuditService _audit;

    public UserAdminService(IIdentityService identity, IApplicationDbContext db, IAuditService audit)
    {
        _identity = identity;
        _db = db;
        _audit = audit;
    }

    public async Task<PagedResult<UserListItemDto>> GetPagedAsync(string? search, string? role, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var users = await _identity.SearchUsersAsync(search, ct);

        if (!string.IsNullOrWhiteSpace(role))
            users = users.Where(u => u.Roles.Contains(role)).ToList();

        var total = users.Count;
        var pageUsers = users.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var employeeIds = pageUsers.Where(u => u.EmployeeId.HasValue).Select(u => u.EmployeeId!.Value).Distinct().ToList();

        var employeeNames = await _db.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName, ct);

        var items = pageUsers.Select(u => new UserListItemDto
        {
            Id = u.Id,
            Email = u.Email,
            DisplayName = u.DisplayName,
            Roles = u.Roles,
            IsActive = u.IsActive,
            EmployeeId = u.EmployeeId,
            EmployeeName = u.EmployeeId.HasValue && employeeNames.TryGetValue(u.EmployeeId.Value, out var n) ? n : null,
            LastLoginUtc = u.LastLoginUtc
        }).ToList();

        return PagedResult<UserListItemDto>.Create(items, page, pageSize, total);
    }

    public async Task<UserListItemDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var u = await _identity.FindByIdAsync(id, ct);
        if (u is null) return null;

        string? employeeName = null;
        if (u.EmployeeId.HasValue)
        {
            employeeName = await _db.Employees.AsNoTracking()
                .Where(e => e.Id == u.EmployeeId).Select(e => e.FirstName + " " + e.LastName)
                .FirstOrDefaultAsync(ct);
        }

        return new UserListItemDto
        {
            Id = u.Id, Email = u.Email, DisplayName = u.DisplayName, Roles = u.Roles,
            IsActive = u.IsActive, EmployeeId = u.EmployeeId, EmployeeName = employeeName, LastLoginUtc = u.LastLoginUtc
        };
    }

    public async Task<(bool Success, string? UserId, IReadOnlyList<string> Errors)> CreateAsync(CreateUserDto dto, CancellationToken ct = default)
    {
        if (!AssignableRoles.Contains(dto.Role))
            return (false, null, new[] { "Invalid role selected." });

        if (dto.EmployeeId.HasValue &&
            await _db.Employees.AnyAsync(e => e.Id == dto.EmployeeId && e.UserId != null, ct))
            return (false, null, new[] { "The selected employee is already linked to a user account." });

        var result = await _identity.CreateUserAsync(dto.Email, dto.DisplayName, dto.Password, new[] { dto.Role }, dto.EmployeeId, ct);
        if (!result.Succeeded)
            return (false, null, result.Errors);

        if (dto.EmployeeId.HasValue && result.UserId is not null)
        {
            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, ct);
            if (employee is not null)
            {
                employee.UserId = result.UserId;
                employee.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "User.Created", TargetType = "ApplicationUser", TargetId = result.UserId,
            Metadata = $"Email={dto.Email};Role={dto.Role}"
        }, ct);

        return (true, result.UserId, Array.Empty<string>());
    }

    public async Task UpdateAsync(string id, EditUserDto dto, CancellationToken ct = default)
    {
        if (!AssignableRoles.Contains(dto.Role))
            throw new BusinessRuleException("Invalid role selected.");

        var user = await _identity.FindByIdAsync(id, ct)
            ?? throw new NotFoundException("User not found.");

        await EnsureAnotherAdministratorRemainsAsync(user, dto.Role, dto.IsActive, ct);

        Employee? employeeToLink = null;
        if (dto.EmployeeId.HasValue)
        {
            if (await _db.Employees.AnyAsync(e => e.Id == dto.EmployeeId && e.UserId != null && e.UserId != id, ct))
                throw new ConflictException("The selected employee is already linked to another account.");

            employeeToLink = await _db.Employees.FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, ct)
                ?? throw new BusinessRuleException("Selected employee does not exist.");
        }

        await _identity.SetRolesAsync(id, new[] { dto.Role });
        await _identity.SetActiveAsync(id, dto.IsActive);

        var previouslyLinked = await _db.Employees.Where(e => e.UserId == id).ToListAsync(ct);
        foreach (var e in previouslyLinked) e.UserId = null;

        if (employeeToLink is not null)
        {
            employeeToLink.UserId = id;
            employeeToLink.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        await _identity.RefreshSecurityStampAsync(id, ct);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "User.Updated", TargetType = "ApplicationUser", TargetId = id,
            Metadata = $"Role={dto.Role};Active={dto.IsActive};Email={user.Email}"
        }, ct);
    }

    public async Task ResetPasswordAsync(string id, ResetUserPasswordDto dto, CancellationToken ct = default)
    {
        _ = await _identity.FindByIdAsync(id, ct) ?? throw new NotFoundException("User not found.");
        var ok = await _identity.ResetPasswordAsync(id, dto.NewPassword, ct);
        if (!ok) throw new BusinessRuleException("The password could not be reset. It may not meet the password policy.");

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "User.PasswordReset", TargetType = "ApplicationUser", TargetId = id
        }, ct);
    }

    private async Task EnsureAnotherAdministratorRemainsAsync(AppUserInfo user, string newRole, bool newIsActive, CancellationToken ct)
    {
        if (!user.IsActive || !user.Roles.Contains("Administrator")) return;
        if (newIsActive && string.Equals(newRole, "Administrator", StringComparison.Ordinal)) return;

        var otherAdministrators = await _identity.GetUsersInRoleAsync("Administrator", ct);
        if (!otherAdministrators.Any(u => u.Id != user.Id && u.IsActive))
            throw new BusinessRuleException("At least one active administrator account must remain.");
    }

    public async Task<IReadOnlyList<(int Id, string Name, string Number)>> GetLinkableEmployeesAsync(string? excludeUserId = null, CancellationToken ct = default)
    {
        var query = _db.Employees.AsNoTracking()
            .Where(e => e.UserId == null || (excludeUserId != null && e.UserId == excludeUserId));

        var list = await query.OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .Select(e => new { e.Id, Name = e.FirstName + " " + e.LastName, e.EmployeeNumber })
            .ToListAsync(ct);

        return list.Select(x => (x.Id, x.Name, x.EmployeeNumber)).ToList();
    }
}
