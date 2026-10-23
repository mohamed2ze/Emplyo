using System.Net;
using Emplyo.Application.Announcements;
using Emplyo.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Emplyo.IntegrationTests;

[Collection("integration")]
public class AnnouncementAuthorizationTests
{
    private readonly EmplyoWebAppFactory _factory;

    public AnnouncementAuthorizationTests(EmplyoWebAppFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Employee_cannot_view_an_announcement_targeted_at_administrators()
    {
        var id = await CreateAnnouncementAsync(AnnouncementAudience.Administrators);

        var employeeClient = await SignedInAsync("employee@emplyo.local");
        var employeeResponse = await employeeClient.GetAsync($"/Announcements/Details/{id}");
        Assert.Equal(HttpStatusCode.NotFound, employeeResponse.StatusCode);

        var adminClient = await SignedInAsync(EmplyoWebAppFactory.AdminEmail);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync($"/Announcements/Details/{id}")).StatusCode);
    }

    [Fact]
    public async Task Hr_manager_can_review_a_management_announcement()
    {
        var id = await CreateAnnouncementAsync(AnnouncementAudience.Administrators);

        var hrClient = await SignedInAsync("hr@emplyo.local");

        Assert.Equal(HttpStatusCode.OK, (await hrClient.GetAsync($"/Announcements/Details/{id}")).StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_view_an_announcement_for_another_department()
    {
        var salesId = await TestData.DepartmentIdByNameAsync(_factory, "Sales");
        var id = await CreateAnnouncementAsync(AnnouncementAudience.Department, salesId);

        var employeeClient = await SignedInAsync("employee@emplyo.local");

        Assert.Equal(HttpStatusCode.NotFound, (await employeeClient.GetAsync($"/Announcements/Details/{id}")).StatusCode);
    }

    [Fact]
    public async Task Employee_can_view_a_published_announcement_for_everyone()
    {
        var id = await CreateAnnouncementAsync(AnnouncementAudience.Everyone);

        var employeeClient = await SignedInAsync("employee@emplyo.local");

        Assert.Equal(HttpStatusCode.OK, (await employeeClient.GetAsync($"/Announcements/Details/{id}")).StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_view_a_draft_announcement()
    {
        var id = await CreateAnnouncementAsync(AnnouncementAudience.Everyone, publishNow: false);

        var employeeClient = await SignedInAsync("employee@emplyo.local");

        Assert.Equal(HttpStatusCode.NotFound, (await employeeClient.GetAsync($"/Announcements/Details/{id}")).StatusCode);
    }

    private async Task<int> CreateAnnouncementAsync(AnnouncementAudience audience, int? departmentId = null, bool publishNow = true)
    {
        using var scope = _factory.Services.CreateScope();
        var announcements = scope.ServiceProvider.GetRequiredService<IAnnouncementService>();
        return await announcements.CreateAsync("integration-admin", "Integration Admin", new AnnouncementFormDto
        {
            Title = $"Integration announcement {Guid.NewGuid():N}",
            Body = "Integration test body.",
            Audience = audience,
            DepartmentId = departmentId,
            PublishNow = publishNow
        });
    }

    private async Task<HttpClient> SignedInAsync(string email)
    {
        var password = email == EmplyoWebAppFactory.AdminEmail
            ? EmplyoWebAppFactory.AdminPassword
            : EmplyoWebAppFactory.EmployeePassword;
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await LoginHelper.LoginAsync(client, email, password);
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
            $"Sign-in for {email} returned {response.StatusCode}.");
        return client;
    }
}
