using System.Net;
using System.Threading.RateLimiting;
using Emplyo.Application;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Infrastructure;
using Emplyo.Infrastructure.Identity;
using Emplyo.Infrastructure.Persistence;
using Emplyo.Web.Authorization;
using Emplyo.Web.Filters;
using Emplyo.Web.HealthChecks;
using Emplyo.Web.Security;
using Emplyo.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Resolve document storage against the content root so it is independent of the
// process working directory and always outside the public web root.
var configuredStorageRoot = builder.Configuration["Storage:DocumentsRoot"];
builder.Configuration["Storage:DocumentsRoot"] = string.IsNullOrWhiteSpace(configuredStorageRoot)
    ? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "documents")
    : (Path.IsPathRooted(configuredStorageRoot)
        ? configuredStorageRoot
        : Path.Combine(builder.Environment.ContentRootPath, configuredStorageRoot));

builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddApplicationServices();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
    {
        var permitLimit = httpContext.RequestServices.GetRequiredService<IConfiguration>()
            .GetValue("Security:AuthRateLimitPerMinute", 30);

        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, permitLimit),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

builder.Services.AddOptions<SecurityStampValidatorOptions>()
    .Configure<IConfiguration>((options, configuration) =>
    {
        var minutes = configuration.GetValue("Security:SecurityStampValidationMinutes", 5);
        options.ValidationInterval = TimeSpan.FromMinutes(Math.Max(0, minutes));
    });

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("Security:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
    {
        if (IPAddress.TryParse(proxy, out var address))
            options.KnownProxies.Add(address);
    }
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 12 * 1024 * 1024;
    options.ValueLengthLimit = 1024 * 1024;
    options.ValueCountLimit = 1024;
});

builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = 16 * 1024 * 1024);

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "Emplyo.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredUniqueChars = 4;
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "Emplyo.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.Administrator, p => p.RequireRole("Administrator"));
    options.AddPolicy(Policies.HrManager, p => p.RequireRole("HRManager"));
    options.AddPolicy(Policies.HrOrAdmin, p => p.RequireRole("HRManager", "Administrator"));
    options.AddPolicy(Policies.Authenticated, p => p.RequireAuthenticatedUser());
});

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<DomainExceptionFilter>();
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddSingleton<TimeZoneProvider>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

// Non-web maintenance commands (migrations, administrator provisioning, dev seed).
var cliExitCode = await CliCommands.TryRunAsync(app, args);
if (cliExitCode.HasValue)
{
    Environment.ExitCode = cliExitCode.Value;
    return;
}

if (!app.Environment.IsDevelopment()
    && app.Configuration.GetValue<bool>("Email:Enabled")
    && string.IsNullOrWhiteSpace(app.Configuration["App:PublicBaseUrl"]))
{
    app.Logger.LogWarning(
        "Email is enabled but App:PublicBaseUrl is not configured. " +
        "Password reset links will not be sent until an absolute HTTPS base URL is configured.");
}

var allowedHosts = app.Configuration["AllowedHosts"];
if (!app.Environment.IsDevelopment()
    && (string.IsNullOrWhiteSpace(allowedHosts)
        || allowedHosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(host => host == "*")))
{
    app.Logger.LogWarning(
        "AllowedHosts permits requests for any host. Restrict it to the real production host name(s).");
}

// Controlled initialization: idempotent role seeding is safe on every start.
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    try
    {
        await initializer.EnsureRolesAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Database was unreachable during startup role initialization.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/Home/StatusCode", "?code={0}");
app.UseStaticFiles();
app.UseSecurityHeaders();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHealthChecks("/health");

await app.RunAsync();

public partial class Program { }
