using Emplyo.Application.Employees;
using Emplyo.Domain.Enums;
using Emplyo.Domain.Exceptions;
using Emplyo.UnitTests.TestSupport;
using Xunit;

namespace Emplyo.UnitTests;

public class EmployeeServiceTests
{
    private static EmployeeService CreateService(AppDbContextHolder holder)
        => new(holder.Db, new FakeAudit(), holder.Clock);

    private sealed class AppDbContextHolder : IDisposable
    {
        public required Emplyo.Infrastructure.Persistence.AppDbContext Db { get; init; }
        public required FakeClock Clock { get; init; }
        public void Dispose() => Db.Dispose();
    }

    private static AppDbContextHolder NewHolder()
        => new() { Db = TestDb.Create(), Clock = new FakeClock() };

    private static EmployeeFormDto ValidForm(string number = "EMP-1000", string email = "new@test.local") => new()
    {
        EmployeeNumber = number,
        FirstName = "New",
        LastName = "Hire",
        WorkEmail = email,
        EmploymentType = EmploymentType.FullTime,
        EmploymentStatus = EmploymentStatus.Pending,
        HireDate = new DateOnly(2026, 1, 5)
    };

    [Fact]
    public async Task Create_rejects_duplicate_employee_number()
    {
        using var holder = NewHolder();
        var service = CreateService(holder);
        await service.CreateAsync(ValidForm());

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(ValidForm(number: "EMP-1000", email: "other@test.local")));
    }

    [Fact]
    public async Task Create_rejects_duplicate_work_email()
    {
        using var holder = NewHolder();
        var service = CreateService(holder);
        await service.CreateAsync(ValidForm());

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(ValidForm(number: "EMP-2000", email: "new@test.local")));
    }

    [Fact]
    public async Task Create_requires_name_and_email()
    {
        using var holder = NewHolder();
        var service = CreateService(holder);
        var form = ValidForm();
        form.FirstName = "";

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(form));
    }

    [Fact]
    public async Task Change_status_rejects_invalid_transition()
    {
        using var holder = NewHolder();
        var employee = await Seed.EmployeeAsync(holder.Db, status: EmploymentStatus.Active);
        var service = CreateService(holder);

        await service.ChangeStatusAsync(employee.Id, EmploymentStatus.Terminated);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ChangeStatusAsync(employee.Id, EmploymentStatus.OnLeave));
    }

    [Fact]
    public async Task Terminating_sets_termination_date()
    {
        using var holder = NewHolder();
        var employee = await Seed.EmployeeAsync(holder.Db, status: EmploymentStatus.Active);
        var service = CreateService(holder);

        await service.ChangeStatusAsync(employee.Id, EmploymentStatus.Terminated);

        var reloaded = await service.GetByIdAsync(employee.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(EmploymentStatus.Terminated, reloaded!.EmploymentStatus);
        Assert.NotNull(reloaded.TerminationDate);
    }

    [Fact]
    public async Task Rehiring_clears_the_termination_date()
    {
        using var holder = NewHolder();
        var employee = await Seed.EmployeeAsync(holder.Db, status: EmploymentStatus.Active);
        var service = CreateService(holder);
        await service.ChangeStatusAsync(employee.Id, EmploymentStatus.Terminated);

        await service.ChangeStatusAsync(employee.Id, EmploymentStatus.Active);

        var reloaded = await service.GetByIdAsync(employee.Id);
        Assert.Equal(EmploymentStatus.Active, reloaded!.EmploymentStatus);
        Assert.Null(reloaded.TerminationDate);
    }

    [Fact]
    public async Task Create_normalizes_employee_number_and_email()
    {
        using var holder = NewHolder();
        var service = CreateService(holder);
        var form = ValidForm(number: "  EMP-3000  ", email: "  Mixed.Case@Test.Local  ");
        form.FirstName = "  Ada  ";
        form.LastName = "  Lovelace  ";

        var id = await service.CreateAsync(form);

        var employee = await service.GetByIdAsync(id);
        Assert.Equal("EMP-3000", employee!.EmployeeNumber);
        Assert.Equal("mixed.case@test.local", employee.WorkEmail);
        Assert.Equal("Ada", employee.FirstName);
        Assert.Equal("Lovelace", employee.LastName);
    }

    [Fact]
    public async Task Create_initializes_balances_for_active_leave_types_only()
    {
        using var holder = NewHolder();
        var active = await Seed.LeaveTypeAsync(holder.Db, name: "Annual Leave", entitlement: 20);
        await Seed.InactiveLeaveTypeAsync(holder.Db, name: "Legacy Leave", entitlement: 5);
        var service = CreateService(holder);

        var id = await service.CreateAsync(ValidForm());

        var balances = holder.Db.LeaveBalances.Where(b => b.EmployeeId == id).ToList();
        var balance = Assert.Single(balances);
        Assert.Equal(active.Id, balance.LeaveTypeId);
        Assert.Equal(20m, balance.Entitlement);
        Assert.Equal(2026, balance.Year);
    }

    [Fact]
    public async Task Create_rejects_a_hire_date_more_than_two_years_in_the_future()
    {
        using var holder = NewHolder();
        var service = CreateService(holder);
        var form = ValidForm();
        form.HireDate = new DateOnly(2028, 6, 16);

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(form));
    }

    [Fact]
    public async Task Create_rejects_a_department_that_does_not_exist()
    {
        using var holder = NewHolder();
        var service = CreateService(holder);
        var form = ValidForm();
        form.DepartmentId = 999;

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(form));
    }

    [Fact]
    public async Task Update_rejects_making_an_employee_their_own_manager()
    {
        using var holder = NewHolder();
        var employee = await Seed.EmployeeAsync(holder.Db);
        var service = CreateService(holder);
        var form = ValidForm(number: employee.EmployeeNumber, email: employee.WorkEmail);
        form.ManagerId = employee.Id;

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateAsync(employee.Id, form));
    }

    [Fact]
    public async Task Update_allows_keeping_the_current_employee_number_and_email()
    {
        using var holder = NewHolder();
        var employee = await Seed.EmployeeAsync(holder.Db);
        var service = CreateService(holder);
        var form = ValidForm(number: employee.EmployeeNumber, email: employee.WorkEmail);
        form.FirstName = "Renamed";

        await service.UpdateAsync(employee.Id, form);

        var reloaded = await service.GetByIdAsync(employee.Id);
        Assert.Equal("Renamed", reloaded!.FirstName);
    }

    [Fact]
    public async Task Manager_options_exclude_terminated_employees_and_an_excluded_id()
    {
        using var holder = NewHolder();
        var active = await Seed.EmployeeAsync(holder.Db, number: "EMP-1001", email: "active@test.local");
        await Seed.EmployeeAsync(holder.Db, number: "EMP-1002", email: "terminated@test.local", status: EmploymentStatus.Terminated);
        await Seed.EmployeeAsync(holder.Db, number: "EMP-1003", email: "excluded@test.local");
        var service = CreateService(holder);

        var options = await service.GetManagerOptionsAsync(excludeEmployeeId: active.Id);

        Assert.DoesNotContain(options, o => o.EmployeeNumber == "EMP-1002");
        Assert.DoesNotContain(options, o => o.EmployeeNumber == "EMP-1001");
        Assert.Contains(options, o => o.EmployeeNumber == "EMP-1003");
    }
}
