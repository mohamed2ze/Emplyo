using Emplyo.Application.Attendance;
using Emplyo.Domain.Enums;
using Emplyo.Domain.Exceptions;
using Emplyo.Infrastructure.Persistence;
using Emplyo.UnitTests.TestSupport;
using Xunit;

namespace Emplyo.UnitTests;

public class AttendanceServiceTests
{
    private static AttendanceService Create(AppDbContext db, FakeClock clock, FakeSettings? settings = null)
        => new(db, new FakeAudit(), new FakeCurrentUser(), settings ?? new FakeSettings(), clock);

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

    [Fact]
    public async Task An_arrival_at_the_configured_start_time_is_not_late()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var clock = new FakeClock { UtcNow = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc) };
        var service = Create(db, clock, new FakeSettings().With("Attendance.WorkDayStart", "10:00"));

        await service.ClockInAsync(employee.Id);

        Assert.Equal(AttendanceStatus.Present, (await service.GetTodayAsync(employee.Id)).Status);
    }

    [Fact]
    public async Task The_configured_workday_start_controls_late_detection()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var clock = new FakeClock { UtcNow = new DateTime(2026, 6, 15, 10, 5, 0, DateTimeKind.Utc) };
        var service = Create(db, clock, new FakeSettings().With("Attendance.WorkDayStart", "10:00"));

        await service.ClockInAsync(employee.Id);

        Assert.Equal(AttendanceStatus.Late, (await service.GetTodayAsync(employee.Id)).Status);
    }

    [Fact]
    public async Task Clock_in_uses_the_organization_time_zone_date()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var clock = new FakeClock { UtcNow = new DateTime(2026, 6, 15, 23, 30, 0, DateTimeKind.Utc) };
        var settings = new FakeSettings { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo") };
        var service = Create(db, clock, settings);

        await service.ClockInAsync(employee.Id);

        var record = db.AttendanceRecords.Single();
        Assert.Equal(new DateOnly(2026, 6, 16), record.WorkDate);
        Assert.Equal(new DateOnly(2026, 6, 16), (await service.GetTodayAsync(employee.Id)).WorkDate);
    }

    [Fact]
    public async Task Two_clock_ins_on_the_same_local_day_are_rejected_across_a_utc_date_boundary()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var clock = new FakeClock { UtcNow = new DateTime(2026, 6, 15, 23, 30, 0, DateTimeKind.Utc) };
        var settings = new FakeSettings { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo") };
        var service = Create(db, clock, settings);

        await service.ClockInAsync(employee.Id);
        clock.UtcNow = new DateTime(2026, 6, 16, 1, 0, 0, DateTimeKind.Utc);

        await Assert.ThrowsAsync<ConflictException>(() => service.ClockInAsync(employee.Id));
    }

    [Fact]
    public async Task Today_flags_reflect_the_clock_in_and_clock_out_state()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var clock = new FakeClock();
        var service = Create(db, clock);

        var initial = await service.GetTodayAsync(employee.Id);
        Assert.True(initial.CanClockIn);
        Assert.False(initial.CanClockOut);

        await service.ClockInAsync(employee.Id);
        var afterIn = await service.GetTodayAsync(employee.Id);
        Assert.False(afterIn.CanClockIn);
        Assert.True(afterIn.CanClockOut);

        clock.UtcNow = clock.UtcNow.AddHours(8);
        await service.ClockOutAsync(employee.Id);
        var afterOut = await service.GetTodayAsync(employee.Id);
        Assert.False(afterOut.CanClockIn);
        Assert.False(afterOut.CanClockOut);
    }

    [Fact]
    public async Task Clock_out_before_clock_in_is_rejected()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var clock = new FakeClock();
        var service = Create(db, clock);
        await service.ClockInAsync(employee.Id);

        clock.UtcNow = clock.UtcNow.AddMinutes(-30);

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ClockOutAsync(employee.Id));
        var record = db.AttendanceRecords.Single();
        Assert.Null(record.ClockOutUtc);
    }

    [Fact]
    public async Task Correction_rejects_clock_out_before_clock_in()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db, new FakeClock());

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CorrectAsync(new AttendanceCorrectionDto
        {
            WorkDate = new DateOnly(2026, 6, 15),
            ClockIn = new DateTime(2026, 6, 15, 10, 0, 0),
            ClockOut = new DateTime(2026, 6, 15, 9, 0, 0),
            Status = AttendanceStatus.Present
        }, null, employee.Id));
    }

    [Fact]
    public async Task Correction_creates_a_record_and_records_who_corrected_it()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var audit = new FakeAudit();
        var user = new FakeCurrentUser { UserId = "hr-user" };
        var service = new AttendanceService(db, audit, user, new FakeSettings(), new FakeClock());

        var id = await service.CorrectAsync(new AttendanceCorrectionDto
        {
            WorkDate = new DateOnly(2026, 6, 15),
            ClockIn = new DateTime(2026, 6, 15, 9, 0, 0),
            ClockOut = new DateTime(2026, 6, 15, 17, 0, 0),
            Status = AttendanceStatus.Remote,
            Notes = "Forgot to clock in"
        }, null, employee.Id);

        var record = await db.AttendanceRecords.FindAsync(id);
        Assert.NotNull(record);
        Assert.Equal(AttendanceStatus.Remote, record!.Status);
        Assert.Equal("Forgot to clock in", record.Notes);
        Assert.Equal("hr-user", record.CorrectedByUserId);
        Assert.NotNull(record.CorrectedAtUtc);
        Assert.Contains("Attendance.Corrected", audit.Recorded);
    }

    [Fact]
    public async Task Summary_counts_statuses_and_computes_worked_hours()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        db.AttendanceRecords.AddRange(
            new Emplyo.Domain.Entities.AttendanceRecord
            {
                EmployeeId = employee.Id, WorkDate = new DateOnly(2026, 6, 15),
                ClockInUtc = new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc),
                ClockOutUtc = new DateTime(2026, 6, 15, 17, 30, 0, DateTimeKind.Utc),
                Status = AttendanceStatus.Present
            },
            new Emplyo.Domain.Entities.AttendanceRecord
            {
                EmployeeId = employee.Id, WorkDate = new DateOnly(2026, 6, 16),
                ClockInUtc = new DateTime(2026, 6, 16, 9, 30, 0, DateTimeKind.Utc),
                ClockOutUtc = new DateTime(2026, 6, 16, 17, 0, 0, DateTimeKind.Utc),
                Status = AttendanceStatus.Late
            },
            new Emplyo.Domain.Entities.AttendanceRecord
            {
                EmployeeId = employee.Id, WorkDate = new DateOnly(2026, 6, 17),
                Status = AttendanceStatus.Absent
            });
        await db.SaveChangesAsync();
        var service = Create(db, new FakeClock());

        var summary = await service.GetSummaryAsync(employee.Id, new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 17));

        Assert.Equal(1, summary.PresentDays);
        Assert.Equal(1, summary.LateDays);
        Assert.Equal(16.0, summary.TotalWorkedHours);
    }
}
