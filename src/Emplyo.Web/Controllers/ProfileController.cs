using System.ComponentModel.DataAnnotations;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Employees;
using Emplyo.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

public class ContactInfoViewModel
{
    [Phone, StringLength(30)]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "Address")]
    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }

    [Display(Name = "Emergency contact name")]
    public string? EmergencyContactName { get; set; }

    [Display(Name = "Emergency contact phone")]
    public string? EmergencyContactPhone { get; set; }
}

public class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 12), DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

[Authorize]
public class ProfileController : Controller
{
    private readonly IEmployeeService _employees;
    private readonly ICurrentUserService _currentUser;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IFileStorageService _storage;

    public ProfileController(IEmployeeService employees, ICurrentUserService currentUser,
        UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager,
        IFileStorageService storage)
    {
        _employees = employees;
        _currentUser = currentUser;
        _userManager = userManager;
        _signInManager = signInManager;
        _storage = storage;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "My profile";
        var employeeId = _currentUser.EmployeeId;
        if (!employeeId.HasValue)
        {
            TempData["Warning"] = "Your account is not linked to an employee record.";
            return View("~/Views/Home/NoEmployee.cshtml");
        }

        var employee = await _employees.GetByIdAsync(employeeId.Value, ct);
        if (employee is null) return View("~/Views/Home/NoEmployee.cshtml");
        employee.AnnualCompensation = null; // Personal profile never exposes compensation.
        return View(employee);
    }

    [HttpGet]
    public async Task<IActionResult> EditContact(CancellationToken ct)
    {
        var employeeId = _currentUser.EmployeeId;
        if (!employeeId.HasValue) return View("~/Views/Home/NoEmployee.cshtml");
        var employee = await _employees.GetByIdAsync(employeeId.Value, ct);
        if (employee is null) return View("~/Views/Home/NoEmployee.cshtml");
        ViewData["Title"] = "Update my information";
        return View(new ContactInfoViewModel
        {
            PhoneNumber = employee.PhoneNumber,
            AddressLine = employee.AddressLine,
            City = employee.City,
            Country = employee.Country,
            EmergencyContactName = employee.EmergencyContactName,
            EmergencyContactPhone = employee.EmergencyContactPhone
        });
    }

    [HttpPost]
    public async Task<IActionResult> EditContact(ContactInfoViewModel model, CancellationToken ct)
    {
        var employeeId = _currentUser.EmployeeId;
        if (!employeeId.HasValue) return View("~/Views/Home/NoEmployee.cshtml");
        if (!ModelState.IsValid) return View(model);

        await _employees.UpdateContactInfoAsync(employeeId.Value, model.PhoneNumber, model.AddressLine,
            model.City, model.Country, model.EmergencyContactName, model.EmergencyContactPhone, ct);
        TempData["Success"] = "Your information has been updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult ChangePassword()
    {
        ViewData["Title"] = "Change password";
        return View(new ChangePasswordViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        ViewData["Title"] = "Change password";
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToAction("Login", "Account");

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (result.Succeeded)
        {
            await _signInManager.RefreshSignInAsync(user);
            TempData["Success"] = "Your password has been changed.";
            return RedirectToAction(nameof(Index));
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> UploadPhoto(IFormFile? photo, CancellationToken ct)
    {
        var employeeId = _currentUser.EmployeeId;
        if (!employeeId.HasValue) return View("~/Views/Home/NoEmployee.cshtml");

        if (photo is null || photo.Length == 0)
        {
            TempData["Error"] = "Select an image file.";
            return RedirectToAction(nameof(Index));
        }

        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        if (!allowed.Contains(Path.GetExtension(photo.FileName), StringComparer.OrdinalIgnoreCase) || photo.Length > 4 * 1024 * 1024)
        {
            TempData["Error"] = "Profile photo must be a JPG, PNG or WEBP image under 4 MB.";
            return RedirectToAction(nameof(Index));
        }

        await using var stream = photo.OpenReadStream();
        var stored = await _storage.SaveAsync(stream, photo.FileName, ct);
        await _employees.UpdatePhotoAsync(employeeId.Value, stored, ct);
        TempData["Success"] = "Profile photo updated.";
        return RedirectToAction(nameof(Index));
    }
}
