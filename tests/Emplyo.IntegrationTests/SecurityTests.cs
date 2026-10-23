using System.Net;
using System.Text;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Employees;
using Emplyo.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Emplyo.IntegrationTests;

[Collection("integration")]
public class SecurityTests
{
    private readonly EmplyoWebAppFactory _factory;

    public SecurityTests(EmplyoWebAppFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task A_state_changing_request_without_an_antiforgery_token_is_rejected()
    {
        var client = await SignedInAsync("employee@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

        var response = await client.PostAsync("/Attendance/ClockIn", new StringContent(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Repeated_failed_sign_ins_lock_the_account()
    {
        var email = $"lockout-{Guid.NewGuid():N}@emplyo.test";
        using (var scope = _factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            var created = await identity.CreateUserAsync(email, "Lockout Test", "Lockout!Pass2026", new[] { "Employee" }, null);
            Assert.True(created.Succeeded);
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failure = await LoginHelper.LoginAsync(client, email, "Wrong!Pass2026");
            Assert.Equal(HttpStatusCode.OK, failure.StatusCode);
        }

        var locked = await LoginHelper.LoginAsync(client, email, "Lockout!Pass2026");

        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Contains("locked", await locked.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Csv_export_neutralizes_formula_injection_and_includes_a_utf8_bom()
    {
        var number = "CSV-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using (var scope = _factory.Services.CreateScope())
        {
            var employees = scope.ServiceProvider.GetRequiredService<IEmployeeService>();
            await employees.CreateAsync(new EmployeeFormDto
            {
                EmployeeNumber = number,
                FirstName = "=1+1",
                LastName = "Injection",
                WorkEmail = $"{number.ToLowerInvariant()}@emplyo.test",
                EmploymentType = EmploymentType.FullTime,
                EmploymentStatus = EmploymentStatus.Pending,
                HireDate = new DateOnly(2026, 2, 1)
            });
        }

        var client = await SignedInAsync("hr@emplyo.local", EmplyoWebAppFactory.EmployeePassword);
        var response = await client.GetAsync("/Reports/ExportEmployees");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Contains("'=1+1", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public async Task A_domain_error_does_not_redirect_to_an_external_referer()
    {
        var client = await SignedInAsync(EmplyoWebAppFactory.AdminEmail, EmplyoWebAppFactory.AdminPassword);
        var number = "REF-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await CreateEmployeeAsync(client, number, $"{number.ToLowerInvariant()}@emplyo.test");
        var fields = EmployeeForm(number, $"{number.ToLowerInvariant()}2@emplyo.test");
        fields["__RequestVerificationToken"] = await Antiforgery.GetTokenAsync(client, "/Employees/Create");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/Employees/Create")
        {
            Content = new FormUrlEncodedContent(fields)
        };
        request.Headers.Referrer = new Uri("https://evil.example/phishing");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_domain_error_redirects_back_to_a_same_origin_referer()
    {
        var client = await SignedInAsync(EmplyoWebAppFactory.AdminEmail, EmplyoWebAppFactory.AdminPassword);
        var number = "REF-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await CreateEmployeeAsync(client, number, $"{number.ToLowerInvariant()}@emplyo.test");
        var fields = EmployeeForm(number, $"{number.ToLowerInvariant()}2@emplyo.test");
        fields["__RequestVerificationToken"] = await Antiforgery.GetTokenAsync(client, "/Employees/Create");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/Employees/Create")
        {
            Content = new FormUrlEncodedContent(fields)
        };
        request.Headers.Referrer = new Uri("http://localhost/Employees/Create");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Employees/Create", response.Headers.Location!.OriginalString);
    }

    private async Task CreateEmployeeAsync(HttpClient client, string number, string email)
    {
        var token = await Antiforgery.GetTokenAsync(client, "/Employees/Create");
        var fields = EmployeeForm(number, email);
        fields["__RequestVerificationToken"] = token;

        var response = await client.PostAsync("/Employees/Create", new FormUrlEncodedContent(fields));

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
            $"Create returned {response.StatusCode}.");
    }

    private static Dictionary<string, string> EmployeeForm(string number, string email) => new()
    {
        ["Form.EmployeeNumber"] = number,
        ["Form.FirstName"] = "Referer",
        ["Form.LastName"] = "Test",
        ["Form.WorkEmail"] = email,
        ["Form.HireDate"] = "2026-02-01",
        ["Form.EmploymentType"] = "FullTime",
        ["Form.EmploymentStatus"] = "Pending",
        ["Form.CompensationCurrency"] = "USD"
    };

    private async Task<HttpClient> SignedInAsync(string email, string password)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await LoginHelper.LoginAsync(client, email, password);
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
            $"Sign-in for {email} returned {response.StatusCode}.");
        return client;
    }
}
