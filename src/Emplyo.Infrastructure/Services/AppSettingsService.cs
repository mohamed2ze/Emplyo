using Emplyo.Application.Common.Interfaces;
using Emplyo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Emplyo.Infrastructure.Services;

public class AppSettingsService : IAppSettingsService
{
    public const string OrganizationNameKey = "Organization.Name";
    public const string OrganizationTimeZoneKey = "Organization.TimeZone";
    public const string WorkDayStartKey = "Attendance.WorkDayStart";

    private readonly IApplicationDbContext _db;

    public AppSettingsService(IApplicationDbContext db) => _db = db;

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var setting = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is not null) return setting.Value;
        return key switch
        {
            OrganizationTimeZoneKey => "UTC",
            OrganizationNameKey => "Emplyo",
            WorkDayStartKey => "09:00",
            _ => null
        };
    }

    public async Task<string> GetAsync(string key, string defaultValue, CancellationToken ct = default)
        => await GetAsync(key, ct) ?? defaultValue;

    public async Task SetAsync(string key, string value, string? description = null, CancellationToken ct = default)
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting { Key = key, Value = value, Description = description, UpdatedAtUtc = DateTime.UtcNow });
        }
        else
        {
            setting.Value = value;
            if (description is not null) setting.Description = description;
            setting.UpdatedAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<TimeZoneInfo> GetOrganizationTimeZoneAsync(CancellationToken ct = default)
    {
        var id = await GetAsync(OrganizationTimeZoneKey, "UTC", ct);
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    public Task<string> GetOrganizationNameAsync(CancellationToken ct = default)
        => GetAsync(OrganizationNameKey, "Emplyo", ct);

    public async Task<IReadOnlyList<(string Key, string Value, string? Description)>> GetAllAsync(CancellationToken ct = default)
    {
        var settings = await _db.AppSettings.AsNoTracking().OrderBy(s => s.Key)
            .Select(s => new { s.Key, s.Value, s.Description })
            .ToListAsync(ct);
        return settings.Select(s => (s.Key, s.Value, s.Description)).ToList();
    }
}
