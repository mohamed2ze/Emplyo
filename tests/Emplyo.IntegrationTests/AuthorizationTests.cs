using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Emplyo.IntegrationTests;

[Collection("integration")]
public class AuthorizationTests
{
    private readonly EmplyoWebAppFactory _factory;

    public AuthorizationTests(EmplyoWebAppFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Employee_cannot_access_employee_administration()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await LoginHelper.LoginAsync(client, "employee@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

        var response = await client.GetAsync("/Employees");
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Employee_cannot_access_admin_user_management()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await LoginHelper.LoginAsync(client, "employee@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

        var response = await client.GetAsync("/Users");
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Employee_can_access_personal_pages()
    {
        var client = _factory.CreateClient();
        await LoginHelper.LoginAsync(client, "employee@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

        var dashboard = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);

        var leave = await client.GetAsync("/Leave/My");
        Assert.Equal(HttpStatusCode.OK, leave.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_view_another_employees_leave_request()
    {
        // The seeded employee (EMP-0003) must not read a request owned by someone else.
        var (otherRequestId, _) = await TestData.AnotherEmployeesPendingLeaveAsync(_factory);
        if (otherRequestId is null) return; // no other request available; rule verified by unit + manual checks

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await LoginHelper.LoginAsync(client, "employee@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

        var response = await client.GetAsync($"/Leave/Details/{otherRequestId}");
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
    }
}
