using System.Net;
using System.Text;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Employees;
using Emplyo.Domain.Enums;
using Emplyo.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
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
    public async Task Repeated_failed_sign_ins_lock_the_account_without_revealing_it()
    {
        var email = $"lockout-{Guid.NewGuid():N}@emplyo.test";
        string userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            var created = await identity.CreateUserAsync(email, "Lockout Test", "Lockout!Pass2026", new[] { "Employee" }, null);
            Assert.True(created.Succeeded);
            userId = created.UserId!;
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failure = await LoginHelper.LoginAsync(client, email, "Wrong!Pass2026");
            Assert.Equal(HttpStatusCode.OK, failure.StatusCode);
        }

        var locked = await LoginHelper.LoginAsync(client, email, "Lockout!Pass2026");
        var body = await locked.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Contains("Invalid sign-in attempt", body);
        Assert.DoesNotContain("locked", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Employees")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId);
            Assert.True(await userManager.IsLockedOutAsync(user!));
        }
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

    [Fact]
    public async Task Requests_with_an_untrusted_host_header_are_rejected()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Account/Login");
        request.Headers.Host = "evil.example";

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Security_headers_are_set_on_public_and_authenticated_pages()
    {
        var anonymous = _factory.CreateClient();
        var login = await anonymous.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal("nosniff", login.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", login.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", login.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", login.Headers.GetValues("Cache-Control").Single());
        Assert.Contains("default-src 'self'", login.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", login.Headers.GetValues("Content-Security-Policy").Single());

        var client = await SignedInAsync(EmplyoWebAppFactory.AdminEmail, EmplyoWebAppFactory.AdminPassword);
        var dashboard = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Equal("same-origin", dashboard.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("nosniff", dashboard.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task An_employee_cannot_read_another_employees_photo()
    {
        var otherId = await TestData.EmployeeIdByNumberAsync(_factory, "EMP-0001");
        var stored = await SetPhotoAsync(otherId, "photo.jpg");

        try
        {
            var client = await SignedInAsync("employee@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

            AccessControl.AssertDenied(await client.GetAsync($"/Employees/Photo/{otherId}"));
        }
        finally
        {
            await ClearPhotoAsync(otherId, stored);
        }
    }

    [Fact]
    public async Task A_manager_can_read_a_direct_reports_photo_but_not_an_unrelated_photo()
    {
        var reportId = await TestData.EmployeeIdByNumberAsync(_factory, "EMP-0003");
        var unrelatedId = await TestData.EmployeeIdByNumberAsync(_factory, "EMP-0001");
        var reportPhoto = await SetPhotoAsync(reportId, "report.jpg");
        var unrelatedPhoto = await SetPhotoAsync(unrelatedId, "unrelated.jpg");

        try
        {
            var manager = await SignedInAsync("manager@emplyo.local", EmplyoWebAppFactory.EmployeePassword);
            Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/Employees/Photo/{reportId}")).StatusCode);
            AccessControl.AssertDenied(await manager.GetAsync($"/Employees/Photo/{unrelatedId}"));

            var employee = await SignedInAsync("employee@emplyo.local", EmplyoWebAppFactory.EmployeePassword);
            Assert.Equal(HttpStatusCode.OK, (await employee.GetAsync($"/Employees/Photo/{reportId}")).StatusCode);
        }
        finally
        {
            await ClearPhotoAsync(reportId, reportPhoto);
            await ClearPhotoAsync(unrelatedId, unrelatedPhoto);
        }
    }

    [Fact]
    public async Task Repeated_login_posts_are_rate_limited()
    {
        using var factory = new EmplyoWebAppFactory { AuthRateLimitPerMinute = 2 };
        await factory.InitializeDatabaseAsync();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var token = await Antiforgery.GetTokenAsync(client, "/Account/Login");
            var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = $"rate-limit-{Guid.NewGuid():N}@emplyo.test",
                ["Password"] = "Wrong!Pass2026",
                ["__RequestVerificationToken"] = token
            }));
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task Role_demotion_invalidates_an_existing_session()
    {
        using var factory = new EmplyoWebAppFactory { SecurityStampValidationMinutes = 0 };
        await factory.InitializeDatabaseAsync();

        var email = $"demote-{Guid.NewGuid():N}@emplyo.test";
        const string password = "Demote!Pass2026";
        string userId;

        using (var scope = factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            var created = await identity.CreateUserAsync(email, "Demote User", password, new[] { "Administrator" }, null);
            Assert.True(created.Succeeded);
            userId = created.UserId!;
        }

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await LoginHelper.LoginAsync(client, email, password);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Users")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            await identity.SetRolesAsync(userId, new[] { "Employee" });
        }

        var afterDemotion = await client.GetAsync("/Users");

        Assert.NotEqual(HttpStatusCode.OK, afterDemotion.StatusCode);
        Assert.True(afterDemotion.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task A_domain_error_does_not_redirect_to_a_backslash_referer()
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
        request.Headers.TryAddWithoutValidation("Referer", "http://localhost/\\evil.example");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    private async Task<string> SetPhotoAsync(int employeeId, string fileName)
    {
        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        var employees = scope.ServiceProvider.GetRequiredService<IEmployeeService>();
        var stored = await storage.SaveAsync(new MemoryStream(Encoding.UTF8.GetBytes("photo")), fileName);
        await employees.UpdatePhotoAsync(employeeId, stored);
        return stored;
    }

    private async Task ClearPhotoAsync(int employeeId, string stored)
    {
        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        var employees = scope.ServiceProvider.GetRequiredService<IEmployeeService>();
        await employees.UpdatePhotoAsync(employeeId, null);
        storage.Delete(stored);
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
