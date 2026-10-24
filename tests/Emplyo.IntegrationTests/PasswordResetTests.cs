using System.Net;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    [Fact]
    public async Task Reset_password_does_not_reveal_whether_an_account_exists()
    {
        var existingEmail = $"reset-enum-{Guid.NewGuid():N}@emplyo.test";
        using (var scope = _factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            var created = await identity.CreateUserAsync(
                existingEmail, "Enumeration User", "Identity!Pass2026", new[] { "Employee" }, null);
            Assert.True(created.Succeeded);
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resetPath = $"/Account/ResetPassword?email={Uri.EscapeDataString(existingEmail)}&token=invalid-token";
        var antiforgery = await Antiforgery.GetTokenAsync(client, resetPath);

        async Task<HttpResponseMessage> SubmitAsync(string email)
            => await client.PostAsync("/Account/ResetPassword", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = email,
                ["Token"] = "invalid-token",
                ["Password"] = "NewPassword!2026",
                ["ConfirmPassword"] = "NewPassword!2026",
                ["__RequestVerificationToken"] = antiforgery
            }));

        var existing = await SubmitAsync(existingEmail);
        var missing = await SubmitAsync($"missing-{Guid.NewGuid():N}@emplyo.test");

        Assert.Equal(HttpStatusCode.OK, existing.StatusCode);
        Assert.Equal(existing.StatusCode, missing.StatusCode);

        var existingBody = await existing.Content.ReadAsStringAsync();
        var missingBody = await missing.Content.ReadAsStringAsync();
        Assert.Contains("invalid or has expired", existingBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalid or has expired", missingBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Forgot_password_uses_the_configured_public_base_url_not_the_request_host()
    {
        var sender = new CapturingEmailSender();
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["App:PublicBaseUrl"] = "https://emplyo.example",
                    ["AllowedHosts"] = "*"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAppEmailSender>();
                services.AddSingleton<IAppEmailSender>(sender);
            });
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var antiforgery = await Antiforgery.GetTokenAsync(client, "/Account/ForgotPassword");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/ForgotPassword")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = "employee@emplyo.local",
                ["__RequestVerificationToken"] = antiforgery
            })
        };
        request.Headers.Host = "evil.example";

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(sender.LastHtml);
        Assert.Contains("https://emplyo.example/Account/ResetPassword", sender.LastHtml);
        Assert.DoesNotContain("evil.example", sender.LastHtml);
    }

    private sealed class CapturingEmailSender : IAppEmailSender
    {
        public bool IsEnabled => true;
        public string? LastHtml { get; private set; }

        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            LastHtml = htmlBody;
            return Task.CompletedTask;
        }
    }
}
