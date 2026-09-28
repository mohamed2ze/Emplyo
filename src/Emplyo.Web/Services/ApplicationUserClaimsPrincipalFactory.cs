using System.Security.Claims;
using Emplyo.Infrastructure.Identity;
using Emplyo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Emplyo.Web.Services;

public class ApplicationUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
{
    private readonly AppDbContext _db;

    public ApplicationUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> options,
        AppDbContext db)
        : base(userManager, roleManager, options)
    {
        _db = db;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim("display_name", string.IsNullOrEmpty(user.DisplayName) ? (user.Email ?? "") : user.DisplayName));

        var employeeId = await _db.Employees.AsNoTracking()
            .Where(e => e.UserId == user.Id)
            .Select(e => (int?)e.Id)
            .FirstOrDefaultAsync();

        if (employeeId.HasValue)
            identity.AddClaim(new Claim("employee_id", employeeId.Value.ToString()));

        return identity;
    }
}
