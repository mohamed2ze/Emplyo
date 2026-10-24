using Emplyo.Application.Users;
using Emplyo.Domain.Exceptions;
using Emplyo.Infrastructure.Persistence;
using Emplyo.UnitTests.TestSupport;
using Xunit;

namespace Emplyo.UnitTests;

public class UserAdminServiceTests
{
    private static UserAdminService Create(AppDbContext db, FakeIdentityService identity, FakeAudit? audit = null)
        => new(identity, db, audit ?? new FakeAudit());

    private static CreateUserDto ValidCreate(string role = "Employee", int? employeeId = null) => new()
    {
        Email = "new.user@test.local",
        DisplayName = "New User",
        Password = "ValidPass!1234",
        Role = role,
        EmployeeId = employeeId
    };

    [Fact]
    public async Task Create_rejects_a_role_outside_the_assignable_set()
    {
        using var db = TestDb.Create();
        var identity = new FakeIdentityService();

        var result = await Create(db, identity).CreateAsync(ValidCreate(role: "SuperUser"));

        Assert.False(result.Success);
        Assert.Equal(0, identity.CreateUserCalls);
        Assert.Contains("Invalid role", result.Errors.Single());
    }

    [Fact]
    public async Task Create_rejects_an_employee_already_linked_to_another_account()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        employee.UserId = "existing-user";
        await db.SaveChangesAsync();
        var identity = new FakeIdentityService();

        var result = await Create(db, identity).CreateAsync(ValidCreate(employeeId: employee.Id));

