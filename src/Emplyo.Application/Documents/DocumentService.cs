using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Common.Models;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Application.Documents;

public class DocumentService : IDocumentService
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg", ".txt", ".csv"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "image/png", "image/jpeg", "text/plain", "text/csv", "application/octet-stream"
    };

    private readonly IApplicationDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly IAuditService _audit;
    private readonly IDateTimeProvider _clock;

    public DocumentService(IApplicationDbContext db, IFileStorageService storage, IAuditService audit, IDateTimeProvider clock)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<DocumentListItemDto>> GetForEmployeeAsync(int employeeId, bool includeConfidential, CancellationToken ct = default)
    {
        var query = _db.EmployeeDocuments.AsNoTracking().Where(d => d.EmployeeId == employeeId);
        if (!includeConfidential) query = query.Where(d => !d.IsConfidential);

        return await query.OrderByDescending(d => d.UploadedAtUtc)
            .Select(d => new DocumentListItemDto
            {
                Id = d.Id, EmployeeId = d.EmployeeId, Title = d.Title, Category = d.Category,
                OriginalFileName = d.OriginalFileName, ContentType = d.ContentType, SizeBytes = d.SizeBytes,
                ExpirationDate = d.ExpirationDate, IsConfidential = d.IsConfidential,
                UploadedByName = d.UploadedByName, UploadedAtUtc = d.UploadedAtUtc,
                EmployeeName = d.Employee.FirstName + " " + d.Employee.LastName
            })
            .ToListAsync(ct);
    }

    public async Task<DocumentDownloadDto?> GetForDownloadAsync(int id, CancellationToken ct = default)
    {
        return await _db.EmployeeDocuments.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new DocumentDownloadDto
            {
                Id = d.Id, EmployeeId = d.EmployeeId, StoredFileName = d.StoredFileName,
                OriginalFileName = d.OriginalFileName, ContentType = d.ContentType, IsConfidential = d.IsConfidential
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> UploadAsync(DocumentUploadRequest request, string userId, string userName, CancellationToken ct = default)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == request.EmployeeId, ct))
            throw new NotFoundException("Employee not found.");
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new BusinessRuleException("Document title is required.");
        if (request.SizeBytes <= 0)
            throw new BusinessRuleException("The uploaded file is empty.");
        if (request.SizeBytes > MaxFileSizeBytes)
            throw new BusinessRuleException($"File exceeds the maximum allowed size of {MaxFileSizeBytes / (1024 * 1024)} MB.");

        var extension = Path.GetExtension(request.OriginalFileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            throw new BusinessRuleException($"File type '{extension}' is not allowed.");
        if (!string.IsNullOrEmpty(request.ContentType) && !AllowedContentTypes.Contains(request.ContentType))
            throw new BusinessRuleException("The file content type is not allowed.");

        var storedName = await _storage.SaveAsync(request.Content, request.OriginalFileName, ct);

        var document = new EmployeeDocument
        {
            EmployeeId = request.EmployeeId,
            Title = request.Title.Trim(),
            Category = request.Category,
            StoredFileName = storedName,
            OriginalFileName = Path.GetFileName(request.OriginalFileName),
            ContentType = string.IsNullOrEmpty(request.ContentType) ? "application/octet-stream" : request.ContentType,
            SizeBytes = request.SizeBytes,
            ExpirationDate = request.ExpirationDate,
            IsConfidential = request.IsConfidential,
            UploadedByUserId = userId,
            UploadedByName = userName,
            UploadedAtUtc = _clock.UtcNow
        };

        _db.EmployeeDocuments.Add(document);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "Document.Uploaded", TargetType = nameof(EmployeeDocument), TargetId = document.Id.ToString(),
            Metadata = $"EmployeeId={request.EmployeeId};Category={request.Category}"
        }, ct);

        return document.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var document = await _db.EmployeeDocuments.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new NotFoundException("Document not found.");

        var storedName = document.StoredFileName;
        _db.EmployeeDocuments.Remove(document);
        await _db.SaveChangesAsync(ct);
        try { _storage.Delete(storedName); } catch { /* file already removed */ }

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "Document.Deleted", TargetType = nameof(EmployeeDocument), TargetId = id.ToString()
        }, ct);
    }
}
