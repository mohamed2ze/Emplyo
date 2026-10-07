using Emplyo.Application.Lifecycle;
using Emplyo.Domain.Enums;
using Emplyo.Domain.Exceptions;
using Emplyo.Infrastructure.Persistence;
using Emplyo.UnitTests.TestSupport;
using Xunit;

namespace Emplyo.UnitTests;

public class LifecycleServiceTests
{
    private static LifecycleService Create(AppDbContext db)
        => new(db, new FakeAudit(), new FakeCurrentUser(), new FakeClock());

    [Fact]
    public async Task Apply_template_is_idempotent_guarded()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var service = Create(db);

        await service.ApplyOnboardingTemplateAsync(employee.Id);
        var detail = await service.GetOnboardingDetailAsync(employee.Id);
        Assert.NotEmpty(detail!.Tasks);

        await Assert.ThrowsAsync<ConflictException>(() => service.ApplyOnboardingTemplateAsync(employee.Id));
    }

    [Fact]
    public async Task Complete_offboarding_requires_all_tasks_completed()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db, status: EmploymentStatus.Active);
        var service = Create(db);

        var caseId = await service.InitiateOffboardingAsync(employee.Id, new OffboardingInitiateDto
        {
            DepartureDate = new DateOnly(2026, 8, 1),
            Reason = "Resignation"
        });

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CompleteOffboardingAsync(caseId));
    }

    [Fact]
    public async Task Completing_offboarding_terminates_employee()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db, status: EmploymentStatus.Active);
        var service = Create(db);

        var caseId = await service.InitiateOffboardingAsync(employee.Id, new OffboardingInitiateDto
        {
            DepartureDate = new DateOnly(2026, 8, 1)
        });
        var detail = await service.GetOffboardingDetailAsync(caseId);
        foreach (var task in detail!.Tasks)
        {
            await service.SetOffboardingTaskStatusAsync(task.Id, TaskItemStatus.Completed);
        }

        await service.CompleteOffboardingAsync(caseId);

        var reloaded = await db.Employees.FindAsync(employee.Id);
        Assert.Equal(EmploymentStatus.Terminated, reloaded!.EmploymentStatus);
    }
}
