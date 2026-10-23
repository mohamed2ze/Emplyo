using Emplyo.Application.Organization;
using Emplyo.Domain.Exceptions;
using Emplyo.Infrastructure.Persistence;
using Emplyo.UnitTests.TestSupport;
using Xunit;

namespace Emplyo.UnitTests;

public class DepartmentServiceTests
{
    private static DepartmentService Create(AppDbContext db) => new(db, new FakeAudit(), new FakeClock());

    private static DepartmentFormDto Form(string name = "Engineering") => new() { Name = name, IsActive = true };

    [Fact]
    public async Task Create_trims_the_name_and_rejects_a_duplicate()
    {
        using var db = TestDb.Create();
        var service = Create(db);

        var id = await service.CreateAsync(Form("  Engineering  "));

        Assert.Equal("Engineering", (await db.Departments.FindAsync(id))!.Name);
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(Form("Engineering")));
    }

    [Fact]
    public async Task Name_exists_ignores_the_department_being_edited()
    {
        using var db = TestDb.Create();
        var department = await Seed.DepartmentAsync(db);
        var service = Create(db);

        Assert.True(await service.NameExistsAsync("Engineering"));
        Assert.False(await service.NameExistsAsync("Engineering", excludeId: department.Id));
        Assert.False(await service.NameExistsAsync("  Engineering  ", excludeId: department.Id));
    }

    [Fact]
    public async Task Update_rejects_renaming_to_another_departments_name()
    {
        using var db = TestDb.Create();
        await Seed.DepartmentAsync(db, "Engineering");
        var sales = await Seed.DepartmentAsync(db, "Sales");
        var service = Create(db);

        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(sales.Id, Form("Engineering")));
    }

    [Fact]
    public async Task The_active_lookup_excludes_inactive_departments()
    {
        using var db = TestDb.Create();
        await Seed.DepartmentAsync(db, "Engineering");
        await Seed.DepartmentAsync(db, "Legacy", isActive: false);
        var service = Create(db);

        var active = await service.GetLookupAsync();
        var all = await service.GetLookupAsync(activeOnly: false);

        Assert.Equal("Engineering", Assert.Single(active).Name);
        Assert.Equal(2, all.Count);
    }
}

public class PositionServiceTests
{
    private static PositionService Create(AppDbContext db) => new(db, new FakeAudit(), new FakeClock());

    private static PositionFormDto Form(string title = "Software Engineer", int? departmentId = null) => new()
    {
        Title = title,
        DepartmentId = departmentId,
        IsActive = true
    };

    [Fact]
    public async Task Create_rejects_a_duplicate_title_in_the_same_department()
    {
        using var db = TestDb.Create();
        var department = await Seed.DepartmentAsync(db);
        var service = Create(db);
        await service.CreateAsync(Form(departmentId: department.Id));

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(Form("  Software Engineer  ", department.Id)));
    }

    [Fact]
    public async Task The_same_title_is_allowed_in_a_different_department()
    {
        using var db = TestDb.Create();
        var engineering = await Seed.DepartmentAsync(db, "Engineering");
        var sales = await Seed.DepartmentAsync(db, "Sales");
        var service = Create(db);

        await service.CreateAsync(Form(departmentId: engineering.Id));
        var id = await service.CreateAsync(Form(departmentId: sales.Id));

        Assert.Equal("Sales", (await service.GetByIdAsync(id))!.DepartmentName);
    }

    [Fact]
    public async Task Update_ignores_the_current_position_when_checking_for_a_duplicate()
    {
        using var db = TestDb.Create();
        var service = Create(db);
        var id = await service.CreateAsync(Form());

        await service.UpdateAsync(id, Form("  Software Engineer  "));

        Assert.Equal("Software Engineer", (await db.Positions.FindAsync(id))!.Title);
    }

    [Fact]
    public async Task The_lookup_filters_by_department_and_active_state()
    {
        using var db = TestDb.Create();
        var engineering = await Seed.DepartmentAsync(db, "Engineering");
        var sales = await Seed.DepartmentAsync(db, "Sales");
        await Seed.PositionAsync(db, "Engineer", engineering.Id);
        await Seed.PositionAsync(db, "Retired Role", engineering.Id, isActive: false);
        await Seed.PositionAsync(db, "Sales Lead", sales.Id);
        var service = Create(db);

        var engineeringActive = await service.GetLookupAsync(engineering.Id);

        Assert.Equal("Engineer", Assert.Single(engineeringActive).Name);
    }
}
