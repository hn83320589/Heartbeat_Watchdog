using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeartbeatWatchdog.Api.Services;
using HeartbeatWatchdog.Api.Workers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace HeartbeatWatchdog.Tests.Api;

/// <summary>
/// 測試用的 API host：獨立的 SQLite 檔、可快轉的時鐘、攔截 webhook 的假 handler。
/// 背景 worker 會被移除，改由測試直接呼叫 <see cref="EvaluateAsync"/> / <see cref="DispatchAsync"/>
/// 模擬每一個 tick，所以完全不用等待真實時間。
/// </summary>
public sealed class WatchdogApiFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "test-api-key-0123456789";
    public const string WebhookUrl = "http://webhook.test/hook";

    /// <summary>TestServer 不會設定連線的來源 IP，這裡模擬一個固定值（RFC 5737 文件用位址）。</summary>
    public static readonly IPAddress ClientIp = IPAddress.Parse("203.0.113.7");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"watchdog-test-{Guid.NewGuid():N}.db");

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

    public StubWebhookHandler Webhook { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Watchdog", $"Data Source={_databasePath}");
        builder.UseSetting("Watchdog:ApiKey", ApiKey);
        builder.UseSetting("Watchdog:DefaultWebhookUrl", WebhookUrl);
        builder.UseSetting("Watchdog:WebhookRetryBaseDelay", "00:00:05");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            foreach (var worker in services.Where(d => d.ImplementationType?.Namespace == typeof(MonitorCheckWorker).Namespace).ToList())
            {
                services.Remove(worker);
            }

            services.AddHttpClient(WebhookSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Webhook);
            services.AddSingleton<IStartupFilter, FixedRemoteIpStartupFilter>();
        });
    }

    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    /// <summary>模擬背景檢查服務跑一個 tick。</summary>
    public async Task EvaluateAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MonitorEvaluator>().EvaluateAllAsync(CancellationToken.None);
    }

    /// <summary>模擬通知派送服務跑一個 tick。</summary>
    public async Task DispatchAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchDueAsync(CancellationToken.None);
    }

    public async Task<JsonElement> CreateMonitorAsync(object request)
    {
        var response = await CreateAdminClient().PostAsJsonAsync("/api/monitors", request, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    public static object HourlyMonitor(string slug = "hourly-sync", int graceMinutes = 10) => new
    {
        name = "Hourly sync",
        slug,
        scheduleType = "Interval",
        intervalMinutes = 60,
        graceMinutes,
        timeZone = "UTC",
    };

    public async Task<JsonElement> GetMonitorAsync(int id) =>
        await CreateAdminClient().GetFromJsonAsync<JsonElement>($"/api/monitors/{id}", Json);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            File.Delete(_databasePath + suffix);
        }
    }
}

public sealed class FixedRemoteIpStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress = WatchdogApiFactory.ClientIp;
            return nextMiddleware(context);
        });
        next(app);
    };
}

public sealed class StubWebhookHandler : HttpMessageHandler
{
    private readonly List<string> _bodies = [];

    public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;

    public IReadOnlyList<JsonElement> Received
    {
        get
        {
            lock (_bodies)
            {
                return _bodies.Select(b => JsonDocument.Parse(b).RootElement).ToList();
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        lock (_bodies)
        {
            _bodies.Add(body);
        }
        return new HttpResponseMessage(ResponseStatus);
    }
}
