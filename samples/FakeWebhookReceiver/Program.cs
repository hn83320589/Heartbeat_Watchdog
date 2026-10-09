using System.Collections.Concurrent;
using System.Text.Json;

// 示範用的假 Webhook 接收端：
//   POST /webhook          → 印出 payload 並回 200
//   POST /webhook/fail     → 印出 payload 並回 500（用來示範重試 3 次）
//   GET  /received         → 列出目前收到的所有通知
var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
var app = builder.Build();

var received = new ConcurrentQueue<ReceivedWebhook>();
var pretty = new JsonSerializerOptions { WriteIndented = true };

app.MapPost("/webhook", (HttpRequest request, JsonElement payload) => Receive(request, payload, StatusCodes.Status200OK));
app.MapPost("/webhook/fail", (HttpRequest request, JsonElement payload) => Receive(request, payload, StatusCodes.Status500InternalServerError));
app.MapGet("/received", () => received.ToArray());
app.MapGet("/", () => "Fake webhook receiver. POST /webhook (200) or /webhook/fail (500); GET /received.");

app.Run();

IResult Receive(HttpRequest request, JsonElement payload, int statusCode)
{
    var item = new ReceivedWebhook(
        DateTime.UtcNow,
        request.Path,
        request.Headers["X-Watchdog-Notification-Id"].ToString(),
        request.Headers["X-Watchdog-Attempt"].ToString(),
        statusCode,
        payload);
    received.Enqueue(item);

    app.Logger.LogInformation(
        "{Path} notification #{NotificationId} attempt {Attempt} -> responding {StatusCode}\n{Payload}",
        item.Path, item.NotificationId, item.Attempt, statusCode, JsonSerializer.Serialize(payload, pretty));
    return Results.StatusCode(statusCode);
}

internal sealed record ReceivedWebhook(
    DateTime ReceivedAt, string Path, string NotificationId, string Attempt, int RespondedWith, JsonElement Payload);
