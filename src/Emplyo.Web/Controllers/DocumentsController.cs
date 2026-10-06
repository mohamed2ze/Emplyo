using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Documents;
using Emplyo.Application.Employees;
using Emplyo.Domain.Enums;
using Emplyo.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

[Authorize]
public class DocumentsController : Controller
{
    private readonly IDocumentService _documents;
    private readonly IEmployeeService _employees;
    private readonly IFileStorageService _storage;
    private readonly ICurrentUserService _currentUser;

    public DocumentsController(IDocumentService documents, IEmployeeService employees,
        IFileStorageService storage, ICurrentUserService currentUser)
    {
        _documents = documents;
        _employees = employees;
        _storage = storage;
        _currentUser = currentUser;
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    public async Task<IActionResult> Index(int? employeeId, CancellationToken ct)
    {
        ViewData["Title"] = "Documents";
        ViewBag.Employees = await _employees.GetManagerOptionsAsync(null, ct);
        ViewBag.EmployeeId = employeeId;
        if (!employeeId.HasValue) return View(Array.Empty<DocumentListItemDto>());

        ViewBag.Employee = await _employees.GetByIdAsync(employeeId.Value, ct);
        return View(await _documents.GetForEmployeeAsync(employeeId.Value, includeConfidential: true, ct));
    }

    public async Task<IActionResult> My(CancellationToken ct)
    {
        ViewData["Title"] = "My documents";
        var employeeId = _currentUser.EmployeeId;
        if (!employeeId.HasValue)
        {
            TempData["Warning"] = "Your account is not linked to an employee record.";
            return View("~/Views/Home/NoEmployee.cshtml");
        }
        var isStaff = User.IsInRole(Roles.Administrator) || User.IsInRole(Roles.HrManager);
        return View(await _documents.GetForEmployeeAsync(employeeId.Value, includeConfidential: isStaff, ct));
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpGet]
    public async Task<IActionResult> Upload(int employeeId, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(employeeId, ct);
        if (employee is null) return NotFound();
        ViewData["Title"] = $"Upload document · {employee.FullName}";
        ViewBag.Employee = employee;
        return View(new DocumentUploadRequest { EmployeeId = employeeId });
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpPost]
    public async Task<IActionResult> Upload(int employeeId, string title, DocumentCategory category, DateOnly? expirationDate, bool isConfidential, IFormFile? file, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(employeeId, ct);
        if (employee is null) return NotFound();
        ViewData["Title"] = "Upload document";
        ViewBag.Employee = employee;

        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Select a file to upload.";
            return RedirectToAction(nameof(Upload), new { employeeId });
        }

        await using var stream = file.OpenReadStream();
        var request = new DocumentUploadRequest
        {
            EmployeeId = employeeId,
            Title = string.IsNullOrWhiteSpace(title) ? file.FileName : title,
            Category = category,
            ExpirationDate = expirationDate,
            IsConfidential = isConfidential,
            OriginalFileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            Content = stream
        };

        var id = await _documents.UploadAsync(request, _currentUser.UserId!, _currentUser.DisplayName, ct);
        TempData["Success"] = "Document uploaded.";
        return RedirectToAction(nameof(Index), new { employeeId });
    }

    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var document = await _documents.GetForDownloadAsync(id, ct);
        if (document is null) return NotFound();

        var isStaff = User.IsInRole(Roles.Administrator) || User.IsInRole(Roles.HrManager);
        var isOwner = _currentUser.EmployeeId == document.EmployeeId;

        if (!isStaff && !isOwner) return Forbid();
        if (document.IsConfidential && !isStaff) return Forbid();
        if (!_storage.Exists(document.StoredFileName)) return NotFound();

        var stream = _storage.OpenRead(document.StoredFileName);
        return File(stream, document.ContentType, document.OriginalFileName);
    }

    [Authorize(Policy = Policies.HrOrAdmin)]
    [HttpPost]
    public async Task<IActionResult> Delete(int id, int employeeId, CancellationToken ct)
    {
        await _documents.DeleteAsync(id, ct);
        TempData["Success"] = "Document deleted.";
        return RedirectToAction(nameof(Index), new { employeeId });
    }
}
