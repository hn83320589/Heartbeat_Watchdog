using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HeartbeatWatchdog.Tests.Api;

public class NotificationTests : IDisposable
{
    private readonly WatchdogApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<(int Id, string Token)> CreateUpHourlyAsync()
    {
        var monitor = await _factory.CreateMonitorAsync(WatchdogApiFactory.HourlyMonitor(graceMinutes: 10));
        var token = monitor.GetProperty("pingToken").GetString()!;
        await _factory.CreateClient().GetAsync($"/ping/{token}");
        return (monitor.GetProperty("id").GetInt32(), token);
    }

    private async Task<JsonElement[]> NotificationsAsync(int id)
    {
        var page = await _factory.CreateAdminClient().GetFromJsonAsync<JsonElement>($"/api/monitors/{id}/notifications");
        return page.GetProperty("items").EnumerateArray().ToArray();
    }

    [Fact]
    public async Task FailPing_GoesDownImmediately_AndSendsDownWebhook()
    {
        var (id, token) = await CreateUpHourlyAsync();

        await _factory.CreateClient().PostAsync($"/ping/{token}/fail", null);

        Assert.Equal("Down", (await _factory.GetMonitorAsync(id)).GetProperty("status").GetString());
        await _factory.DispatchAsync();
        var payload = Assert.Single(_factory.Webhook.Received);
        Assert.Equal("monitor.down", payload.GetProperty("event").GetString());
        Assert.Equal("FailPing", payload.GetProperty("reason").GetString());
        Assert.Equal("hourly-sync", payload.GetProperty("monitor").GetProperty("slug").GetString());
    }

    [Fact]
    public async Task DownPeriod_IsNotifiedExactlyOnce_NoMatterHowManyTicks()
    {
        var (_, token) = await CreateUpHourlyAsync();

        // 跑 4 小時的 tick（每 30 秒一次 = 480 個 tick），期間持續派送通知
        for (var tick = 0; tick < 480; tick++)
        {
            _factory.Clock.Advance(TimeSpan.FromSeconds(30));
            await _factory.EvaluateAsync();
            await _factory.DispatchAsync();
        }

        var down = Assert.Single(_factory.Webhook.Received);
        Assert.Equal("monitor.down", down.GetProperty("event").GetString());
        Assert.Equal("GracePeriodExceeded", down.GetProperty("reason").GetString());

        // 恢復後只送一次 Up
        await _factory.CreateClient().GetAsync($"/ping/{token}");
        await _factory.EvaluateAsync();
        await _factory.DispatchAsync();
        await _factory.DispatchAsync();
        Assert.Equal(["monitor.down", "monitor.up"], _factory.Webhook.Received.Select(r => r.GetProperty("event").GetString()));
    }

    [Fact]
    public async Task Webhook500_IsAttempted3Times_WithExponentialBackoff_AndEveryAttemptIsLogged()
    {
        _factory.Webhook.ResponseStatus = HttpStatusCode.InternalServerError;
        var (id, token) = await CreateUpHourlyAsync();
        await _factory.CreateClient().PostAsync($"/ping/{token}/fail", null);

        await _factory.DispatchAsync();                         // 第 1 次：立即
        _factory.Clock.Advance(TimeSpan.FromSeconds(4));
        await _factory.DispatchAsync();                         // 退避 5 秒還沒到，不送
        Assert.Single(_factory.Webhook.Received);
        _factory.Clock.Advance(TimeSpan.FromSeconds(1));
        await _factory.DispatchAsync();                         // 第 2 次：+5 秒
        _factory.Clock.Advance(TimeSpan.FromSeconds(10));
        await _factory.DispatchAsync();                         // 第 3 次：再 +10 秒
        _factory.Clock.Advance(TimeSpan.FromMinutes(10));
        await _factory.DispatchAsync();                         // 已達上限，不再送

        Assert.Equal(3, _factory.Webhook.Received.Count);
        var notification = Assert.Single(await NotificationsAsync(id));
        Assert.Equal("Failed", notification.GetProperty("status").GetString());
        var attempts = notification.GetProperty("attempts").EnumerateArray().ToArray();
        Assert.Equal([1, 2, 3], attempts.Select(a => a.GetProperty("attemptNumber").GetInt32()));
        Assert.All(attempts, a => Assert.Equal(500, a.GetProperty("httpStatusCode").GetInt32()));
        Assert.All(attempts, a => Assert.False(a.GetProperty("succeeded").GetBoolean()));
        Assert.Equal(
            ["2026-07-01T00:00:00Z", "2026-07-01T00:00:05Z", "2026-07-01T00:00:15Z"],
            attempts.Select(a => a.GetProperty("attemptedAt").GetString()));
    }

    [Fact]
    public async Task SuccessfulDelivery_IsRecordedAsDelivered()
    {
        var (id, token) = await CreateUpHourlyAsync();
        await _factory.CreateClient().PostAsync($"/ping/{token}/fail", null);

        await _factory.DispatchAsync();

        var notification = Assert.Single(await NotificationsAsync(id));
        Assert.Equal("Delivered", notification.GetProperty("status").GetString());
        Assert.Single(notification.GetProperty("attempts").EnumerateArray());
    }

    [Fact]
    public async Task PausedMonitor_SendsNothing_AndResumeRecalculatesFromResumeTime()
    {
        var (id, token) = await CreateUpHourlyAsync();
        var admin = _factory.CreateAdminClient();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/monitors/{id}/pause", null)).StatusCode);

        // 暫停 3 天，期間不論 tick 或 fail ping 都不通知
        for (var tick = 0; tick < 100; tick++)
        {
            _factory.Clock.Advance(TimeSpan.FromHours(1));
            await _factory.EvaluateAsync();
        }
        await _factory.CreateClient().PostAsync($"/ping/{token}/fail", null);
        await _factory.DispatchAsync();
        Assert.Empty(_factory.Webhook.Received);
        Assert.Equal("Paused", (await _factory.GetMonitorAsync(id)).GetProperty("status").GetString());

        var resumedAt = _factory.Clock.GetUtcNow().UtcDateTime;
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/monitors/{id}/resume", null)).StatusCode);

        var resumed = await _factory.GetMonitorAsync(id);
        Assert.Equal("Up", resumed.GetProperty("status").GetString());
        Assert.Equal(resumedAt.AddMinutes(60), resumed.GetProperty("nextExpectedAt").GetDateTime().ToUniversalTime());

        // 恢復後第一個 tick 不會因為暫停前的舊期限而通知
        _factory.Clock.Advance(TimeSpan.FromSeconds(30));
        await _factory.EvaluateAsync();
        await _factory.DispatchAsync();
        Assert.Empty(_factory.Webhook.Received);
    }

    [Fact]
    public async Task PendingNotification_IsSuppressed_WhenMonitorGetsPausedBeforeDelivery()
    {
        var (id, token) = await CreateUpHourlyAsync();
        await _factory.CreateClient().PostAsync($"/ping/{token}/fail", null);
        await _factory.CreateAdminClient().PostAsync($"/api/monitors/{id}/pause", null);

        await _factory.DispatchAsync();

        Assert.Empty(_factory.Webhook.Received);
        Assert.Equal("Suppressed", Assert.Single(await NotificationsAsync(id)).GetProperty("status").GetString());
    }
}
