using Emplyo.Application.LeaveManagement;
using Emplyo.Domain.Enums;
using Emplyo.Domain.Exceptions;
using Emplyo.Infrastructure.Persistence;
using Emplyo.UnitTests.TestSupport;
using Xunit;

namespace Emplyo.UnitTests;

public class LeaveServiceTests
{
    private static LeaveService Create(AppDbContext db, FakeCurrentUser user, FakeClock clock)
        => new(db, new FakeAudit(), user, new FakeNotifications(), clock);

    [Fact]
    public async Task Submit_rejects_end_before_start()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        var form = new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 7, 5),
            EndDate = new DateOnly(2026, 7, 1)
        };

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(employee.Id, form));
    }

    [Fact]
    public async Task Submit_excludes_weekends_from_day_count()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        // Fri 2026-07-03 .. Mon 2026-07-06 => Fri + Mon = 2 working days.
        var form = new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 7, 3),
            EndDate = new DateOnly(2026, 7, 6)
        };

        var id = await service.SubmitAsync(employee.Id, form);
        var request = await service.GetByIdAsync(id);
        Assert.Equal(2m, request!.TotalDays);
    }

    [Fact]
    public async Task Submit_allows_unpaid_leave_without_balance()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db, name: "Unpaid Leave", entitlement: 0, isPaid: false);
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 3)
        });

        var request = await service.GetByIdAsync(id);
        Assert.Equal(3m, request!.TotalDays);
    }

    [Fact]
    public async Task Submit_rejects_overlapping_request()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 3)
        });

        await Assert.ThrowsAsync<ConflictException>(() => service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 7, 2),
            EndDate = new DateOnly(2026, 7, 4)
        }));
    }

    [Fact]
    public async Task Approve_deducts_balance_and_cannot_be_repeated()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db, entitlement: 20);
        var employee = await Seed.EmployeeAsync(db);
        var user = new FakeCurrentUser { UserId = "approver" };
        var service = Create(db, user, new FakeClock());

        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 3)
        });

        await service.ApproveAsync(id, new LeaveDecisionDto { Comments = "ok" });

        var balances = await service.GetBalancesAsync(employee.Id, 2026);
        Assert.Equal(3m, balances.Single(b => b.LeaveTypeId == type.Id).Used);

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ApproveAsync(id, new LeaveDecisionDto()));
    }

    [Fact]
    public async Task Cancelling_approved_future_leave_restores_balance()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db, entitlement: 20);
        var employee = await Seed.EmployeeAsync(db);
        var user = new FakeCurrentUser { UserId = "approver" };
        var service = Create(db, user, new FakeClock());

        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 3)
        });
        await service.ApproveAsync(id, new LeaveDecisionDto());
        await service.CancelAsync(id, employee.Id, isPrivileged: false);

        var balances = await service.GetBalancesAsync(employee.Id, 2026);
        Assert.Equal(0m, balances.Single(b => b.LeaveTypeId == type.Id).Used);
    }

    [Fact]
    public async Task Cannot_approve_own_request()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        employee.UserId = "self-user";
        await db.SaveChangesAsync();

        var user = new FakeCurrentUser { UserId = "self-user" };
        var service = Create(db, user, new FakeClock());
        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 2)
        });

        await Assert.ThrowsAsync<ForbiddenException>(() => service.ApproveAsync(id, new LeaveDecisionDto()));
    }
}
