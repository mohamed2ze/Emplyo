using Emplyo.Application.Reporting;
using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Emplyo.Infrastructure.Persistence;
using Emplyo.UnitTests.TestSupport;
using Xunit;

namespace Emplyo.UnitTests;

public class ReportServiceTests
{
    [Fact]
    public async Task Headcount_excludes_terminated_employees_and_includes_unassigned_staff()
    {
        using var db = TestDb.Create();
        var engineering = await Seed.DepartmentAsync(db, "Engineering");
        var sales = await Seed.DepartmentAsync(db, "Sales");
        await Seed.DepartmentAsync(db, "Empty");
        AddEmployee(db, "EMP-1001", "A", "One", EmploymentStatus.Active, engineering.Id);
        AddEmployee(db, "EMP-1002", "B", "Two", EmploymentStatus.Active, engineering.Id);
        AddEmployee(db, "EMP-1003", "C", "Three", EmploymentStatus.Pending, engineering.Id);
        AddEmployee(db, "EMP-1004", "D", "Four", EmploymentStatus.Terminated, engineering.Id);
        AddEmployee(db, "EMP-1005", "E", "Five", EmploymentStatus.Active, sales.Id);
        AddEmployee(db, "EMP-1006", "F", "Six", EmploymentStatus.Active);
        AddEmployee(db, "EMP-1007", "G", "Seven", EmploymentStatus.Terminated);
        await db.SaveChangesAsync();
        var service = new ReportService(db);

        var rows = await service.GetHeadcountByDepartmentAsync();

        Assert.Equal(4, rows.Count);
        Assert.Equal(2, rows.Single(r => r.DepartmentName == "Engineering").ActiveCount);
        Assert.Equal(3, rows.Single(r => r.DepartmentName == "Engineering").TotalCount);
        Assert.Equal(0, rows.Single(r => r.DepartmentName == "Empty").TotalCount);
        Assert.Equal(1, rows.Single(r => r.DepartmentName == "Unassigned").ActiveCount);
    }

    [Fact]
    public async Task Status_summary_includes_every_status_even_when_unused()
    {
        using var db = TestDb.Create();
        AddEmployee(db, "EMP-1001", "A", "One", EmploymentStatus.Active);
        AddEmployee(db, "EMP-1002", "B", "Two", EmploymentStatus.Active);
        AddEmployee(db, "EMP-1003", "C", "Three", EmploymentStatus.Terminated);
        await db.SaveChangesAsync();
        var service = new ReportService(db);

        var rows = await service.GetStatusSummaryAsync();

        Assert.Equal(Enum.GetValues<EmploymentStatus>().Length, rows.Count);
        Assert.Equal(2, rows.Single(r => r.Status == EmploymentStatus.Active).Count);
        Assert.Equal(1, rows.Single(r => r.Status == EmploymentStatus.Terminated).Count);
        Assert.Equal(0, rows.Single(r => r.Status == EmploymentStatus.OnLeave).Count);
    }

    [Fact]
    public async Task Leave_utilization_filters_by_year_and_department()
    {
        using var db = TestDb.Create();
        var engineering = await Seed.DepartmentAsync(db, "Engineering");
        var sales = await Seed.DepartmentAsync(db, "Sales");
        var type = await Seed.LeaveTypeAsync(db, name: "Annual Leave", entitlement: 20);
        var engineer = AddEmployee(db, "EMP-1001", "A", "One", EmploymentStatus.Active, engineering.Id);
        var seller = AddEmployee(db, "EMP-1002", "B", "Two", EmploymentStatus.Active, sales.Id);
        await db.SaveChangesAsync();
        db.LeaveBalances.AddRange(
            new LeaveBalance { EmployeeId = engineer.Id, LeaveTypeId = type.Id, Year = 2026, Entitlement = 20, Used = 4 },
            new LeaveBalance { EmployeeId = seller.Id, LeaveTypeId = type.Id, Year = 2026, Entitlement = 20, Used = 2 },
            new LeaveBalance { EmployeeId = engineer.Id, LeaveTypeId = type.Id, Year = 2025, Entitlement = 20, Used = 10 });
        await db.SaveChangesAsync();
        var service = new ReportService(db);

        var rows = await service.GetLeaveUtilizationAsync(2026, engineering.Id);

        var row = Assert.Single(rows);
        Assert.Equal("A One", row.EmployeeName);
        Assert.Equal(16m, row.Remaining);
    }

