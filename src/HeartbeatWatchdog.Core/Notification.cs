namespace HeartbeatWatchdog.Core;

public enum NotificationStatus
{
    /// <summary>等待（重新）派送。</summary>
    Pending,
    Delivered,

    /// <summary>所有嘗試都失敗。</summary>
    Failed,

    /// <summary>派送前監控項目被暫停或刪除，依規則不發送。</summary>
    Suppressed,

    /// <summary>沒有設定任何 webhook URL。</summary>
    Skipped,
}

/// <summary>
/// 待送通知（outbox）。和狀態轉換在同一個 transaction 寫入，
/// 由派送服務非同步送出並重試，所以 webhook 掛掉不會拖慢 ping 或狀態評估。
/// </summary>
public class Notification
{
    private Notification()
    {
    }

    public long Id { get; private set; }
    public int MonitorId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? TargetUrl { get; private set; }
    public string Payload { get; private set; } = "";
    public NotificationStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public List<NotificationAttempt> Attempts { get; private set; } = [];

    public static Notification Create(int monitorId, NotificationKind kind, string? targetUrl, string payload, DateTime nowUtc) => new()
    {
        MonitorId = monitorId,
        Kind = kind,
        TargetUrl = targetUrl,
        Payload = payload,
        CreatedAtUtc = nowUtc,
        Status = targetUrl is null ? NotificationStatus.Skipped : NotificationStatus.Pending,
        NextAttemptAtUtc = targetUrl is null ? null : nowUtc,
        CompletedAtUtc = targetUrl is null ? nowUtc : null,
    };

    /// <summary>
    /// 記錄一次派送嘗試。失敗時以指數退避排定下一次（base、2×base、4×base…），
    /// 達到 <paramref name="maxAttempts"/> 後標記為 Failed。
    /// </summary>
    public void RecordAttempt(NotificationAttempt attempt, int maxAttempts, TimeSpan retryBaseDelay)
    {
        AttemptCount++;
        Attempts.Add(attempt);

        if (attempt.Succeeded)
        {
            Complete(NotificationStatus.Delivered, attempt.AttemptedAtUtc);
        }
        else if (AttemptCount >= maxAttempts)
        {
            Complete(NotificationStatus.Failed, attempt.AttemptedAtUtc);
        }
        else
        {
            NextAttemptAtUtc = attempt.AttemptedAtUtc + retryBaseDelay * Math.Pow(2, AttemptCount - 1);
        }
    }

    public void Suppress(DateTime nowUtc) => Complete(NotificationStatus.Suppressed, nowUtc);

    private void Complete(NotificationStatus status, DateTime nowUtc)
    {
        Status = status;
        CompletedAtUtc = nowUtc;
        NextAttemptAtUtc = null;
    }
}

public class NotificationAttempt
{
    private NotificationAttempt()
    {
    }

    public long Id { get; private set; }
    public long NotificationId { get; private set; }
    public int AttemptNumber { get; private set; }
    public DateTime AttemptedAtUtc { get; private set; }
    public bool Succeeded { get; private set; }
    public int? HttpStatusCode { get; private set; }
    public string? Error { get; private set; }
    public long ElapsedMs { get; private set; }

    public static NotificationAttempt Create(int attemptNumber, DateTime attemptedAtUtc, bool succeeded, int? httpStatusCode, string? error, TimeSpan elapsed) => new()
    {
        AttemptNumber = attemptNumber,
        AttemptedAtUtc = attemptedAtUtc,
        Succeeded = succeeded,
        HttpStatusCode = httpStatusCode,
        Error = error,
        ElapsedMs = (long)elapsed.TotalMilliseconds,
    };
}
