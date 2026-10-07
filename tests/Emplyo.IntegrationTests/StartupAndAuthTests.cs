using System.Net;
using Xunit;

namespace Emplyo.IntegrationTests;

[Collection("integration")]
public class StartupAndAuthTests
{
    private readonly EmplyoWebAppFactory _factory;

    public StartupAndAuthTests(EmplyoWebAppFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Application_starts_and_health_endpoint_is_healthy()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_page_is_public()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Protected_page_redirects_anonymous_user_to_login()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync("/Employees");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Administrator_can_sign_in_and_reach_employee_directory()
    {
        var client = _factory.CreateClient();
        var login = await LoginHelper.LoginAsync(client, EmplyoWebAppFactory.AdminEmail, EmplyoWebAppFactory.AdminPassword);
        Assert.True(login.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect);

        var employees = await client.GetAsync("/Employees");
        Assert.Equal(HttpStatusCode.OK, employees.StatusCode);
        Assert.Contains("Employees", await employees.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invalid_credentials_do_not_authenticate()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await LoginHelper.LoginAsync(client, EmplyoWebAppFactory.AdminEmail, "wrong-password-123456");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // re-rendered login form with errors

        var protectedPage = await client.GetAsync("/Employees");
        Assert.Equal(HttpStatusCode.Redirect, protectedPage.StatusCode);
    }
}
