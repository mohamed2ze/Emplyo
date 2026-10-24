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
    public static async Task<int?> TryRunAsync(WebApplication app, string[] args)
    {
        if (args.Length == 0) return null;

        var command = args[0].TrimStart('-').ToLowerInvariant();
        return command switch
        {
            "migrate" => await MigrateAsync(app),
            "seed-admin" or "create-admin" => await SeedAdminAsync(app, args),
            "seed-dev-data" => await SeedDevDataAsync(app, args),
            "help" => PrintHelp(),
            _ => null
        };
    }

    private static async Task<int> MigrateAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();

        Console.WriteLine("Applying EF Core migrations to the configured database...");
        await db.Database.MigrateAsync();
        await initializer.EnsureRolesAsync();
        await initializer.EnsureDefaultSettingsAsync();
        Console.WriteLine("Migrations applied and baseline settings ensured.");
        return 0;
    }

    private static async Task<int> SeedAdminAsync(WebApplication app, string[] args)
    {
        var email = GetOption(args, "--email") ?? Environment.GetEnvironmentVariable("EMPLYO_ADMIN_EMAIL");
        var password = GetOption(args, "--password") ?? Environment.GetEnvironmentVariable("EMPLYO_ADMIN_PASSWORD");
        var name = GetOption(args, "--name") ?? Environment.GetEnvironmentVariable("EMPLYO_ADMIN_NAME") ?? "Administrator";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine(
                "Administrator email and password are required. Provide them via environment variables " +
                "EMPLYO_ADMIN_EMAIL and EMPLYO_ADMIN_PASSWORD (recommended) or --email/--password (not recommended: visible in shell history).");
            return 1;
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

        var (_, message) = await initializer.EnsureAdministratorAsync(email, password, name);
        Console.WriteLine(message);
        return message.StartsWith("Failed", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private static async Task<int> SeedDevDataAsync(WebApplication app, string[] args)
    {
        var allowNonDevelopment = string.Equals(
            Environment.GetEnvironmentVariable("EMPLYO_ALLOW_DEV_SEED"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!app.Environment.IsDevelopment() && !allowNonDevelopment)
        {
            Console.Error.WriteLine(
                "Refusing to seed fictional development data outside Development. " +
                "Set EMPLYO_ALLOW_DEV_SEED=true only for an isolated non-production environment.");
            return 1;
        }

        var password = GetOption(args, "--password")
            ?? Environment.GetEnvironmentVariable("EMPLYO_DEV_PASSWORD");

        if (string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine(
                "A password for the demo accounts is required. Provide it via the EMPLYO_DEV_PASSWORD " +
                "environment variable (recommended) or --password (not recommended: visible in shell history).");
            return 1;
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
        return 0;
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

    private static int PrintHelp()
    {
        Console.WriteLine("Emplyo maintenance commands:");
        Console.WriteLine("  migrate          Apply EF Core migrations and ensure roles/settings.");
        Console.WriteLine("  seed-admin       Provision the first administrator (requires EMPLYO_ADMIN_EMAIL / EMPLYO_ADMIN_PASSWORD).");
        Console.WriteLine("  seed-dev-data    Seed fictional development data (requires EMPLYO_DEV_PASSWORD; dev only).");
        Console.WriteLine("  help             Show this help.");
        return 0;
    }
}
