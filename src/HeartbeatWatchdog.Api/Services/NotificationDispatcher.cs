using HeartbeatWatchdog.Api.Configuration;
using HeartbeatWatchdog.Api.Data;
using HeartbeatWatchdog.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HeartbeatWatchdog.Api.Services;

/// <summary>派送到期的通知；每次嘗試都寫入 NotificationAttempts。</summary>
public sealed class NotificationDispatcher(
    WatchdogDbContext db,
    WebhookSender sender,
    TimeProvider timeProvider,
    IOptions<WatchdogOptions> options,
    ILogger<NotificationDispatcher> logger)
{
    private const int BatchSize = 50;

    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var due = await db.Notifications
            .Where(n => n.Status == NotificationStatus.Pending && n.NextAttemptAtUtc <= nowUtc)
            .OrderBy(n => n.NextAttemptAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var notification in due)
        {
            await DispatchAsync(notification, cancellationToken);
            // 每則通知各自存檔：單一通知出錯時，前面已送出的嘗試紀錄不會遺失
            await db.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }

    private async Task DispatchAsync(Notification notification, CancellationToken cancellationToken)
    {
        var monitorStatus = await db.Monitors
            .Where(m => m.Id == notification.MonitorId)
            .Select(m => (MonitorStatus?)m.Status)
            .SingleOrDefaultAsync(cancellationToken);
        if (monitorStatus is null or MonitorStatus.Paused)
        {
            notification.Suppress(timeProvider.GetUtcNow().UtcDateTime);
            logger.LogInformation("Notification {NotificationId} suppressed because monitor {MonitorId} is paused or deleted",
                notification.Id, notification.MonitorId);
            return;
        }

        var attemptNumber = notification.AttemptCount + 1;
        var attemptedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var result = await sender.SendAsync(notification, attemptNumber, cancellationToken);

        var settings = options.Value;
        notification.RecordAttempt(
            NotificationAttempt.Create(attemptNumber, attemptedAtUtc, result.Succeeded, result.HttpStatusCode, result.Error, result.Elapsed),
            settings.WebhookMaxAttempts,
            settings.WebhookRetryBaseDelay);

        switch (notification.Status)
        {
            case NotificationStatus.Delivered:
                logger.LogInformation("Notification {NotificationId} ({Kind}) delivered to {Url} on attempt {Attempt}",
                    notification.Id, notification.Kind, notification.TargetUrl, attemptNumber);
                break;
            case NotificationStatus.Failed:
                logger.LogError("Notification {NotificationId} ({Kind}) to {Url} failed after {Attempts} attempts; last error: {Error}",
                    notification.Id, notification.Kind, notification.TargetUrl, attemptNumber, result.Error);
                break;
            default:
                logger.LogWarning("Notification {NotificationId} attempt {Attempt} to {Url} failed ({Error}); retrying at {NextAttemptAtUtc:O}",
                    notification.Id, attemptNumber, notification.TargetUrl, result.Error, notification.NextAttemptAtUtc);
                break;
        }
    }
}