        Assert.False(result.Success);
        Assert.Equal(0, identity.CreateUserCalls);
        Assert.Contains("already linked", result.Errors.Single());
    }

    [Fact]
    public async Task Create_links_the_new_account_to_the_selected_employee()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        var identity = new FakeIdentityService();
        var audit = new FakeAudit();

        var result = await Create(db, identity, audit).CreateAsync(ValidCreate(employeeId: employee.Id));

        Assert.True(result.Success);
        Assert.NotNull(result.UserId);
        Assert.Equal(result.UserId, (await db.Employees.FindAsync(employee.Id))!.UserId);
        Assert.Contains("User.Created", audit.Recorded);
    }

    [Fact]
    public async Task Update_rejects_an_invalid_role_without_changing_the_account()
    {
        using var db = TestDb.Create();
        var identity = new FakeIdentityService();
        identity.AddUser("user-1", "user@test.local", "User One", isActive: true, "Employee");
        var service = Create(db, identity);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateAsync("user-1", new EditUserDto { Role = "SuperUser", IsActive = false }));

        Assert.Equal("Employee", Assert.Single(identity.Users["user-1"].Roles));
        Assert.True(identity.Users["user-1"].IsActive);
    }

    [Fact]
    public async Task Update_rejects_an_unknown_user()
    {
        using var db = TestDb.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Create(db, new FakeIdentityService()).UpdateAsync("missing", new EditUserDto { Role = "Employee" }));
    }

    [Fact]
    public async Task Update_does_not_apply_role_or_active_changes_when_the_employee_link_conflicts()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        employee.UserId = "other-user";
        await db.SaveChangesAsync();
        var identity = new FakeIdentityService();
        identity.AddUser("user-1", "user@test.local", "User One", isActive: true, "Employee");
        var service = Create(db, identity);

        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync("user-1", new EditUserDto
        {
            Role = "Administrator",
            IsActive = false,
            EmployeeId = employee.Id
        }));

        Assert.Equal("Employee", Assert.Single(identity.Users["user-1"].Roles));
        Assert.True(identity.Users["user-1"].IsActive);
        Assert.Equal("other-user", (await db.Employees.FindAsync(employee.Id))!.UserId);
    }

    [Fact]
    public async Task Update_does_not_apply_role_or_active_changes_when_the_employee_does_not_exist()
    {
        using var db = TestDb.Create();
        var identity = new FakeIdentityService();
        identity.AddUser("user-1", "user@test.local", "User One", isActive: true, "Employee");
        var service = Create(db, identity);

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateAsync("user-1", new EditUserDto
        {
            Role = "Administrator",
            IsActive = false,
            EmployeeId = 999
        }));

        Assert.Equal("Employee", Assert.Single(identity.Users["user-1"].Roles));
        Assert.True(identity.Users["user-1"].IsActive);
    }

    [Fact]
    public async Task Update_moves_the_link_from_the_previous_employee_to_the_new_one()
    {
        using var db = TestDb.Create();
        var previous = await Seed.EmployeeAsync(db, number: "EMP-1001", email: "previous@test.local");
        previous.UserId = "user-1";
        var next = await Seed.EmployeeAsync(db, number: "EMP-1002", email: "next@test.local");
        await db.SaveChangesAsync();
        var identity = new FakeIdentityService();
        identity.AddUser("user-1", "user@test.local", "User One", isActive: true, "Employee");
        var service = Create(db, identity);

        await service.UpdateAsync("user-1", new EditUserDto
        {
            Role = "HRManager",
            IsActive = true,
            EmployeeId = next.Id
        });

        Assert.Null((await db.Employees.FindAsync(previous.Id))!.UserId);
        Assert.Equal("user-1", (await db.Employees.FindAsync(next.Id))!.UserId);
        Assert.Equal("HRManager", Assert.Single(identity.Users["user-1"].Roles));
    }

    [Fact]
    public async Task Update_without_an_employee_unlinks_the_current_employee()
    {
        using var db = TestDb.Create();
        var employee = await Seed.EmployeeAsync(db);
        employee.UserId = "user-1";
        await db.SaveChangesAsync();
        var identity = new FakeIdentityService();
        identity.AddUser("user-1", "user@test.local", "User One", isActive: true, "Employee");
        var service = Create(db, identity);

        await service.UpdateAsync("user-1", new EditUserDto { Role = "Employee", IsActive = true });

        Assert.Null((await db.Employees.FindAsync(employee.Id))!.UserId);
    }

    [Fact]
    public async Task Update_cannot_remove_or_deactivate_the_last_active_administrator()
    {
        using var db = TestDb.Create();
        var identity = new FakeIdentityService();
        identity.AddUser("admin-1", "admin@test.local", "Admin One", isActive: true, "Administrator");
        var service = Create(db, identity);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateAsync("admin-1", new EditUserDto { Role = "Employee", IsActive = true }));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateAsync("admin-1", new EditUserDto { Role = "Administrator", IsActive = false }));

        Assert.Equal("Administrator", Assert.Single(identity.Users["admin-1"].Roles));
        Assert.True(identity.Users["admin-1"].IsActive);
    }

    [Fact]
    public async Task Update_allows_removing_an_administrator_when_another_active_one_exists()
    {
        using var db = TestDb.Create();
        var identity = new FakeIdentityService();
        identity.AddUser("admin-1", "admin1@test.local", "Admin One", isActive: true, "Administrator");
        identity.AddUser("admin-2", "admin2@test.local", "Admin Two", isActive: true, "Administrator");
        var service = Create(db, identity);

        await service.UpdateAsync("admin-1", new EditUserDto { Role = "Employee", IsActive = true });

        Assert.Equal("Employee", Assert.Single(identity.Users["admin-1"].Roles));
        Assert.Equal("Administrator", Assert.Single(identity.Users["admin-2"].Roles));
    }

    [Fact]
    public async Task Reset_password_rejects_an_unknown_user()
    {
        using var db = TestDb.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Create(db, new FakeIdentityService()).ResetPasswordAsync("missing", new ResetUserPasswordDto { NewPassword = "ValidPass!1234" }));
    }

    [Fact]
    public async Task Reset_password_reports_a_policy_failure()
    {
        using var db = TestDb.Create();
        var identity = new FakeIdentityService { ResetPasswordResult = false };
        identity.AddUser("user-1", "user@test.local", "User One", isActive: true, "Employee");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Create(db, identity).ResetPasswordAsync("user-1", new ResetUserPasswordDto { NewPassword = "weak" }));
    }

    [Fact]
    public async Task Reset_password_records_an_audit_entry_on_success()
    {
        using var db = TestDb.Create();
        var identity = new FakeIdentityService();
        identity.AddUser("user-1", "user@test.local", "User One", isActive: true, "Employee");
        var audit = new FakeAudit();

        await Create(db, identity, audit).ResetPasswordAsync("user-1", new ResetUserPasswordDto { NewPassword = "ValidPass!1234" });

        Assert.Contains("User.PasswordReset", audit.Recorded);
    }

    [Fact]
    public async Task Linkable_employees_exclude_accounts_linked_to_other_users()
    {
        using var db = TestDb.Create();
        var linkedToMe = await Seed.EmployeeAsync(db, number: "EMP-1001", email: "me@test.local");
        linkedToMe.UserId = "user-1";
        var linkedElsewhere = await Seed.EmployeeAsync(db, number: "EMP-1002", email: "other@test.local");
        linkedElsewhere.UserId = "user-2";
        var available = await Seed.EmployeeAsync(db, number: "EMP-1003", email: "free@test.local");
        await db.SaveChangesAsync();
        var service = Create(db, new FakeIdentityService());

        var withoutUser = await service.GetLinkableEmployeesAsync();
        var forCurrentUser = await service.GetLinkableEmployeesAsync("user-1");

        Assert.Equal(new[] { available.Id }, withoutUser.Select(e => e.Id));
        Assert.Equal(new[] { linkedToMe.Id, available.Id }, forCurrentUser.Select(e => e.Id).OrderBy(id => id));
    }
}
