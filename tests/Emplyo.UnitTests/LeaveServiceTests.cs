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

    [Fact]
    public async Task Submit_rejects_unknown_leave_type()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = 999,
            StartDate = new DateOnly(2026, 6, 15),
            EndDate = new DateOnly(2026, 6, 16)
        }));
    }

    [Fact]
    public async Task Submit_rejects_inactive_leave_type()
    {
        using var db = TestDb.Create();
        var type = await Seed.InactiveLeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 15),
            EndDate = new DateOnly(2026, 6, 16)
        }));
    }

    [Fact]
    public async Task Submit_rejects_a_start_date_before_yesterday()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 13),
            EndDate = new DateOnly(2026, 6, 15)
        }));
    }

    [Fact]
    public async Task Submit_accepts_a_request_starting_yesterday()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 14),
            EndDate = new DateOnly(2026, 6, 15)
        });

        var request = await service.GetByIdAsync(id);
        Assert.Equal(1m, request!.TotalDays);
    }

    [Fact]
    public async Task Submit_excludes_configured_holidays()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        await Seed.HolidayAsync(db, new DateOnly(2026, 6, 17), "Foundation Day");
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 15),
            EndDate = new DateOnly(2026, 6, 19)
        });

        Assert.Equal(4m, (await service.GetByIdAsync(id))!.TotalDays);
    }

    [Fact]
    public async Task Submit_excludes_a_recurring_holiday_from_a_previous_year()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        await Seed.HolidayAsync(db, new DateOnly(2025, 12, 25), "Year-end", recurring: true);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 12, 24),
            EndDate = new DateOnly(2026, 12, 28)
        });

        Assert.Equal(2m, (await service.GetByIdAsync(id))!.TotalDays);
    }

    [Fact]
    public async Task Submit_rejects_a_half_day_spanning_multiple_dates()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 17),
            EndDate = new DateOnly(2026, 6, 18),
            IsHalfDay = true
        }));
    }

    [Fact]
    public async Task Submit_treats_a_working_day_half_day_as_half_a_day()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 17),
            EndDate = new DateOnly(2026, 6, 17),
            IsHalfDay = true
        });

        Assert.Equal(0.5m, (await service.GetByIdAsync(id))!.TotalDays);
    }

    [Fact]
    public async Task Submit_rejects_a_half_day_on_a_weekend()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 20),
            EndDate = new DateOnly(2026, 6, 20),
            IsHalfDay = true
        }));
    }

    [Fact]
    public async Task Submit_rejects_a_half_day_on_a_holiday()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        await Seed.HolidayAsync(db, new DateOnly(2026, 6, 17), "Foundation Day");
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 17),
            EndDate = new DateOnly(2026, 6, 17),
            IsHalfDay = true
        }));
    }

    [Fact]
    public async Task Submit_rejects_a_paid_request_exceeding_the_balance()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db, entitlement: 20);
        var employee = await Seed.EmployeeAsync(db);
        await Seed.BalanceAsync(db, employee.Id, type.Id, 2026, entitlement: 2);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 15),
            EndDate = new DateOnly(2026, 6, 17)
        }));
    }

    [Fact]
    public async Task Submit_ignores_rejected_and_cancelled_requests_when_checking_overlap()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        await Seed.LeaveRequestAsync(db, employee.Id, type.Id, new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 16), LeaveRequestStatus.Rejected);
        await Seed.LeaveRequestAsync(db, employee.Id, type.Id, new DateOnly(2026, 6, 17), new DateOnly(2026, 6, 18), LeaveRequestStatus.Cancelled);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());

        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 15),
            EndDate = new DateOnly(2026, 6, 18)
        });

        Assert.True(id > 0);
    }

    [Fact]
    public async Task Submit_records_an_audit_entry_and_notifies_the_approver_roles()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        var audit = new FakeAudit();
        var notifications = new FakeNotifications();
        var service = new LeaveService(db, audit, new FakeCurrentUser(), notifications, new FakeClock());

        await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 15),
            EndDate = new DateOnly(2026, 6, 16)
        });

        Assert.Contains("Leave.Submitted", audit.Recorded);
        Assert.Equal(new string?[] { "HRManager", "Administrator" }, notifications.Calls.Select(c => c.Role));
        Assert.All(notifications.Calls, c => Assert.Equal(NotificationType.LeaveSubmitted, c.Type));
    }

    [Fact]
    public async Task Approve_rejects_when_the_balance_is_no_longer_sufficient()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db, entitlement: 5);
        var employee = await Seed.EmployeeAsync(db);
        var balance = await Seed.BalanceAsync(db, employee.Id, type.Id, 2026, entitlement: 5);
        var service = Create(db, new FakeCurrentUser { UserId = "approver" }, new FakeClock());

        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 15),
            EndDate = new DateOnly(2026, 6, 16)
        });

        balance.Used = 4;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ApproveAsync(id, new LeaveDecisionDto()));

        var request = await db.LeaveRequests.FindAsync(id);
        Assert.Equal(LeaveRequestStatus.Pending, request!.Status);
        Assert.Equal(4m, balance.Used);
    }

    [Fact]
    public async Task Approve_creates_the_balance_from_the_leave_type_default_when_missing()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db, entitlement: 20);
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeCurrentUser { UserId = "approver" }, new FakeClock());

        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 15),
            EndDate = new DateOnly(2026, 6, 17)
        });

        await service.ApproveAsync(id, new LeaveDecisionDto());

        var balance = db.LeaveBalances.Single(b => b.EmployeeId == employee.Id && b.LeaveTypeId == type.Id);
        Assert.Equal(20m, balance.Entitlement);
        Assert.Equal(3m, balance.Used);
    }

    [Fact]
    public async Task Reject_sets_the_status_without_changing_the_balance_and_notifies_the_owner()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db, entitlement: 20);
        var employee = await Seed.EmployeeAsync(db);
        employee.UserId = "employee-user";
        await db.SaveChangesAsync();
        await Seed.BalanceAsync(db, employee.Id, type.Id, 2026, entitlement: 20);

        var notifications = new FakeNotifications();
        var service = new LeaveService(db, new FakeAudit(), new FakeCurrentUser { UserId = "approver" }, notifications, new FakeClock());
        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 15),
            EndDate = new DateOnly(2026, 6, 16)
        });

        await service.RejectAsync(id, new LeaveDecisionDto { Comments = "Not approved" });

        var request = await db.LeaveRequests.FindAsync(id);
        Assert.Equal(LeaveRequestStatus.Rejected, request!.Status);
        Assert.Equal("Not approved", request.DecisionComments);
        Assert.Equal(0m, db.LeaveBalances.Single().Used);
        Assert.Equal("employee-user", notifications.Calls.Last().UserId);
        Assert.Equal(NotificationType.LeaveRejected, notifications.Calls.Last().Type);
    }

    [Fact]
    public async Task A_non_owner_cannot_cancel_someone_elses_request()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());
        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 15),
            EndDate = new DateOnly(2026, 6, 16)
        });

        await Assert.ThrowsAsync<ForbiddenException>(() => service.CancelAsync(id, employee.Id + 1, isPrivileged: false));
    }

    [Fact]
    public async Task Cancelling_a_pending_request_does_not_change_the_balance()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db, entitlement: 20);
        var employee = await Seed.EmployeeAsync(db);
        await Seed.BalanceAsync(db, employee.Id, type.Id, 2026, entitlement: 20);
        var service = Create(db, new FakeCurrentUser(), new FakeClock());
        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 15),
            EndDate = new DateOnly(2026, 6, 16)
        });

        await service.CancelAsync(id, employee.Id, isPrivileged: false);

        Assert.Equal(LeaveRequestStatus.Cancelled, (await db.LeaveRequests.FindAsync(id))!.Status);
        Assert.Equal(0m, db.LeaveBalances.Single().Used);
    }

    [Fact]
    public async Task An_approved_request_that_has_already_started_cannot_be_cancelled()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db, entitlement: 20);
        var employee = await Seed.EmployeeAsync(db);
        await Seed.BalanceAsync(db, employee.Id, type.Id, 2026, entitlement: 20);
        var clock = new FakeClock();
        var service = Create(db, new FakeCurrentUser { UserId = "approver" }, clock);
        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 22),
            EndDate = new DateOnly(2026, 6, 24)
        });
        await service.ApproveAsync(id, new LeaveDecisionDto());

        clock.UtcNow = new DateTime(2026, 6, 22, 8, 0, 0, DateTimeKind.Utc);

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CancelAsync(id, employee.Id, isPrivileged: false));
    }

    [Fact]
    public async Task Cannot_reject_own_request()
    {
        using var db = TestDb.Create();
        var type = await Seed.LeaveTypeAsync(db);
        var employee = await Seed.EmployeeAsync(db);
        employee.UserId = "self-user";
        await db.SaveChangesAsync();
        var service = Create(db, new FakeCurrentUser { UserId = "self-user" }, new FakeClock());
        var id = await service.SubmitAsync(employee.Id, new LeaveRequestFormDto
        {
            LeaveTypeId = type.Id,
            StartDate = new DateOnly(2026, 6, 15),
            EndDate = new DateOnly(2026, 6, 16)
        });

        await Assert.ThrowsAsync<ForbiddenException>(() => service.RejectAsync(id, new LeaveDecisionDto()));
    }
}
