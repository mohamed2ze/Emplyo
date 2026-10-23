using Emplyo.Application.LeaveManagement;
using Emplyo.Domain.Exceptions;
using Emplyo.Infrastructure.Persistence;
using Emplyo.UnitTests.TestSupport;
using Xunit;

namespace Emplyo.UnitTests;

public class LeaveTypeServiceTests
{
    private static LeaveTypeService Create(AppDbContext db, FakeAudit? audit = null)
        => new(db, audit ?? new FakeAudit(), new FakeClock());

    private static LeaveTypeFormDto Form(string name = "Study Leave") => new()
    {
        Name = name,
        IsPaid = true,
        DefaultAnnualEntitlement = 5,
        RequiresApproval = true,
        IsActive = true
    };

    [Fact]
    public async Task Create_trims_the_name_and_records_an_audit_entry()
    {
        using var db = TestDb.Create();
        var audit = new FakeAudit();

        var id = await Create(db, audit).CreateAsync(Form("  Study Leave  "));

        Assert.Equal("Study Leave", (await db.LeaveTypes.FindAsync(id))!.Name);
        Assert.Contains("LeaveType.Created", audit.Recorded);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_name()
    {
        using var db = TestDb.Create();
        var service = Create(db);
        await service.CreateAsync(Form());

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(Form("Study Leave")));
    }

    [Fact]
    public async Task Update_rejects_a_name_used_by_another_leave_type()
    {
        using var db = TestDb.Create();
        var service = Create(db);
        await service.CreateAsync(Form("Study Leave"));
        var id = await service.CreateAsync(Form("Sabbatical"));

        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(id, Form("Study Leave")));
    }

    [Fact]
    public async Task Get_all_can_include_inactive_leave_types()
    {
        using var db = TestDb.Create();
        await Seed.LeaveTypeAsync(db, name: "Annual Leave");
        await Seed.InactiveLeaveTypeAsync(db, name: "Legacy");
        var service = Create(db);

        Assert.Single(await service.GetAllAsync());
        Assert.Equal(2, (await service.GetAllAsync(includeInactive: true)).Count);
    }
}

public class HolidayServiceTests
{
    private static HolidayService Create(AppDbContext db, FakeAudit? audit = null)
        => new(db, audit ?? new FakeAudit(), new FakeClock { UtcNow = new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc) });

    private static HolidayFormDto Form(string name = "Foundation Day", DateOnly? date = null, bool recurring = false) => new()
    {
        Name = name,
        Date = date ?? new DateOnly(2026, 7, 4),
        IsRecurringAnnually = recurring
    };

    [Fact]
    public async Task The_year_filter_includes_recurring_holidays()
    {
        using var db = TestDb.Create();
        await Seed.HolidayAsync(db, new DateOnly(2025, 12, 25), "Year End", recurring: true);
        await Seed.HolidayAsync(db, new DateOnly(2025, 7, 4), "Old Summer");
        await Seed.HolidayAsync(db, new DateOnly(2026, 7, 4), "Summer");
        var service = Create(db);

        var holidays = await service.GetAsync(2026);

        Assert.Equal(new[] { "Summer", "Year End" }, holidays.Select(h => h.Name).OrderBy(n => n));
    }

    [Fact]
    public async Task Upcoming_holidays_use_the_next_occurrence_for_recurring_entries()
    {
        using var db = TestDb.Create();
        await Seed.HolidayAsync(db, new DateOnly(2026, 1, 1), "New Year", recurring: true);
        await Seed.HolidayAsync(db, new DateOnly(2025, 12, 31), "Year End", recurring: true);
        await Seed.HolidayAsync(db, new DateOnly(2026, 7, 4), "Summer");
        await Seed.HolidayAsync(db, new DateOnly(2026, 6, 1), "Past Event");
        var service = Create(db);

        var holidays = await service.GetUpcomingAsync(10);

        Assert.Equal(new[] { "Summer", "Year End", "New Year" }, holidays.Select(h => h.Name));
        Assert.Equal(new[] { new DateOnly(2026, 7, 4), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 1) }, holidays.Select(h => h.Date));
    }

    [Fact]
    public async Task Upcoming_holidays_honor_the_take_limit()
    {
        using var db = TestDb.Create();
        await Seed.HolidayAsync(db, new DateOnly(2026, 7, 4), "Summer");
        await Seed.HolidayAsync(db, new DateOnly(2026, 8, 1), "Late Summer");
        await Seed.HolidayAsync(db, new DateOnly(2026, 9, 1), "Autumn");
        var service = Create(db);

        var holidays = await service.GetUpcomingAsync(2);

        Assert.Equal(new[] { "Summer", "Late Summer" }, holidays.Select(h => h.Name));
    }

    [Fact]
    public async Task Upcoming_holidays_include_a_recurring_holiday_that_falls_today()
    {
        using var db = TestDb.Create();
        await Seed.HolidayAsync(db, new DateOnly(2020, 6, 15), "Company Day", recurring: true);
        var service = Create(db);

        var holidays = await service.GetUpcomingAsync(1);

        Assert.Equal(new DateOnly(2026, 6, 15), Assert.Single(holidays).Date);
    }

    [Fact]
    public async Task Create_trims_the_name_and_rejects_an_exact_duplicate()
    {
        using var db = TestDb.Create();
        var service = Create(db);

        var id = await service.CreateAsync(Form("  Foundation Day  "));

        Assert.Equal("Foundation Day", (await db.Holidays.FindAsync(id))!.Name);
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(Form("Foundation Day")));
    }

    [Fact]
    public async Task Delete_removes_the_holiday()
    {
        using var db = TestDb.Create();
        var holiday = await Seed.HolidayAsync(db, new DateOnly(2026, 7, 4), "Summer");
        var audit = new FakeAudit();

        await Create(db, audit).DeleteAsync(holiday.Id);

        Assert.Null(await db.Holidays.FindAsync(holiday.Id));
        Assert.Contains("Holiday.Deleted", audit.Recorded);
    }
}
