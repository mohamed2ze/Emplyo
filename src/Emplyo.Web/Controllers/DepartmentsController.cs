using Emplyo.Application.Employees;
using Emplyo.Application.Organization;
using Emplyo.Web.Authorization;
using Emplyo.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize(Policy = Policies.HrOrAdmin)]
public class DepartmentsController : Controller
{
    private readonly IDepartmentService _departments;
    private readonly IPositionService _positions;
    private readonly IEmployeeService _employees;

    public DepartmentsController(IDepartmentService departments, IPositionService positions, IEmployeeService employees)
    {
        _departments = departments;
        _positions = positions;
        _employees = employees;
    }

    public async Task<IActionResult> Index(string? search, bool? active, int page = 1, int pageSize = 15, CancellationToken ct = default)
    {
        ViewData["Title"] = "Departments";
        ViewBag.Search = search;
        ViewBag.Active = active;
        return View(await _departments.GetPagedAsync(search, active, page, pageSize, ct));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var model = await _departments.GetByIdAsync(id, ct);
        if (model is null) return NotFound();
        ViewData["Title"] = model.Name;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        ViewData["Title"] = "Add department";
        ViewBag.Employees = await _employees.GetManagerOptionsAsync(null, ct);
        return View(new DepartmentFormViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Create(DepartmentFormViewModel model, CancellationToken ct)
    {
        ViewData["Title"] = "Add department";
        if (!ModelState.IsValid) return View(model);
        var id = await _departments.CreateAsync(model.Form, ct);
        TempData["Success"] = $"Department '{model.Form.Name}' was created.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var dept = await _departments.GetByIdAsync(id, ct);
        if (dept is null) return NotFound();
        ViewData["Title"] = $"Edit {dept.Name}";
        ViewBag.Employees = await _employees.GetManagerOptionsAsync(null, ct);
        return View(new DepartmentFormViewModel
        {
            IsEdit = true,
            Id = id,
            Form = new DepartmentFormDto
            {
                Name = dept.Name,
                Description = dept.Description,
                DepartmentHeadId = dept.DepartmentHeadId,
                IsActive = dept.IsActive
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, DepartmentFormViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        await _departments.UpdateAsync(id, model.Form, ct);
        TempData["Success"] = "Department updated.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
