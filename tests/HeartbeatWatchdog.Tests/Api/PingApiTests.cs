using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HeartbeatWatchdog.Tests.Api;

public class PingApiTests : IDisposable
{
    private readonly WatchdogApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<(int Id, string Token)> CreateHourlyAsync(int graceMinutes = 10)
    {
        var monitor = await _factory.CreateMonitorAsync(WatchdogApiFactory.HourlyMonitor(graceMinutes: graceMinutes));
        return (monitor.GetProperty("id").GetInt32(), monitor.GetProperty("pingToken").GetString()!);
    }

    private async Task<JsonElement[]> EventsAsync(int id)
    {
        var page = await _factory.CreateAdminClient().GetFromJsonAsync<JsonElement>($"/api/monitors/{id}/events?pageSize=100");
        return page.GetProperty("items").EnumerateArray().ToArray();
    }

    [Fact]
    public async Task UnknownToken_Returns404()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/ping/does-not-exist")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/ping/does-not-exist/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/ping/does-not-exist/fail", null)).StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task SuccessPing_WithoutApiKey_SetsUp_AndRecordsEvent(string method)
    {
        var (id, token) = await CreateHourlyAsync();

        var response = await _factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), $"/ping/{token}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var monitor = await _factory.GetMonitorAsync(id);
        Assert.Equal("Up", monitor.GetProperty("status").GetString());
        Assert.Equal("2026-07-01T01:00:00Z", monitor.GetProperty("nextExpectedAt").GetString());

        var events = await EventsAsync(id);
        var ping = Assert.Single(events, e => e.GetProperty("kind").GetString() == "PingSuccess");
        Assert.Equal("2026-07-01T00:00:00Z", ping.GetProperty("occurredAt").GetString());
        Assert.Equal("203.0.113.7", ping.GetProperty("sourceIp").GetString());
        Assert.Contains(events, e => e.GetProperty("kind").GetString() == "StatusChanged"
                                     && e.GetProperty("toStatus").GetString() == "Up");
    }

    [Fact]
    public async Task StartThenSuccess_RecordsRunDuration_FromInjectedClock()
    {
        var (id, token) = await CreateHourlyAsync();
        var client = _factory.CreateClient();

        await client.PostAsync($"/ping/{token}/start", null);
        _factory.Clock.Advance(TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(34));
        await client.PostAsync($"/ping/{token}", null);

        var success = (await EventsAsync(id)).Single(e => e.GetProperty("kind").GetString() == "PingSuccess");
        Assert.Equal(754_000, success.GetProperty("durationMs").GetInt64());
    }

    [Fact]
    public async Task LateWithinGrace_ThenDownAfterGrace()
    {
        var (id, token) = await CreateHourlyAsync(graceMinutes: 10);
        await _factory.CreateClient().GetAsync($"/ping/{token}");

        _factory.Clock.Advance(TimeSpan.FromMinutes(65));
        await _factory.EvaluateAsync();
        Assert.Equal("Late", (await _factory.GetMonitorAsync(id)).GetProperty("status").GetString());

        _factory.Clock.Advance(TimeSpan.FromMinutes(6));
        await _factory.EvaluateAsync();
        Assert.Equal("Down", (await _factory.GetMonitorAsync(id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task EventHistory_IsPaginated_NewestFirst()
    {
        var (id, token) = await CreateHourlyAsync();
        var client = _factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            await client.GetAsync($"/ping/{token}");
            _factory.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var page = await _factory.CreateAdminClient().GetFromJsonAsync<JsonElement>($"/api/monitors/{id}/events?page=2&pageSize=2");

        // 5 次成功 ping + 1 次 New→Up 狀態事件 = 6 筆
        Assert.Equal(6, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, page.GetProperty("totalPages").GetInt32());
        var items = page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.True(items[0].GetProperty("occurredAt").GetDateTime() >= items[1].GetProperty("occurredAt").GetDateTime());
    }
}
