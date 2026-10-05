using Emplyo.Application.Employees;
using Emplyo.Application.Lifecycle;
using Emplyo.Domain.Enums;
using Emplyo.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize(Policy = Policies.HrOrAdmin)]
public class OffboardingController : Controller
{
    private readonly ILifecycleService _lifecycle;
    private readonly IEmployeeService _employees;

    public OffboardingController(ILifecycleService lifecycle, IEmployeeService employees)
    {
        _lifecycle = lifecycle;
        _employees = employees;
    }

    public async Task<IActionResult> Index(string? search, int page = 1, int pageSize = 15, CancellationToken ct = default)
    {
        ViewData["Title"] = "Offboarding";
        ViewBag.Search = search;
        return View(await _lifecycle.GetOffboardingAsync(search, page, pageSize, ct));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var model = await _lifecycle.GetOffboardingDetailAsync(id, ct);
        if (model is null) return NotFound();
        ViewData["Title"] = $"Offboarding · {model.EmployeeName}";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Initiate(int employeeId, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(employeeId, ct);
        if (employee is null) return NotFound();
        ViewData["Title"] = $"Initiate offboarding · {employee.FullName}";
        ViewBag.Employee = employee;
        return View(new OffboardingInitiateDto { DepartureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30) });
    }

    [HttpPost]
    public async Task<IActionResult> Initiate(int employeeId, OffboardingInitiateDto model, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(employeeId, ct);
        if (employee is null) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Initiate offboarding";
            ViewBag.Employee = employee;
            return View(model);
        }
        var caseId = await _lifecycle.InitiateOffboardingAsync(employeeId, model, ct);
        TempData["Success"] = "Offboarding case initiated.";
        return RedirectToAction(nameof(Details), new { id = caseId });
    }

    [HttpPost]
    public async Task<IActionResult> SetTaskStatus(int id, int taskId, TaskItemStatus status, CancellationToken ct)
    {
        await _lifecycle.SetOffboardingTaskStatusAsync(taskId, status, ct);
        TempData["Success"] = "Task updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Complete(int id, CancellationToken ct)
    {
        await _lifecycle.CompleteOffboardingAsync(id, ct);
        TempData["Success"] = "Offboarding completed. Employee status set to terminated.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
