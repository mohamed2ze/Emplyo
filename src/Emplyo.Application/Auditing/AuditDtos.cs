using Emplyo.Application.Common.Models;
using Emplyo.Domain.Enums;

namespace Emplyo.Application.Auditing;

public class AuditLogDto
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public AuditOutcome Outcome { get; set; }
    public string? Metadata { get; set; }
    public DateTime TimestampUtc { get; set; }
}

public class AuditFilter
{
    public string? Search { get; set; }
    public string? Action { get; set; }
    public string? TargetType { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public interface IAuditQueryService
{
    Task<PagedResult<AuditLogDto>> GetPagedAsync(AuditFilter filter, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetDistinctActionsAsync(CancellationToken ct = default);
}
