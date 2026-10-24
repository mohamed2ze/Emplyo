using Emplyo.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Emplyo.Infrastructure.Identity;
using Emplyo.Infrastructure.Persistence;

namespace Emplyo.Infrastructure.Services;

public class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly AppDbContext _db;

    public IdentityService(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, AppDbContext db)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
    }

    public async Task<CreateUserResult> CreateUserAsync(string email, string displayName, string password,
        IReadOnlyCollection<string> roles, int? employeeId, CancellationToken ct = default)
    {
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
            return new CreateUserResult { Succeeded = false, Errors = new[] { "A user with this email already exists." } };

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
            return new CreateUserResult { Succeeded = false, Errors = createResult.Errors.Select(e => e.Description).ToList() };

        var assignResult = await _userManager.AddToRolesAsync(user, roles);
        if (!assignResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            return new CreateUserResult { Succeeded = false, Errors = assignResult.Errors.Select(e => e.Description).ToList() };
        }

        return new CreateUserResult { Succeeded = true, UserId = user.Id };
    }

    public async Task<AppUserInfo?> FindByIdAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        return user is null ? null : await MapAsync(user);
    }

    public async Task<IReadOnlyList<AppUserInfo>> SearchUsersAsync(string? query, CancellationToken ct = default)
    {
        var usersQuery = _userManager.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            usersQuery = usersQuery.Where(u => u.Email!.Contains(term) || u.DisplayName.Contains(term));
        }

        var users = await usersQuery.OrderBy(u => u.DisplayName).ToListAsync(ct);
        var userInfos = new List<AppUserInfo>(users.Count);
        foreach (var user in users)
        {
            userInfos.Add(await MapAsync(user));
        }
        return userInfos;
    }

    public async Task<IReadOnlyList<AppUserInfo>> GetUsersInRoleAsync(string role, CancellationToken ct = default)
    {
        var users = await _userManager.GetUsersInRoleAsync(role);
        var userInfos = new List<AppUserInfo>(users.Count);
        foreach (var user in users.Where(u => u.IsActive))
        {
            userInfos.Add(await MapAsync(user));
        }
        return userInfos;
    }

    public async Task SetRolesAsync(string userId, IReadOnlyCollection<string> roles, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return;

        var current = await _userManager.GetRolesAsync(user);
        var toRemove = current.Except(roles).ToList();
        var toAdd = roles.Except(current).ToList();

        if (toRemove.Count > 0) await _userManager.RemoveFromRolesAsync(user, toRemove);
        if (toAdd.Count > 0) await _userManager.AddToRolesAsync(user, toAdd);

        if (toRemove.Count > 0 || toAdd.Count > 0)
            await _userManager.UpdateSecurityStampAsync(user);
    }

    public async Task<bool> ResetPasswordAsync(string userId, string newPassword, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return false;

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await _userManager.ResetPasswordAsync(user, token, newPassword);
        return resetResult.Succeeded;
    }

    public async Task SetActiveAsync(string userId, bool isActive, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return;

        user.IsActive = isActive;
        if (!isActive)
        {
            user.LockoutEnd = DateTimeOffset.MaxValue;
        }
        else
        {
            user.LockoutEnd = null;
            user.LockoutEnabled = true;
        }
        await _userManager.UpdateAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);
    }

    public async Task RefreshSecurityStampAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is not null)
            await _userManager.UpdateSecurityStampAsync(user);
    }

    public async Task RecordLoginAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return;
        user.LastLoginUtc = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);
    }

    private async Task<AppUserInfo> MapAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var employeeId = await _db.Employees.AsNoTracking()
            .Where(e => e.UserId == user.Id)
            .Select(e => (int?)e.Id)
            .FirstOrDefaultAsync();

        return new AppUserInfo
        {
            Id = user.Id,
            Email = user.Email,
            UserName = user.UserName,
            DisplayName = user.DisplayName,
            IsActive = user.IsActive && (user.LockoutEnd is null || user.LockoutEnd <= DateTimeOffset.UtcNow),
            EmployeeId = employeeId,
            LastLoginUtc = user.LastLoginUtc,
            Roles = roles.ToList()
        };
    }
}
