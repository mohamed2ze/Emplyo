using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Emplyo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Emplyo.IntegrationTests;

[Collection("integration")]
public class DatabaseConstraintTests
{
    private readonly EmplyoWebAppFactory _factory;

    public DatabaseConstraintTests(EmplyoWebAppFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task The_database_enforces_unique_employee_numbers()
    {
        var number = Unique("EMP");
        using var first = CreateContext();
        first.Employees.Add(NewEmployee(number, $"{number.ToLowerInvariant()}@constraints.test"));
        await first.SaveChangesAsync();

        using var second = CreateContext();
        second.Employees.Add(NewEmployee(number, $"{number.ToLowerInvariant()}-other@constraints.test"));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task The_database_enforces_one_attendance_record_per_employee_and_day()
    {
        using var db = CreateContext();
        var employee = NewEmployee(Unique("EMP"), $"{Unique("attendance").ToLowerInvariant()}@constraints.test");
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        var date = new DateOnly(2035, 1, 15);
        db.AttendanceRecords.AddRange(
            new AttendanceRecord { EmployeeId = employee.Id, WorkDate = date, Status = AttendanceStatus.Present },
            new AttendanceRecord { EmployeeId = employee.Id, WorkDate = date, Status = AttendanceStatus.Late });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_database_enforces_one_leave_balance_per_employee_type_and_year()
    {
        using var db = CreateContext();
        var employee = NewEmployee(Unique("EMP"), $"{Unique("balance").ToLowerInvariant()}@constraints.test");
        var type = new LeaveType { Name = Unique("Leave type"), IsPaid = true, DefaultAnnualEntitlement = 10, IsActive = true };
        db.Employees.Add(employee);
        db.LeaveTypes.Add(type);
        await db.SaveChangesAsync();
        db.LeaveBalances.AddRange(
            new LeaveBalance { EmployeeId = employee.Id, LeaveTypeId = type.Id, Year = 2035, Entitlement = 10, Used = 0 },
            new LeaveBalance { EmployeeId = employee.Id, LeaveTypeId = type.Id, Year = 2035, Entitlement = 10, Used = 1 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Employee_row_version_prevents_lost_updates()
    {
        var number = Unique("EMP");
        using (var seed = CreateContext())
        {
            seed.Employees.Add(NewEmployee(number, $"{number.ToLowerInvariant()}@constraints.test"));
            await seed.SaveChangesAsync();
        }

        using var first = CreateContext();
        using var second = CreateContext();
        var firstCopy = await first.Employees.SingleAsync(e => e.EmployeeNumber == number);
        var secondCopy = await second.Employees.SingleAsync(e => e.EmployeeNumber == number);

        firstCopy.FirstName = "First writer";
        await first.SaveChangesAsync();

        secondCopy.FirstName = "Second writer";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Leave_balance_row_version_prevents_lost_updates()
    {
        using var seed = CreateContext();
        var employee = NewEmployee(Unique("EMP"), $"{Unique("leavebalance").ToLowerInvariant()}@constraints.test");
        var type = new LeaveType { Name = Unique("Leave type"), IsPaid = true, DefaultAnnualEntitlement = 10, IsActive = true };
        seed.Employees.Add(employee);
        seed.LeaveTypes.Add(type);
        await seed.SaveChangesAsync();
        seed.LeaveBalances.Add(new LeaveBalance { EmployeeId = employee.Id, LeaveTypeId = type.Id, Year = 2035, Entitlement = 10 });
        await seed.SaveChangesAsync();

        using var first = CreateContext();
        using var second = CreateContext();
        var firstCopy = await first.LeaveBalances.SingleAsync(b => b.EmployeeId == employee.Id && b.LeaveTypeId == type.Id);
        var secondCopy = await second.LeaveBalances.SingleAsync(b => b.EmployeeId == employee.Id && b.LeaveTypeId == type.Id);

        firstCopy.Used = 2;
        await first.SaveChangesAsync();

        secondCopy.Used = 4;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task A_department_with_employees_cannot_be_deleted()
    {
        using var db = CreateContext();
        var department = new Department { Name = Unique("Department") };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        db.Employees.Add(new Employee
        {
            EmployeeNumber = Unique("EMP"),
            FirstName = "Department",
            LastName = "Member",
            WorkEmail = $"{Unique("department").ToLowerInvariant()}@constraints.test",
            DepartmentId = department.Id,
            EmploymentStatus = EmploymentStatus.Active,
            HireDate = new DateOnly(2024, 1, 1)
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM [Departments] WHERE [Id] = {0}", department.Id));
    }

    private AppDbContext CreateContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_factory.ConnectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
            })
            .Options);

    private static Employee NewEmployee(string number, string email) => new()
    {
        EmployeeNumber = number,
        FirstName = "Constraint",
        LastName = "Test",
        WorkEmail = email,
        EmploymentStatus = EmploymentStatus.Active,
        HireDate = new DateOnly(2024, 1, 1)
    };

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 50)];
}
