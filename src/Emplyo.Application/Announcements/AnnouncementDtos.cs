using System.ComponentModel.DataAnnotations;
using Emplyo.Application.Common.Models;
using Emplyo.Domain.Enums;

namespace Emplyo.Application.Announcements;

public class AnnouncementListItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public AnnouncementAudience Audience { get; set; }
    public string? DepartmentName { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class AnnouncementDetailDto : AnnouncementListItemDto
{
    public string Body { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

public class AnnouncementFormDto
{
    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    [DataType(DataType.MultilineText)]
    public string Body { get; set; } = string.Empty;

    [Display(Name = "Audience")]
    public AnnouncementAudience Audience { get; set; } = AnnouncementAudience.Everyone;

    [Display(Name = "Department")]
    public int? DepartmentId { get; set; }

    [Display(Name = "Publish now")]
    public bool PublishNow { get; set; }

    [DataType(DataType.DateTime)]
    [Display(Name = "Publish at")]
    public DateTime? PublishAt { get; set; }

    [DataType(DataType.DateTime)]
    [Display(Name = "Expires at")]
    public DateTime? ExpiresAt { get; set; }
}

public interface IAnnouncementService
{
    Task<PagedResult<AnnouncementListItemDto>> GetPagedAsync(string? search, bool? published, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<AnnouncementListItemDto>> GetVisibleForUserAsync(IReadOnlyCollection<string> roles, int? departmentId, int take = 5, CancellationToken ct = default);
    Task<IReadOnlyList<AnnouncementListItemDto>> GetActiveForRoleAsync(IReadOnlyCollection<string> roles, int? departmentId, CancellationToken ct = default);
    Task<AnnouncementDetailDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<int> CreateAsync(string userId, string userName, AnnouncementFormDto dto, CancellationToken ct = default);
    Task UpdateAsync(int id, AnnouncementFormDto dto, CancellationToken ct = default);
    Task SetPublishedAsync(int id, bool published, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
