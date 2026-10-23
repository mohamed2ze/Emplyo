using Emplyo.Application.Announcements;
using Emplyo.Application.Attendance;
using Emplyo.Application.Dashboard;
using Emplyo.Application.LeaveManagement;
using Emplyo.Application.Reporting;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Emplyo.Infrastructure.Persistence;
using Emplyo.UnitTests.TestSupport;
using Xunit;

namespace Emplyo.UnitTests;

public class DashboardServiceTests
{
    private static DashboardService Create(AppDbContext db, FakeClock clock, FakeSettings? settings = null)
    {
        var audit = new FakeAudit();
        var user = new FakeCurrentUser();
        var settingsService = settings ?? new FakeSettings();
        var leave = new LeaveService(db, audit, user, new FakeNotifications(), clock);
        var holidays = new HolidayService(db, audit, clock);
        var attendance = new AttendanceService(db, audit, user, settingsService, clock);
        var announcements = new AnnouncementService(db, audit, clock);
        var reports = new ReportService(db);
        return new DashboardService(db, reports, holidays, leave, attendance, announcements, settingsService, clock);
    }

    [Fact]
    public async Task Admin_dashboard_combines_counts_pending_leave_and_recent_activity()
    {
        using var db = TestDb.Create();
        var clock = new FakeClock { UtcNow = new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc) };
        var engineering = await Seed.DepartmentAsync(db, "Engineering");
        await Seed.PositionAsync(db, "Engineer", engineering.Id);
        var active = await Seed.EmployeeAsync(db, number: "EMP-1001", email: "active@test.local");
        active.DepartmentId = engineering.Id;
        var onLeave = await Seed.EmployeeAsync(db, number: "EMP-1002", email: "leave@test.local", status: EmploymentStatus.OnLeave);
        await Seed.EmployeeAsync(db, number: "EMP-1003", email: "terminated@test.local", status: EmploymentStatus.Terminated);
        var type = await Seed.LeaveTypeAsync(db);
        await Seed.LeaveRequestAsync(db, active.Id, type.Id, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2), LeaveRequestStatus.Pending);
        db.AuditLogs.AddRange(
            new AuditLog { Action = "Old", TargetType = "Test", TimestampUtc = clock.UtcNow.AddHours(-2) },
            new AuditLog { Action = "New", TargetType = "Test", TimestampUtc = clock.UtcNow.AddHours(-1) });
        await db.SaveChangesAsync();

        var dashboard = await Create(db, clock).GetAdminDashboardAsync();

        Assert.Equal(3, dashboard.TotalEmployees);
        Assert.Equal(1, dashboard.ActiveEmployees);
        Assert.Equal(1, dashboard.OnLeaveCount);
        Assert.Equal(1, dashboard.TerminatedCount);
        Assert.Equal(1, dashboard.DepartmentCount);
        Assert.Equal(1, dashboard.PositionCount);
        Assert.Equal(1, dashboard.PendingLeaveRequests);
        Assert.Equal(new[] { "New", "Old" }, dashboard.RecentActivity.Select(a => a.Action));
        Assert.Equal(Enum.GetValues<EmploymentStatus>().Length, dashboard.StatusSummary.Count);
    }

    [Fact]
    public async Task Hr_dashboard_reports_today_attendance_hires_and_anniversaries()
    {
        using var db = TestDb.Create();
        var clock = new FakeClock { UtcNow = new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc) };
        var present = await Seed.EmployeeAsync(db, number: "EMP-2001", email: "present@test.local");
        present.HireDate = new DateOnly(2024, 6, 20);
        var late = await Seed.EmployeeAsync(db, number: "EMP-2002", email: "late@test.local");
        var absent = await Seed.EmployeeAsync(db, number: "EMP-2003", email: "absent@test.local");
        var newHire = await Seed.EmployeeAsync(db, number: "EMP-2004", email: "newhire@test.local");
        newHire.HireDate = new DateOnly(2026, 6, 1);
        db.AttendanceRecords.AddRange(
            new AttendanceRecord { EmployeeId = present.Id, WorkDate = new DateOnly(2026, 6, 15), Status = AttendanceStatus.Present },
            new AttendanceRecord { EmployeeId = late.Id, WorkDate = new DateOnly(2026, 6, 15), Status = AttendanceStatus.Late },
            new AttendanceRecord { EmployeeId = absent.Id, WorkDate = new DateOnly(2026, 6, 15), Status = AttendanceStatus.Absent });
        db.OnboardingTasks.Add(new OnboardingTask { EmployeeId = newHire.Id, Title = "Sign contract", SortOrder = 1 });
        await db.SaveChangesAsync();

        var dashboard = await Create(db, clock).GetHrDashboardAsync();

        Assert.Equal(1, dashboard.PresentToday);
        Assert.Equal(1, dashboard.LateToday);
        Assert.Equal(1, dashboard.AbsentToday);
        Assert.Equal(1, dashboard.NewHiresThisMonth);
        Assert.Equal(1, dashboard.OnboardingInProgress);
        Assert.Contains(dashboard.UpcomingMilestones, m => m.EmployeeId == present.Id && m.Years == 2);
    }

    [Fact]
    public async Task Employee_dashboard_returns_personal_balances_attendance_leave_and_announcements()
    {
        using var db = TestDb.Create();
        var clock = new FakeClock { UtcNow = new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc) };
        var department = await Seed.DepartmentAsync(db, "Engineering");
        var employee = await Seed.EmployeeAsync(db, number: "EMP-3001", email: "employee@test.local");
        employee.DepartmentId = department.Id;
        var type = await Seed.LeaveTypeAsync(db, entitlement: 20);
        await Seed.BalanceAsync(db, employee.Id, type.Id, 2026, entitlement: 20, used: 3);
        await Seed.LeaveRequestAsync(db, employee.Id, type.Id, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2), LeaveRequestStatus.Pending);
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            EmployeeId = employee.Id,
            WorkDate = new DateOnly(2026, 6, 15),
            Status = AttendanceStatus.Present,
            ClockInUtc = new DateTime(2026, 6, 15, 8, 55, 0, DateTimeKind.Utc)
        });
        db.Announcements.Add(new Announcement
        {
            Title = "Everyone update",
            Body = "Body",
            Audience = AnnouncementAudience.Everyone,
            IsPublished = true,
            PublishAtUtc = clock.UtcNow.AddHours(-1),
            CreatedByName = "HR",
            CreatedAtUtc = clock.UtcNow.AddDays(-1),
            UpdatedAtUtc = clock.UtcNow.AddDays(-1)
        });
        await Seed.HolidayAsync(db, new DateOnly(2026, 7, 4), "Summer");
        await db.SaveChangesAsync();

        var dashboard = await Create(db, clock).GetEmployeeDashboardAsync(employee.Id, new[] { "Employee" });

        Assert.NotNull(dashboard);
        Assert.Equal("Test Employee", dashboard!.EmployeeName);
        Assert.Equal(17m, Assert.Single(dashboard.LeaveBalances).Remaining);
        Assert.Single(dashboard.RecentLeaveRequests);
        Assert.Single(dashboard.RecentAttendance);
        Assert.Equal("Everyone update", Assert.Single(dashboard.Announcements).Title);
        Assert.Equal("Summer", Assert.Single(dashboard.UpcomingHolidays).Name);
        Assert.NotNull(dashboard.Today);
        Assert.True(dashboard.Today!.CanClockOut);
    }

    [Fact]
    public async Task Employee_dashboard_returns_null_for_an_unknown_employee()
    {
        using var db = TestDb.Create();
        var service = Create(db, new FakeClock());

        Assert.Null(await service.GetEmployeeDashboardAsync(999, new[] { "Employee" }));
    }
}
