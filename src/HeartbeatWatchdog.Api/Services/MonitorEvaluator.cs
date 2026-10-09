using HeartbeatWatchdog.Api.Data;
using HeartbeatWatchdog.Core;
using Microsoft.EntityFrameworkCore;

namespace HeartbeatWatchdog.Api.Services;

/// <summary>背景檢查服務每個 tick 執行一次：讓逾時的 Up/Late 項目轉為 Late/Down。</summary>
public sealed class MonitorEvaluator(
    WatchdogDbContext db,
    StatusChangeRecorder recorder,
    MonitorMutationLock mutationLock,
    TimeProvider timeProvider,
    ILogger<MonitorEvaluator> logger)
{
    public async Task<int> EvaluateAllAsync(CancellationToken cancellationToken)
    {
        using var _ = await mutationLock.AcquireAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var candidates = await db.Monitors
            .Where(m => m.Status == MonitorStatus.Up || m.Status == MonitorStatus.Late)
            .ToListAsync(cancellationToken);

        var transitions = 0;
        foreach (var monitor in candidates)
        {
            if (monitor.Evaluate(nowUtc) is { } transition)
            {
                await recorder.RecordAsync(monitor, transition, cancellationToken);
                transitions++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogDebug("Evaluated {Count} monitors at {NowUtc:O}; {Transitions} transitions", candidates.Count, nowUtc, transitions);
        return transitions;
    }
}
