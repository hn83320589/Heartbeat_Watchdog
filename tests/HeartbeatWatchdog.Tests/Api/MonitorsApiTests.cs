using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HeartbeatWatchdog.Tests.Api;

public class MonitorsApiTests : IDisposable
{
    private readonly WatchdogApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static object CronMonitor(string cron = "30 2 * * *", string timeZone = "Europe/Berlin", int grace = 30) => new
    {
        name = "Berlin backup",
        slug = "berlin-backup",
        scheduleType = "Cron",
        cronExpression = cron,
        graceMinutes = grace,
        timeZone,
    };

    private async Task<JsonElement> PostExpecting400(object body)
    {
        var response = await _factory.CreateAdminClient().PostAsJsonAsync("/api/monitors", body, WatchdogApiFactory.Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Create_ReturnsMonitorWithGeneratedTokenAndPingUrl()
    {
        var monitor = await _factory.CreateMonitorAsync(CronMonitor());

        Assert.Equal("New", monitor.GetProperty("status").GetString());
        var token = monitor.GetProperty("pingToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.EndsWith($"/ping/{token}", monitor.GetProperty("pingUrl").GetString());
    }

    [Fact]
    public async Task Create_InvalidCron_Returns400WithClearMessage()
    {
        var problem = await PostExpecting400(CronMonitor(cron: "99 * * * *"));

        var message = problem.GetProperty("errors").GetProperty("cronExpression")[0].GetString();
        Assert.Contains("not a valid 5-field cron expression", message);
    }

    [Fact]
    public async Task Create_InvalidTimeZone_Returns400WithClearMessage()
    {
        var problem = await PostExpecting400(CronMonitor(timeZone: "Europe/Atlantis"));

        var message = problem.GetProperty("errors").GetProperty("timeZone")[0].GetString();
        Assert.Contains("not a valid IANA time zone", message);
    }

    [Fact]
    public async Task Create_NegativeGrace_Returns400WithClearMessage()
    {
        var problem = await PostExpecting400(CronMonitor(grace: -5));

        var message = problem.GetProperty("errors").GetProperty("graceMinutes")[0].GetString();
        Assert.Equal("graceMinutes must be greater than or equal to 0.", message);
    }

    [Fact]
    public async Task Create_UnknownScheduleType_Returns400()
    {
        await PostExpecting400(new { name = "x", slug = "x", scheduleType = "Weekly", graceMinutes = 0, timeZone = "UTC" });
    }

    [Fact]
    public async Task Create_DuplicateSlug_Returns409()
    {
        await _factory.CreateMonitorAsync(CronMonitor());

        var response = await _factory.CreateAdminClient().PostAsJsonAsync("/api/monitors", CronMonitor(), WatchdogApiFactory.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Crud_UpdateAndDelete()
    {
        var created = await _factory.CreateMonitorAsync(WatchdogApiFactory.HourlyMonitor());
        var id = created.GetProperty("id").GetInt32();
        var admin = _factory.CreateAdminClient();

        var update = await admin.PutAsJsonAsync($"/api/monitors/{id}", new
        {
            name = "Renamed",
            slug = "hourly-sync",
            scheduleType = "Interval",
            intervalMinutes = 30,
            graceMinutes = 5,
            timeZone = "Asia/Taipei",
        }, WatchdogApiFactory.Json);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await _factory.GetMonitorAsync(id);
        Assert.Equal("Renamed", updated.GetProperty("name").GetString());
        Assert.Equal(created.GetProperty("pingToken").GetString(), updated.GetProperty("pingToken").GetString());

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/monitors");
        Assert.Equal(1, list.GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/monitors/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/monitors/{id}")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task AdminApi_WithoutValidApiKey_Returns401(string? apiKey)
    {
        var client = _factory.CreateClient();
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/monitors")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/monitors", WatchdogApiFactory.HourlyMonitor())).StatusCode);
    }
}
