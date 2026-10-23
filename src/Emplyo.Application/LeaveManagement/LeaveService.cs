using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Common.Models;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Emplyo.Domain.Exceptions;
using Emplyo.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Application.LeaveManagement;

public class LeaveService : ILeaveService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly INotificationService _notifications;
    private readonly IDateTimeProvider _clock;

    public LeaveService(IApplicationDbContext db, IAuditService audit, ICurrentUserService currentUser,
        INotificationService notifications, IDateTimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _currentUser = currentUser;
        _notifications = notifications;
        _clock = clock;
    }

    public async Task<PagedResult<LeaveRequestListItemDto>> GetPagedAsync(LeaveFilter filter, CancellationToken ct = default)
    {
        var (page, pageSize) = Paging.Normalize(filter.Page, filter.PageSize);
        var query = _db.LeaveRequests.AsNoTracking()
            .Include(l => l.Employee).ThenInclude(e => e.Department)
            .Include(l => l.LeaveType)
            .AsQueryable();

        if (filter.EmployeeId.HasValue)
            query = query.Where(l => l.EmployeeId == filter.EmployeeId);
        if (filter.LeaveTypeId.HasValue)
            query = query.Where(l => l.LeaveTypeId == filter.LeaveTypeId);
        if (filter.Status.HasValue)
            query = query.Where(l => l.Status == filter.Status);
        if (filter.From.HasValue)
            query = query.Where(l => l.EndDate >= filter.From);
        if (filter.To.HasValue)
            query = query.Where(l => l.StartDate <= filter.To);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(l => l.Employee.FirstName.Contains(term)
                || l.Employee.LastName.Contains(term)
                || l.Employee.EmployeeNumber.Contains(term));
        }

        var desc = string.Equals(filter.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = (filter.SortBy?.ToLowerInvariant()) switch
        {
            "employee" => desc ? query.OrderByDescending(l => l.Employee.FirstName) : query.OrderBy(l => l.Employee.FirstName),
            "status" => desc ? query.OrderByDescending(l => l.Status) : query.OrderBy(l => l.Status),
            "startdate" => desc ? query.OrderByDescending(l => l.StartDate) : query.OrderBy(l => l.StartDate),
            _ => desc ? query.OrderByDescending(l => l.CreatedAtUtc) : query.OrderBy(l => l.CreatedAtUtc)
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new LeaveRequestListItemDto
            {
                Id = l.Id,
                EmployeeId = l.EmployeeId,
                EmployeeName = l.Employee.FirstName + " " + l.Employee.LastName,
                EmployeeNumber = l.Employee.EmployeeNumber,
                DepartmentName = l.Employee.Department != null ? l.Employee.Department.Name : null,
                LeaveTypeName = l.LeaveType.Name,
                StartDate = l.StartDate,
                EndDate = l.EndDate,
                TotalDays = l.TotalDays,
                Status = l.Status,
                CreatedAtUtc = l.CreatedAtUtc
            })
            .ToListAsync(ct);

        return PagedResult<LeaveRequestListItemDto>.Create(items, page, pageSize, total);
    }

    public async Task<LeaveRequestDetailDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.LeaveRequests.AsNoTracking()
            .Include(l => l.Employee).ThenInclude(e => e.Department)
            .Include(l => l.LeaveType)
            .Where(l => l.Id == id)
            .Select(l => new LeaveRequestDetailDto
            {
                Id = l.Id,
                EmployeeId = l.EmployeeId,
                EmployeeName = l.Employee.FirstName + " " + l.Employee.LastName,
                EmployeeNumber = l.Employee.EmployeeNumber,
                DepartmentName = l.Employee.Department != null ? l.Employee.Department.Name : null,
                LeaveTypeName = l.LeaveType.Name,
                StartDate = l.StartDate,
                EndDate = l.EndDate,
                TotalDays = l.TotalDays,
                Status = l.Status,
                CreatedAtUtc = l.CreatedAtUtc,
                IsHalfDay = l.IsHalfDay,
                Reason = l.Reason,
                DecisionByName = l.DecisionByName,
                DecisionAtUtc = l.DecisionAtUtc,
                DecisionComments = l.DecisionComments,
                UpdatedAtUtc = l.UpdatedAtUtc
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> SubmitAsync(int employeeId, LeaveRequestFormDto dto, CancellationToken ct = default)
    {
        if (dto.EndDate < dto.StartDate)
            throw new BusinessRuleException("The end date cannot be before the start date.");
        if (dto.StartDate < DateOnly.FromDateTime(_clock.UtcNow).AddDays(-1))
            throw new BusinessRuleException("Leave cannot be requested for a date in the past.");

        var leaveType = await _db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == dto.LeaveTypeId, ct)
            ?? throw new BusinessRuleException("Selected leave type does not exist.");
        if (!leaveType.IsActive)
            throw new BusinessRuleException("Selected leave type is not available.");

        if (dto.IsHalfDay && dto.StartDate != dto.EndDate)
            throw new BusinessRuleException("A half-day request must start and end on the same date.");

        var holidayDates = await GetHolidayDatesAsync(dto.StartDate, dto.EndDate, ct);
        var workingDays = WorkingDayCalculator.CountWorkingDays(dto.StartDate, dto.EndDate, holidayDates);

        if (workingDays <= 0m)
            throw new BusinessRuleException("The selected dates contain no working days.");

        var totalDays = dto.IsHalfDay ? 0.5m : workingDays;

        var overlapping = await _db.LeaveRequests.AsNoTracking()
            .AnyAsync(l => l.EmployeeId == employeeId
                && l.Status != LeaveRequestStatus.Rejected
                && l.Status != LeaveRequestStatus.Cancelled
                && l.StartDate <= dto.EndDate && dto.StartDate <= l.EndDate, ct);
        if (overlapping)
            throw new ConflictException("You already have a leave request that overlaps these dates.");

        var year = dto.StartDate.Year;
        var balance = await _db.LeaveBalances
            .FirstOrDefaultAsync(b => b.EmployeeId == employeeId && b.LeaveTypeId == leaveType.Id && b.Year == year, ct);

        if (leaveType.IsPaid && balance is not null && balance.Remaining < totalDays)
        {
            throw new BusinessRuleException(
                $"Insufficient {leaveType.Name} balance. Available: {balance.Remaining} day(s), requested: {totalDays}.");
        }

        var now = _clock.UtcNow;
        var request = new LeaveRequest
        {
            EmployeeId = employeeId,
            LeaveTypeId = leaveType.Id,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            IsHalfDay = dto.IsHalfDay,
            TotalDays = totalDays,
            Reason = dto.Reason,
            Status = LeaveRequestStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _db.LeaveRequests.Add(request);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "Leave.Submitted",
            TargetType = nameof(LeaveRequest),
            TargetId = request.Id.ToString(),
            Metadata = $"EmployeeId={employeeId};Days={totalDays}"
        }, ct);

        await _notifications.NotifyRoleAsync("HRManager", "New leave request",
            $"{request.TotalDays} day(s) {leaveType.Name} request awaiting review.",
            $"/Leave/Details/{request.Id}", NotificationType.LeaveSubmitted, ct);
        await _notifications.NotifyRoleAsync("Administrator", "New leave request",
            $"A {leaveType.Name} request was submitted.",
            $"/Leave/Details/{request.Id}", NotificationType.LeaveSubmitted, ct);

        return request.Id;
    }

    public async Task ApproveAsync(int id, LeaveDecisionDto decision, CancellationToken ct = default)
    {
        var request = await _db.LeaveRequests
            .Include(l => l.Employee)
            .Include(l => l.LeaveType)
            .FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("Leave request not found.");

        if (request.Status != LeaveRequestStatus.Pending)
            throw new BusinessRuleException($"Only pending requests can be approved. Current status: {request.Status}.");

        if (IsSelf(request.Employee.UserId))
            throw new ForbiddenException("You cannot approve your own leave request.");

        await ApplyBalanceChangeAsync(request, deduct: true, ct);

        request.Status = LeaveRequestStatus.Approved;
        request.DecisionByUserId = _currentUser.UserId;
        request.DecisionByName = _currentUser.DisplayName;
        request.DecisionAtUtc = _clock.UtcNow;
        request.DecisionComments = decision.Comments;
        request.UpdatedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "Leave.Approved",
            TargetType = nameof(LeaveRequest),
            TargetId = id.ToString()
        }, ct);

        if (!string.IsNullOrEmpty(request.Employee.UserId))
        {
            await _notifications.NotifyUserAsync(request.Employee.UserId, "Leave approved",
                $"Your {request.LeaveType.Name} request was approved.",
                $"/Leave/Details/{id}", NotificationType.LeaveApproved, ct);
        }
    }

    public async Task RejectAsync(int id, LeaveDecisionDto decision, CancellationToken ct = default)
    {
        var request = await _db.LeaveRequests
            .Include(l => l.Employee)
            .Include(l => l.LeaveType)
            .FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("Leave request not found.");

        if (request.Status != LeaveRequestStatus.Pending)
            throw new BusinessRuleException($"Only pending requests can be rejected. Current status: {request.Status}.");

        if (IsSelf(request.Employee.UserId))
            throw new ForbiddenException("You cannot reject your own leave request.");

        request.Status = LeaveRequestStatus.Rejected;
        request.DecisionByUserId = _currentUser.UserId;
        request.DecisionByName = _currentUser.DisplayName;
        request.DecisionAtUtc = _clock.UtcNow;
        request.DecisionComments = decision.Comments;
        request.UpdatedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "Leave.Rejected",
            TargetType = nameof(LeaveRequest),
            TargetId = id.ToString()
        }, ct);

        if (!string.IsNullOrEmpty(request.Employee.UserId))
        {
            await _notifications.NotifyUserAsync(request.Employee.UserId, "Leave rejected",
                $"Your {request.LeaveType.Name} request was rejected.",
                $"/Leave/Details/{id}", NotificationType.LeaveRejected, ct);
        }
    }

    public async Task CancelAsync(int id, int? actingEmployeeId, bool isPrivileged, CancellationToken ct = default)
    {
        var request = await _db.LeaveRequests
            .Include(l => l.Employee)
            .Include(l => l.LeaveType)
            .FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("Leave request not found.");

        if (!isPrivileged && actingEmployeeId != request.EmployeeId)
            throw new ForbiddenException("You may only cancel your own leave requests.");

        if (request.Status is LeaveRequestStatus.Rejected or LeaveRequestStatus.Cancelled)
            throw new BusinessRuleException($"This request cannot be cancelled. Current status: {request.Status}.");

        if (request.Status == LeaveRequestStatus.Approved)
        {
            if (request.StartDate <= DateOnly.FromDateTime(_clock.UtcNow))
                throw new BusinessRuleException("An approved request that has already started cannot be cancelled online.");
            await ApplyBalanceChangeAsync(request, deduct: false, ct);
        }

        request.Status = LeaveRequestStatus.Cancelled;
        request.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "Leave.Cancelled",
            TargetType = nameof(LeaveRequest),
            TargetId = id.ToString()
        }, ct);
    }

    public async Task<IReadOnlyList<LeaveBalanceDto>> GetBalancesAsync(int employeeId, int? year = null, CancellationToken ct = default)
    {
        var y = year ?? _clock.UtcNow.Year;
        return await _db.LeaveBalances.AsNoTracking()
            .Include(b => b.LeaveType)
            .Where(b => b.EmployeeId == employeeId && b.Year == y)
            .OrderBy(b => b.LeaveType.Name)
            .Select(b => new LeaveBalanceDto
            {
                LeaveTypeId = b.LeaveTypeId,
                LeaveTypeName = b.LeaveType.Name,
                Entitlement = b.Entitlement,
                Used = b.Used,
                Remaining = b.Entitlement - b.Used,
                Year = b.Year
            })
            .ToListAsync(ct);
    }

    public Task<int> GetPendingCountAsync(int? employeeId = null, CancellationToken ct = default)
        => _db.LeaveRequests.CountAsync(l => l.Status == LeaveRequestStatus.Pending
            && (!employeeId.HasValue || l.EmployeeId == employeeId), ct);

    private bool IsSelf(string? ownerUserId)
        => !string.IsNullOrEmpty(ownerUserId) && ownerUserId == _currentUser.UserId;

    private async Task<IReadOnlyList<DateOnly>> GetHolidayDatesAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var holidays = await _db.Holidays.AsNoTracking()
            .Where(h => h.IsRecurringAnnually || (h.Date >= start && h.Date <= end))
            .Select(h => new { h.Date, h.IsRecurringAnnually })
            .ToListAsync(ct);

        var dates = new HashSet<DateOnly>();
        foreach (var holiday in holidays)
        {
            if (!holiday.IsRecurringAnnually)
            {
                dates.Add(holiday.Date);
                continue;
            }

            for (var year = start.Year; year <= end.Year; year++)
            {
                var day = Math.Min(holiday.Date.Day, DateTime.DaysInMonth(year, holiday.Date.Month));
                var occurrence = new DateOnly(year, holiday.Date.Month, day);
                if (occurrence >= start && occurrence <= end) dates.Add(occurrence);
            }
        }

        return dates.ToList();
    }

    private async Task ApplyBalanceChangeAsync(LeaveRequest request, bool deduct, CancellationToken ct)
    {
        var year = request.StartDate.Year;
        var balance = await _db.LeaveBalances
            .FirstOrDefaultAsync(b => b.EmployeeId == request.EmployeeId
                && b.LeaveTypeId == request.LeaveTypeId && b.Year == year, ct);

        if (balance is null)
        {
            balance = new LeaveBalance
            {
                EmployeeId = request.EmployeeId,
                LeaveTypeId = request.LeaveTypeId,
                Year = year,
                Entitlement = request.LeaveType.DefaultAnnualEntitlement,
                Used = 0m
            };
            _db.LeaveBalances.Add(balance);
        }

        if (!request.LeaveType.IsPaid) return;

        if (deduct)
        {
            if (balance.Entitlement > 0 && balance.Remaining < request.TotalDays)
                throw new BusinessRuleException(
                    $"Insufficient balance to approve. Available: {balance.Remaining} day(s).");
            balance.Used += request.TotalDays;
        }
        else
        {
            balance.Used = Math.Max(0m, balance.Used - request.TotalDays);
        }

        balance.UpdatedAtUtc = _clock.UtcNow;
    }
}
