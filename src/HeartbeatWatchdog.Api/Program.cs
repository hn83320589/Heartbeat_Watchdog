using System.Text.Json.Serialization;
using HeartbeatWatchdog.Api.Auth;
using HeartbeatWatchdog.Api.Configuration;
using HeartbeatWatchdog.Api.Data;
using HeartbeatWatchdog.Api.Services;
using HeartbeatWatchdog.Api.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddSimpleConsole(console =>
{
    console.SingleLine = true;
    console.UseUtcTimestamp = true;
    console.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
});

builder.Services.AddOptions<WatchdogOptions>()
    .Bind(builder.Configuration.GetSection(WatchdogOptions.SectionName))
    .Validate(o => o.ApiKey.Length >= WatchdogOptions.MinimumApiKeyLength,
        $"Watchdog:ApiKey must be set (at least {WatchdogOptions.MinimumApiKeyLength} characters). Use the environment variable Watchdog__ApiKey.")
    .Validate(o => o.CheckInterval > TimeSpan.Zero && o.DispatchInterval > TimeSpan.Zero, "Watchdog intervals must be positive.")
    .Validate(o => o.WebhookMaxAttempts >= 1, "Watchdog:WebhookMaxAttempts must be at least 1.")
    .Validate(o => o.DefaultWebhookUrl is null or "" || Uri.TryCreate(o.DefaultWebhookUrl, UriKind.Absolute, out _),
        "Watchdog:DefaultWebhookUrl must be an absolute URL.")
    .PostConfigure(o => o.DefaultWebhookUrl = string.IsNullOrWhiteSpace(o.DefaultWebhookUrl) ? null : o.DefaultWebhookUrl)
    .ValidateOnStart();

builder.Services.AddDbContext<WatchdogDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Watchdog")
        ?? throw new InvalidOperationException("Connection string 'Watchdog' is not configured.")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MonitorMutationLock>();
builder.Services.AddSingleton<WorkerHeartbeats>();
builder.Services.AddScoped<StatusChangeRecorder>();
builder.Services.AddScoped<MonitorCommands>();
builder.Services.AddScoped<MonitorEvaluator>();
builder.Services.AddScoped<NotificationDispatcher>();
builder.Services.AddScoped<WebhookSender>();
builder.Services.AddHttpClient(WebhookSender.HttpClientName, (services, client) =>
    client.Timeout = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<WatchdogOptions>>().Value.WebhookTimeout);

builder.Services.AddHostedService<MonitorCheckWorker>();
builder.Services.AddHostedService<NotificationDispatchWorker>();
builder.Services.AddHealthChecks().AddCheck<WorkerHealthCheck>("workers");

builder.Services.AddProblemDetails();
builder.Services.AddControllers()
    .AddJsonOptions(json => json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(swagger =>
{
    swagger.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Heartbeat Watchdog API",
        Version = "v1",
        Description = "排程任務心跳監控。管理 API（`/api/*`）需要 `X-Api-Key` header；`/ping/*` 只靠 token，不需要 API key。",
    });
    swagger.AddSecurityDefinition(ApiKeyOperationFilter.SchemeId, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = RequireApiKeyAttribute.HeaderName,
        Description = "管理 API 金鑰",
    });
    swagger.OperationFilter<ApiKeyOperationFilter>();
    swagger.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, "HeartbeatWatchdog.Api.xml"));
});

var app = builder.Build();
var logger = app.Logger;

// 最後一道防線：任何沒被接住的例外都要留下 Critical 日誌再結束，而不是無聲消失。
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception (terminating: {IsTerminating})", e.IsTerminating);
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    logger.LogError(e.Exception, "Unobserved task exception");
    e.SetObserved();
};
app.Lifetime.ApplicationStarted.Register(() => logger.LogInformation("Heartbeat Watchdog started ({Environment})", app.Environment.EnvironmentName));
app.Lifetime.ApplicationStopping.Register(() => logger.LogWarning("Heartbeat Watchdog is stopping"));

try
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<WatchdogDbContext>();
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrated ({DataSource})", db.Database.GetDbConnection().DataSource);
    }

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapControllers();
    app.MapHealthChecks("/health");
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    logger.LogCritical(ex, "Heartbeat Watchdog terminated unexpectedly");
    throw;
}

public partial class Program;
