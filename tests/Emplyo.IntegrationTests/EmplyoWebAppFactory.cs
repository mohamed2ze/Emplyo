using System.Reflection;
using System.Text.Json;
using Emplyo.Infrastructure.Identity;
using Emplyo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Emplyo.IntegrationTests;

/// <summary>
/// Boots the real web application against a dedicated SQL Server test database.
/// The test database name is derived from the configured DefaultConnection
/// (Emplyo -> Emplyo_Test) unless EMPLYO_TEST_CONNECTION overrides it.
/// </summary>
public class EmplyoWebAppFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "it.admin@emplyo.test";
    public const string AdminPassword = "IntTest!Admin2026";
    public const string EmployeePassword = "IntTest!Employee2026";

    public string ConnectionString { get; private set; } = string.Empty;
    public int AuthRateLimitPerMinute { get; init; } = 100_000;
    public int SecurityStampValidationMinutes { get; init; } = 60;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:AuthRateLimitPerMinute"] = AuthRateLimitPerMinute.ToString(),
                ["Security:SecurityStampValidationMinutes"] = SecurityStampValidationMinutes.ToString()
            }));

        var baseConnection = Environment.GetEnvironmentVariable("EMPLYO_TEST_CONNECTION") ?? ReadConnectionFromSecretsFile();
        var testConnection = baseConnection?.Replace("Database=Emplyo;", "Database=Emplyo_Test;");
        ConnectionString = testConnection ?? string.Empty;

        builder.ConfigureServices(services =>
        {
            // Replace the application's SQL Server options with the dedicated test database.
            var toRemove = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                d.ServiceType == typeof(DbContextOptions) ||
                d.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>) ||
                d.ServiceType == typeof(DbContextOptions<AppDbContext>)).ToList();
            foreach (var descriptor in toRemove)
            {
                services.Remove(descriptor);
            }

            if (!string.IsNullOrWhiteSpace(ConnectionString))
            {
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlServer(ConnectionString, sql =>
                    {
                        sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                        sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
                    }));
            }
        });
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
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.TryGetProperty("ConnectionStrings:DefaultConnection", out var value)
            ? value.GetString()
            : null;
    }

    public async Task InitializeDatabaseAsync()
    {
        if (_initialized) return;
        await _gate.WaitAsync();
        try
        {
            if (_initialized) return;
            await InitializeCoreAsync();
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task InitializeCoreAsync()
    {
        using var scope = Services.CreateScope();
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                "No test connection string could be resolved. Set EMPLYO_TEST_CONNECTION or ensure " +
                "ConnectionStrings:DefaultConnection is configured for Emplyo.Web (User Secrets).");
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        await initializer.EnsureRolesAsync();
        await initializer.EnsureDefaultSettingsAsync();

        // Provision the integration administrator even when other administrators exist.
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await userManager.FindByEmailAsync(AdminEmail) is null)
        {
            var user = new ApplicationUser
            {
                UserName = AdminEmail,
                Email = AdminEmail,
                EmailConfirmed = true,
                DisplayName = "IT Administrator",
                IsActive = true
            };
            var created = await userManager.CreateAsync(user, AdminPassword);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not create integration administrator: " +
                    string.Join("; ", created.Errors.Select(e => e.Description)));
            }
            await userManager.AddToRoleAsync(user, "Administrator");
        }

        await initializer.SeedDevelopmentDataAsync(EmployeePassword);
    }
}
