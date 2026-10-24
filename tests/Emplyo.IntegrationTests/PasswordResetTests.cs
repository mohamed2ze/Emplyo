using System.Net;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Emplyo.IntegrationTests;

[Collection("integration")]
public class PasswordResetTests
{
    private readonly EmplyoWebAppFactory _factory;

    public PasswordResetTests(EmplyoWebAppFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task A_user_can_reset_their_password_with_a_valid_token()
    {
        var email = $"reset-{Guid.NewGuid():N}@emplyo.test";
        const string oldPassword = "Identity!Pass2026";
        const string newPassword = "ResetPassword!Pass2026";

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var created = await identity.CreateUserAsync(email, "Reset User", oldPassword, new[] { "Employee" }, null);
            Assert.True(created.Succeeded);
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            token = await userManager.GeneratePasswordResetTokenAsync(user!);
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resetUrl = $"/Account/ResetPassword?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        var page = await client.GetAsync(resetUrl);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        var antiforgery = await Antiforgery.GetTokenAsync(client, resetUrl);
        var response = await client.PostAsync("/Account/ResetPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Token"] = token,
            ["Password"] = newPassword,
            ["ConfirmPassword"] = newPassword,
            ["__RequestVerificationToken"] = antiforgery
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());

        var newLogin = await LoginHelper.LoginAsync(client, email, newPassword);
        Assert.True(newLogin.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK);

        var oldLoginClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var oldLogin = await LoginHelper.LoginAsync(oldLoginClient, email, oldPassword);
        Assert.Equal(HttpStatusCode.OK, oldLogin.StatusCode);
    }
}
