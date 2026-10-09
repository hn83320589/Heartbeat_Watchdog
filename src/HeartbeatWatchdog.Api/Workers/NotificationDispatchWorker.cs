using HeartbeatWatchdog.Api.Configuration;
using HeartbeatWatchdog.Api.Services;
using Microsoft.Extensions.Options;

namespace HeartbeatWatchdog.Api.Workers;

/// <summary>定期派送到期的通知（含重試）。間隔要比重試退避時間短，退避才準確。</summary>
public sealed class NotificationDispatchWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    WorkerHeartbeats heartbeats,
    IOptions<WatchdogOptions> options,
    ILogger<NotificationDispatchWorker> logger) : PeriodicWorker(scopeFactory, timeProvider, heartbeats, logger)
{
    protected override string Name => "notification-dispatch";
    protected override TimeSpan Interval => options.Value.DispatchInterval;

    protected override Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<NotificationDispatcher>().DispatchDueAsync(cancellationToken);
}
