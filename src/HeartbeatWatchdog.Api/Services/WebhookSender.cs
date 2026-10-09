using System.Diagnostics;
using System.Text;
using HeartbeatWatchdog.Core;

namespace HeartbeatWatchdog.Api.Services;

public sealed record WebhookResult(bool Succeeded, int? HttpStatusCode, string? Error, TimeSpan Elapsed);

public sealed class WebhookSender(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "webhook";

    public async Task<WebhookResult> SendAsync(Notification notification, int attemptNumber, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, notification.TargetUrl)
            {
                Content = new StringContent(notification.Payload, Encoding.UTF8, "application/json"),
            };
            // 讓接收端可以用 notification id 去重（重試時 id 不變）
            request.Headers.Add("X-Watchdog-Notification-Id", notification.Id.ToString());
            request.Headers.Add("X-Watchdog-Attempt", attemptNumber.ToString());

            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            return new WebhookResult(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                stopwatch.Elapsed);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // 連線失敗、逾時、目標 URL 有問題…都算一次失敗的嘗試，由呼叫端記錄並決定是否重試。
            return new WebhookResult(false, null, $"{ex.GetType().Name}: {ex.Message}", stopwatch.Elapsed);
        }
    }
}
