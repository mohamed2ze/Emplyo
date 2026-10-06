using Emplyo.Application.Announcements;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Employees;
using Emplyo.Application.Organization;
using Emplyo.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize]
public class AnnouncementsController : Controller
{
    private readonly IAnnouncementService _announcements;
    private readonly IDepartmentService _departments;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmployeeService _employees;

    public AnnouncementsController(IAnnouncementService announcements, IDepartmentService departments,
        ICurrentUserService currentUser, IEmployeeService employees)
    {
        _announcements = announcements;
        _departments = departments;
        _currentUser = currentUser;
        _employees = employees;
    }

    public async Task<IActionResult> Index(string? search, bool? published, int page = 1, int pageSize = 15, CancellationToken ct = default)
    {
        ViewData["Title"] = "Announcements";
        var isStaff = User.IsInRole(Roles.Administrator) || User.IsInRole(Roles.HrManager);
        ViewBag.IsStaff = isStaff;

        if (isStaff)
        {
            ViewBag.Search = search;
            ViewBag.Published = published;
            return View("Manage", await _announcements.GetPagedAsync(search, published, page, pageSize, ct));
        }

        var employee = _currentUser.EmployeeId.HasValue ? await _employees.GetByIdAsync(_currentUser.EmployeeId.Value, ct) : null;
        return View("Feed", await _announcements.GetActiveForRoleAsync(_currentUser.Roles, employee?.DepartmentId, ct));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var model = await _announcements.GetByIdAsync(id, ct);
        if (model is null) return NotFound();

        var isStaff = User.IsInRole(Roles.Administrator) || User.IsInRole(Roles.HrManager);
        if (!isStaff && !model.IsPublished) return NotFound();

        ViewData["Title"] = model.Title;
        ViewBag.IsStaff = isStaff;
        return View(model);
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        ViewData["Title"] = "New announcement";
        ViewBag.Departments = await _departments.GetLookupAsync(true, ct);
        return View(new AnnouncementFormDto { PublishNow = true });
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpPost]
    public async Task<IActionResult> Create(AnnouncementFormDto model, CancellationToken ct)
    {
        ViewData["Title"] = "New announcement";
        if (!ModelState.IsValid)
        {
            ViewBag.Departments = await _departments.GetLookupAsync(true, ct);
            return View(model);
        }
        var id = await _announcements.CreateAsync(_currentUser.UserId!, _currentUser.DisplayName, model, ct);
        TempData["Success"] = "Announcement created.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var a = await _announcements.GetByIdAsync(id, ct);
        if (a is null) return NotFound();
        ViewData["Title"] = "Edit announcement";
        ViewBag.Departments = await _departments.GetLookupAsync(true, ct);
        return View(new AnnouncementFormDto
        {
            Title = a.Title,
            Body = a.Body,
            Audience = a.Audience,
            PublishAt = a.PublishAtUtc,
            ExpiresAt = a.ExpiresAtUtc
        });
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpPost]
    public async Task<IActionResult> Edit(int id, AnnouncementFormDto model, CancellationToken ct)
    {
        ViewData["Title"] = "Edit announcement";
        if (!ModelState.IsValid)
        {
            ViewBag.Departments = await _departments.GetLookupAsync(true, ct);
            return View(model);
        }
        await _announcements.UpdateAsync(id, model, ct);
        TempData["Success"] = "Announcement updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpPost]
    public async Task<IActionResult> SetPublished(int id, bool published, CancellationToken ct)
    {
        await _announcements.SetPublishedAsync(id, published, ct);
        TempData["Success"] = published ? "Announcement published." : "Announcement archived.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _announcements.DeleteAsync(id, ct);
        TempData["Success"] = "Announcement deleted.";
        return RedirectToAction(nameof(Index));
    }
}
