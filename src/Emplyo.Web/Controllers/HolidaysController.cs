using Emplyo.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize]
public class HolidaysController : Controller
{
    private readonly Emplyo.Application.LeaveManagement.IHolidayService _holidays;

    public HolidaysController(Emplyo.Application.LeaveManagement.IHolidayService holidays) => _holidays = holidays;

    public async Task<IActionResult> Index(int? year, CancellationToken ct)
    {
        ViewData["Title"] = "Holidays";
        ViewBag.Year = year ?? DateTime.UtcNow.Year;
        return View(await _holidays.GetAsync(year ?? DateTime.UtcNow.Year, ct));
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpGet]
    public IActionResult Create()
    {
        ViewData["Title"] = "Add holiday";
        return View(new Emplyo.Application.LeaveManagement.HolidayFormDto());
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpPost]
    public async Task<IActionResult> Create(Emplyo.Application.LeaveManagement.HolidayFormDto model, CancellationToken ct)
    {
        ViewData["Title"] = "Add holiday";
        if (!ModelState.IsValid) return View(model);
        await _holidays.CreateAsync(model, ct);
        TempData["Success"] = $"Holiday '{model.Name}' added.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var form = await _holidays.GetFormAsync(id, ct);
        if (form is null) return NotFound();
        ViewData["Title"] = "Edit holiday";
        ViewBag.Id = id;
        return View(form);
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpPost]
    public async Task<IActionResult> Edit(int id, Emplyo.Application.LeaveManagement.HolidayFormDto model, CancellationToken ct)
    {
        ViewData["Title"] = "Edit holiday";
        if (!ModelState.IsValid) { ViewBag.Id = id; return View(model); }
        await _holidays.UpdateAsync(id, model, ct);
        TempData["Success"] = "Holiday updated.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _holidays.DeleteAsync(id, ct);
        TempData["Success"] = "Holiday removed.";
        return RedirectToAction(nameof(Index));
    }
}
