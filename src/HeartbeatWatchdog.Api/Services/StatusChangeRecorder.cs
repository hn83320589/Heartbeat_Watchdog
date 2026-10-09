using HeartbeatWatchdog.Api.Configuration;
using HeartbeatWatchdog.Api.Data;
using HeartbeatWatchdog.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HeartbeatWatchdog.Api.Services;

/// <summary>
/// 把一次狀態轉換的所有副作用加進同一個 DbContext：事件紀錄、狀態區間、待送通知。
/// 呼叫端一次 SaveChanges 就全部原子性寫入 —— 狀態變了就一定有通知，反之亦然。
/// </summary>
public sealed class StatusChangeRecorder(
    WatchdogDbContext db,
    IOptions<WatchdogOptions> options,
    ILogger<StatusChangeRecorder> logger)
{
    public async Task RecordAsync(HeartbeatMonitor monitor, StatusTransition transition, CancellationToken cancellationToken)
    {
        db.Events.Add(MonitorEvent.StatusChanged(monitor.Id, transition));

        var openPeriods = await db.StatusPeriods
            .Where(p => p.MonitorId == monitor.Id && p.EndedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var period in openPeriods)
        {
            period.Close(transition.AtUtc);
        }
        db.StatusPeriods.Add(new StatusPeriod(transition.To, transition.AtUtc, null) { MonitorId = monitor.Id });

        logger.LogInformation(
            "Monitor {MonitorSlug} ({MonitorId}) changed {FromStatus} -> {ToStatus} because {Reason}",
            monitor.Slug, monitor.Id, transition.From, transition.To, transition.Reason);

        if (transition.Notification is not { } kind)
        {
            return;
        }

        var targetUrl = monitor.WebhookUrl ?? options.Value.DefaultWebhookUrl;
        if (targetUrl is null)
        {
            logger.LogWarning(
                "Monitor {MonitorSlug} went {ToStatus} but no webhook URL is configured; notification skipped",
                monitor.Slug, transition.To);
        }

        db.Notifications.Add(Notification.Create(
            monitor.Id, kind, targetUrl, WebhookPayload.Serialize(monitor, transition, kind), transition.AtUtc));
    }
}
