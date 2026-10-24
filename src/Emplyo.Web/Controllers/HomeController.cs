using System.Diagnostics;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Dashboard;
using Emplyo.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly IDashboardService _dashboard;
    private readonly ICurrentUserService _currentUser;

    public HomeController(IDashboardService dashboard, ICurrentUserService currentUser)
    {
        _dashboard = dashboard;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (User.IsInRole(Roles.Administrator))
        {
            ViewData["Title"] = "Administrator dashboard";
            return View("Admin", await _dashboard.GetAdminDashboardAsync(ct));
        }

        if (User.IsInRole(Roles.HrManager))
        {
            ViewData["Title"] = "HR dashboard";
            return View("Hr", await _dashboard.GetHrDashboardAsync(ct));
        }

        var employeeId = _currentUser.EmployeeId;
        if (!employeeId.HasValue)
        {
            ViewData["Title"] = "Dashboard";
            TempData["Warning"] = "Your user account is not linked to an employee record yet. Contact your administrator.";
            return View("NoEmployee");
        }

        var model = await _dashboard.GetEmployeeDashboardAsync(employeeId.Value, _currentUser.Roles, ct);
        if (model is null)
        {
            TempData["Warning"] = "We could not find the employee record linked to your account.";
            return View("NoEmployee");
        }

        ViewData["Title"] = "My dashboard";
        return View("Employee", model);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new Models.ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public new IActionResult StatusCode(int code)
    {
        Response.StatusCode = code;
        ViewData["Code"] = code;
        return View("Status");
    }
}
