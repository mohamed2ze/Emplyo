using System.Security.Claims;
using Emplyo.Application.Common.Interfaces;

namespace Emplyo.Web.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUserService(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? UserName => Principal?.Identity?.Name;

    public string DisplayName =>
        Principal?.FindFirst("display_name")?.Value
        ?? Principal?.Identity?.Name
        ?? "system";

    public int? EmployeeId =>
        int.TryParse(Principal?.FindFirst("employee_id")?.Value, out var id) ? id : null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? (IReadOnlyCollection<string>)Array.Empty<string>();
}
