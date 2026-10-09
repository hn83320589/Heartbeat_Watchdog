using System.Collections.Concurrent;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HeartbeatWatchdog.Api.Workers;

/// <summary>
/// 記錄每個背景 worker 最後一次成功完成 tick 的時間。
/// 「監控別人的服務自己默默掛掉」最難察覺，所以 /health 會在 worker 卡住或連續失敗時回 503，
/// 讓外部的 uptime 監控（或 docker / k8s health check）能發現。
/// </summary>
public sealed class WorkerHeartbeats(TimeProvider timeProvider)
{
    private readonly DateTimeOffset _startedAt = timeProvider.GetUtcNow();
    private readonly ConcurrentDictionary<string, (TimeSpan Interval, DateTimeOffset? LastSuccess)> _workers = new();

    public void Register(string worker, TimeSpan interval) => _workers[worker] = (interval, null);

    public void MarkSuccess(string worker) =>
        _workers.AddOrUpdate(worker, _ => (TimeSpan.Zero, timeProvider.GetUtcNow()), (_, w) => (w.Interval, timeProvider.GetUtcNow()));

    public HealthCheckResult Check()
    {
        var now = timeProvider.GetUtcNow();
        var data = new Dictionary<string, object>();
        var stale = new List<string>();

        foreach (var (name, (interval, lastSuccess)) in _workers)
        {
            data[name] = lastSuccess?.ToString("O") ?? "never";
            // 容許錯過兩個 tick；超過就判定卡住
            var allowed = interval * 3;
            if (now - (lastSuccess ?? _startedAt) > allowed)
            {
                stale.Add(name);
            }
        }

        return stale.Count == 0
            ? HealthCheckResult.Healthy("All background workers are ticking.", data)
            : HealthCheckResult.Unhealthy($"Background worker(s) not ticking: {string.Join(", ", stale)}.", data: data);
    }
}

public sealed class WorkerHealthCheck(WorkerHeartbeats heartbeats) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(heartbeats.Check());
}
