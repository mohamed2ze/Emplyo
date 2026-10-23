using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Emplyo.IntegrationTests;

[CollectionDefinition("integration")]
public class IntegrationCollection : ICollectionFixture<EmplyoWebAppFactory> { }

public static class Antiforgery
{
    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    public static async Task<string> GetTokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = TokenRegex.Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException($"No antiforgery token found at {url}.");
        }
        return match.Groups[1].Value;
    }
}

public static class LoginHelper
{
    public static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password)
    {
        var token = await Antiforgery.GetTokenAsync(client, "/Account/Login");
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false",
            ["__RequestVerificationToken"] = token
        });
        return await client.PostAsync("/Account/Login", content);
    }
}

public static class AccessControl
{
    public static void AssertDenied(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect,
            $"Expected access to be denied but received {response.StatusCode}.");
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            Assert.Contains("/Account/AccessDenied", response.Headers.Location!.ToString());
        }
    }
}
