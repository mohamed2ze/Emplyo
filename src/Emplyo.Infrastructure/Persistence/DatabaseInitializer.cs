using Emplyo.Domain.Entities;
using Emplyo.Domain.Enums;
using Emplyo.Domain.ValueObjects;
using Emplyo.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Emplyo.Infrastructure.Persistence;

public class DatabaseInitializer
{
    public static readonly string[] Roles = { "Administrator", "HRManager", "Employee" };

    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(AppDbContext db, UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager, ILogger<DatabaseInitializer> logger)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task EnsureRolesAsync(CancellationToken ct = default)
    {
        foreach (var role in Roles)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                await _roleManager.CreateAsync(new IdentityRole(role));
                _logger.LogInformation("Created role {Role}.", role);
            }
        }
    }

    public async Task EnsureDefaultSettingsAsync(CancellationToken ct = default)
    {
        var defaults = new (string Key, string Value, string Description)[]
        {
            ("Organization.Name", "Emplyo", "The organization's display name."),
            ("Organization.TimeZone", "UTC", "IANA time zone used for displaying dates and computing attendance days."),
            ("Attendance.WorkDayStart", "09:00", "Daily start time (HH:mm) used to flag late arrivals.")
        };

        foreach (var (key, value, description) in defaults)
        {
            if (!await _db.AppSettings.AnyAsync(s => s.Key == key, ct))
            {
                _db.AppSettings.Add(new AppSetting { Key = key, Value = value, Description = description });
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Provisions the initial administrator. Will not overwrite an existing account with
    /// the same email and will not create a second administrator if one already exists.
    /// </summary>
    public async Task<(bool Created, string Message)> EnsureAdministratorAsync(string email, string password, string displayName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return (false, "Administrator email and password are required.");

        await EnsureRolesAsync(ct);

        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser is not null)
            return (false, $"A user with email '{email}' already exists. No changes were made.");

        var admins = await _userManager.GetUsersInRoleAsync("Administrator");
        if (admins.Count > 0)
            return (false, "An administrator account already exists. Provisioning was skipped to avoid duplicate privileged accounts.");

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Administrator" : displayName,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            return (false, "Failed to create administrator: " + string.Join("; ", result.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(user, "Administrator");
        _logger.LogInformation("Initial administrator account provisioned.");
        return (true, "Administrator account created successfully.");
    }

    /// <summary>
    /// Seeds realistic fictional development data. Idempotent: safe to run repeatedly.
    /// Must never run against a production database.
    /// </summary>
    public async Task SeedDevelopmentDataAsync(string defaultPassword, CancellationToken ct = default)
    {
        await EnsureRolesAsync(ct);

        if (!await _db.Departments.AnyAsync(ct))
        {
            _db.Departments.AddRange(
                new Department { Name = "Engineering", Description = "Product engineering and platform" },
                new Department { Name = "Human Resources", Description = "People operations" },
                new Department { Name = "Finance", Description = "Finance and accounting" },
                new Department { Name = "Sales", Description = "Revenue and business development" });
            await _db.SaveChangesAsync(ct);
        }

        var departments = await _db.Departments.ToDictionaryAsync(d => d.Name, ct);

        if (!await _db.Positions.AnyAsync(ct))
        {
            _db.Positions.AddRange(
                new Position { Title = "Software Engineer", DepartmentId = departments["Engineering"].Id },
                new Position { Title = "Engineering Manager", DepartmentId = departments["Engineering"].Id },
                new Position { Title = "HR Specialist", DepartmentId = departments["Human Resources"].Id },
                new Position { Title = "Accountant", DepartmentId = departments["Finance"].Id },
                new Position { Title = "Sales Representative", DepartmentId = departments["Sales"].Id });
            await _db.SaveChangesAsync(ct);
        }

        var positions = await _db.Positions.ToDictionaryAsync(p => p.Title, ct);

        if (!await _db.LeaveTypes.AnyAsync(ct))
        {
            _db.LeaveTypes.AddRange(
                new LeaveType { Name = "Annual Leave", IsPaid = true, DefaultAnnualEntitlement = 20 },
                new LeaveType { Name = "Sick Leave", IsPaid = true, DefaultAnnualEntitlement = 10 },
                new LeaveType { Name = "Personal Leave", IsPaid = true, DefaultAnnualEntitlement = 5 },
                new LeaveType { Name = "Unpaid Leave", IsPaid = false, DefaultAnnualEntitlement = 0 });
            await _db.SaveChangesAsync(ct);
        }

        var year = DateTime.UtcNow.Year;
        if (!await _db.Holidays.AnyAsync(h => h.Date.Year == year, ct))
        {
            _db.Holidays.AddRange(
                new Holiday { Name = "New Year's Day", Date = new DateOnly(year, 1, 1), IsRecurringAnnually = true },
                new Holiday { Name = "Labour Day", Date = new DateOnly(year, 5, 1), IsRecurringAnnually = true },
                new Holiday { Name = "Independence Day", Date = new DateOnly(year, 7, 4), IsRecurringAnnually = true },
                new Holiday { Name = "Christmas Day", Date = new DateOnly(year, 12, 25), IsRecurringAnnually = true });
            await _db.SaveChangesAsync(ct);
        }

        var seedEmployees = new (string Number, string First, string Last, string Email, string Dept, string Position, EmploymentStatus Status)[]
        {
            ("EMP-0001", "Amina", "Yusuf", "amina.yusuf@emplyo.local", "Human Resources", "HR Specialist", EmploymentStatus.Active),
            ("EMP-0002", "Daniel", "Okafor", "daniel.okafor@emplyo.local", "Engineering", "Engineering Manager", EmploymentStatus.Active),
            ("EMP-0003", "Lena", "Haddad", "lena.haddad@emplyo.local", "Engineering", "Software Engineer", EmploymentStatus.Active),
            ("EMP-0004", "Marcus", "Berg", "marcus.berg@emplyo.local", "Finance", "Accountant", EmploymentStatus.Active),
            ("EMP-0005", "Priya", "Nair", "priya.nair@emplyo.local", "Sales", "Sales Representative", EmploymentStatus.Active),
            ("EMP-0006", "Tomas", "Novak", "tomas.novak@emplyo.local", "Engineering", "Software Engineer", EmploymentStatus.Pending)
        };

        var addedEmployees = new List<Employee>();
        foreach (var e in seedEmployees)
        {
            if (await _db.Employees.AnyAsync(x => x.EmployeeNumber == e.Number, ct)) continue;
            var employee = new Employee
            {
                EmployeeNumber = e.Number,
                FirstName = e.First,
                LastName = e.Last,
                WorkEmail = e.Email,
                DepartmentId = departments[e.Dept].Id,
                PositionId = positions[e.Position].Id,
                EmploymentType = EmploymentType.FullTime,
                EmploymentStatus = e.Status,
                HireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-Random.Shared.Next(1, 30))),
                WorkLocation = "Head Office"
            };
            _db.Employees.Add(employee);
            addedEmployees.Add(employee);
        }
        await _db.SaveChangesAsync(ct);

        var manager = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeNumber == "EMP-0002", ct);
        if (manager is not null)
        {
            var reports = await _db.Employees.Where(e => e.DepartmentId == manager.DepartmentId && e.Id != manager.Id).ToListAsync(ct);
            foreach (var r in reports) r.ManagerId = manager.Id;
            await _db.SaveChangesAsync(ct);
        }

        // Leave balances for the current year.
        var leaveTypes = await _db.LeaveTypes.ToListAsync(ct);
        var allEmployees = await _db.Employees.ToListAsync(ct);
        foreach (var employee in allEmployees)
        {
            foreach (var type in leaveTypes)
            {
                if (await _db.LeaveBalances.AnyAsync(b => b.EmployeeId == employee.Id && b.LeaveTypeId == type.Id && b.Year == year, ct))
                    continue;
                _db.LeaveBalances.Add(new LeaveBalance
                {
                    EmployeeId = employee.Id, LeaveTypeId = type.Id, Year = year,
                    Entitlement = type.DefaultAnnualEntitlement, Used = 0
                });
            }
        }
        await _db.SaveChangesAsync(ct);

        // Demo user accounts (development only).
        await EnsureDemoUserAsync("admin@emplyo.local", "System Administrator", "Administrator", defaultPassword, ct);
        await EnsureDemoUserAsync("hr@emplyo.local", "Amina Yusuf", "HRManager", defaultPassword, ct, "EMP-0001");
        await EnsureDemoUserAsync("manager@emplyo.local", "Daniel Okafor", "Employee", defaultPassword, ct, "EMP-0002");
        await EnsureDemoUserAsync("employee@emplyo.local", "Lena Haddad", "Employee", defaultPassword, ct, "EMP-0003");

        if (!await _db.Announcements.AnyAsync(ct))
        {
            _db.Announcements.Add(new Announcement
            {
                Title = "Welcome to Emplyo",
                Body = "Emplyo is now your central place for people operations. Explore your dashboard, submit leave requests, and stay up to date with company announcements.",
                Audience = AnnouncementAudience.Everyone,
                IsPublished = true,
                PublishAtUtc = DateTime.UtcNow,
                CreatedByUserId = "seed",
                CreatedByName = "System"
            });
            await _db.SaveChangesAsync(ct);
        }

        await SeedSampleAttendanceAsync(ct);
        await SeedSampleLeaveAsync(ct);
        _logger.LogInformation("Development seed data ensured. Fictional data only.");
    }

    private async Task EnsureDemoUserAsync(string email, string displayName, string role, string password,
        CancellationToken ct, string? employeeNumber = null)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email, Email = email, EmailConfirmed = true,
                DisplayName = displayName, IsActive = true, CreatedAtUtc = DateTime.UtcNow
            };
            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Could not create demo user {Email}: {Errors}", email,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
                return;
            }
        }

        if (!await _userManager.IsInRoleAsync(user, role))
            await _userManager.AddToRoleAsync(user, role);

        if (employeeNumber is not null)
        {
            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeNumber == employeeNumber, ct);
            if (employee is not null && employee.UserId != user.Id)
            {
                employee.UserId = user.Id;
                await _db.SaveChangesAsync(ct);
            }
        }
    }

    /// <summary>
    /// Creates two illustrative leave requests (one approved with a balance deduction,
    /// one pending for the approval queue) so the workflow is visible in a fresh demo.
    /// Idempotent: skipped when the employee already has leave requests.
    /// </summary>
    private async Task SeedSampleLeaveAsync(CancellationToken ct)
    {
        var approvedEmployee = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeNumber == "EMP-0003", ct);
        var pendingEmployee = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeNumber == "EMP-0002", ct);
        var annual = await _db.LeaveTypes.FirstOrDefaultAsync(t => t.Name == "Annual Leave", ct);
        var sick = await _db.LeaveTypes.FirstOrDefaultAsync(t => t.Name == "Sick Leave", ct);

        if (approvedEmployee is null || pendingEmployee is null || annual is null || sick is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var year = now.Year;
        var holidays = await _db.Holidays
            .Where(h => h.Date.Year == year)
            .Select(h => h.Date)
            .ToListAsync(ct);

        var approvedStart = NextWeekday(DateOnly.FromDateTime(now).AddDays(1), DayOfWeek.Monday);
        var approvedEnd = approvedStart.AddDays(2);
        var pendingStart = approvedStart.AddDays(7);
        var pendingEnd = pendingStart.AddDays(1);

        if (!await _db.LeaveRequests.AnyAsync(l => l.EmployeeId == approvedEmployee.Id, ct))
        {
            var days = WorkingDayCalculator.CountWorkingDays(approvedStart, approvedEnd, holidays);
            if (days > 0)
            {
                _db.LeaveRequests.Add(new LeaveRequest
                {
                    EmployeeId = approvedEmployee.Id,
                    LeaveTypeId = annual.Id,
                    StartDate = approvedStart,
                    EndDate = approvedEnd,
                    TotalDays = days,
                    Reason = "Family event",
                    Status = LeaveRequestStatus.Approved,
                    DecisionByName = "System Administrator",
                    DecisionAtUtc = now,
                    DecisionComments = "Approved — enjoy your time off.",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                var balance = await _db.LeaveBalances.FirstOrDefaultAsync(
                    b => b.EmployeeId == approvedEmployee.Id && b.LeaveTypeId == annual.Id && b.Year == year, ct);
                if (balance is not null)
                {
                    balance.Used += days;
                    balance.UpdatedAtUtc = now;
                }
            }
        }

        if (!await _db.LeaveRequests.AnyAsync(l => l.EmployeeId == pendingEmployee.Id, ct))
        {
            var days = WorkingDayCalculator.CountWorkingDays(pendingStart, pendingEnd, holidays);
            if (days > 0)
            {
                _db.LeaveRequests.Add(new LeaveRequest
                {
                    EmployeeId = pendingEmployee.Id,
                    LeaveTypeId = sick.Id,
                    StartDate = pendingStart,
                    EndDate = pendingEnd,
                    TotalDays = days,
                    Reason = "Medical appointment",
                    Status = LeaveRequestStatus.Pending,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private static DateOnly NextWeekday(DateOnly from, DayOfWeek target)
    {
        var date = from;
        while (date.DayOfWeek != target)
        {
            date = date.AddDays(1);
        }
        return date;
    }

    private async Task SeedSampleAttendanceAsync(CancellationToken ct)
    {
        var employees = await _db.Employees.Where(e => e.EmploymentStatus == EmploymentStatus.Active).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var employee in employees)
        {
            for (var offset = 1; offset <= 10; offset++)
            {
                var date = today.AddDays(-offset);
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                if (await _db.AttendanceRecords.AnyAsync(a => a.EmployeeId == employee.Id && a.WorkDate == date, ct)) continue;

                var clockIn = date.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc).AddMinutes(Random.Shared.Next(-15, 45));
                var clockOut = clockIn.AddHours(8).AddMinutes(Random.Shared.Next(-20, 40));
                _db.AttendanceRecords.Add(new AttendanceRecord
                {
                    EmployeeId = employee.Id,
                    WorkDate = date,
                    ClockInUtc = clockIn,
                    ClockOutUtc = clockOut,
                    Status = clockIn.TimeOfDay > new TimeSpan(9, 10, 0) ? AttendanceStatus.Late : AttendanceStatus.Present
                });
            }
        }
        await _db.SaveChangesAsync(ct);
    }
}
