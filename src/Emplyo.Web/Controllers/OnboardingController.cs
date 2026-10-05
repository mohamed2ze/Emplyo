using Emplyo.Application.Employees;
using Emplyo.Application.Lifecycle;
using Emplyo.Domain.Enums;
using Emplyo.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize(Policy = Policies.HrOrAdmin)]
public class OnboardingController : Controller
{
    private readonly ILifecycleService _lifecycle;
    private readonly IEmployeeService _employees;

    public OnboardingController(ILifecycleService lifecycle, IEmployeeService employees)
    {
        _lifecycle = lifecycle;
        _employees = employees;
    }

    public async Task<IActionResult> Index(string? search, int page = 1, int pageSize = 15, CancellationToken ct = default)
    {
        ViewData["Title"] = "Onboarding";
        ViewBag.Search = search;
        return View(await _lifecycle.GetOnboardingAsync(search, page, pageSize, ct));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var model = await _lifecycle.GetOnboardingDetailAsync(id, ct);
        if (model is null) return NotFound();
        ViewData["Title"] = $"Onboarding · {model.EmployeeName}";
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> ApplyTemplate(int id, CancellationToken ct)
    {
        await _lifecycle.ApplyOnboardingTemplateAsync(id, ct);
        TempData["Success"] = "Standard onboarding checklist applied.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> AddTask(int id, TaskFormDto model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Enter a task title.";
            return RedirectToAction(nameof(Details), new { id });
        }
        await _lifecycle.AddOnboardingTaskAsync(id, model, ct);
        TempData["Success"] = "Task added.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> SetTaskStatus(int id, int taskId, TaskItemStatus status, CancellationToken ct)
    {
        await _lifecycle.SetOnboardingTaskStatusAsync(taskId, status, ct);
        TempData["Success"] = "Task updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteTask(int id, int taskId, CancellationToken ct)
    {
        await _lifecycle.DeleteOnboardingTaskAsync(taskId, ct);
        TempData["Success"] = "Task removed.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
