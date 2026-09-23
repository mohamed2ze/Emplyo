using Emplyo.Application.Common.Interfaces;
using Emplyo.Domain.Entities;
using Emplyo.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<ApplicationUser>, IApplicationDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();
    public DbSet<OnboardingTask> OnboardingTasks => Set<OnboardingTask>();
    public DbSet<OffboardingCase> OffboardingCases => Set<OffboardingCase>();
    public DbSet<OffboardingTask> OffboardingTasks => Set<OffboardingTask>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        var user = builder.Entity<ApplicationUser>();
        user.Property(u => u.DisplayName).HasMaxLength(120);
        user.Property(u => u.ProfilePhotoPath).HasMaxLength(400);
        user.HasOne(u => u.Employee)
            .WithOne()
            .HasForeignKey<Employee>(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Department>(e =>
        {
            e.Property(d => d.Name).IsRequired().HasMaxLength(120);
            e.Property(d => d.Description).HasMaxLength(500);
            e.HasIndex(d => d.Name).IsUnique();
            e.HasOne(d => d.DepartmentHead)
                .WithMany()
                .HasForeignKey(d => d.DepartmentHeadId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Position>(e =>
        {
            e.Property(p => p.Title).IsRequired().HasMaxLength(120);
            e.Property(p => p.Description).HasMaxLength(500);
            e.HasIndex(p => new { p.Title, p.DepartmentId }).IsUnique();
            e.HasOne(p => p.Department)
                .WithMany(d => d.Positions)
                .HasForeignKey(p => p.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Employee>(e =>
        {
            e.Property(x => x.EmployeeNumber).IsRequired().HasMaxLength(50);
            e.Property(x => x.FirstName).IsRequired().HasMaxLength(100);
            e.Property(x => x.LastName).IsRequired().HasMaxLength(100);
            e.Property(x => x.WorkEmail).IsRequired().HasMaxLength(256);
            e.Property(x => x.PhoneNumber).HasMaxLength(30);
            e.Property(x => x.ProfilePhotoPath).HasMaxLength(400);
            e.Property(x => x.WorkLocation).HasMaxLength(150);
            e.Property(x => x.AddressLine).HasMaxLength(200);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.Country).HasMaxLength(100);
            e.Property(x => x.EmergencyContactName).HasMaxLength(150);
            e.Property(x => x.EmergencyContactPhone).HasMaxLength(30);
            e.Property(x => x.AnnualCompensation).HasPrecision(18, 2);
            e.Property(x => x.CompensationCurrency).HasMaxLength(3);

            e.HasIndex(x => x.EmployeeNumber).IsUnique();
            e.HasIndex(x => x.WorkEmail).IsUnique();
            e.HasIndex(x => x.DepartmentId);
            e.HasIndex(x => x.EmploymentStatus);

            e.Property(x => x.RowVersion).IsRowVersion();

            e.HasOne(x => x.Department)
                .WithMany(d => d.Employees)
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Position)
                .WithMany(p => p.Employees)
                .HasForeignKey(x => x.PositionId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Manager)
                .WithMany(m => m.DirectReports)
                .HasForeignKey(x => x.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Ignore(x => x.FullName);
        });

        builder.Entity<LeaveType>(e =>
        {
            e.Property(t => t.Name).IsRequired().HasMaxLength(80);
            e.Property(t => t.Description).HasMaxLength(400);
            e.Property(t => t.DefaultAnnualEntitlement).HasPrecision(6, 2);
            e.HasIndex(t => t.Name).IsUnique();
        });

        builder.Entity<LeaveBalance>(e =>
        {
            e.Property(b => b.Entitlement).HasPrecision(6, 2);
            e.Property(b => b.Used).HasPrecision(6, 2);
            e.HasIndex(b => new { b.EmployeeId, b.LeaveTypeId, b.Year }).IsUnique();
            e.HasOne(b => b.Employee).WithMany(x => x.LeaveBalances).HasForeignKey(b => b.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(b => b.LeaveType).WithMany(t => t.LeaveBalances).HasForeignKey(b => b.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
            e.Property(b => b.RowVersion).IsRowVersion();
            e.Ignore(b => b.Remaining);
        });

        builder.Entity<LeaveRequest>(e =>
        {
            e.Property(l => l.Reason).HasMaxLength(500);
            e.Property(l => l.TotalDays).HasPrecision(6, 2);
            e.Property(l => l.DecisionComments).HasMaxLength(500);
            e.Property(l => l.DecisionByName).HasMaxLength(150);
            e.HasIndex(l => new { l.EmployeeId, l.Status });
            e.HasIndex(l => l.StartDate);
            e.HasOne(l => l.Employee).WithMany(x => x.LeaveRequests).HasForeignKey(l => l.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.LeaveType).WithMany(t => t.LeaveRequests).HasForeignKey(l => l.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
            e.Property(l => l.RowVersion).IsRowVersion();
        });

        builder.Entity<AttendanceRecord>(e =>
        {
            e.Property(a => a.Notes).HasMaxLength(500);
            e.HasIndex(a => new { a.EmployeeId, a.WorkDate }).IsUnique();
            e.HasIndex(a => a.WorkDate);
            e.HasOne(a => a.Employee).WithMany(x => x.AttendanceRecords).HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.Property(a => a.RowVersion).IsRowVersion();
        });

        builder.Entity<Holiday>(e =>
        {
            e.Property(h => h.Name).IsRequired().HasMaxLength(120);
            e.Property(h => h.Description).HasMaxLength(300);
            e.HasIndex(h => new { h.Date, h.Name }).IsUnique();
        });

        builder.Entity<Announcement>(e =>
        {
            e.Property(a => a.Title).IsRequired().HasMaxLength(200);
            e.Property(a => a.Body).IsRequired().HasMaxLength(4000);
            e.Property(a => a.CreatedByName).HasMaxLength(150);
            e.Property(a => a.CreatedByUserId).HasMaxLength(450);
            e.HasIndex(a => a.IsPublished);
            e.HasOne(a => a.Department).WithMany().HasForeignKey(a => a.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Notification>(e =>
        {
            e.Property(n => n.Title).IsRequired().HasMaxLength(200);
            e.Property(n => n.Message).IsRequired().HasMaxLength(1000);
            e.Property(n => n.Url).HasMaxLength(400);
            e.Property(n => n.UserId).IsRequired().HasMaxLength(450);
            e.HasIndex(n => new { n.UserId, n.IsRead });
        });

        builder.Entity<EmployeeDocument>(e =>
        {
            e.Property(d => d.Title).IsRequired().HasMaxLength(200);
            e.Property(d => d.StoredFileName).IsRequired().HasMaxLength(300);
            e.Property(d => d.OriginalFileName).IsRequired().HasMaxLength(300);
            e.Property(d => d.ContentType).IsRequired().HasMaxLength(150);
            e.Property(d => d.UploadedByName).HasMaxLength(150);
            e.Property(d => d.UploadedByUserId).HasMaxLength(450);
            e.HasIndex(d => d.EmployeeId);
            e.HasOne(d => d.Employee).WithMany(x => x.Documents).HasForeignKey(d => d.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.Property(d => d.RowVersion).IsRowVersion();
        });

        builder.Entity<OnboardingTask>(e =>
        {
            e.Property(t => t.Title).IsRequired().HasMaxLength(200);
            e.Property(t => t.Description).HasMaxLength(500);
            e.HasIndex(t => t.EmployeeId);
            e.HasOne(t => t.Employee).WithMany(x => x.OnboardingTasks).HasForeignKey(t => t.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OffboardingCase>(e =>
        {
            e.Property(c => c.Reason).HasMaxLength(500);
            e.Property(c => c.InitiatedByUserId).HasMaxLength(450);
            e.HasIndex(c => c.EmployeeId);
            e.HasOne(c => c.Employee).WithMany(x => x.OffboardingCases).HasForeignKey(c => c.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OffboardingTask>(e =>
        {
            e.Property(t => t.Title).IsRequired().HasMaxLength(200);
            e.Property(t => t.Description).HasMaxLength(500);
            e.HasOne(t => t.OffboardingCase).WithMany(c => c.Tasks).HasForeignKey(t => t.OffboardingCaseId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AuditLog>(e =>
        {
            e.Property(a => a.ActorName).HasMaxLength(200);
            e.Property(a => a.Action).IsRequired().HasMaxLength(120);
            e.Property(a => a.TargetType).IsRequired().HasMaxLength(120);
            e.Property(a => a.TargetId).HasMaxLength(80);
            e.Property(a => a.CorrelationId).HasMaxLength(120);
            e.Property(a => a.Metadata).HasMaxLength(1000);
            e.HasIndex(a => a.TimestampUtc);
            e.HasIndex(a => a.Action);
            e.HasIndex(a => a.TargetType);
        });

        builder.Entity<AppSetting>(e =>
        {
            e.Property(s => s.Key).IsRequired().HasMaxLength(150);
            e.Property(s => s.Value).IsRequired().HasMaxLength(500);
            e.Property(s => s.Description).HasMaxLength(300);
            e.HasIndex(s => s.Key).IsUnique();
        });
    }
}
