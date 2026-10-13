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
}
