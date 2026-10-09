using System.Net.Http.Json;
using System.Text.Json;

namespace HeartbeatWatchdog.Tests.Api;

public class AvailabilityApiTests : IDisposable
{
    private readonly WatchdogApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Availability_ExcludesPausedTimeFromDenominator()
    {
        var monitor = await _factory.CreateMonitorAsync(WatchdogApiFactory.HourlyMonitor(graceMinutes: 0));
        var id = monitor.GetProperty("id").GetInt32();
        var token = monitor.GetProperty("pingToken").GetString()!;
        var client = _factory.CreateClient();
        var admin = _factory.CreateAdminClient();

        await client.GetAsync($"/ping/{token}");                    // 00:00 Up
        _factory.Clock.Advance(TimeSpan.FromHours(6));
        await client.GetAsync($"/ping/{token}/fail");               // 06:00 Down
        _factory.Clock.Advance(TimeSpan.FromHours(2));
        await admin.PostAsync($"/api/monitors/{id}/pause", null);   // 08:00 Paused
        _factory.Clock.Advance(TimeSpan.FromHours(10));
        await admin.PostAsync($"/api/monitors/{id}/resume", null);  // 18:00 Up
        _factory.Clock.Advance(TimeSpan.FromHours(2));              // 20:00

        var availability = await admin.GetFromJsonAsync<JsonElement>($"/api/monitors/{id}/availability");

        // Up 6h + 2h = 8h；受監控 = 8h + Down 2h = 10h；Paused 10h 不計入 → 80%
        Assert.Equal(0.8, availability.GetProperty("availability").GetDouble(), precision: 6);
        Assert.Equal(10 * 3600, availability.GetProperty("monitoredSeconds").GetDouble(), precision: 3);
        Assert.Equal(10 * 3600, availability.GetProperty("pausedSeconds").GetDouble(), precision: 3);
        Assert.Equal(30, availability.GetProperty("days").GetInt32());
    }
}
