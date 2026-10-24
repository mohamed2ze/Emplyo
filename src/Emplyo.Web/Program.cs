using Emplyo.Application;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Infrastructure;
using Emplyo.Infrastructure.Identity;
using Emplyo.Infrastructure.Persistence;
using Emplyo.Web.Authorization;
using Emplyo.Web.Filters;
using Emplyo.Web.HealthChecks;
using Emplyo.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

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

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
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
    app.UseHsts();
    app.UseHttpsRedirection();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseStatusCodePagesWithReExecute("/Home/StatusCode", "?code={0}");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHealthChecks("/health");

await app.RunAsync();

public partial class Program { }
