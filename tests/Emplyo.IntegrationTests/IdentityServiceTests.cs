using Emplyo.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Emplyo.IntegrationTests;

[Collection("integration")]
public class IdentityServiceTests
{
    private readonly EmplyoWebAppFactory _factory;

    public IdentityServiceTests(EmplyoWebAppFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Create_find_and_record_login_for_a_user()
    {
        using var scope = _factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var email = UniqueEmail();
        var created = await identity.CreateUserAsync(email, "Identity User", "Identity!Pass2026", new[] { "Employee" }, null);
        Assert.True(created.Succeeded);

        var found = await identity.FindByIdAsync(created.UserId!);

        Assert.NotNull(found);
        Assert.Equal(email, found!.Email);
        Assert.Equal("Identity User", found.DisplayName);
        Assert.True(found.IsActive);
        Assert.Contains("Employee", found.Roles);

        await identity.RecordLoginAsync(created.UserId!);
        Assert.NotNull((await identity.FindByIdAsync(created.UserId!))!.LastLoginUtc);
    }

    [Fact]
    public async Task Duplicate_email_creation_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var email = UniqueEmail();
        Assert.True((await identity.CreateUserAsync(email, "First", "Identity!Pass2026", new[] { "Employee" }, null)).Succeeded);

        var duplicate = await identity.CreateUserAsync(email, "Second", "Identity!Pass2026", new[] { "Employee" }, null);

        Assert.False(duplicate.Succeeded);
        Assert.Contains("already exists", duplicate.Errors.Single());
    }

    [Fact]
    public async Task Role_changes_are_persisted()
    {
        using var scope = _factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var created = await identity.CreateUserAsync(UniqueEmail(), "Role User", "Identity!Pass2026", new[] { "Employee" }, null);

        await identity.SetRolesAsync(created.UserId!, new[] { "HRManager" });

        Assert.Equal("HRManager", Assert.Single((await identity.FindByIdAsync(created.UserId!))!.Roles));
    }

    [Fact]
    public async Task Deactivated_users_are_excluded_from_role_membership_until_reactivated()
    {
        using var scope = _factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var email = UniqueEmail();
        var created = await identity.CreateUserAsync(email, "Toggle User", "Identity!Pass2026", new[] { "HRManager" }, null);

        await identity.SetActiveAsync(created.UserId!, false);

        Assert.False((await identity.FindByIdAsync(created.UserId!))!.IsActive);
        Assert.DoesNotContain(await identity.GetUsersInRoleAsync("HRManager"), u => u.Id == created.UserId);

        await identity.SetActiveAsync(created.UserId!, true);

        Assert.True((await identity.FindByIdAsync(created.UserId!))!.IsActive);
        Assert.Contains(await identity.GetUsersInRoleAsync("HRManager"), u => u.Id == created.UserId);
    }

    [Fact]
    public async Task Search_matches_email_or_display_name()
    {
        using var scope = _factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var marker = Guid.NewGuid().ToString("N")[..10];
        var created = await identity.CreateUserAsync($"{marker}@search.test", $"Searchable {marker}", "Identity!Pass2026", new[] { "Employee" }, null);

        var byEmail = await identity.SearchUsersAsync(marker);
        var byName = await identity.SearchUsersAsync($"Searchable {marker}");

        Assert.Contains(byEmail, u => u.Id == created.UserId);
        Assert.Contains(byName, u => u.Id == created.UserId);
    }

    [Fact]
    public async Task Password_reset_rejects_a_weak_password_and_accepts_a_valid_one()
    {
        using var scope = _factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var created = await identity.CreateUserAsync(UniqueEmail(), "Password User", "Identity!Pass2026", new[] { "Employee" }, null);

        Assert.False(await identity.ResetPasswordAsync(created.UserId!, "weak"));
        Assert.True(await identity.ResetPasswordAsync(created.UserId!, "NewIdentity!Pass2026"));
    }

    private static string UniqueEmail() => $"identity-{Guid.NewGuid():N}@emplyo.test";
}
