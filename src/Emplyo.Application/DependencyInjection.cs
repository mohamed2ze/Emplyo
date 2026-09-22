using Emplyo.Application.Announcements;
using Emplyo.Application.Attendance;
using Emplyo.Application.Auditing;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Application.Dashboard;
using Emplyo.Application.Documents;
using Emplyo.Application.Employees;
using Emplyo.Application.LeaveManagement;
using Emplyo.Application.Lifecycle;
using Emplyo.Application.Notifications;
using Emplyo.Application.Organization;
using Emplyo.Application.Reporting;
using Emplyo.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Emplyo.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IPositionService, PositionService>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<ILeaveService, LeaveService>();
        services.AddScoped<ILeaveTypeService, LeaveTypeService>();
        services.AddScoped<IHolidayService, HolidayService>();
        services.AddScoped<ILifecycleService, LifecycleService>();
        services.AddScoped<IAnnouncementService, AnnouncementService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
        services.AddScoped<INotificationQueryService>(sp => sp.GetRequiredService<NotificationService>());

        return services;
    }
}
