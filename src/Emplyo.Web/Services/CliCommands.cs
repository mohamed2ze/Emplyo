using Emplyo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Web.Services;

/// <summary>
/// Non-web maintenance commands executed via the .NET CLI, e.g.
/// <c>dotnet run --project src/Emplyo.Web -- migrate</c>.
/// These are the only supported ways to create/migrate the database, provision the
/// first administrator, or seed development data. Ordinary startup never does these.
/// </summary>
public static class CliCommands
{
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        if (args.Length == 0) return false;

        var command = args[0].TrimStart('-').ToLowerInvariant();
        switch (command)
        {
            case "migrate":
                await MigrateAsync(app);
                return true;
            case "seed-admin":
            case "create-admin":
                await SeedAdminAsync(app, args);
                return true;
            case "seed-dev-data":
                await SeedDevDataAsync(app, args);
                return true;
            case "help":
                PrintHelp();
                return true;
            default:
                return false;
        }
    }

    private static async Task MigrateAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();

        Console.WriteLine("Applying EF Core migrations to the configured database...");
        await db.Database.MigrateAsync();
        await initializer.EnsureRolesAsync();
        await initializer.EnsureDefaultSettingsAsync();
        Console.WriteLine("Migrations applied and baseline settings ensured.");
    }

    private static async Task SeedAdminAsync(WebApplication app, string[] args)
    {
        var email = GetOption(args, "--email") ?? Environment.GetEnvironmentVariable("EMPLYO_ADMIN_EMAIL");
        var password = GetOption(args, "--password") ?? Environment.GetEnvironmentVariable("EMPLYO_ADMIN_PASSWORD");
        var name = GetOption(args, "--name") ?? Environment.GetEnvironmentVariable("EMPLYO_ADMIN_NAME") ?? "Administrator";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine(
                "Administrator email and password are required. Provide them via environment variables " +
                "EMPLYO_ADMIN_EMAIL and EMPLYO_ADMIN_PASSWORD (recommended) or --email/--password (not recommended: visible in shell history).");
            return;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();

        if ((await db.Database.GetPendingMigrationsAsync()).Any())
        {
            Console.WriteLine("Applying pending migrations before provisioning the administrator...");
            await db.Database.MigrateAsync();
        }

        await initializer.EnsureRolesAsync();
        await initializer.EnsureDefaultSettingsAsync();

        var (created, message) = await initializer.EnsureAdministratorAsync(email, password, name);
        Console.WriteLine(message);
    }

    private static async Task SeedDevDataAsync(WebApplication app, string[] args)
    {
        var password = GetOption(args, "--password")
            ?? Environment.GetEnvironmentVariable("EMPLYO_DEV_PASSWORD");

        if (string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine(
                "A password for the demo accounts is required. Provide it via the EMPLYO_DEV_PASSWORD " +
                "environment variable (recommended) or --password (not recommended: visible in shell history).");
            return;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();

        if ((await db.Database.GetPendingMigrationsAsync()).Any())
        {
            Console.WriteLine("Applying pending migrations before seeding...");
            await db.Database.MigrateAsync();
        }

        await initializer.EnsureDefaultSettingsAsync();
        await initializer.SeedDevelopmentDataAsync(password);
        Console.WriteLine("Development data seeded. Demo accounts: admin@emplyo.local, hr@emplyo.local, manager@emplyo.local, employee@emplyo.local");
        Console.WriteLine("Demo accounts use the password supplied via EMPLYO_DEV_PASSWORD.");
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Emplyo maintenance commands:");
        Console.WriteLine("  migrate          Apply EF Core migrations and ensure roles/settings.");
        Console.WriteLine("  seed-admin       Provision the first administrator (requires EMPLYO_ADMIN_EMAIL / EMPLYO_ADMIN_PASSWORD).");
        Console.WriteLine("  seed-dev-data    Seed fictional development data (requires EMPLYO_DEV_PASSWORD; dev only).");
        Console.WriteLine("  help             Show this help.");
    }
}
