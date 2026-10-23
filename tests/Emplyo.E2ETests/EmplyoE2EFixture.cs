using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Playwright;
using Xunit;

namespace Emplyo.E2ETests;

public sealed class EmplyoE2EFixture : IAsyncLifetime
{
    public const string SeedPassword = "IntTest!Employee2026";
    public const string AdminEmail = "admin@emplyo.local";
    public const string AdminPassword = SeedPassword;

    private readonly ConcurrentQueue<string> _processLog = new();
    private string _baseUrl = string.Empty;
    private Process? _app;
    private IPlaywright? _playwright;

    public string BaseUrl => _baseUrl;
    public IBrowser Browser { get; private set; } = null!;
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"emplyo-e2e-{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        var repoRoot = FindRepositoryRoot();
        var webProject = Path.Combine(repoRoot, "src", "Emplyo.Web");
        var configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

        var connection = BuildE2EConnectionString();
        var environment = new Dictionary<string, string?>
        {
            ["ConnectionStrings__DefaultConnection"] = connection,
            ["Storage__DocumentsRoot"] = StorageRoot,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = string.Empty,
            ["EMPLYO_DEV_PASSWORD"] = SeedPassword
        };

        await RunMaintenanceCommandAsync(repoRoot, webProject, configuration, environment, "migrate");
        await RunMaintenanceCommandAsync(repoRoot, webProject, configuration, environment, "seed-dev-data");

        _baseUrl = $"http://127.0.0.1:{FindFreePort()}";
        environment["ASPNETCORE_URLS"] = _baseUrl;
        _app = StartApp(repoRoot, webProject, configuration, environment);
        await WaitForHealthAsync();

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.CloseAsync();
        _playwright?.Dispose();

        if (_app is { HasExited: false })
        {
            try { _app.Kill(entireProcessTree: true); } catch { }
            try { await _app.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token); } catch { }
        }
        _app?.Dispose();

        try
        {
            if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, recursive: true);
        }
        catch
        {
        }
    }

    private async Task RunMaintenanceCommandAsync(string repoRoot, string webProject, string configuration,
        Dictionary<string, string?> environment, string command)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(webProject);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(configuration);
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(command);
        ApplyEnvironment(startInfo, environment);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{command}'.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException($"The '{command}' command timed out.");
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The '{command}' command failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }
    }

    private Process StartApp(string repoRoot, string webProject, string configuration, Dictionary<string, string?> environment)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(webProject);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(configuration);
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-launch-profile");
        ApplyEnvironment(startInfo, environment);

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the web application.");
        process.OutputDataReceived += (_, args) => EnqueueLog(args.Data);
        process.ErrorDataReceived += (_, args) => EnqueueLog(args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private async Task WaitForHealthAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (_app is { HasExited: true })
            {
                throw new InvalidOperationException(
                    $"The web application exited during startup with code {_app.ExitCode}.{Environment.NewLine}{LogTail()}");
            }

            try
            {
                var response = await client.GetAsync($"{_baseUrl}/health");
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException($"The web application did not become healthy in time.{Environment.NewLine}{LogTail()}");
    }

    private void EnqueueLog(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line)) _processLog.Enqueue(line);
    }

    private string LogTail() => string.Join(Environment.NewLine, _processLog.TakeLast(60));

    private static void ApplyEnvironment(ProcessStartInfo startInfo, Dictionary<string, string?> environment)
    {
        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }
    }

    private static string BuildE2EConnectionString()
    {
        var baseConnection = Environment.GetEnvironmentVariable("EMPLYO_TEST_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? ReadConnectionFromSecretsFile()
            ?? throw new InvalidOperationException(
                "No test connection string could be resolved. Set EMPLYO_TEST_CONNECTION or configure " +
                "ConnectionStrings:DefaultConnection for Emplyo.Web.");

        var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = baseConnection };
        if (builder.ContainsKey("Initial Catalog")) builder.Remove("Initial Catalog");
        builder["Database"] = "Emplyo_E2E";
        return builder.ConnectionString;
    }

    private static string? ReadConnectionFromSecretsFile()
    {
        var id = typeof(Program).Assembly.GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId;
        if (string.IsNullOrEmpty(id)) return null;

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".microsoft", "usersecrets", id, "secrets.json");
        if (!File.Exists(path)) return null;

        var text = File.ReadAllText(path).TrimStart('\uFEFF');
        using var document = JsonDocument.Parse(text);
        return document.RootElement.TryGetProperty("ConnectionStrings:DefaultConnection", out var value)
            ? value.GetString()
            : null;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Emplyo.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root (Emplyo.sln).");
    }

    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

[CollectionDefinition("e2e")]
public class E2ECollection : ICollectionFixture<EmplyoE2EFixture> { }
