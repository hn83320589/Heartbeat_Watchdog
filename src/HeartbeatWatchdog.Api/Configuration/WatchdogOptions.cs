namespace HeartbeatWatchdog.Api.Configuration;

public sealed class WatchdogOptions
{
    public const string SectionName = "Watchdog";
    public const int MinimumApiKeyLength = 16;

    /// <summary>管理 API 的金鑰（X-Api-Key header）。建議用環境變數 Watchdog__ApiKey 設定。</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>監控項目沒有自己的 webhookUrl 時使用。</summary>
    public string? DefaultWebhookUrl { get; set; }

    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan DispatchInterval { get; set; } = TimeSpan.FromSeconds(2);
    public int WebhookMaxAttempts { get; set; } = 3;
    public TimeSpan WebhookRetryBaseDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan WebhookTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
