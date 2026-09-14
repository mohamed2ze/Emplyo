using Emplyo.Domain.Enums;

namespace Emplyo.Domain.Entities;

public class Announcement
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public AnnouncementAudience Audience { get; set; } = AnnouncementAudience.Everyone;
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
