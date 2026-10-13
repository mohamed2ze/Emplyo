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
    /// Seeds fictional development data for a small company: 20 employees across five
    /// departments with staggered hire dates, mixed employment types, an open leave
    /// request, an employee currently on leave, a completed offboarding, and onboarding
    /// tasks in progress. Idempotent: safe to run repeatedly. Never run in production.
    /// </summary>
    public async Task SeedDevelopmentDataAsync(string defaultPassword, CancellationToken ct = default)
    {
        await EnsureRolesAsync(ct);

        if (!await _db.Departments.AnyAsync(ct))
        {
            _db.Departments.AddRange(
                new Department { Name = "Engineering", Description = "Product development, platform and quality" },
                new Department { Name = "Sales", Description = "New business and account management" },
                new Department { Name = "Customer Support", Description = "Customer inquiries and service levels" },
                new Department { Name = "Finance", Description = "Accounting, payroll and reporting" },
                new Department { Name = "Human Resources", Description = "People operations and recruitment" });
            await _db.SaveChangesAsync(ct);
        }

        var departments = await _db.Departments.ToDictionaryAsync(d => d.Name, ct);

        if (!await _db.Positions.AnyAsync(ct))
        {
            _db.Positions.AddRange(
                new Position { Title = "Engineering Manager", DepartmentId = departments["Engineering"].Id },
                new Position { Title = "Senior Software Engineer", DepartmentId = departments["Engineering"].Id },
                new Position { Title = "Software Engineer", DepartmentId = departments["Engineering"].Id },
                new Position { Title = "QA Engineer", DepartmentId = departments["Engineering"].Id },
                new Position { Title = "Sales Manager", DepartmentId = departments["Sales"].Id },
                new Position { Title = "Account Executive", DepartmentId = departments["Sales"].Id },
                new Position { Title = "Sales Representative", DepartmentId = departments["Sales"].Id },
                new Position { Title = "Support Team Lead", DepartmentId = departments["Customer Support"].Id },
                new Position { Title = "Support Specialist", DepartmentId = departments["Customer Support"].Id },
                new Position { Title = "Finance Manager", DepartmentId = departments["Finance"].Id },
                new Position { Title = "Accountant", DepartmentId = departments["Finance"].Id },
                new Position { Title = "HR Manager", DepartmentId = departments["Human Resources"].Id },
                new Position { Title = "HR Specialist", DepartmentId = departments["Human Resources"].Id });
            await _db.SaveChangesAsync(ct);
        }

        var positions = await _db.Positions.ToDictionaryAsync(p => p.Title, ct);

        if (!await _db.LeaveTypes.AnyAsync(ct))
        {
            _db.LeaveTypes.AddRange(
                new LeaveType { Name = "Annual Leave", IsPaid = true, DefaultAnnualEntitlement = 20, Description = "Paid vacation days, accrued per calendar year." },
                new LeaveType { Name = "Sick Leave", IsPaid = true, DefaultAnnualEntitlement = 10, Description = "Paid days for illness and medical appointments." },
                new LeaveType { Name = "Parental Leave", IsPaid = true, DefaultAnnualEntitlement = 5, Description = "Additional days for parental responsibilities." },
                new LeaveType { Name = "Unpaid Leave", IsPaid = false, DefaultAnnualEntitlement = 0, Description = "Extended time away without pay, subject to approval." });
            await _db.SaveChangesAsync(ct);
        }

        var year = DateTime.UtcNow.Year;
        if (!await _db.Holidays.AnyAsync(h => h.Date.Year == year, ct))
        {
            _db.Holidays.AddRange(
                new Holiday { Name = "New Year's Day", Date = new DateOnly(year, 1, 1), IsRecurringAnnually = true, Description = "Public holiday" },
                new Holiday { Name = "Founders' Day", Date = new DateOnly(year, 4, 12), IsRecurringAnnually = true, Description = "Company anniversary, office closed" },
                new Holiday { Name = "Summer Bank Holiday", Date = new DateOnly(year, 8, 24), IsRecurringAnnually = true, Description = "Public holiday" },
                new Holiday { Name = "Year-end Closure", Date = new DateOnly(year, 12, 25), IsRecurringAnnually = true, Description = "Office closed through the end of December" });
            await _db.SaveChangesAsync(ct);
        }

        if (!await _db.Employees.AnyAsync(ct))
        {
            var roster = new (string Number, string First, string Last, string? Phone, string Dept, string Position,
                EmploymentStatus Status, EmploymentType Type, DateOnly HireDate, DateOnly? Termination,
                string? Location, string? City, string? Country, decimal? Salary, string? EmergencyName)[]
            {
                ("EMP-0001", "Amina", "Yusuf", "202-555-0147", "Human Resources", "HR Manager", EmploymentStatus.Active, EmploymentType.FullTime, new DateOnly(2018, 3, 5), null, "Head Office", "Lisbon", "Portugal", 91250, "Karim Yusuf"),
                ("EMP-0002", "Daniel", "Okafor", "202-555-0163", "Engineering", "Engineering Manager", EmploymentStatus.Active, EmploymentType.FullTime, new DateOnly(2019, 1, 14), null, "Head Office", null, null, 118400, "Ngozi Okafor"),
                ("EMP-0003", "Lena", "Haddad", "202-555-0128", "Engineering", "Software Engineer", EmploymentStatus.Active, EmploymentType.FullTime, new DateOnly(2021, 9, 6), null, "Remote", null, null, 87450, null),
                ("EMP-0004", "Marcus", "Berg", null, "Finance", "Finance Manager", EmploymentStatus.Active, EmploymentType.FullTime, new DateOnly(2019, 11, 18), null, "Head Office", "Stockholm", "Sweden", 105300, "Elin Berg"),
                ("EMP-0005", "Priya", "Nair", "202-555-0171", "Sales", "Sales Manager", EmploymentStatus.Active, EmploymentType.FullTime, new DateOnly(2020, 5, 4), null, "Head Office", null, null, 98700, null),
                ("EMP-0006", "Tomas", "Novak", "202-555-0192", "Engineering", "QA Engineer", EmploymentStatus.Pending, EmploymentType.FullTime, new DateOnly(2026, 10, 1), null, "Head Office", "Prague", "Czech Republic", 68900, null),
                ("EMP-0007", "Sofia", "Reyes", "202-555-0135", "Customer Support", "Support Team Lead", EmploymentStatus.Active, EmploymentType.FullTime, new DateOnly(2020, 2, 17), null, "Remote", null, null, 76400, "Marco Reyes"),
                ("EMP-0008", "Hassan", "Farouk", null, "Engineering", "Senior Software Engineer", EmploymentStatus.OnLeave, EmploymentType.FullTime, new DateOnly(2019, 6, 24), null, "Head Office", null, null, 112250, "Dalia Farouk"),
                ("EMP-0009", "Grace", "Kim", "202-555-0189", "Human Resources", "HR Specialist", EmploymentStatus.Active, EmploymentType.FullTime, new DateOnly(2022, 4, 11), null, "Head Office", null, null, 67350, null),
                ("EMP-0010", "Oliver", "Smith", "202-555-0154", "Engineering", "Software Engineer", EmploymentStatus.Active, EmploymentType.FullTime, new DateOnly(2023, 1, 9), null, "Remote", null, null, 92180, null),
                ("EMP-0011", "Fatima", "Zahra", null, "Finance", "Accountant", EmploymentStatus.Active, EmploymentType.FullTime, new DateOnly(2022, 8, 22), null, "Head Office", null, null, 71450, "Yasmine Zahra"),
                ("EMP-0012", "Nikolai", "Petrov", null, "Engineering", "Software Engineer", EmploymentStatus.Active, EmploymentType.FullTime, new DateOnly(2021, 3, 15), null, "Remote", null, null, null, null),
                ("EMP-0013", "Aisha", "Bello", "202-555-0116", "Customer Support", "Support Specialist", EmploymentStatus.Active, EmploymentType.Intern, new DateOnly(2024, 2, 5), null, "Head Office", null, null, 32400, null),
                ("EMP-0014", "Jakub", "Kowalski", "202-555-0143", "Engineering", "Software Engineer", EmploymentStatus.Active, EmploymentType.FullTime, new DateOnly(2024, 6, 10), null, "Head Office", "Krakow", "Poland", 85375, null),
                ("EMP-0015", "Mei", "Chen", null, "Sales", "Sales Representative", EmploymentStatus.Active, EmploymentType.Contract, new DateOnly(2023, 9, 18), null, "Remote", null, null, 61250, null),
                ("EMP-0016", "Carlos", "Mendes", "202-555-0178", "Sales", "Account Executive", EmploymentStatus.Active, EmploymentType.FullTime, new DateOnly(2022, 1, 31), null, "Head Office", "Porto", "Portugal", 79950, "Ines Mendes"),
                ("EMP-0017", "Ingrid", "Larsen", null, "Customer Support", "Support Specialist", EmploymentStatus.Active, EmploymentType.PartTime, new DateOnly(2025, 3, 3), null, "Remote", null, null, 31800, null),
                ("EMP-0018", "Youssef", "Mansour", "202-555-0122", "Engineering", "Software Engineer", EmploymentStatus.Active, EmploymentType.FullTime, new DateOnly(2025, 8, 25), null, "Head Office", null, null, 82600, null),
                ("EMP-0019", "Elena", "Popescu", "202-555-0108", "Sales", "Sales Representative", EmploymentStatus.Active, EmploymentType.FullTime, new DateOnly(2026, 1, 12), null, "Head Office", null, null, 58400, null),
                ("EMP-0020", "Ravi", "Sharma", null, "Engineering", "Software Engineer", EmploymentStatus.Terminated, EmploymentType.FullTime, new DateOnly(2020, 10, 5), new DateOnly(2025, 11, 28), "Head Office", null, null, 90750, null)
            };

            foreach (var r in roster)
            {
                if (await _db.Employees.AnyAsync(x => x.EmployeeNumber == r.Number, ct)) continue;
                _db.Employees.Add(new Employee
                {
                    EmployeeNumber = r.Number,
                    FirstName = r.First,
                    LastName = r.Last,
                    WorkEmail = $"{r.First}.{r.Last}@emplyo.local".ToLowerInvariant(),
                    PhoneNumber = r.Phone,
                    DepartmentId = departments[r.Dept].Id,
                    PositionId = positions[r.Position].Id,
                    EmploymentType = r.Type,
                    EmploymentStatus = r.Status,
                    HireDate = r.HireDate,
                    TerminationDate = r.Termination,
                    WorkLocation = r.Location,
                    City = r.City,
                    Country = r.Country,
                    AnnualCompensation = r.Salary,
                    CompensationCurrency = r.Salary.HasValue ? "USD" : null,
                    EmergencyContactName = r.EmergencyName,
                    EmergencyContactPhone = r.EmergencyName is null ? null : "202-555-0110",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
            await _db.SaveChangesAsync(ct);
        }

        var employees = await _db.Employees.ToDictionaryAsync(e => e.EmployeeNumber, ct);

        var managerByDepartment = new Dictionary<string, string>
        {
            ["Engineering"] = "EMP-0002",
            ["Sales"] = "EMP-0005",
            ["Customer Support"] = "EMP-0007",
            ["Finance"] = "EMP-0004",
            ["Human Resources"] = "EMP-0001"
        };

        foreach (var (departmentName, managerNumber) in managerByDepartment)
        {
            if (!employees.TryGetValue(managerNumber, out var head)) continue;
            var department = departments[departmentName];
            if (department.DepartmentHeadId != head.Id)
                department.DepartmentHeadId = head.Id;

            var reports = await _db.Employees
                .Where(e => e.DepartmentId == department.Id && e.Id != head.Id && e.ManagerId != head.Id)
                .ToListAsync(ct);
            foreach (var report in reports) report.ManagerId = head.Id;
        }
        await _db.SaveChangesAsync(ct);

        var leaveTypes = await _db.LeaveTypes.ToDictionaryAsync(t => t.Name, ct);
        var now = DateTime.UtcNow;
        foreach (var employee in employees.Values)
        {
            foreach (var type in leaveTypes.Values)
            {
                if (await _db.LeaveBalances.AnyAsync(b => b.EmployeeId == employee.Id && b.LeaveTypeId == type.Id && b.Year == year, ct))
                    continue;
                _db.LeaveBalances.Add(new LeaveBalance
                {
                    EmployeeId = employee.Id,
                    LeaveTypeId = type.Id,
                    Year = year,
                    Entitlement = type.DefaultAnnualEntitlement,
                    Used = 0
                });
            }
        }
        await _db.SaveChangesAsync(ct);

        await EnsureDemoUserAsync("admin@emplyo.local", "System Administrator", "Administrator", defaultPassword, ct);
        await EnsureDemoUserAsync("hr@emplyo.local", "Amina Yusuf", "HRManager", defaultPassword, ct, "EMP-0001");
        await EnsureDemoUserAsync("manager@emplyo.local", "Daniel Okafor", "Employee", defaultPassword, ct, "EMP-0002");
        await EnsureDemoUserAsync("employee@emplyo.local", "Lena Haddad", "Employee", defaultPassword, ct, "EMP-0003");

        await SeedSampleLeaveAsync(leaveTypes, employees, now, ct);
        await SeedSampleAttendanceAsync(ct);
        await SeedLifecycleAsync(employees, now, ct);

        if (!await _db.Announcements.AnyAsync(ct))
        {
            var hrUser = await _userManager.FindByEmailAsync("hr@emplyo.local");
            _db.Announcements.AddRange(
                new Announcement
                {
                    Title = "Head office closed this Saturday for maintenance",
                    Body = "The head office will be closed this Saturday for scheduled electrical maintenance.\n\nBuilding access will be disabled for the day. Badge access resumes on Monday morning. Please take any equipment you need home on Friday.",
                    Audience = AnnouncementAudience.Everyone,
                    IsPublished = true,
                    PublishAtUtc = now,
                    CreatedByUserId = hrUser?.Id ?? "system",
                    CreatedByName = "Amina Yusuf"
                },
                new Announcement
                {
                    Title = "Performance review cycle opens next month",
                    Body = "The next performance review cycle opens on the first working day of next month.\n\nManagers: complete your team's reviews before the closing date. Draft notes can be stored in the Documents library under Performance.",
                    Audience = AnnouncementAudience.Everyone,
                    IsPublished = true,
                    PublishAtUtc = now.AddDays(-2),
                    CreatedByUserId = hrUser?.Id ?? "system",
                    CreatedByName = "Amina Yusuf"
                });
            await _db.SaveChangesAsync(ct);
        }

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

    private async Task SeedSampleLeaveAsync(Dictionary<string, LeaveType> leaveTypes,
        Dictionary<string, Employee> employees, DateTime now, CancellationToken ct)
    {
        if (employees.Count == 0) return;

        var year = now.Year;
        var today = DateOnly.FromDateTime(now);
        var holidays = await _db.Holidays
            .Where(h => h.Date.Year == year)
            .Select(h => h.Date)
            .ToListAsync(ct);

        var annual = leaveTypes["Annual Leave"];
        var sick = leaveTypes["Sick Leave"];

        async Task AddRequestAsync(string employeeNumber, LeaveType type, DateOnly start, DateOnly end,
            string reason, LeaveRequestStatus status, string? decisionBy, string? decisionComment)
        {
            if (!employees.TryGetValue(employeeNumber, out var employee)) return;
            if (await _db.LeaveRequests.AnyAsync(l => l.EmployeeId == employee.Id, ct)) return;

            var days = WorkingDayCalculator.CountWorkingDays(start, end, holidays);
            if (days <= 0) return;

            _db.LeaveRequests.Add(new LeaveRequest
            {
                EmployeeId = employee.Id,
                LeaveTypeId = type.Id,
                StartDate = start,
                EndDate = end,
                TotalDays = days,
                Reason = reason,
                Status = status,
                DecisionByName = decisionBy,
                DecisionAtUtc = status == LeaveRequestStatus.Pending ? null : now.AddDays(-2),
                DecisionComments = decisionComment,
                CreatedAtUtc = now.AddDays(-3),
                UpdatedAtUtc = now
            });

            if (status == LeaveRequestStatus.Approved)
            {
                var balance = await _db.LeaveBalances.FirstOrDefaultAsync(
                    b => b.EmployeeId == employee.Id && b.LeaveTypeId == type.Id && b.Year == year, ct);
                if (balance is not null)
                {
                    balance.Used += days;
                    balance.UpdatedAtUtc = now;
                }
            }
        }

        await AddRequestAsync("EMP-0003", annual, NextWeekday(today.AddDays(4), DayOfWeek.Monday),
            NextWeekday(today.AddDays(4), DayOfWeek.Monday).AddDays(4), "Family trip", LeaveRequestStatus.Approved,
            "Amina Yusuf", "Approved. Cover arranged with Jakub.");
        await AddRequestAsync("EMP-0008", annual, today.AddDays(-1), today.AddDays(4),
            "Extended leave agreed with your manager", LeaveRequestStatus.Approved, "Amina Yusuf", "Approved.");
        await AddRequestAsync("EMP-0002", sick, NextWeekday(today.AddDays(6), DayOfWeek.Monday),
            NextWeekday(today.AddDays(6), DayOfWeek.Monday).AddDays(1), "Medical appointment", LeaveRequestStatus.Pending,
            null, null);
        await AddRequestAsync("EMP-0013", annual, NextWeekday(today.AddDays(9), DayOfWeek.Wednesday),
            NextWeekday(today.AddDays(9), DayOfWeek.Wednesday).AddDays(2), "University exams", LeaveRequestStatus.Pending,
            null, null);
        await AddRequestAsync("EMP-0010", annual, today.AddDays(-21), today.AddDays(-19),
            "Long weekend", LeaveRequestStatus.Rejected, "Amina Yusuf",
            "Two engineers are already out that week. Please pick another week.");

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
        var employees = await _db.Employees
            .Where(e => e.EmploymentStatus == EmploymentStatus.Active)
            .ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var employee in employees)
        {
            for (var offset = 1; offset <= 10; offset++)
            {
                var date = today.AddDays(-offset);
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                if (await _db.AttendanceRecords.AnyAsync(a => a.EmployeeId == employee.Id && a.WorkDate == date, ct)) continue;

                if (employee.WorkLocation == "Remote" && date.DayOfWeek == DayOfWeek.Friday)
                {
                    _db.AttendanceRecords.Add(new AttendanceRecord
                    {
                        EmployeeId = employee.Id,
                        WorkDate = date,
                        ClockInUtc = date.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc).AddMinutes(employee.Id % 25),
                        ClockOutUtc = date.ToDateTime(new TimeOnly(17, 30), DateTimeKind.Utc),
                        Status = AttendanceStatus.Remote
                    });
                    continue;
                }

                var arrivalOffset = (employee.Id * 7 + date.DayNumber) % 40 - 12;
                var departureOffset = (employee.Id * 11 + date.DayNumber) % 35 - 10;
                var clockIn = date.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc).AddMinutes(arrivalOffset);
                var clockOut = date.ToDateTime(new TimeOnly(17, 30), DateTimeKind.Utc).AddMinutes(departureOffset);
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

    private async Task SeedLifecycleAsync(Dictionary<string, Employee> employees, DateTime now, CancellationToken ct)
    {
        if (employees.TryGetValue("EMP-0006", out var newHire) && !await _db.OnboardingTasks.AnyAsync(t => t.EmployeeId == newHire.Id, ct))
        {
            var due = DateOnly.FromDateTime(now).AddDays(10);
            _db.OnboardingTasks.AddRange(
                new OnboardingTask { EmployeeId = newHire.Id, Title = "Sign employment contract", Status = TaskItemStatus.Completed, DueDate = due.AddDays(-7), SortOrder = 1, CompletedAtUtc = now.AddDays(-5) },
                new OnboardingTask { EmployeeId = newHire.Id, Title = "Collect identity documents", Description = "Passport or national ID, and tax number.", Status = TaskItemStatus.InProgress, DueDate = due.AddDays(-3), SortOrder = 2 },
                new OnboardingTask { EmployeeId = newHire.Id, Title = "Issue laptop and accounts", Description = "Laptop, email, and access to the engineering toolchain.", Status = TaskItemStatus.Pending, DueDate = due, SortOrder = 3 },
                new OnboardingTask { EmployeeId = newHire.Id, Title = "Schedule induction session", Status = TaskItemStatus.Pending, DueDate = due.AddDays(4), SortOrder = 4 });
        }

        if (employees.TryGetValue("EMP-0020", out var leaver) && !await _db.OffboardingCases.AnyAsync(c => c.EmployeeId == leaver.Id, ct))
        {
            _db.OffboardingCases.Add(new OffboardingCase
            {
                EmployeeId = leaver.Id,
                DepartureDate = leaver.TerminationDate ?? DateOnly.FromDateTime(now),
                Reason = "Resignation for relocation",
                Status = OffboardingStatus.Completed,
                InitiatedByUserId = "seed",
                InitiatedAtUtc = now.AddDays(-45),
                CompletedAtUtc = now.AddDays(-38),
                UpdatedAtUtc = now.AddDays(-38),
                Tasks = new List<OffboardingTask>
                {
                    new() { Title = "Return laptop and access badge", Status = TaskItemStatus.Completed, SortOrder = 1, CompletedAtUtc = now.AddDays(-40) },
                    new() { Title = "Hand over open tickets and projects", Status = TaskItemStatus.Completed, SortOrder = 2, CompletedAtUtc = now.AddDays(-39) },
                    new() { Title = "Revoke system access", Status = TaskItemStatus.Completed, SortOrder = 3, CompletedAtUtc = now.AddDays(-38) },
                    new() { Title = "Final payroll and documents", Status = TaskItemStatus.Completed, SortOrder = 4, CompletedAtUtc = now.AddDays(-38) }
                }
            });
        }

        await _db.SaveChangesAsync(ct);
    }
}
