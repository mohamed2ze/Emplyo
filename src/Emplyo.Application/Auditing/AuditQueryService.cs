using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Application.Auditing;

public class AuditQueryService : IAuditQueryService
{
    private readonly IApplicationDbContext _db;

    public AuditQueryService(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<AuditLogDto>> GetPagedAsync(AuditFilter filter, CancellationToken ct = default)
    {
        var (page, pageSize) = Paging.Normalize(filter.Page, filter.PageSize);
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(a => a.ActorName.Contains(term)
                || a.Action.Contains(term)
                || (a.TargetId != null && a.TargetId.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(a => a.Action == filter.Action);
        if (!string.IsNullOrWhiteSpace(filter.TargetType))
            query = query.Where(a => a.TargetType == filter.TargetType);
        if (filter.Outcome.HasValue)
            query = query.Where(a => a.Outcome == filter.Outcome);
        if (filter.From.HasValue)
        {
            var from = filter.From.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(a => a.TimestampUtc >= from);
        }
        if (filter.To.HasValue)
        {
            var to = filter.To.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(a => a.TimestampUtc <= to);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.TimestampUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AuditLogDto
            {
                Id = a.Id, EventId = a.EventId, ActorName = a.ActorName, Action = a.Action,
                TargetType = a.TargetType, TargetId = a.TargetId, Outcome = a.Outcome,
                Metadata = a.Metadata, TimestampUtc = a.TimestampUtc
            }).ToListAsync(ct);

        return PagedResult<AuditLogDto>.Create(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<string>> GetDistinctActionsAsync(CancellationToken ct = default)
        => await _db.AuditLogs.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct);
}
