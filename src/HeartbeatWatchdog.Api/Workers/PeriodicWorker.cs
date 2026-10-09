namespace HeartbeatWatchdog.Api.Workers;

/// <summary>
/// 以 <see cref="TimeProvider"/> 驅動的週期性背景服務。
/// 單一 tick 失敗只記錄錯誤並等下一個 tick，不讓整個 worker 停掉；
/// 每個 tick 用新的 DI scope，避免 DbContext 跨 tick 累積追蹤狀態。
/// </summary>
public abstract class PeriodicWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    WorkerHeartbeats heartbeats,
    ILogger logger) : BackgroundService
{
    protected abstract string Name { get; }
    protected abstract TimeSpan Interval { get; }
    protected abstract Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        heartbeats.Register(Name, Interval);
        logger.LogInformation("{Worker} started; interval {Interval}", Name, Interval);

        try
        {
            using var timer = new PeriodicTimer(Interval, timeProvider);
            do
            {
                await TickAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("{Worker} stopping", Name);
        }
        catch (Exception ex)
        {
            // 理論上不會到這裡（tick 內的例外都已處理）；真的發生就留下 Critical 日誌，
            // 並讓例外往上拋，由 Host 依 BackgroundServiceExceptionBehavior.StopHost 結束行程，
            // 交給 systemd / docker 重啟，而不是在沒有檢查的狀態下繼續假裝健康。
            logger.LogCritical(ex, "{Worker} crashed; the host will stop", Name);
            throw;
        }
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await RunOnceAsync(scope.ServiceProvider, stoppingToken);
            heartbeats.MarkSuccess(Name);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "{Worker} tick failed; will retry in {Interval}", Name, Interval);
        }
    }
}
