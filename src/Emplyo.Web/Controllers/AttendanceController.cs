using Emplyo.Application.Attendance;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Employees;
using Emplyo.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize]
public class AttendanceController : Controller
{
    private readonly IAttendanceService _attendance;
    private readonly IEmployeeService _employees;
    private readonly ICurrentUserService _currentUser;
    private readonly IAppSettingsService _settings;

    public AttendanceController(IAttendanceService attendance, IEmployeeService employees,
        ICurrentUserService currentUser, IAppSettingsService settings)
    {
        _attendance = attendance;
        _employees = employees;
        _currentUser = currentUser;
        _settings = settings;
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    public async Task<IActionResult> Index([FromQuery] AttendanceFilter filter, CancellationToken ct)
    {
        ViewData["Title"] = "Attendance";
        ViewBag.Filter = filter;
        return View(await _attendance.GetPagedAsync(filter, ct));
    }

    public async Task<IActionResult> My(CancellationToken ct)
    {
        ViewData["Title"] = "My attendance";
        var employeeId = _currentUser.EmployeeId;
        if (!employeeId.HasValue)
        {
            TempData["Warning"] = "Your account is not linked to an employee record.";
            return View("~/Views/Home/NoEmployee.cshtml");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-45);
        ViewBag.Today = await _attendance.GetTodayAsync(employeeId.Value, ct);
        ViewBag.Summary = await _attendance.GetSummaryAsync(employeeId.Value, from, today, ct);
        return View(await _attendance.GetForEmployeeAsync(employeeId.Value, from, today, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClockIn(CancellationToken ct)
    {
        var employeeId = _currentUser.EmployeeId;
        if (!employeeId.HasValue) return BadRequest(new { success = false, message = "No employee record is linked to your account." });
        await _attendance.ClockInAsync(employeeId.Value, ct);
        return Ok(new { success = true, message = "Clocked in." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClockOut(CancellationToken ct)
    {
        var employeeId = _currentUser.EmployeeId;
        if (!employeeId.HasValue) return BadRequest(new { success = false, message = "No employee record is linked to your account." });
        await _attendance.ClockOutAsync(employeeId.Value, ct);
        return Ok(new { success = true, message = "Clocked out." });
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpGet]
    public async Task<IActionResult> Correct(int? employeeId, int? id, CancellationToken ct)
    {
        ViewData["Title"] = "Correct attendance";
        ViewBag.Employees = await _employees.GetManagerOptionsAsync(null, ct);

        if (id.HasValue)
        {
            var record = await _attendance.GetByIdAsync(id.Value, ct);
            if (record is null) return NotFound();
            ViewBag.SelectedEmployeeId = record.EmployeeId;
            ViewBag.RecordId = record.Id;
            var tz = await _settings.GetOrganizationTimeZoneAsync(ct);
            return View(new AttendanceCorrectionDto
            {
                WorkDate = record.WorkDate,
                ClockIn = ToLocal(record.ClockInUtc, tz),
                ClockOut = ToLocal(record.ClockOutUtc, tz),
                Status = record.Status,
                Notes = record.Notes
            });
        }

        ViewBag.SelectedEmployeeId = employeeId;
        return View(new AttendanceCorrectionDto { WorkDate = DateOnly.FromDateTime(DateTime.UtcNow) });
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpPost]
    public async Task<IActionResult> Correct(int employeeId, int? id, AttendanceCorrectionDto model, CancellationToken ct)
    {
        ViewData["Title"] = "Correct attendance";
        ViewBag.Employees = await _employees.GetManagerOptionsAsync(null, ct);
        ViewBag.SelectedEmployeeId = employeeId;
        ViewBag.RecordId = id;

        if (!ModelState.IsValid) return View(model);

        var tz = await _settings.GetOrganizationTimeZoneAsync(ct);
        model.ClockIn = ToUtc(model.ClockIn, tz);
        model.ClockOut = ToUtc(model.ClockOut, tz);

        await _attendance.CorrectAsync(model, id, employeeId, ct);
        TempData["Success"] = "Attendance record saved.";
        return RedirectToAction(nameof(Index), new { EmployeeId = employeeId });
    }

    private static DateTime? ToLocal(DateTime? utc, TimeZoneInfo tz)
        => utc.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc), tz) : null;

    private static DateTime? ToUtc(DateTime? local, TimeZoneInfo tz)
        => local.HasValue ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.Value, DateTimeKind.Unspecified), tz) : null;
}
