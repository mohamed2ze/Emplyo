using System.Net;
using Emplyo.Application.Attendance;
using Emplyo.Application.Employees;
using Emplyo.Application.LeaveManagement;
using Emplyo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Emplyo.IntegrationTests;

[Collection("integration")]
public class WorkflowTests
{
    private readonly EmplyoWebAppFactory _factory;

    public WorkflowTests(EmplyoWebAppFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Administrator_can_create_employee()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await LoginHelper.LoginAsync(client, EmplyoWebAppFactory.AdminEmail, EmplyoWebAppFactory.AdminPassword);

        var token = await Antiforgery.GetTokenAsync(client, "/Employees/Create");
        var number = "IT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Form.EmployeeNumber"] = number,
            ["Form.FirstName"] = "Integration",
            ["Form.LastName"] = "Hire",
            ["Form.WorkEmail"] = $"{number.ToLowerInvariant()}@emplyo.test",
            ["Form.HireDate"] = "2026-02-01",
            ["Form.EmploymentType"] = "FullTime",
            ["Form.EmploymentStatus"] = "Pending",
            ["Form.CompensationCurrency"] = "USD",
            ["__RequestVerificationToken"] = token
        });

        var response = await client.PostAsync("/Employees/Create", content);
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
            $"Create returned {response.StatusCode} ({response.Headers.Location}).");
        Assert.True(await TestData.EmployeeExistsAsync(_factory, number), "Employee should have been persisted.");
    }

    [Fact]
    public async Task Creating_duplicate_employee_number_is_rejected()
    {
        var client = _factory.CreateClient();
        await LoginHelper.LoginAsync(client, EmplyoWebAppFactory.AdminEmail, EmplyoWebAppFactory.AdminPassword);

        var number = "DUP-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        async Task<HttpResponseMessage> Create(string email)
        {
            var token = await Antiforgery.GetTokenAsync(client, "/Employees/Create");
            return await client.PostAsync("/Employees/Create", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Form.EmployeeNumber"] = number,
                ["Form.FirstName"] = "Dup",
                ["Form.LastName"] = "Test",
                ["Form.WorkEmail"] = email,
                ["Form.HireDate"] = "2026-02-01",
                ["Form.EmploymentType"] = "FullTime",
                ["Form.EmploymentStatus"] = "Pending",
                ["__RequestVerificationToken"] = token
            }));
        }

        await Create($"{number.ToLowerInvariant()}@emplyo.test");
        var before = await CountEmployeesWithNumber(number);
        await Create($"{number.ToLowerInvariant()}2@emplyo.test");
        var after = await CountEmployeesWithNumber(number);

        Assert.Equal(1, before);
        Assert.Equal(1, after);
    }

    [Fact]
    public async Task Leave_request_can_be_submitted_and_approved_with_balance_update()
    {
        var employeeId = await TestData.EmployeeIdByNumberAsync(_factory, "EMP-0003");
        var leaveTypeId = await TestData.FirstLeaveTypeIdAsync(_factory);

        var start = await NextFreeFutureMondayAsync(employeeId);
        var end = start.AddDays(2); // Wednesday => 3 working days
        var year = start.Year;
        var before = await TestData.UsedLeaveAsync(_factory, employeeId, leaveTypeId, year);

        var employeeClient = _factory.CreateClient();
        await LoginHelper.LoginAsync(employeeClient, "employee@emplyo.local", EmplyoWebAppFactory.EmployeePassword);
        var token = await Antiforgery.GetTokenAsync(employeeClient, "/Leave/Create");
        var submit = await employeeClient.PostAsync("/Leave/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["LeaveTypeId"] = leaveTypeId.ToString(),
            ["StartDate"] = start.ToString("yyyy-MM-dd"),
            ["EndDate"] = end.ToString("yyyy-MM-dd"),
            ["IsHalfDay"] = "false",
            ["Reason"] = "Integration test",
            ["__RequestVerificationToken"] = token
        }));
        Assert.True(submit.IsSuccessStatusCode, $"Submit returned {submit.StatusCode}");

        var requestId = await RequestIdByDatesAsync(employeeId, start, end);
        Assert.NotNull(requestId);
        Assert.Equal("Pending", await RequestStatusAsync(requestId.Value));

        var adminClient = _factory.CreateClient();
        await LoginHelper.LoginAsync(adminClient, EmplyoWebAppFactory.AdminEmail, EmplyoWebAppFactory.AdminPassword);
        var approveToken = await Antiforgery.GetTokenAsync(adminClient, $"/Leave/Details/{requestId}");
        var approve = await adminClient.PostAsync($"/Leave/Approve/{requestId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Comments"] = "Approved by integration test",
            ["__RequestVerificationToken"] = approveToken
        }));
        Assert.True(approve.IsSuccessStatusCode, $"Approve returned {approve.StatusCode}");

        var status = await RequestStatusAsync(requestId!.Value);
        Assert.Equal("Approved", status);

        var after = await TestData.UsedLeaveAsync(_factory, employeeId, leaveTypeId, year);
        Assert.True(after >= before + 3m, $"Expected balance to increase by 3, before={before} after={after}");
    }

    private async Task<DateOnly> NextFreeFutureMondayAsync(int employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var start = new DateOnly(Math.Max(2031, DateTime.UtcNow.Year + 5), 1, 1);
        while (start.DayOfWeek != DayOfWeek.Monday) start = start.AddDays(1);

        for (var week = 0; week < 520; week++, start = start.AddDays(7))
        {
            var end = start.AddDays(2);
            var overlaps = await db.LeaveRequests.AsNoTracking()
                .AnyAsync(l => l.EmployeeId == employeeId
                    && l.StartDate <= end && start <= l.EndDate);
            if (!overlaps) return start;
        }

        throw new InvalidOperationException("No free future week was found for the leave workflow test.");
    }

    private async Task<int> CountEmployeesWithNumber(string number)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Employees.CountAsync(e => e.EmployeeNumber == number);
    }

    private async Task<int?> RequestIdByDatesAsync(int employeeId, DateOnly start, DateOnly end)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.LeaveRequests
            .Where(l => l.EmployeeId == employeeId && l.StartDate == start && l.EndDate == end)
            .OrderByDescending(l => l.Id)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync();
    }

    private async Task<string> RequestStatusAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var status = await db.LeaveRequests.Where(l => l.Id == id).Select(l => l.Status).FirstAsync();
        return status.ToString();
    }
}
