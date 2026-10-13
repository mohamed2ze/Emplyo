using Emplyo.Application.Common.Interfaces;

namespace Emplyo.Web.Services;

/// <summary>
/// Caches the configured organizational time zone for display formatting in views.
/// Invalidated by the settings page when the time zone changes.
/// </summary>
public class TimeZoneProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private TimeZoneInfo _timeZone = TimeZoneInfo.Utc;
    private DateTime _loadedAt = DateTime.MinValue;
    private readonly object _gate = new();

    public TimeZoneProvider(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public TimeZoneInfo Get()
    {
        if (DateTime.UtcNow - _loadedAt < TimeSpan.FromMinutes(5)) return _timeZone;

        lock (_gate)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
                _timeZone = settings.GetOrganizationTimeZoneAsync().GetAwaiter().GetResult();
                _loadedAt = DateTime.UtcNow;
            }
            catch
            {
                // The settings table may be unreachable (for example during startup or a
                // database outage). Keep the last known zone and retry within a minute.
                _loadedAt = DateTime.UtcNow.AddMinutes(-4);
            }
        }
        return _timeZone;
    }

    public void Invalidate() => _loadedAt = DateTime.MinValue;

    public DateTime? ToLocal(DateTime? utc)
        => utc.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc), Get()) : null;

    public DateTime ToLocal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Get());
}