    [Fact]
    public async Task Attendance_report_aggregates_statuses_and_hours_for_the_selected_range()
    {
        using var db = TestDb.Create();
        var engineering = await Seed.DepartmentAsync(db, "Engineering");
        var sales = await Seed.DepartmentAsync(db, "Sales");
        var engineer = AddEmployee(db, "EMP-1001", "A", "One", EmploymentStatus.Active, engineering.Id);
        var seller = AddEmployee(db, "EMP-1002", "B", "Two", EmploymentStatus.Active, sales.Id);
        await db.SaveChangesAsync();
        db.AttendanceRecords.AddRange(
            NewRecord(engineer.Id, new DateOnly(2026, 6, 15), AttendanceStatus.Present, 9, 17),
            NewRecord(engineer.Id, new DateOnly(2026, 6, 16), AttendanceStatus.Late, 10, 17),
            NewRecord(engineer.Id, new DateOnly(2026, 6, 17), AttendanceStatus.Absent),
            NewRecord(engineer.Id, new DateOnly(2026, 6, 18), AttendanceStatus.Remote, 9, 13),
            NewRecord(engineer.Id, new DateOnly(2026, 5, 1), AttendanceStatus.Present, 9, 17),
            NewRecord(seller.Id, new DateOnly(2026, 6, 15), AttendanceStatus.Present, 9, 17));
        await db.SaveChangesAsync();
        var service = new ReportService(db);

        var rows = await service.GetAttendanceReportAsync(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 18), engineering.Id);

        var row = Assert.Single(rows);
        Assert.Equal(1, row.PresentDays);
        Assert.Equal(1, row.LateDays);
        Assert.Equal(1, row.AbsentDays);
        Assert.Equal(1, row.RemoteDays);
        Assert.Equal(19d, row.TotalHours);
    }

    [Fact]
    public async Task Recent_hires_are_ordered_by_hire_date_and_limited()
    {
        using var db = TestDb.Create();
        AddEmployee(db, "EMP-1001", "A", "One", EmploymentStatus.Active, hireDate: new DateOnly(2024, 1, 1));
        AddEmployee(db, "EMP-1002", "B", "Two", EmploymentStatus.Active, hireDate: new DateOnly(2026, 1, 1));
        AddEmployee(db, "EMP-1003", "C", "Three", EmploymentStatus.Active, hireDate: new DateOnly(2025, 1, 1));
        await db.SaveChangesAsync();
        var service = new ReportService(db);

        var rows = await service.GetRecentHiresAsync(2);

        Assert.Equal(new[] { "B Two", "C Three" }, rows.Select(r => r.EmployeeName));
    }

    [Fact]
    public async Task Employee_directory_filters_by_department_and_status_and_includes_the_manager()
    {
        using var db = TestDb.Create();
        var engineering = await Seed.DepartmentAsync(db, "Engineering");
        var manager = AddEmployee(db, "EMP-1000", "M", "Manager", EmploymentStatus.Active, engineering.Id);
        var report = AddEmployee(db, "EMP-1001", "R", "Report", EmploymentStatus.Active, engineering.Id);
        report.ManagerId = manager.Id;
        AddEmployee(db, "EMP-1002", "T", "Terminated", EmploymentStatus.Terminated, engineering.Id);
        await db.SaveChangesAsync();
        var service = new ReportService(db);

        var rows = await service.GetEmployeeDirectoryAsync(engineering.Id, EmploymentStatus.Active);

        Assert.Equal(2, rows.Count);
        Assert.Equal("M Manager", rows.Single(r => r.EmployeeNumber == "EMP-1001").ManagerName);
        Assert.DoesNotContain(rows, r => r.EmployeeNumber == "EMP-1002");
    }

    private static Employee AddEmployee(AppDbContext db, string number, string firstName, string lastName,
        EmploymentStatus status, int? departmentId = null, DateOnly? hireDate = null)
    {
        var employee = new Employee
        {
            EmployeeNumber = number,
            FirstName = firstName,
            LastName = lastName,
            WorkEmail = $"{number.ToLowerInvariant()}@test.local",
            EmploymentStatus = status,
            DepartmentId = departmentId,
            HireDate = hireDate ?? new DateOnly(2024, 1, 1)
        };
        db.Employees.Add(employee);
        return employee;
    }

    private static AttendanceRecord NewRecord(int employeeId, DateOnly date, AttendanceStatus status, int? startHour = null, int? endHour = null)
        => new()
        {
            EmployeeId = employeeId,
            WorkDate = date,
            Status = status,
            ClockInUtc = startHour.HasValue ? date.ToDateTime(new TimeOnly(startHour.Value, 0), DateTimeKind.Utc) : null,
            ClockOutUtc = endHour.HasValue ? date.ToDateTime(new TimeOnly(endHour.Value, 0), DateTimeKind.Utc) : null
        };
}
