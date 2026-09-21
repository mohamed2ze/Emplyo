using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Common.Models;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Emplyo.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Application.Lifecycle;

public class LifecycleService : ILifecycleService
{
    private static readonly string[] DefaultOnboardingTasks =
    {
        "Sign employment contract",
        "Submit identification documents",
        "Set up workstation and equipment",
        "Create corporate email account",
        "Complete orientation session"
    };

    private static readonly string[] DefaultOffboardingTasks =
    {
        "Return company equipment",
        "Complete knowledge handover",
        "Revoke system and building access",
        "Process final settlement",
        "Conduct exit interview"
    };

    private readonly IApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public LifecycleService(IApplicationDbContext db, IAuditService audit, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<PagedResult<OnboardingOverviewDto>> GetOnboardingAsync(string? search, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var query = _db.Employees.AsNoTracking()
            .Where(e => e.EmploymentStatus == EmploymentStatus.Pending || e.EmploymentStatus == EmploymentStatus.Active)
            .Where(e => e.OnboardingTasks.Any())
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e => e.FirstName.Contains(term) || e.LastName.Contains(term) || e.EmployeeNumber.Contains(term));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.HireDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new OnboardingOverviewDto
            {
                EmployeeId = e.Id,
                EmployeeName = e.FirstName + " " + e.LastName,
                EmployeeNumber = e.EmployeeNumber,
                DepartmentName = e.Department != null ? e.Department.Name : null,
                PositionTitle = e.Position != null ? e.Position.Title : null,
                HireDate = e.HireDate,
                EmploymentStatus = e.EmploymentStatus,
                TotalTasks = e.OnboardingTasks.Count,
                CompletedTasks = e.OnboardingTasks.Count(t => t.Status == TaskItemStatus.Completed)
            })
            .ToListAsync(ct);

