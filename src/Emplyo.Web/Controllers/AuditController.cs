using Emplyo.Application.Auditing;
using Emplyo.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize(Policy = Policies.Administrator)]
public class AuditController : Controller
{
    private readonly IAuditQueryService _audit;

    public AuditController(IAuditQueryService audit) => _audit = audit;

    public async Task<IActionResult> Index([FromQuery] AuditFilter filter, CancellationToken ct)
    {
        ViewData["Title"] = "Audit logs";
        ViewBag.Filter = filter;
        ViewBag.Actions = await _audit.GetDistinctActionsAsync(ct);
        return View(await _audit.GetPagedAsync(filter, ct));
    }
}
