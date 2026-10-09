using HeartbeatWatchdog.Api.Configuration;
using HeartbeatWatchdog.Api.Services;
using Microsoft.Extensions.Options;

namespace HeartbeatWatchdog.Api.Workers;

/// <summary>每 30 秒（可設定）評估所有監控項目。</summary>
public sealed class MonitorCheckWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    WorkerHeartbeats heartbeats,
    IOptions<WatchdogOptions> options,
    ILogger<MonitorCheckWorker> logger) : PeriodicWorker(scopeFactory, timeProvider, heartbeats, logger)
{
    protected override string Name => "monitor-check";
    protected override TimeSpan Interval => options.Value.CheckInterval;

    protected override Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<MonitorEvaluator>().EvaluateAllAsync(cancellationToken);
}
