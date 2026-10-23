using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Emplyo.Application.Documents;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Emplyo.IntegrationTests;

[Collection("integration")]
public class DocumentAccessTests
{
    private const string DocumentContent = "integration document content";

    private readonly EmplyoWebAppFactory _factory;

    public DocumentAccessTests(EmplyoWebAppFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task The_linked_employee_can_download_their_own_non_confidential_document()
    {
        var employeeId = await TestData.EmployeeIdByNumberAsync(_factory, "EMP-0003");
        var title = UniqueTitle();
        var adminClient = await SignedInAsync(EmplyoWebAppFactory.AdminEmail, EmplyoWebAppFactory.AdminPassword);
        await UploadAsync(adminClient, employeeId, title, confidential: false);

        try
        {
            var id = await TestData.DocumentIdByTitleAsync(_factory, title);
            var employeeClient = await SignedInAsync("employee@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

            var response = await employeeClient.GetAsync($"/Documents/Download/{id}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal(DocumentContent, await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await DeleteDocumentAsync(title);
        }
    }

    [Fact]
    public async Task Another_employee_cannot_download_a_document()
    {
        var employeeId = await TestData.EmployeeIdByNumberAsync(_factory, "EMP-0003");
        var title = UniqueTitle();
        var adminClient = await SignedInAsync(EmplyoWebAppFactory.AdminEmail, EmplyoWebAppFactory.AdminPassword);
        await UploadAsync(adminClient, employeeId, title, confidential: false);

        try
        {
            var id = await TestData.DocumentIdByTitleAsync(_factory, title);
            var otherClient = await SignedInAsync("manager@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

            var response = await otherClient.GetAsync($"/Documents/Download/{id}");

            AccessControl.AssertDenied(response);
        }
        finally
        {
            await DeleteDocumentAsync(title);
        }
    }

    [Fact]
    public async Task The_owner_cannot_download_a_confidential_document_but_hr_can()
    {
        var employeeId = await TestData.EmployeeIdByNumberAsync(_factory, "EMP-0003");
        var title = UniqueTitle();
        var adminClient = await SignedInAsync(EmplyoWebAppFactory.AdminEmail, EmplyoWebAppFactory.AdminPassword);
        await UploadAsync(adminClient, employeeId, title, confidential: true);

        try
        {
            var id = await TestData.DocumentIdByTitleAsync(_factory, title);
            var employeeClient = await SignedInAsync("employee@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

            AccessControl.AssertDenied(await employeeClient.GetAsync($"/Documents/Download/{id}"));
            Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync($"/Documents/Download/{id}")).StatusCode);
        }
        finally
        {
            await DeleteDocumentAsync(title);
        }
    }

    [Fact]
    public async Task An_employee_cannot_reach_the_document_upload_page()
    {
        var employeeId = await TestData.EmployeeIdByNumberAsync(_factory, "EMP-0003");
        var client = await SignedInAsync("employee@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

        AccessControl.AssertDenied(await client.GetAsync($"/Documents/Upload?employeeId={employeeId}"));
    }

    [Fact]
    public async Task Downloading_a_missing_document_returns_not_found()
    {
        var client = await SignedInAsync("employee@emplyo.local", EmplyoWebAppFactory.EmployeePassword);

        var response = await client.GetAsync("/Documents/Download/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task UploadAsync(HttpClient client, int employeeId, string title, bool confidential)
    {
        var token = await Antiforgery.GetTokenAsync(client, $"/Documents/Upload?employeeId={employeeId}");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(employeeId.ToString()), "employeeId" },
            { new StringContent(title), "title" },
            { new StringContent("Other"), "category" },
            { new StringContent(confidential ? "true" : "false"), "isConfidential" },
            { new StringContent(token), "__RequestVerificationToken" }
        };
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(DocumentContent));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "integration.txt");

        var response = await client.PostAsync($"/Documents/Upload?employeeId={employeeId}", form);

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
            $"Upload returned {response.StatusCode}.");
    }

    private async Task DeleteDocumentAsync(string title)
    {
        using var scope = _factory.Services.CreateScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var id = await TestData.DocumentIdByTitleAsync(_factory, title);
        await documents.DeleteAsync(id);
    }

    private async Task<HttpClient> SignedInAsync(string email, string password)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await LoginHelper.LoginAsync(client, email, password);
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
            $"Sign-in for {email} returned {response.StatusCode}.");
        return client;
    }

    private static string UniqueTitle() => $"Integration document {Guid.NewGuid():N}";
}
