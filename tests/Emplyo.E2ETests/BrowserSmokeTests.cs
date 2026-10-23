using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace Emplyo.E2ETests;

[Collection("e2e")]
public class BrowserSmokeTests
{
    private readonly EmplyoE2EFixture _fixture;

    public BrowserSmokeTests(EmplyoE2EFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Anonymous_user_is_redirected_to_the_login_page()
    {
        await using var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{_fixture.BaseUrl}/Employees");

        await page.WaitForURLAsync("**/Account/Login**");
        Assert.Equal("Sign in", await page.Locator("h1").InnerTextAsync());
        Assert.True(await page.GetByLabel("Email address").IsVisibleAsync());
        Assert.True(await page.GetByLabel("Password").IsVisibleAsync());
    }

    [Fact]
    public async Task Employee_can_sign_in_and_navigate_personal_pages()
    {
        await using var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "employee@emplyo.local", EmplyoE2EFixture.SeedPassword);

        Assert.Contains("Welcome, Lena", await page.Locator("h1").InnerTextAsync());
        await page.GotoAsync($"{_fixture.BaseUrl}/Leave/My");
        Assert.Equal("My leave", await page.Locator("h1").InnerTextAsync());
        await page.GotoAsync($"{_fixture.BaseUrl}/Announcements");
        Assert.Equal("Announcements", await page.Locator("h1").InnerTextAsync());
    }

    [Fact]
    public async Task Employee_is_denied_access_to_management_pages()
    {
        await using var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page, "employee@emplyo.local", EmplyoE2EFixture.SeedPassword);

        await page.GotoAsync($"{_fixture.BaseUrl}/Reports");

        Assert.Contains("/Account/AccessDenied", page.Url);
        Assert.Equal("Access denied", await page.Locator("h1").InnerTextAsync());
    }

    [Fact]
    public async Task Hr_manager_can_open_reports_and_download_a_csv_export()
    {
        await using var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page, "hr@emplyo.local", EmplyoE2EFixture.SeedPassword);

        await page.GotoAsync($"{_fixture.BaseUrl}/Reports");

        Assert.Equal("Reports", await page.Locator("h1").InnerTextAsync());
        var download = await page.RunAndWaitForDownloadAsync(async () =>
            await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Headcount" }).ClickAsync());
        Assert.EndsWith(".csv", download.SuggestedFilename, StringComparison.OrdinalIgnoreCase);
        var path = await download.PathAsync();
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
    }

    [Fact]
    public async Task Administrator_can_open_settings()
    {
        await using var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page, EmplyoE2EFixture.AdminEmail, EmplyoE2EFixture.AdminPassword);

        await page.GotoAsync($"{_fixture.BaseUrl}/Settings");

        Assert.Equal("Settings", await page.Locator("h1").InnerTextAsync());
        Assert.True(await page.GetByLabel("Organization name").IsVisibleAsync());
    }

    [Fact]
    public async Task Employee_can_submit_and_cancel_a_leave_request_through_the_browser()
    {
        await using var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await LoginAsync(page, "employee@emplyo.local", EmplyoE2EFixture.SeedPassword);
        var start = NextMonday(new DateOnly(2100, 1, 4));

        await page.GotoAsync($"{_fixture.BaseUrl}/Leave/Create");
        await page.Locator("select[name='LeaveTypeId']").SelectOptionAsync(new SelectOptionValue { Label = "Annual Leave" });
        await page.GetByLabel("Start date").FillAsync(start.ToString("yyyy-MM-dd"));
        await page.GetByLabel("End date").FillAsync(start.ToString("yyyy-MM-dd"));
        await page.GetByLabel("Reason / notes").FillAsync("Browser smoke test");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Submit request" }).ClickAsync();

        await page.WaitForURLAsync("**/Leave/Details/*");
        Assert.Equal("Annual Leave request", await page.Locator("h1").InnerTextAsync());
        Assert.Contains("Pending", await page.Locator(".badge-status").First.InnerTextAsync());

        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cancel request" }).ClickAsync();
        await page.GetByText("This request was cancelled.").WaitForAsync();
    }

    [Fact]
    public async Task Key_pages_have_basic_accessibility_structure()
    {
        await using var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{_fixture.BaseUrl}/Account/Login");
        await AssertBasicStructureAsync(page);

        await LoginAsync(page, "employee@emplyo.local", EmplyoE2EFixture.SeedPassword);
        await AssertBasicStructureAsync(page);

        await page.GotoAsync($"{_fixture.BaseUrl}/Leave/Create");
        await AssertBasicStructureAsync(page);
    }

    private async Task LoginAsync(IPage page, string email, string password)
    {
        await page.GotoAsync($"{_fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Email address").FillAsync(email);
        await page.GetByLabel("Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.WaitForURLAsync(new Regex($"{Regex.Escape(_fixture.BaseUrl)}/$"));
    }

    private static async Task AssertBasicStructureAsync(IPage page)
    {
        Assert.False(string.IsNullOrWhiteSpace(await page.TitleAsync()));
        Assert.Equal(1, await page.Locator("h1").CountAsync());
        Assert.Equal(0, await page.Locator("img:not([alt])").CountAsync());
    }

    private static DateOnly NextMonday(DateOnly date)
    {
        while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
        return date;
    }
}
