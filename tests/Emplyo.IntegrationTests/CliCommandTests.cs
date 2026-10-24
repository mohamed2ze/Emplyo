using System.Diagnostics;
using Xunit;

namespace Emplyo.IntegrationTests;

[Collection("integration")]
public class CliCommandTests
{
    private readonly EmplyoWebAppFactory _factory;

    public CliCommandTests(EmplyoWebAppFactory factory)
    {
        _factory = factory;
        _factory.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Help_exits_successfully_and_lists_commands()
    {
        var result = await RunAsync("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("migrate", result.Output);
        Assert.Contains("seed-admin", result.Output);
        Assert.Contains("seed-dev-data", result.Output);
    }

    [Fact]
    public async Task Seed_admin_without_credentials_exits_non_zero()
    {
        var result = await RunAsync("seed-admin", adminEmail: "", adminPassword: "");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Administrator email and password are required", result.Error);
    }

    [Fact]
    public async Task Seed_dev_data_without_a_password_exits_non_zero()
    {
        var result = await RunAsync("seed-dev-data", devPassword: "");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("password for the demo accounts is required", result.Error);
    }

    [Fact]
    public async Task Create_admin_alias_without_credentials_exits_non_zero()
    {
        var result = await RunAsync("create-admin", adminEmail: "", adminPassword: "");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Administrator email and password are required", result.Error);
    }

    [Fact]
    public async Task Seed_dev_data_is_refused_in_production()
    {
        var result = await RunAsync("seed-dev-data", devPassword: "DevSeed!Pass2026", environment: "Production");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Refusing to seed fictional development data", result.Error);
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(string command,
        string? adminEmail = null, string? adminPassword = null, string? devPassword = null,
        string environment = "Development")
    {
        var repoRoot = FindRepositoryRoot();
        var webProject = Path.Combine(repoRoot, "src", "Emplyo.Web");
        var configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

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
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = _factory.ConnectionString;
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
        startInfo.Environment["EMPLYO_ADMIN_EMAIL"] = adminEmail;
        startInfo.Environment["EMPLYO_ADMIN_PASSWORD"] = adminPassword;
        startInfo.Environment["EMPLYO_DEV_PASSWORD"] = devPassword;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the CLI command.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException($"CLI command '{command}' timed out.");
        }

        return (process.ExitCode, await outputTask, await errorTask);
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
}
