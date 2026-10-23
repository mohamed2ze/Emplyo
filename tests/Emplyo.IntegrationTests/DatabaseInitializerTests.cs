using Emplyo.Application.Common.Interfaces;
using Emplyo.Infrastructure.Identity;
using Emplyo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Emplyo.IntegrationTests;

[Collection("integration")]
public class DatabaseInitializerTests
{
    private readonly EmplyoWebAppFactory _factory;

    public DatabaseInitializerTests(EmplyoWebAppFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task The_three_application_roles_exist()
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in DatabaseInitializer.Roles)
        {
            Assert.True(await roleManager.RoleExistsAsync(role), $"Role {role} is missing.");
        }
    }

    [Fact]
    public async Task Default_settings_are_idempotent()
    {
        using var scope = _factory.Services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.AppSettings.CountAsync();

        await initializer.EnsureDefaultSettingsAsync();
        await initializer.EnsureDefaultSettingsAsync();

        Assert.Equal(before, await db.AppSettings.CountAsync());
        Assert.True(await db.AppSettings.AnyAsync(s => s.Key == "Organization.Name"));
        Assert.True(await db.AppSettings.AnyAsync(s => s.Key == "Organization.TimeZone"));
        Assert.True(await db.AppSettings.AnyAsync(s => s.Key == "Attendance.WorkDayStart"));
    }

    [Fact]
    public async Task Development_seed_is_idempotent()
    {
        using var scope = _factory.Services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var beforeDepartments = await db.Departments.CountAsync();
        var beforeEmployees = await db.Employees.CountAsync();
        var beforeLeaveTypes = await db.LeaveTypes.CountAsync();
        var beforeAnnouncements = await db.Announcements.CountAsync();

        await initializer.SeedDevelopmentDataAsync(EmplyoWebAppFactory.EmployeePassword);

        Assert.Equal(beforeDepartments, await db.Departments.CountAsync());
        Assert.Equal(beforeEmployees, await db.Employees.CountAsync());
        Assert.Equal(beforeLeaveTypes, await db.LeaveTypes.CountAsync());
        Assert.Equal(beforeAnnouncements, await db.Announcements.CountAsync());
    }

    [Fact]
    public async Task Administrator_provisioning_is_refused_when_an_administrator_already_exists()
    {
        using var scope = _factory.Services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var before = (await identity.GetUsersInRoleAsync("Administrator")).Count;

        var (created, message) = await initializer.EnsureAdministratorAsync(
            $"another-admin-{Guid.NewGuid():N}@emplyo.test", "Another!Admin2026", "Another Admin");

        Assert.False(created);
        Assert.Contains("administrator account already exists", message);
        Assert.Equal(before, (await identity.GetUsersInRoleAsync("Administrator")).Count);
    }

    [Fact]
    public async Task Administrator_provisioning_requires_credentials()
    {
        using var scope = _factory.Services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();

        var missingEmail = await initializer.EnsureAdministratorAsync("", "Another!Admin2026", "Admin");
        var missingPassword = await initializer.EnsureAdministratorAsync("admin@test.local", "", "Admin");

        Assert.False(missingEmail.Created);
        Assert.False(missingPassword.Created);
        Assert.Contains("required", missingEmail.Message);
    }
}
