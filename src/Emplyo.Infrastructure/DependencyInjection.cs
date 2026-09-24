using Emplyo.Application.Common.Interfaces;
using Emplyo.Infrastructure.Persistence;
using Emplyo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Emplyo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' was not found. Configure ConnectionStrings:DefaultConnection " +
                "via User Secrets, environment variables, or appsettings.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
            }));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddSingleton<IDateTimeProvider, SystemClock>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAppSettingsService, AppSettingsService>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<DatabaseInitializer>();

        var storageOptions = new FileStorageOptions();
        configuration.GetSection("Storage").Bind(storageOptions);
        services.AddSingleton(storageOptions);
        services.AddScoped<IFileStorageService>(sp =>
            new FileStorageService(storageOptions, sp.GetRequiredService<ILogger<FileStorageService>>()));

        var emailOptions = new EmailOptions();
        configuration.GetSection("Email").Bind(emailOptions);
        services.AddSingleton(emailOptions);
        services.AddScoped<IAppEmailSender, LoggingEmailSender>();

        return services;
    }
}
