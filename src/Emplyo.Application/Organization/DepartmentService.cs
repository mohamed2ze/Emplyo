using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Common.Models;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Emplyo.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Application.Organization;

public class DepartmentService : IDepartmentService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly IDateTimeProvider _clock;

    public DepartmentService(IApplicationDbContext db, IAuditService audit, IDateTimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<PagedResult<DepartmentListItemDto>> GetPagedAsync(string? search, bool? active, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var query = _db.Departments.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(d => d.Name.Contains(search.Trim()));
        if (active.HasValue)
            query = query.Where(d => d.IsActive == active);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(d => d.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new DepartmentListItemDto
            {
                Id = d.Id,
                Name = d.Name,
                Description = d.Description,
                DepartmentHeadName = d.DepartmentHead != null ? d.DepartmentHead.FirstName + " " + d.DepartmentHead.LastName : null,
                IsActive = d.IsActive,
                EmployeeCount = d.Employees.Count(e => e.EmploymentStatus != EmploymentStatus.Terminated),
                PositionCount = d.Positions.Count(p => p.IsActive)
            })
            .ToListAsync(ct);

        return PagedResult<DepartmentListItemDto>.Create(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<LookupItemDto>> GetLookupAsync(bool activeOnly = true, CancellationToken ct = default)
    {
        var query = _db.Departments.AsNoTracking().AsQueryable();
        if (activeOnly) query = query.Where(d => d.IsActive);
        return await query.OrderBy(d => d.Name)
            .Select(d => new LookupItemDto { Id = d.Id, Name = d.Name })
            .ToListAsync(ct);
    }

    public async Task<DepartmentDetailDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var dept = await _db.Departments.AsNoTracking()
            .Include(d => d.DepartmentHead)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dept is null) return null;

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.DepartmentId == id && e.EmploymentStatus != EmploymentStatus.Terminated)
            .OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .Select(e => new EmployeeSummaryDto
            {
                Id = e.Id,
                FullName = e.FirstName + " " + e.LastName,
                PositionTitle = e.Position != null ? e.Position.Title : null,
                Status = e.EmploymentStatus,
                ProfilePhotoPath = e.ProfilePhotoPath
            })
            .ToListAsync(ct);

        var positions = await _db.Positions.AsNoTracking()
            .Where(p => p.DepartmentId == id)
            .OrderBy(p => p.Title)
            .Select(p => new PositionListItemDto
            {
                Id = p.Id,
                Title = p.Title,
                Description = p.Description,
                DepartmentId = p.DepartmentId,
                DepartmentName = dept.Name,
                IsActive = p.IsActive,
                EmployeeCount = p.Employees.Count(e => e.EmploymentStatus != EmploymentStatus.Terminated)
            })
            .ToListAsync(ct);

        return new DepartmentDetailDto
        {
            Id = dept.Id,
            Name = dept.Name,
            Description = dept.Description,
            DepartmentHeadId = dept.DepartmentHeadId,
            DepartmentHeadName = dept.DepartmentHead != null ? dept.DepartmentHead.FirstName + " " + dept.DepartmentHead.LastName : null,
            IsActive = dept.IsActive,
            EmployeeCount = employees.Count,
            CreatedAtUtc = dept.CreatedAtUtc,
            UpdatedAtUtc = dept.UpdatedAtUtc,
            Employees = employees,
            Positions = positions
        };
    }

    public async Task<int> CreateAsync(DepartmentFormDto dto, CancellationToken ct = default)
    {
        if (await NameExistsAsync(dto.Name, null, ct))
            throw new ConflictException($"Department '{dto.Name}' already exists.");

        var dept = new Department
        {
            Name = dto.Name.Trim(),
            Description = dto.Description,
            DepartmentHeadId = dto.DepartmentHeadId,
            IsActive = dto.IsActive,
            CreatedAtUtc = _clock.UtcNow,
            UpdatedAtUtc = _clock.UtcNow
        };
        _db.Departments.Add(dept);
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry { Action = "Department.Created", TargetType = nameof(Department), TargetId = dept.Id.ToString() }, ct);
        return dept.Id;
    }

    public async Task UpdateAsync(int id, DepartmentFormDto dto, CancellationToken ct = default)
    {
        var dept = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new NotFoundException("Department not found.");
        if (await NameExistsAsync(dto.Name, id, ct))
            throw new ConflictException($"Department '{dto.Name}' already exists.");

        dept.Name = dto.Name.Trim();
        dept.Description = dto.Description;
        dept.DepartmentHeadId = dto.DepartmentHeadId;
        dept.IsActive = dto.IsActive;
        dept.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry { Action = "Department.Updated", TargetType = nameof(Department), TargetId = id.ToString() }, ct);
    }

    public Task<bool> NameExistsAsync(string name, int? excludeId = null, CancellationToken ct = default)
    {
        var n = name.Trim();
        return _db.Departments.AnyAsync(d => d.Name == n && (!excludeId.HasValue || d.Id != excludeId), ct);
    }
}
