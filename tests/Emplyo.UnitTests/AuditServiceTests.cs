using Emplyo.Application.Common.Models;
using Emplyo.Domain.Enums;
using Emplyo.Infrastructure.Services;
using Emplyo.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Emplyo.UnitTests;

public class AuditServiceTests
{
    [Fact]
    public async Task Record_persists_the_actor_outcome_and_timestamp()
    {
        using var db = TestDb.Create();
        var clock = new FakeClock { UtcNow = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc) };
        var user = new FakeCurrentUser { UserId = "user-1", DisplayName = "Alice", IsAuthenticated = true };
        var service = new AuditService(db, user, clock, NullLogger<AuditService>.Instance);

        await service.RecordAsync(new AuditEntry
        {
            Action = "Employee.Created",
            TargetType = "Employee",
            TargetId = "42",
            Outcome = AuditOutcome.Failure,
            Metadata = "EmployeeNumber=EMP-1"
        });

        var log = Assert.Single(await db.AuditLogs.ToListAsync());
        Assert.Equal("user-1", log.ActorUserId);
        Assert.Equal("Alice", log.ActorName);
        Assert.Equal("Employee.Created", log.Action);
        Assert.Equal("Employee", log.TargetType);
        Assert.Equal("42", log.TargetId);
        Assert.Equal(AuditOutcome.Failure, log.Outcome);
        Assert.Equal("EmployeeNumber=EMP-1", log.Metadata);
        Assert.Equal(clock.UtcNow, log.TimestampUtc);
        Assert.NotEqual(Guid.Empty, log.EventId);
    }

    [Fact]
    public async Task Unauthenticated_work_is_attributed_to_the_system()
    {
        using var db = TestDb.Create();
        var user = new FakeCurrentUser { UserId = null, IsAuthenticated = false };
        var service = new AuditService(db, user, new FakeClock(), NullLogger<AuditService>.Instance);

        await service.RecordAsync(new AuditEntry { Action = "System.Task", TargetType = "System" });

        Assert.Equal("system", (await db.AuditLogs.SingleAsync()).ActorName);
    }

    [Fact]
    public async Task Long_values_are_truncated_to_the_persistence_limits()
    {
        using var db = TestDb.Create();
        var service = new AuditService(db, new FakeCurrentUser(), new FakeClock(), NullLogger<AuditService>.Instance);

        await service.RecordAsync(new AuditEntry
        {
            Action = new string('a', 130),
            TargetType = new string('t', 130),
            TargetId = new string('1', 90),
            CorrelationId = new string('c', 130),
            Metadata = new string('m', 1100)
        });

        var log = await db.AuditLogs.SingleAsync();
        Assert.Equal(120, log.Action.Length);
        Assert.Equal(120, log.TargetType.Length);
        Assert.Equal(80, log.TargetId!.Length);
        Assert.Equal(120, log.CorrelationId!.Length);
        Assert.Equal(1000, log.Metadata!.Length);
    }

    [Fact]
    public async Task Persistence_failures_do_not_break_the_business_operation()
    {
        var db = TestDb.Create();
        var service = new AuditService(db, new FakeCurrentUser(), new FakeClock(), NullLogger<AuditService>.Instance);
        db.Dispose();

        await service.RecordAsync(new AuditEntry { Action = "Employee.Created", TargetType = "Employee" });
    }
}
