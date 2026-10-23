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

    public static TheoryData<string> EmployeeManagementPaths => new()
    {
        "/Employees",
        "/Employees/Create",
        "/Departments",
        "/Positions",
        "/Attendance",
        "/Leave",
        "/Reports",
        "/Users",
        "/Audit",
        "/Settings",
        "/LeaveTypes",
        "/Onboarding",
        "/Offboarding",
        "/Documents",
        "/Announcements/Create",
        "/Holidays/Create"
    };

    public static TheoryData<string> AdministratorOnlyPaths => new()
    {
        "/Users",
        "/Audit",
        "/Settings",
        "/LeaveTypes"
    };

    public static TheoryData<string> HrOperationalPaths => new()
    {
        "/Employees",
        "/Reports",
        "/Leave",
        "/Attendance",
        "/Departments",
        "/Positions",
        "/Onboarding",
        "/Offboarding",
        "/Holidays/Create",
        "/Documents",
        "/Announcements/Create"
    };

    [Theory]
    [MemberData(nameof(EmployeeManagementPaths))]
    public async Task Employee_cannot_access_management_endpoints(string path)
    {
        var client = await SignedInAsync("employee@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

        var response = await client.GetAsync(path);

        AccessControl.AssertDenied(response);
    }

    [Theory]
    [MemberData(nameof(AdministratorOnlyPaths))]
    public async Task Hr_manager_cannot_access_administrator_endpoints(string path)
    {
        var client = await SignedInAsync("hr@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

        var response = await client.GetAsync(path);

        AccessControl.AssertDenied(response);
    }

    [Theory]
    [MemberData(nameof(HrOperationalPaths))]
    public async Task Hr_manager_can_access_operational_endpoints(string path)
    {
        var client = await SignedInAsync("hr@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

        var attendance = await client.GetAsync("/Attendance/My");
        Assert.Equal(HttpStatusCode.OK, attendance.StatusCode);

        var profile = await client.GetAsync("/Profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_view_another_employees_leave_request()
    {
        var (otherRequestId, _) = await TestData.AnotherEmployeesPendingLeaveAsync(_factory);
        Assert.NotNull(otherRequestId);

        var client = await SignedInAsync("employee@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

        var response = await client.GetAsync($"/Leave/Details/{otherRequestId}");

        AccessControl.AssertDenied(response);
    }

    [Fact]
    public async Task Employee_cannot_view_another_employees_details()
    {
        var ownId = await TestData.EmployeeIdByNumberAsync(_factory, "EMP-0003");
        var otherId = await TestData.EmployeeIdByNumberAsync(_factory, "EMP-0001");
        var client = await SignedInAsync("employee@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

        var own = await client.GetAsync($"/Employees/Details/{ownId}");
        var other = await client.GetAsync($"/Employees/Details/{otherId}");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        AccessControl.AssertDenied(other);
    }

    [Fact]
    public async Task Manager_can_view_direct_reports_but_not_unrelated_employees()
    {
        var managerId = await TestData.EmployeeIdByNumberAsync(_factory, "EMP-0002");
        var reportId = await TestData.EmployeeIdByNumberAsync(_factory, "EMP-0003");
        var unrelatedId = await TestData.EmployeeIdByNumberAsync(_factory, "EMP-0001");
        var client = await SignedInAsync("manager@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Employees/Details/{managerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Employees/Details/{reportId}")).StatusCode);
        AccessControl.AssertDenied(await client.GetAsync($"/Employees/Details/{unrelatedId}"));
    }

    private async Task<HttpClient> SignedInAsync(string email, string password)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await LoginHelper.LoginAsync(client, email, password);
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
            $"Sign-in for {email} returned {response.StatusCode}.");
        return client;
    }

}