        return PagedResult<OnboardingOverviewDto>.Create(items, page, pageSize, total);
    }

    public async Task<OnboardingDetailDto?> GetOnboardingDetailAsync(int employeeId, CancellationToken ct = default)
    {
        var employee = await _db.Employees.AsNoTracking()
            .Include(e => e.Department).Include(e => e.Position)
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return null;

        var tasks = await _db.OnboardingTasks.AsNoTracking()
            .Where(t => t.EmployeeId == employeeId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .Select(t => new TaskDto
            {
                Id = t.Id, Title = t.Title, Description = t.Description, DueDate = t.DueDate,
                Status = t.Status, CompletedAtUtc = t.CompletedAtUtc, SortOrder = t.SortOrder
            }).ToListAsync(ct);

        return new OnboardingDetailDto
        {
            EmployeeId = employee.Id,
            EmployeeName = employee.FirstName + " " + employee.LastName,
            DepartmentName = employee.Department?.Name,
            PositionTitle = employee.Position?.Title,
            HireDate = employee.HireDate,
            Tasks = tasks
        };
    }

    public async Task ApplyOnboardingTemplateAsync(int employeeId, CancellationToken ct = default)
    {
        var exists = await _db.Employees.AnyAsync(e => e.Id == employeeId, ct);
        if (!exists) throw new NotFoundException("Employee not found.");

        if (await _db.OnboardingTasks.AnyAsync(t => t.EmployeeId == employeeId, ct))
            throw new ConflictException("This employee already has onboarding tasks.");

        var now = _clock.UtcNow;
        var order = 1;
        foreach (var title in DefaultOnboardingTasks)
        {
            _db.OnboardingTasks.Add(new OnboardingTask
            {
                EmployeeId = employeeId, Title = title, Status = TaskItemStatus.Pending,
                SortOrder = order++, CreatedAtUtc = now, UpdatedAtUtc = now
            });
        }
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(new AuditEntry { Action = "Onboarding.TemplateApplied", TargetType = nameof(Employee), TargetId = employeeId.ToString() }, ct);
    }

    public async Task AddOnboardingTaskAsync(int employeeId, TaskFormDto dto, CancellationToken ct = default)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId, ct))
            throw new NotFoundException("Employee not found.");

        var now = _clock.UtcNow;
        var maxOrder = await _db.OnboardingTasks.Where(t => t.EmployeeId == employeeId)
            .Select(t => (int?)t.SortOrder).MaxAsync(ct) ?? 0;

        _db.OnboardingTasks.Add(new OnboardingTask
        {
            EmployeeId = employeeId, Title = dto.Title.Trim(), Description = dto.Description,
            DueDate = dto.DueDate, SortOrder = dto.SortOrder > 0 ? dto.SortOrder : maxOrder + 1,
            CreatedAtUtc = now, UpdatedAtUtc = now
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetOnboardingTaskStatusAsync(int taskId, TaskItemStatus status, CancellationToken ct = default)
    {
        var task = await _db.OnboardingTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Onboarding task not found.");
        task.Status = status;
        task.CompletedAtUtc = status == TaskItemStatus.Completed ? _clock.UtcNow : null;
        task.CompletedByUserId = status == TaskItemStatus.Completed ? _currentUser.UserId : null;
        task.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteOnboardingTaskAsync(int taskId, CancellationToken ct = default)
    {
        var task = await _db.OnboardingTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Onboarding task not found.");
        _db.OnboardingTasks.Remove(task);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<OffboardingOverviewDto>> GetOffboardingAsync(string? search, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var query = _db.OffboardingCases.AsNoTracking()
            .Include(c => c.Employee).ThenInclude(e => e.Department)
            .Include(c => c.Tasks)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => c.Employee.FirstName.Contains(term) || c.Employee.LastName.Contains(term));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.InitiatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new OffboardingOverviewDto
            {
                CaseId = c.Id,
                EmployeeId = c.EmployeeId,
                EmployeeName = c.Employee.FirstName + " " + c.Employee.LastName,
                EmployeeNumber = c.Employee.EmployeeNumber,
                DepartmentName = c.Employee.Department != null ? c.Employee.Department.Name : null,
                DepartureDate = c.DepartureDate,
                Reason = c.Reason,
                Status = c.Status,
                TotalTasks = c.Tasks.Count,
                CompletedTasks = c.Tasks.Count(t => t.Status == TaskItemStatus.Completed),
                InitiatedAtUtc = c.InitiatedAtUtc
            })
            .ToListAsync(ct);

        return PagedResult<OffboardingOverviewDto>.Create(items, page, pageSize, total);
    }

    public async Task<OffboardingDetailDto?> GetOffboardingDetailAsync(int caseId, CancellationToken ct = default)
    {
        var c = await _db.OffboardingCases.AsNoTracking()
            .Include(x => x.Employee)
            .FirstOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null) return null;

        var tasks = await _db.OffboardingTasks.AsNoTracking()
            .Where(t => t.OffboardingCaseId == caseId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .Select(t => new TaskDto
            {
                Id = t.Id, Title = t.Title, Description = t.Description, DueDate = t.DueDate,
                Status = t.Status, CompletedAtUtc = t.CompletedAtUtc, SortOrder = t.SortOrder
            }).ToListAsync(ct);

        return new OffboardingDetailDto
        {
            CaseId = c.Id,
            EmployeeId = c.EmployeeId,
            EmployeeName = c.Employee.FirstName + " " + c.Employee.LastName,
            DepartureDate = c.DepartureDate,
            Reason = c.Reason,
            Status = c.Status,
            InitiatedAtUtc = c.InitiatedAtUtc,
            CompletedAtUtc = c.CompletedAtUtc,
            Tasks = tasks
        };
    }

    public async Task<int> InitiateOffboardingAsync(int employeeId, OffboardingInitiateDto dto, CancellationToken ct = default)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw new NotFoundException("Employee not found.");
        if (employee.EmploymentStatus == EmploymentStatus.Terminated)
            throw new BusinessRuleException("This employee is already terminated.");
        if (await _db.OffboardingCases.AnyAsync(c => c.EmployeeId == employeeId
                && c.Status != OffboardingStatus.Cancelled && c.Status != OffboardingStatus.Completed, ct))
            throw new ConflictException("An active offboarding case already exists for this employee.");

        var now = _clock.UtcNow;
        var offboardingCase = new OffboardingCase
        {
            EmployeeId = employeeId,
            DepartureDate = dto.DepartureDate,
            Reason = dto.Reason,
            Status = OffboardingStatus.Initiated,
            InitiatedByUserId = _currentUser.UserId ?? string.Empty,
            InitiatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var order = 1;
        foreach (var title in DefaultOffboardingTasks)
        {
            offboardingCase.Tasks.Add(new OffboardingTask
            {
                Title = title, Status = TaskItemStatus.Pending, SortOrder = order++,
                CreatedAtUtc = now, UpdatedAtUtc = now
            });
        }

        _db.OffboardingCases.Add(offboardingCase);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "Offboarding.Initiated", TargetType = nameof(OffboardingCase), TargetId = offboardingCase.Id.ToString(),
            Metadata = $"EmployeeId={employeeId};DepartureDate={dto.DepartureDate}"
        }, ct);

        return offboardingCase.Id;
    }

    public async Task SetOffboardingTaskStatusAsync(int taskId, TaskItemStatus status, CancellationToken ct = default)
    {
        var task = await _db.OffboardingTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Offboarding task not found.");
        task.Status = status;
        task.CompletedAtUtc = status == TaskItemStatus.Completed ? _clock.UtcNow : null;
        task.CompletedByUserId = status == TaskItemStatus.Completed ? _currentUser.UserId : null;
        task.UpdatedAtUtc = _clock.UtcNow;

        var offboardingCase = await _db.OffboardingCases.FirstOrDefaultAsync(c => c.Id == task.OffboardingCaseId, ct);
        if (offboardingCase is not null && offboardingCase.Status == OffboardingStatus.Initiated)
        {
            offboardingCase.Status = OffboardingStatus.InProgress;
            offboardingCase.UpdatedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task CompleteOffboardingAsync(int caseId, CancellationToken ct = default)
    {
        var c = await _db.OffboardingCases
            .Include(x => x.Employee)
            .Include(x => x.Tasks)
            .FirstOrDefaultAsync(x => x.Id == caseId, ct)
            ?? throw new NotFoundException("Offboarding case not found.");

        if (c.Tasks.Any(t => t.Status != TaskItemStatus.Completed))
            throw new BusinessRuleException("All offboarding tasks must be completed first.");

        c.Status = OffboardingStatus.Completed;
        c.CompletedAtUtc = _clock.UtcNow;
        c.UpdatedAtUtc = _clock.UtcNow;

        var employee = c.Employee;
        if (employee.EmploymentStatus != EmploymentStatus.Terminated)
        {
            employee.EmploymentStatus = EmploymentStatus.Terminated;
            employee.TerminationDate = c.DepartureDate;
            employee.UpdatedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "Offboarding.Completed", TargetType = nameof(OffboardingCase), TargetId = caseId.ToString(),
            Metadata = $"EmployeeId={employee.Id}"
        }, ct);
    }
}
