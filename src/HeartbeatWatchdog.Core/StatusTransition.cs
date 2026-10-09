namespace HeartbeatWatchdog.Core;

public enum TransitionReason
{
    SuccessPing,
    FailPing,
    ExpectedTimeMissed,
    GracePeriodExceeded,
    Paused,
    Resumed,
}

public enum NotificationKind
{
    Down,
    Up,
}

public sealed record StatusTransition(MonitorStatus From, MonitorStatus To, DateTime AtUtc, TransitionReason Reason)
{
    /// <summary>
    /// 只有「進入 Down」與「從 Down 恢復到 Up」需要通知。
    /// 因為通知綁在狀態「轉換」而非狀態本身，同一段 Down 期間不論評估幾次都只會通知一次。
    /// </summary>
    public NotificationKind? Notification => (From, To) switch
    {
        (not MonitorStatus.Down, MonitorStatus.Down) => NotificationKind.Down,
        (MonitorStatus.Down, MonitorStatus.Up) => NotificationKind.Up,
        _ => null,
    };
}

public sealed record PingOutcome(StatusTransition? Transition, TimeSpan? Duration);
