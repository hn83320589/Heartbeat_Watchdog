namespace HeartbeatWatchdog.Core;

/// <summary>
/// 監控項目與其狀態機。所有方法都接收呼叫端提供的 UTC 時間，本身不讀取時鐘，
/// 所以狀態轉換規則可以完全用單元測試驗證。狀態轉換規則見 README「狀態機」。
/// </summary>
public class HeartbeatMonitor
{
    // EF Core 用
    private HeartbeatMonitor()
    {
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Slug { get; private set; } = "";
    public string PingToken { get; private set; } = "";
    public ScheduleType ScheduleType { get; private set; }
    public int? IntervalMinutes { get; private set; }
    public string? CronExpression { get; private set; }
    public int GraceMinutes { get; private set; }
    public string TimeZone { get; private set; } = "UTC";
    public string? WebhookUrl { get; private set; }

    public MonitorStatus Status { get; private set; } = MonitorStatus.New;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime StatusChangedAtUtc { get; private set; }
    public DateTime? LastPingAtUtc { get; private set; }
    public DateTime? LastStartedAtUtc { get; private set; }
    public DateTime? NextExpectedAtUtc { get; private set; }

    public MonitorSchedule Schedule => new(ScheduleType, IntervalMinutes, CronExpression, TimeZone);

    public static HeartbeatMonitor Create(MonitorDefinition definition, string pingToken, DateTime nowUtc)
    {
        var monitor = new HeartbeatMonitor
        {
            PingToken = pingToken,
            CreatedAtUtc = nowUtc,
            StatusChangedAtUtc = nowUtc,
        };
        monitor.ApplyDefinition(definition);
        return monitor;
    }

    public void ApplyDefinition(MonitorDefinition definition)
    {
        Name = definition.Name;
        Slug = definition.Slug;
        ScheduleType = definition.ScheduleType;
        IntervalMinutes = definition.ScheduleType == ScheduleType.Interval ? definition.IntervalMinutes : null;
        CronExpression = definition.ScheduleType == ScheduleType.Cron ? definition.CronExpression : null;
        GraceMinutes = definition.GraceMinutes;
        TimeZone = definition.TimeZone;
        WebhookUrl = definition.WebhookUrl;

        // 排程改了，預期時間要依最後一次 ping 重算；New / Paused 本來就沒有預期時間。
        if (Status is MonitorStatus.Up or MonitorStatus.Late or MonitorStatus.Down && LastPingAtUtc is { } lastPing)
        {
            NextExpectedAtUtc = Schedule.NextAfter(lastPing);
        }
    }

    public PingOutcome RecordPing(PingKind kind, DateTime nowUtc)
    {
        if (kind == PingKind.Start)
        {
            LastStartedAtUtc = nowUtc;
            return new PingOutcome(null, null);
        }

        TimeSpan? duration = LastStartedAtUtc is { } startedAt && nowUtc >= startedAt ? nowUtc - startedAt : null;
        LastStartedAtUtc = null;
        LastPingAtUtc = nowUtc;

        // 暫停中只記錄 ping 本身，不改變狀態、不排預期時間。
        if (Status == MonitorStatus.Paused)
        {
            return new PingOutcome(null, duration);
        }

        NextExpectedAtUtc = Schedule.NextAfter(nowUtc);
        var transition = kind == PingKind.Success
            ? TransitionTo(MonitorStatus.Up, TransitionReason.SuccessPing, nowUtc)
            : TransitionTo(MonitorStatus.Down, TransitionReason.FailPing, nowUtc);
        return new PingOutcome(transition, duration);
    }

    /// <summary>由背景檢查服務定期呼叫；只會讓 Up / Late 往更差的方向走。</summary>
    public StatusTransition? Evaluate(DateTime nowUtc)
    {
        if (Status is not (MonitorStatus.Up or MonitorStatus.Late) || NextExpectedAtUtc is not { } expected)
        {
            return null;
        }

        if (nowUtc > expected.AddMinutes(GraceMinutes))
        {
            return TransitionTo(MonitorStatus.Down, TransitionReason.GracePeriodExceeded, nowUtc);
        }

        if (nowUtc > expected)
        {
            return TransitionTo(MonitorStatus.Late, TransitionReason.ExpectedTimeMissed, nowUtc);
        }

        return null;
    }

    public StatusTransition? Pause(DateTime nowUtc)
    {
        if (Status == MonitorStatus.Paused)
        {
            return null;
        }

        NextExpectedAtUtc = null;
        LastStartedAtUtc = null;
        return TransitionTo(MonitorStatus.Paused, TransitionReason.Paused, nowUtc);
    }

    /// <summary>
    /// 恢復時預期時間從「恢復當下」重新起算，不回溯暫停期間錯過的排程。
    /// 曾收過 ping 的項目回到 Up（給一個完整週期），從沒收過的回到 New。
    /// </summary>
    public StatusTransition? Resume(DateTime nowUtc)
    {
        if (Status != MonitorStatus.Paused)
        {
            return null;
        }

        if (LastPingAtUtc is null)
        {
            return TransitionTo(MonitorStatus.New, TransitionReason.Resumed, nowUtc);
        }

        NextExpectedAtUtc = Schedule.NextAfter(nowUtc);
        return TransitionTo(MonitorStatus.Up, TransitionReason.Resumed, nowUtc);
    }

    private StatusTransition? TransitionTo(MonitorStatus target, TransitionReason reason, DateTime nowUtc)
    {
        if (Status == target)
        {
            return null;
        }

        var transition = new StatusTransition(Status, target, nowUtc, reason);
        Status = target;
        StatusChangedAtUtc = nowUtc;
        return transition;
    }
}
