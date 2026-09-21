using Emplyo.Domain.Enums;

namespace Emplyo.Application.Documents;

public class DocumentListItemDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DocumentCategory Category { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public bool IsConfidential { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime UploadedAtUtc { get; set; }
}

public class DocumentDownloadDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string StoredFileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public bool IsConfidential { get; set; }
}

public class DocumentUploadRequest
{
    public int EmployeeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DocumentCategory Category { get; set; } = DocumentCategory.Other;
    public DateOnly? ExpirationDate { get; set; }
    public bool IsConfidential { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public Stream Content { get; set; } = Stream.Null;
}

public interface IDocumentService
{
    Task<IReadOnlyList<DocumentListItemDto>> GetForEmployeeAsync(int employeeId, bool includeConfidential, CancellationToken ct = default);
    Task<DocumentDownloadDto?> GetForDownloadAsync(int id, CancellationToken ct = default);
    Task<int> UploadAsync(DocumentUploadRequest request, string userId, string userName, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
