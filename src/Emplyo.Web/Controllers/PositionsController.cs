using Emplyo.Application.Organization;
using Emplyo.Web.Authorization;
using Emplyo.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize(Policy = Policies.HrOrAdmin)]
public class PositionsController : Controller
{
    private readonly IPositionService _positions;
    private readonly IDepartmentService _departments;

    public PositionsController(IPositionService positions, IDepartmentService departments)
    {
        _positions = positions;
        _departments = departments;
    }

    public async Task<IActionResult> Index(string? search, int? departmentId, bool? active, int page = 1, int pageSize = 15, CancellationToken ct = default)
    {
        ViewData["Title"] = "Positions";
        ViewBag.Search = search;
        ViewBag.DepartmentId = departmentId;
        ViewBag.Active = active;
        ViewBag.Departments = await _departments.GetLookupAsync(false, ct);
        return View(await _positions.GetPagedAsync(search, departmentId, active, page, pageSize, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        ViewData["Title"] = "Add position";
        return View(new PositionFormViewModel { Departments = await _departments.GetLookupAsync(true, ct) });
    }

    [HttpPost]
    public async Task<IActionResult> Create(PositionFormViewModel model, CancellationToken ct)
    {
        ViewData["Title"] = "Add position";
        if (!ModelState.IsValid)
        {
            model.Departments = await _departments.GetLookupAsync(true, ct);
            return View(model);
        }
        await _positions.CreateAsync(model.Form, ct);
        TempData["Success"] = $"Position '{model.Form.Title}' was created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var position = await _positions.GetByIdAsync(id, ct);
        if (position is null) return NotFound();
        ViewData["Title"] = $"Edit {position.Title}";
        return View(new PositionFormViewModel
        {
            IsEdit = true,
            Id = id,
            Departments = await _departments.GetLookupAsync(true, ct),
            Form = new PositionFormDto
            {
                Title = position.Title,
                Description = position.Description,
                DepartmentId = position.DepartmentId,
                IsActive = position.IsActive
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, PositionFormViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            model.Departments = await _departments.GetLookupAsync(true, ct);
            return View(model);
        }
        await _positions.UpdateAsync(id, model.Form, ct);
        TempData["Success"] = "Position updated.";
        return RedirectToAction(nameof(Index));
    }
}
