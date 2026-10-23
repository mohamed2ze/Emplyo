using Emplyo.Infrastructure.Persistence;
using Emplyo.Infrastructure.Services;
using Emplyo.UnitTests.TestSupport;
using Xunit;

namespace Emplyo.UnitTests;

public class AppSettingsServiceTests
{
    [Fact]
    public async Task Known_keys_have_safe_defaults_when_the_table_is_empty()
    {
        using var db = TestDb.Create();
        var service = new AppSettingsService(db);

        Assert.Equal("Emplyo", await service.GetAsync(AppSettingsService.OrganizationNameKey));
        Assert.Equal("UTC", await service.GetAsync(AppSettingsService.OrganizationTimeZoneKey));
        Assert.Equal("09:00", await service.GetAsync(AppSettingsService.WorkDayStartKey));
        Assert.Null(await service.GetAsync("Unknown.Key"));
        Assert.Equal("fallback", await service.GetAsync("Unknown.Key", "fallback"));
    }

    [Fact]
    public async Task Set_creates_and_then_updates_a_setting()
    {
        using var db = TestDb.Create();
        var service = new AppSettingsService(db);

        await service.SetAsync("Custom.Key", "first", "First value");
        await service.SetAsync("Custom.Key", "second");

        Assert.Equal("second", await service.GetAsync("Custom.Key"));
        Assert.Equal("First value", (await service.GetAllAsync()).Single().Description);
    }

    [Fact]
    public async Task GetAll_is_ordered_by_key()
    {
        using var db = TestDb.Create();
        var service = new AppSettingsService(db);

        await service.SetAsync("B.Key", "b");
        await service.SetAsync("A.Key", "a");

        Assert.Equal(new[] { "A.Key", "B.Key" }, (await service.GetAllAsync()).Select(s => s.Key));
    }

    [Fact]
    public async Task A_configured_time_zone_is_resolved()
    {
        using var db = TestDb.Create();
        var service = new AppSettingsService(db);
        await service.SetAsync(AppSettingsService.OrganizationTimeZoneKey, "UTC");

        Assert.Equal(TimeZoneInfo.Utc, await service.GetOrganizationTimeZoneAsync());
    }

    [Fact]
    public async Task An_unknown_time_zone_falls_back_to_utc()
    {
        using var db = TestDb.Create();
        var service = new AppSettingsService(db);
        await service.SetAsync(AppSettingsService.OrganizationTimeZoneKey, "Not/A_Real_Zone");

        Assert.Equal(TimeZoneInfo.Utc, await service.GetOrganizationTimeZoneAsync());
    }
}
