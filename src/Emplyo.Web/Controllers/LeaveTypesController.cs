using Emplyo.Application.LeaveManagement;
using Emplyo.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize(Policy = Policies.Administrator)]
public class LeaveTypesController : Controller
{
    private readonly ILeaveTypeService _leaveTypes;

    public LeaveTypesController(ILeaveTypeService leaveTypes) => _leaveTypes = leaveTypes;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Leave types";
        return View(await _leaveTypes.GetAllAsync(includeInactive: true, ct));
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewData["Title"] = "Add leave type";
        return View(new LeaveTypeFormDto());
    }

    [HttpPost]
    public async Task<IActionResult> Create(LeaveTypeFormDto model, CancellationToken ct)
    {
        ViewData["Title"] = "Add leave type";
        if (!ModelState.IsValid) return View(model);
        await _leaveTypes.CreateAsync(model, ct);
        TempData["Success"] = $"Leave type '{model.Name}' created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var form = await _leaveTypes.GetFormAsync(id, ct);
        if (form is null) return NotFound();
        ViewData["Title"] = "Edit leave type";
        return View(form);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, LeaveTypeFormDto model, CancellationToken ct)
    {
        ViewData["Title"] = "Edit leave type";
        if (!ModelState.IsValid) return View(model);
        await _leaveTypes.UpdateAsync(id, model, ct);
        TempData["Success"] = "Leave type updated.";
        return RedirectToAction(nameof(Index));
    }
}
