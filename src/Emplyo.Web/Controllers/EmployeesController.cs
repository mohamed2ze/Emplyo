using Emplyo.Application.Attendance;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Documents;
using Emplyo.Application.Employees;
using Emplyo.Application.LeaveManagement;
using Emplyo.Application.Organization;
using Emplyo.Domain.Enums;
using Emplyo.Web.Authorization;
using Emplyo.Web.Services;
using Emplyo.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize]
public class EmployeesController : Controller
{
    private readonly IEmployeeService _employees;
    private readonly IDepartmentService _departments;
    private readonly IPositionService _positions;
    private readonly IAttendanceService _attendance;
    private readonly ILeaveService _leave;
    private readonly IDocumentService _documents;
    private readonly IFileStorageService _storage;
    private readonly ICurrentUserService _currentUser;

    public EmployeesController(
        IEmployeeService employees,
        IDepartmentService departments,
        IPositionService positions,
        IAttendanceService attendance,
        ILeaveService leave,
        IDocumentService documents,
        IFileStorageService storage,
        ICurrentUserService currentUser)
    {
        _employees = employees;
        _departments = departments;
        _positions = positions;
        _attendance = attendance;
        _leave = leave;
        _documents = documents;
        _storage = storage;
        _currentUser = currentUser;
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    public async Task<IActionResult> Index([FromQuery] EmployeeFilter filter, CancellationToken ct)
    {
        ViewData["Title"] = "Employees";
        ViewBag.Departments = await _departments.GetLookupAsync(false, ct);
        ViewBag.Positions = await _positions.GetLookupAsync(null, false, ct);
        var result = await _employees.GetPagedAsync(filter, ct);
        ViewBag.Filter = filter;
        return View(result);
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(id, ct);
        if (employee is null) return NotFound();

        var isStaff = User.IsInRole(Roles.Administrator) || User.IsInRole(Roles.HrManager);
        var isSelf = _currentUser.EmployeeId == id;

        if (!isStaff && !isSelf)
        {
            // Managers may view their direct reports only.
            var isManager = employee.ManagerId.HasValue && employee.ManagerId == _currentUser.EmployeeId;
            if (!isManager) return Forbid();
        }

        ViewData["Title"] = employee.FullName;
        var model = new EmployeeDetailsViewModel
        {
            Employee = employee,
            CanViewSensitive = User.IsInRole(Roles.Administrator) || User.IsInRole(Roles.HrManager),
            CanEdit = isStaff,
            IsSelf = isSelf,
            RecentAttendance = await _attendance.GetForEmployeeAsync(id, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), DateOnly.FromDateTime(DateTime.UtcNow), ct),
            RecentLeave = (await _leave.GetPagedAsync(new LeaveFilter { EmployeeId = id, Page = 1, PageSize = 5 }, ct)).Items,
            Documents = await _documents.GetForEmployeeAsync(id, isStaff, ct)
        };

        if (!model.CanViewSensitive)
            model.Employee.AnnualCompensation = null;

        return View(model);
    }

    [HttpGet]
    [Authorize(Policy = Policies.HrOrAdmin)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        ViewData["Title"] = "Add employee";
        var model = new EmployeeFormViewModel
        {
            Departments = await _departments.GetLookupAsync(true, ct),
            Positions = await _positions.GetLookupAsync(null, true, ct),
            Managers = await _employees.GetManagerOptionsAsync(null, ct)
        };
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = Policies.HrOrAdmin)]
    public async Task<IActionResult> Create(EmployeeFormViewModel model, CancellationToken ct)
    {
        ViewData["Title"] = "Add employee";
        if (!ModelState.IsValid)
        {
            await PopulateAsync(model, null, ct);
            return View(model);
        }

        var id = await _employees.CreateAsync(model.Form, ct);
        TempData["Success"] = $"{model.Form.FirstName} {model.Form.LastName} was added.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    [Authorize(Policy = Policies.HrOrAdmin)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(id, ct);
        if (employee is null) return NotFound();

        var model = new EmployeeFormViewModel
        {
            IsEdit = true,
            Id = id,
            ProfilePhotoPath = employee.ProfilePhotoPath,
            EmployeeName = employee.FullName,
            Form = new EmployeeFormDto
            {
                EmployeeNumber = employee.EmployeeNumber,
                FirstName = employee.FirstName,
                LastName = employee.LastName,
                WorkEmail = employee.WorkEmail,
                PhoneNumber = employee.PhoneNumber,
                DepartmentId = employee.DepartmentId,
                PositionId = employee.PositionId,
                ManagerId = employee.ManagerId,
                EmploymentType = employee.EmploymentType,
                EmploymentStatus = employee.EmploymentStatus,
                HireDate = employee.HireDate,
                WorkLocation = employee.WorkLocation,
                AddressLine = employee.AddressLine,
                City = employee.City,
                Country = employee.Country,
                EmergencyContactName = employee.EmergencyContactName,
                EmergencyContactPhone = employee.EmergencyContactPhone,
                AnnualCompensation = employee.AnnualCompensation,
                CompensationCurrency = employee.CompensationCurrency
            }
        };
        await PopulateAsync(model, id, ct);
        ViewData["Title"] = $"Edit {employee.FullName}";
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = Policies.HrOrAdmin)]
    public async Task<IActionResult> Edit(int id, EmployeeFormViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            model.IsEdit = true;
            model.Id = id;
            await PopulateAsync(model, id, ct);
            ViewData["Title"] = "Edit employee";
            return View(model);
        }

        await _employees.UpdateAsync(id, model.Form, ct);
        TempData["Success"] = "Employee record updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = Policies.HrOrAdmin)]
    public async Task<IActionResult> ChangeStatus(int id, EmploymentStatus status, CancellationToken ct)
    {
        await _employees.ChangeStatusAsync(id, status, ct);
        TempData["Success"] = $"Employment status changed to {Ui.Humanize(status)}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = Policies.HrOrAdmin)]
    public async Task<IActionResult> UploadPhoto(int id, IFormFile? photo, CancellationToken ct)
    {
        if (photo is null || photo.Length == 0)
        {
            TempData["Error"] = "Select an image file to upload.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var ext = Path.GetExtension(photo.FileName);
        if (!allowed.Contains(ext, StringComparer.OrdinalIgnoreCase) || photo.Length > 4 * 1024 * 1024)
        {
            TempData["Error"] = "Profile photo must be a JPG, PNG or WEBP image under 4 MB.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        await using var stream = photo.OpenReadStream();
        var stored = await _storage.SaveAsync(stream, photo.FileName, ct);
        await _employees.UpdatePhotoAsync(id, stored, ct);
        TempData["Success"] = "Profile photo updated.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Photo(int id, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(id, ct);
        if (employee?.ProfilePhotoPath is null) return NotFound();
        if (!_storage.Exists(employee.ProfilePhotoPath)) return NotFound();
        return File(_storage.OpenRead(employee.ProfilePhotoPath), "image/jpeg");
    }

    private async Task PopulateAsync(EmployeeFormViewModel model, int? excludeId, CancellationToken ct)
    {
        model.Departments = await _departments.GetLookupAsync(true, ct);
        model.Positions = await _positions.GetLookupAsync(null, true, ct);
        model.Managers = await _employees.GetManagerOptionsAsync(excludeId, ct);
    }
}
