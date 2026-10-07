using Emplyo.Domain.Entities;
using Emplyo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Emplyo.IntegrationTests;

public static class TestData
{
    private static IServiceScopeFactory ScopeFactory(EmplyoWebAppFactory factory) => factory.Services.GetRequiredService<IServiceScopeFactory>();

    public static async Task<int> FirstLeaveTypeIdAsync(EmplyoWebAppFactory factory)
    {
        using var scope = ScopeFactory(factory).CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.LeaveTypes.OrderBy(t => t.Id).Select(t => t.Id).FirstAsync();
    }

    public static async Task<int> EmployeeIdByNumberAsync(EmplyoWebAppFactory factory, string number)
    {
        using var scope = ScopeFactory(factory).CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Employees.Where(e => e.EmployeeNumber == number).Select(e => e.Id).FirstAsync();
    }

    public static async Task<bool> EmployeeExistsAsync(EmplyoWebAppFactory factory, string number)
    {
        using var scope = ScopeFactory(factory).CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Employees.AnyAsync(e => e.EmployeeNumber == number);
    }

    public static async Task<T?> WithDbAsync<TSvc, T>(EmplyoWebAppFactory factory, Func<AppDbContext, Task<T>> action)
    {
        using var scope = ScopeFactory(factory).CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(db);
    }

    public static async Task<(int? Id, int EmployeeId)> AnotherEmployeesPendingLeaveAsync(EmplyoWebAppFactory factory)
    {
        using var scope = ScopeFactory(factory).CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seededEmployeeId = await db.Employees
            .Where(e => e.EmployeeNumber == "EMP-0003")
            .Select(e => e.Id)
            .FirstAsync();

        var request = await db.LeaveRequests
            .Where(l => l.EmployeeId != seededEmployeeId)
            .OrderByDescending(l => l.Id)
            .Select(l => new { l.Id, l.EmployeeId })
            .FirstOrDefaultAsync();

        return request is null ? (null, seededEmployeeId) : (request.Id, request.EmployeeId);
    }

    public static async Task<decimal> UsedLeaveAsync(EmplyoWebAppFactory factory, int employeeId, int leaveTypeId, int year)
    {
        using var scope = ScopeFactory(factory).CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var balance = await db.LeaveBalances
            .FirstOrDefaultAsync(b => b.EmployeeId == employeeId && b.LeaveTypeId == leaveTypeId && b.Year == year);
        return balance?.Used ?? 0m;
    }
}
