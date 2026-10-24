using System.ComponentModel.DataAnnotations;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Infrastructure.Services;
using Emplyo.Web.Authorization;
using Emplyo.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

public class SettingsViewModel
{
    [StringLength(500)]
    public string OrganizationName { get; set; } = "Emplyo";

    [StringLength(150)]
    public string OrganizationTimeZone { get; set; } = "UTC";

    [StringLength(10)]
    public string WorkDayStart { get; set; } = "09:00";
    public IReadOnlyList<(string Key, string Value, string? Description)> All { get; set; } = Array.Empty<(string, string, string?)>();
}

[Authorize(Policy = Policies.Administrator)]
public class SettingsController : Controller
{
    private readonly IAppSettingsService _settings;
    private readonly TimeZoneProvider _timeZoneProvider;

    public SettingsController(IAppSettingsService settings, TimeZoneProvider timeZoneProvider)
    {
        _settings = settings;
        _timeZoneProvider = timeZoneProvider;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Settings";
        var model = new SettingsViewModel
        {
            OrganizationName = await _settings.GetAsync(AppSettingsService.OrganizationNameKey, "Emplyo", ct),
            OrganizationTimeZone = await _settings.GetAsync(AppSettingsService.OrganizationTimeZoneKey, "UTC", ct),
            WorkDayStart = await _settings.GetAsync(AppSettingsService.WorkDayStartKey, "09:00", ct),
            All = await _settings.GetAllAsync(ct)
        };
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Index(SettingsViewModel model, CancellationToken ct)
    {
        ViewData["Title"] = "Settings";
        if (string.IsNullOrWhiteSpace(model.OrganizationName))
            ModelState.AddModelError(nameof(model.OrganizationName), "Organization name is required.");

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(model.OrganizationTimeZone);
        }
        catch
        {
            ModelState.AddModelError(nameof(model.OrganizationTimeZone), "Unknown time zone. Use an IANA identifier such as Europe/Berlin.");
        }

        if (!TimeOnly.TryParse(model.WorkDayStart, out _))
            ModelState.AddModelError(nameof(model.WorkDayStart), "Work day start must be a time such as 09:00.");

        if (!ModelState.IsValid)
        {
            model.All = await _settings.GetAllAsync(ct);
            return View(model);
        }

        await _settings.SetAsync(AppSettingsService.OrganizationNameKey, model.OrganizationName, "Organization display name.", ct);
        await _settings.SetAsync(AppSettingsService.OrganizationTimeZoneKey, model.OrganizationTimeZone, "IANA time zone for display and attendance.", ct);
        await _settings.SetAsync(AppSettingsService.WorkDayStartKey, model.WorkDayStart, "Daily start time for late arrival detection.", ct);
        _timeZoneProvider.Invalidate();

        TempData["Success"] = "Settings saved.";
        return RedirectToAction(nameof(Index));
    }
}
