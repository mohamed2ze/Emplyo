using Emplyo.Application.Attendance;
using Emplyo.Domain.Enums;
using Emplyo.Domain.Exceptions;
using Emplyo.Infrastructure.Persistence;
using Emplyo.UnitTests.TestSupport;
using Xunit;

namespace Emplyo.UnitTests;

public class AttendanceServiceTests
{
    private static AttendanceService Create(AppDbContext db, FakeClock clock)
        => new(db, new FakeAudit(), new FakeCurrentUser(), new FakeSettings(), clock);

    [Fact]
    public async Task Cannot_clock_in_twice_on_same_day()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeClock());

        await service.ClockInAsync(employee.Id);
        await Assert.ThrowsAsync<ConflictException>(() => service.ClockInAsync(employee.Id));
    }

    [Fact]
    public async Task Cannot_clock_out_without_clock_in()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeClock());

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ClockOutAsync(employee.Id));
    }

    [Fact]
    public async Task Cannot_clock_out_twice()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeClock());

        await service.ClockInAsync(employee.Id);
        await service.ClockOutAsync(employee.Id);
        await Assert.ThrowsAsync<ConflictException>(() => service.ClockOutAsync(employee.Id));
    }

    [Fact]
    public async Task Late_arrival_is_flagged()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var clock = new FakeClock { UtcNow = new DateTime(2026, 6, 15, 10, 30, 0, DateTimeKind.Utc) };
        var service = Create(db, clock);

        await service.ClockInAsync(employee.Id);
        var today = await service.GetTodayAsync(employee.Id);
        Assert.Equal(AttendanceStatus.Late, today.Status);
    }
}
