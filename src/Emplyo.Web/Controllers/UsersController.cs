using Emplyo.Application.Users;
using Emplyo.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize(Policy = Policies.Administrator)]
public class UsersController : Controller
{
    private readonly IUserAdminService _users;

    public UsersController(IUserAdminService users) => _users = users;

    public async Task<IActionResult> Index(string? search, string? role, int page = 1, int pageSize = 15, CancellationToken ct = default)
    {
        ViewData["Title"] = "User management";
        ViewBag.Search = search;
        ViewBag.Role = role;
        ViewBag.Roles = UserAdminService.AssignableRoles;
        return View(await _users.GetPagedAsync(search, role, page, pageSize, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        ViewData["Title"] = "Create user";
        ViewBag.Employees = await _users.GetLinkableEmployeesAsync(null, ct);
        return View(new CreateUserDto());
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserDto model, CancellationToken ct)
    {
        ViewData["Title"] = "Create user";
        if (!ModelState.IsValid)
        {
            ViewBag.Employees = await _users.GetLinkableEmployeesAsync(null, ct);
            return View(model);
        }

        var (success, _, errors) = await _users.CreateAsync(model, ct);
        if (!success)
        {
            foreach (var e in errors) ModelState.AddModelError(string.Empty, e);
            ViewBag.Employees = await _users.GetLinkableEmployeesAsync(null, ct);
            return View(model);
        }

        TempData["Success"] = $"User '{model.Email}' created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();
        ViewData["Title"] = $"Edit {user.DisplayName}";
        ViewBag.User = user;
        ViewBag.Employees = await _users.GetLinkableEmployeesAsync(id, ct);
        return View(new EditUserDto
        {
            Role = user.Roles.FirstOrDefault() ?? "Employee",
            EmployeeId = user.EmployeeId,
            IsActive = user.IsActive
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(string id, EditUserDto model, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();
        ViewData["Title"] = $"Edit {user.DisplayName}";
        ViewBag.User = user;

        if (!ModelState.IsValid)
        {
            ViewBag.Employees = await _users.GetLinkableEmployeesAsync(id, ct);
            return View(model);
        }

        await _users.UpdateAsync(id, model, ct);
        TempData["Success"] = "User updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string id, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();
        ViewData["Title"] = $"Reset password · {user.DisplayName}";
        ViewBag.User = user;
        return View(new ResetUserPasswordDto());
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(string id, ResetUserPasswordDto model, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();
        ViewData["Title"] = $"Reset password · {user.DisplayName}";
        ViewBag.User = user;

        if (!ModelState.IsValid) return View(model);

        await _users.ResetPasswordAsync(id, model, ct);
        TempData["Success"] = "Password reset. Share the new password with the user securely.";
        return RedirectToAction(nameof(Index));
    }
}
