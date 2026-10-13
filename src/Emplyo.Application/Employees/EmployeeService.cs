using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Common.Models;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Emplyo.Domain.Exceptions;
using Emplyo.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Application.Employees;

public class EmployeeService : IEmployeeService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly IDateTimeProvider _clock;

    public EmployeeService(IApplicationDbContext db, IAuditService audit, IDateTimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<PagedResult<EmployeeListItemDto>> GetPagedAsync(EmployeeFilter filter, CancellationToken ct = default)
    {
        var (page, pageSize) = Paging.Normalize(filter.Page, filter.PageSize);

        var query = _db.Employees.AsNoTracking()
            .Include(e => e.Department)
            .Include(e => e.Position)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(e =>
                e.FirstName.Contains(term) ||
                e.LastName.Contains(term) ||
                e.EmployeeNumber.Contains(term) ||
                e.WorkEmail.Contains(term));
        }

        if (filter.DepartmentId.HasValue)
            query = query.Where(e => e.DepartmentId == filter.DepartmentId);
        if (filter.PositionId.HasValue)
            query = query.Where(e => e.PositionId == filter.PositionId);
        if (filter.Status.HasValue)
            query = query.Where(e => e.EmploymentStatus == filter.Status);
        if (filter.EmploymentType.HasValue)
            query = query.Where(e => e.EmploymentType == filter.EmploymentType);

        var desc = string.Equals(filter.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var sortKey = filter.SortBy?.Trim().ToLowerInvariant() ?? "name";
        query = sortKey switch
        {
            "lastname" => desc ? query.OrderByDescending(e => e.LastName).ThenBy(e => e.FirstName) : query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName),
            "employeenumber" => desc ? query.OrderByDescending(e => e.EmployeeNumber) : query.OrderBy(e => e.EmployeeNumber),
            "status" => desc ? query.OrderByDescending(e => e.EmploymentStatus) : query.OrderBy(e => e.EmploymentStatus),
            "hiredate" => desc ? query.OrderByDescending(e => e.HireDate) : query.OrderBy(e => e.HireDate),
            "department" => desc ? query.OrderByDescending(e => e.Department!.Name) : query.OrderBy(e => e.Department!.Name),
            _ => desc ? query.OrderByDescending(e => e.FirstName).ThenBy(e => e.LastName) : query.OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EmployeeListItemDto
            {
                Id = e.Id,
                EmployeeNumber = e.EmployeeNumber,
                FullName = e.FirstName + " " + e.LastName,
                WorkEmail = e.WorkEmail,
                DepartmentName = e.Department != null ? e.Department.Name : null,
                PositionTitle = e.Position != null ? e.Position.Title : null,
                Status = e.EmploymentStatus,
                HireDate = e.HireDate,
                ProfilePhotoPath = e.ProfilePhotoPath,
                HasUserAccount = e.UserId != null
            })
            .ToListAsync(ct);

        return PagedResult<EmployeeListItemDto>.Create(items, page, pageSize, total);
    }

    public async Task<EmployeeDetailDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.Employees.AsNoTracking()
            .Include(e => e.Department)
            .Include(e => e.Position)
            .Include(e => e.Manager)
            .Where(e => e.Id == id)
            .Select(e => MapDetail(e))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> CreateAsync(EmployeeFormDto dto, CancellationToken ct = default)
    {
        await ValidateAsync(dto, null, ct);

        var now = _clock.UtcNow;
        var employee = new Employee
        {
            EmployeeNumber = dto.EmployeeNumber.Trim(),
            FirstName = dto.FirstName.Trim(),
            LastName = dto.LastName.Trim(),
            WorkEmail = dto.WorkEmail.Trim().ToLowerInvariant(),
            PhoneNumber = dto.PhoneNumber,
            DepartmentId = dto.DepartmentId,
            PositionId = dto.PositionId,
            ManagerId = dto.ManagerId,
            EmploymentType = dto.EmploymentType,
            EmploymentStatus = dto.EmploymentStatus,
            HireDate = dto.HireDate,
            WorkLocation = dto.WorkLocation,
            AddressLine = dto.AddressLine,
            City = dto.City,
            Country = dto.Country,
            EmergencyContactName = dto.EmergencyContactName,
            EmergencyContactPhone = dto.EmergencyContactPhone,
            AnnualCompensation = dto.AnnualCompensation,
            CompensationCurrency = dto.CompensationCurrency,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _db.Employees.Add(employee);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "Employee.Created",
            TargetType = nameof(Employee),
            TargetId = employee.Id.ToString(),
            Metadata = $"EmployeeNumber={employee.EmployeeNumber}"
        }, ct);

        await EnsureLeaveBalancesAsync(employee.Id, ct);

        return employee.Id;
    }

    public async Task UpdateAsync(int id, EmployeeFormDto dto, CancellationToken ct = default)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("Employee not found.");

        await ValidateAsync(dto, id, ct);

        employee.EmployeeNumber = dto.EmployeeNumber.Trim();
        employee.FirstName = dto.FirstName.Trim();
        employee.LastName = dto.LastName.Trim();
        employee.WorkEmail = dto.WorkEmail.Trim().ToLowerInvariant();
        employee.PhoneNumber = dto.PhoneNumber;
        employee.DepartmentId = dto.DepartmentId;
        employee.PositionId = dto.PositionId;
        employee.ManagerId = dto.ManagerId;
        employee.EmploymentType = dto.EmploymentType;
        employee.HireDate = dto.HireDate;
        employee.WorkLocation = dto.WorkLocation;
        employee.AddressLine = dto.AddressLine;
        employee.City = dto.City;
        employee.Country = dto.Country;
        employee.EmergencyContactName = dto.EmergencyContactName;
        employee.EmergencyContactPhone = dto.EmergencyContactPhone;
        employee.AnnualCompensation = dto.AnnualCompensation;
        employee.CompensationCurrency = dto.CompensationCurrency;
        employee.UpdatedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "Employee.Updated",
            TargetType = nameof(Employee),
            TargetId = id.ToString()
        }, ct);
    }

    public async Task ChangeStatusAsync(int id, EmploymentStatus newStatus, CancellationToken ct = default)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("Employee not found.");

        if (employee.EmploymentStatus == newStatus)
        {
            return;
        }

        if (!EmploymentStatusPolicy.CanTransition(employee.EmploymentStatus, newStatus))
        {
            throw new BusinessRuleException(
                $"Cannot change employment status from {employee.EmploymentStatus} to {newStatus}.");
        }

        var previous = employee.EmploymentStatus;
        employee.EmploymentStatus = newStatus;
        employee.UpdatedAtUtc = _clock.UtcNow;

        if (newStatus == EmploymentStatus.Terminated)
        {
            employee.TerminationDate = DateOnly.FromDateTime(_clock.UtcNow);
        }
        else if (previous == EmploymentStatus.Terminated)
        {
            employee.TerminationDate = null;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "Employee.StatusChanged",
            TargetType = nameof(Employee),
            TargetId = id.ToString(),
            Metadata = $"From={previous};To={newStatus}"
        }, ct);
    }

    public async Task UpdatePhotoAsync(int id, string? storedFileName, CancellationToken ct = default)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("Employee not found.");

        employee.ProfilePhotoPath = storedFileName;
        employee.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateContactInfoAsync(int id, string? phoneNumber, string? addressLine, string? city, string? country,
        string? emergencyContactName, string? emergencyContactPhone, CancellationToken ct = default)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("Employee not found.");

        employee.PhoneNumber = phoneNumber;
        employee.AddressLine = addressLine;
        employee.City = city;
        employee.Country = country;
        employee.EmergencyContactName = emergencyContactName;
        employee.EmergencyContactPhone = emergencyContactPhone;
        employee.UpdatedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "Employee.ContactUpdated",
            TargetType = nameof(Employee),
            TargetId = id.ToString()
        }, ct);
    }

    public async Task<IReadOnlyList<ManagerOptionDto>> GetManagerOptionsAsync(int? excludeEmployeeId = null, CancellationToken ct = default)    {
        var query = _db.Employees.AsNoTracking()
            .Where(e => e.EmploymentStatus != EmploymentStatus.Terminated);

        if (excludeEmployeeId.HasValue)
            query = query.Where(e => e.Id != excludeEmployeeId);

        return await query
            .OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .Select(e => new ManagerOptionDto { Id = e.Id, FullName = e.FirstName + " " + e.LastName, EmployeeNumber = e.EmployeeNumber })
            .ToListAsync(ct);
    }

    public Task<bool> EmployeeNumberExistsAsync(string employeeNumber, int? excludeId = null, CancellationToken ct = default)
    {
        var normalized = employeeNumber.Trim();
        return _db.Employees.AnyAsync(e => e.EmployeeNumber == normalized && (!excludeId.HasValue || e.Id != excludeId), ct);
    }

    public Task<bool> WorkEmailExistsAsync(string workEmail, int? excludeId = null, CancellationToken ct = default)
    {
        var normalized = workEmail.Trim().ToLowerInvariant();
        return _db.Employees.AnyAsync(e => e.WorkEmail == normalized && (!excludeId.HasValue || e.Id != excludeId), ct);
    }

    private async Task ValidateAsync(EmployeeFormDto dto, int? excludeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.EmployeeNumber))
            throw new BusinessRuleException("Employee number is required.");
        if (string.IsNullOrWhiteSpace(dto.FirstName) || string.IsNullOrWhiteSpace(dto.LastName))
            throw new BusinessRuleException("First and last name are required.");
        if (string.IsNullOrWhiteSpace(dto.WorkEmail))
            throw new BusinessRuleException("Work email is required.");
        if (dto.HireDate > DateOnly.FromDateTime(_clock.UtcNow).AddYears(2))
            throw new BusinessRuleException("Hire date cannot be more than two years in the future.");

        if (await EmployeeNumberExistsAsync(dto.EmployeeNumber, excludeId, ct))
            throw new ConflictException($"Employee number '{dto.EmployeeNumber}' is already in use.");
        if (await WorkEmailExistsAsync(dto.WorkEmail, excludeId, ct))
            throw new ConflictException($"Work email '{dto.WorkEmail}' is already in use.");

        if (dto.DepartmentId.HasValue && !await _db.Departments.AnyAsync(d => d.Id == dto.DepartmentId, ct))
            throw new BusinessRuleException("Selected department does not exist.");
        if (dto.PositionId.HasValue && !await _db.Positions.AnyAsync(p => p.Id == dto.PositionId, ct))
            throw new BusinessRuleException("Selected position does not exist.");
        if (dto.ManagerId.HasValue)
        {
            if (excludeId.HasValue && dto.ManagerId == excludeId)
                throw new BusinessRuleException("An employee cannot be their own manager.");
            if (!await _db.Employees.AnyAsync(e => e.Id == dto.ManagerId, ct))
                throw new BusinessRuleException("Selected manager does not exist.");
        }
    }

    private async Task EnsureLeaveBalancesAsync(int employeeId, CancellationToken ct)
    {
        var year = _clock.UtcNow.Year;
        var leaveTypes = await _db.LeaveTypes.Where(t => t.IsActive).ToListAsync(ct);
        var existing = await _db.LeaveBalances
            .Where(b => b.EmployeeId == employeeId && b.Year == year)
            .Select(b => b.LeaveTypeId)
            .ToListAsync(ct);

        foreach (var type in leaveTypes.Where(t => !existing.Contains(t.Id)))
        {
            _db.LeaveBalances.Add(new LeaveBalance
            {
                EmployeeId = employeeId,
                LeaveTypeId = type.Id,
                Year = year,
                Entitlement = type.DefaultAnnualEntitlement,
                Used = 0m
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    private static EmployeeDetailDto MapDetail(Employee e) => new()
    {
        Id = e.Id,
        EmployeeNumber = e.EmployeeNumber,
        FirstName = e.FirstName,
        LastName = e.LastName,
        FullName = e.FirstName + " " + e.LastName,
        WorkEmail = e.WorkEmail,
        PhoneNumber = e.PhoneNumber,
        ProfilePhotoPath = e.ProfilePhotoPath,
        DepartmentId = e.DepartmentId,
        DepartmentName = e.Department != null ? e.Department.Name : null,
        PositionId = e.PositionId,
        PositionTitle = e.Position != null ? e.Position.Title : null,
        ManagerId = e.ManagerId,
        ManagerName = e.Manager != null ? e.Manager.FirstName + " " + e.Manager.LastName : null,
        EmploymentType = e.EmploymentType,
        EmploymentStatus = e.EmploymentStatus,
        HireDate = e.HireDate,
        TerminationDate = e.TerminationDate,
        WorkLocation = e.WorkLocation,
        AddressLine = e.AddressLine,
        City = e.City,
        Country = e.Country,
        EmergencyContactName = e.EmergencyContactName,
        EmergencyContactPhone = e.EmergencyContactPhone,
        AnnualCompensation = e.AnnualCompensation,
        CompensationCurrency = e.CompensationCurrency,
        UserId = e.UserId,
        CreatedAtUtc = e.CreatedAtUtc,
        UpdatedAtUtc = e.UpdatedAtUtc
    };
}
