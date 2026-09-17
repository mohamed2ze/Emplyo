using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Common.Models;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Emplyo.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Application.Organization;

public class PositionService : IPositionService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly IDateTimeProvider _clock;

    public PositionService(IApplicationDbContext db, IAuditService audit, IDateTimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<PagedResult<PositionListItemDto>> GetPagedAsync(string? search, int? departmentId, bool? active, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var query = _db.Positions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Title.Contains(search.Trim()));
        if (departmentId.HasValue)
            query = query.Where(p => p.DepartmentId == departmentId);
        if (active.HasValue)
            query = query.Where(p => p.IsActive == active);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.Title)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new PositionListItemDto
            {
                Id = p.Id,
                Title = p.Title,
                Description = p.Description,
                DepartmentId = p.DepartmentId,
                DepartmentName = p.Department != null ? p.Department.Name : null,
                IsActive = p.IsActive,
                EmployeeCount = p.Employees.Count(e => e.EmploymentStatus != EmploymentStatus.Terminated)
            })
            .ToListAsync(ct);

        return PagedResult<PositionListItemDto>.Create(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<LookupItemDto>> GetLookupAsync(int? departmentId = null, bool activeOnly = true, CancellationToken ct = default)
    {
        var query = _db.Positions.AsNoTracking().AsQueryable();
        if (activeOnly) query = query.Where(p => p.IsActive);
        if (departmentId.HasValue) query = query.Where(p => p.DepartmentId == departmentId);
        return await query.OrderBy(p => p.Title)
            .Select(p => new LookupItemDto { Id = p.Id, Name = p.Title })
            .ToListAsync(ct);
    }

    public async Task<PositionListItemDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.Positions.AsNoTracking()
            .Include(p => p.Department)
            .Where(p => p.Id == id)
            .Select(p => new PositionListItemDto
            {
                Id = p.Id,
                Title = p.Title,
                Description = p.Description,
                DepartmentId = p.DepartmentId,
                DepartmentName = p.Department != null ? p.Department.Name : null,
                IsActive = p.IsActive,
                EmployeeCount = p.Employees.Count(e => e.EmploymentStatus != EmploymentStatus.Terminated)
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> CreateAsync(PositionFormDto dto, CancellationToken ct = default)
    {
        if (await TitleExistsAsync(dto.Title, dto.DepartmentId, null, ct))
            throw new ConflictException($"Position '{dto.Title}' already exists in the selected department.");

        var position = new Position
        {
            Title = dto.Title.Trim(),
            Description = dto.Description,
            DepartmentId = dto.DepartmentId,
            IsActive = dto.IsActive,
            CreatedAtUtc = _clock.UtcNow,
            UpdatedAtUtc = _clock.UtcNow
        };
        _db.Positions.Add(position);
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry { Action = "Position.Created", TargetType = nameof(Position), TargetId = position.Id.ToString() }, ct);
        return position.Id;
    }

    public async Task UpdateAsync(int id, PositionFormDto dto, CancellationToken ct = default)
    {
        var position = await _db.Positions.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Position not found.");
        if (await TitleExistsAsync(dto.Title, dto.DepartmentId, id, ct))
            throw new ConflictException($"Position '{dto.Title}' already exists in the selected department.");

        position.Title = dto.Title.Trim();
        position.Description = dto.Description;
        position.DepartmentId = dto.DepartmentId;
        position.IsActive = dto.IsActive;
        position.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry { Action = "Position.Updated", TargetType = nameof(Position), TargetId = id.ToString() }, ct);
    }

    public Task<bool> TitleExistsAsync(string title, int? departmentId, int? excludeId = null, CancellationToken ct = default)
    {
        var t = title.Trim();
        return _db.Positions.AnyAsync(p => p.Title == t && p.DepartmentId == departmentId && (!excludeId.HasValue || p.Id != excludeId), ct);
    }
}
