using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Common.Models;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Emplyo.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Application.Announcements;

public class AnnouncementService : IAnnouncementService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly IDateTimeProvider _clock;

    public AnnouncementService(IApplicationDbContext db, IAuditService audit, IDateTimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<PagedResult<AnnouncementListItemDto>> GetPagedAsync(string? search, bool? published, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var query = _db.Announcements.AsNoTracking().Include(a => a.Department).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(a => a.Title.Contains(search.Trim()));
        if (published.HasValue)
            query = query.Where(a => a.IsPublished == published);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => MapListItem(a)).ToListAsync(ct);

        return PagedResult<AnnouncementListItemDto>.Create(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<AnnouncementListItemDto>> GetVisibleForUserAsync(IReadOnlyCollection<string> roles, int? departmentId, int take = 5, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var query = ActiveVisibleQuery(roles, departmentId, now)
            .OrderByDescending(a => a.PublishAtUtc ?? a.CreatedAtUtc)
            .Take(take);

        return await query.Select(a => MapListItem(a)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AnnouncementListItemDto>> GetActiveForRoleAsync(IReadOnlyCollection<string> roles, int? departmentId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        return await ActiveVisibleQuery(roles, departmentId, now)
            .OrderByDescending(a => a.PublishAtUtc ?? a.CreatedAtUtc)
            .Select(a => MapListItem(a)).ToListAsync(ct);
    }

    private IQueryable<Announcement> ActiveVisibleQuery(IReadOnlyCollection<string> roles, int? departmentId, DateTime now)
    {
        var query = _db.Announcements.AsNoTracking()
            .Include(a => a.Department)
            .Where(a => a.IsPublished)
            .Where(a => a.PublishAtUtc == null || a.PublishAtUtc <= now)
            .Where(a => a.ExpiresAtUtc == null || a.ExpiresAtUtc >= now);

        var isAdmin = roles.Contains("Administrator");
        var isHr = roles.Contains("HRManager");

        query = query.Where(a =>
            a.Audience == AnnouncementAudience.Everyone ||
            (a.Audience == AnnouncementAudience.Administrators && isAdmin) ||
            (a.Audience == AnnouncementAudience.HrManagers && (isHr || isAdmin)) ||
            (a.Audience == AnnouncementAudience.Employees) ||
            (a.Audience == AnnouncementAudience.Department && departmentId != null && a.DepartmentId == departmentId));

        return query;
    }

    public async Task<AnnouncementDetailDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var a = await _db.Announcements.AsNoTracking().Include(x => x.Department).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return null;
        return new AnnouncementDetailDto
        {
            Id = a.Id, Title = a.Title, Body = a.Body, Audience = a.Audience,
            DepartmentName = a.Department?.Name, IsPublished = a.IsPublished,
            PublishAtUtc = a.PublishAtUtc, ExpiresAtUtc = a.ExpiresAtUtc,
            CreatedByName = a.CreatedByName, CreatedAtUtc = a.CreatedAtUtc, UpdatedAtUtc = a.UpdatedAtUtc
        };
    }

    public async Task<int> CreateAsync(string userId, string userName, AnnouncementFormDto dto, CancellationToken ct = default)
    {
        Validate(dto);
        var now = _clock.UtcNow;
        var announcement = new Announcement
        {
            Title = dto.Title.Trim(),
            Body = dto.Body.Trim(),
            Audience = dto.Audience,
            DepartmentId = dto.Audience == AnnouncementAudience.Department ? dto.DepartmentId : null,
            IsPublished = dto.PublishNow,
            PublishAtUtc = dto.PublishNow ? now : dto.PublishAt,
            ExpiresAtUtc = dto.ExpiresAt,
            CreatedByUserId = userId,
            CreatedByName = userName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        _db.Announcements.Add(announcement);
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry { Action = "Announcement.Created", TargetType = nameof(Announcement), TargetId = announcement.Id.ToString() }, ct);
        return announcement.Id;
    }

    public async Task UpdateAsync(int id, AnnouncementFormDto dto, CancellationToken ct = default)
    {
        Validate(dto);
        var a = await _db.Announcements.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Announcement not found.");
        a.Title = dto.Title.Trim();
        a.Body = dto.Body.Trim();
        a.Audience = dto.Audience;
        a.DepartmentId = dto.Audience == AnnouncementAudience.Department ? dto.DepartmentId : null;
        a.ExpiresAtUtc = dto.ExpiresAt;
        if (dto.PublishAt.HasValue) a.PublishAtUtc = dto.PublishAt;
        a.UpdatedAtUtc = _clock.UtcNow;
        if (dto.PublishNow) a.IsPublished = true;
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry { Action = "Announcement.Updated", TargetType = nameof(Announcement), TargetId = id.ToString() }, ct);
    }

    public async Task SetPublishedAsync(int id, bool published, CancellationToken ct = default)
    {
        var a = await _db.Announcements.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Announcement not found.");
        a.IsPublished = published;
        if (published && a.PublishAtUtc is null) a.PublishAtUtc = _clock.UtcNow;
        a.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry
        {
            Action = published ? "Announcement.Published" : "Announcement.Archived",
            TargetType = nameof(Announcement), TargetId = id.ToString()
        }, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var a = await _db.Announcements.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Announcement not found.");
        _db.Announcements.Remove(a);
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry { Action = "Announcement.Deleted", TargetType = nameof(Announcement), TargetId = id.ToString() }, ct);
    }

    private static void Validate(AnnouncementFormDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new BusinessRuleException("Title is required.");
        if (string.IsNullOrWhiteSpace(dto.Body)) throw new BusinessRuleException("Body is required.");
        if (dto.ExpiresAt.HasValue && dto.PublishAt.HasValue && dto.ExpiresAt < dto.PublishAt)
            throw new BusinessRuleException("Expiration date cannot be before the publication date.");
        if (dto.Audience == AnnouncementAudience.Department && dto.DepartmentId is null)
            throw new BusinessRuleException("Select a department for department-targeted announcements.");
    }

    private static AnnouncementListItemDto MapListItem(Announcement a) => new()
    {
        Id = a.Id, Title = a.Title, Audience = a.Audience,
        DepartmentName = a.Department != null ? a.Department.Name : null,
        IsPublished = a.IsPublished, PublishAtUtc = a.PublishAtUtc, ExpiresAtUtc = a.ExpiresAtUtc,
        CreatedByName = a.CreatedByName, CreatedAtUtc = a.CreatedAtUtc
    };
}
