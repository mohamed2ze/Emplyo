using Emplyo.Domain.Enums;

namespace Emplyo.Domain.Entities;

public class EmployeeDocument
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public DocumentCategory Category { get; set; } = DocumentCategory.Other;
    public string StoredFileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public bool IsConfidential { get; set; }
    public string UploadedByUserId { get; set; } = string.Empty;
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[]? RowVersion { get; set; }
}
