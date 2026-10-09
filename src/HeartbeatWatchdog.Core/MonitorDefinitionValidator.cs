using System.Text.RegularExpressions;
using Cronos;

namespace HeartbeatWatchdog.Core;

/// <summary>
/// 驗證監控項目設定。錯誤以「欄位名稱 → 訊息」回傳，
/// 讓 API 層可以直接轉成 ValidationProblemDetails（400）。
/// </summary>
public static partial class MonitorDefinitionValidator
{
    public const int MaxIntervalMinutes = 60 * 24 * 365;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    public static IReadOnlyDictionary<string, string[]> Validate(MonitorDefinition definition, DateTime nowUtc)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string message) => errors[field] = [message];

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            Add("name", "name is required.");
        }
        else if (definition.Name.Length > 200)
        {
            Add("name", "name must be at most 200 characters.");
        }

        if (definition.Slug is null || definition.Slug.Length > 100 || !SlugPattern().IsMatch(definition.Slug))
        {
            Add("slug", $"slug '{definition.Slug}' is invalid. Use lowercase letters, digits and single hyphens (max 100 characters), e.g. 'nightly-etl'.");
        }

        if (definition.GraceMinutes < 0)
        {
            Add("graceMinutes", "graceMinutes must be greater than or equal to 0.");
        }

        var timeZoneValid = IsIanaTimeZone(definition.TimeZone);
        if (!timeZoneValid)
        {
            Add("timeZone", $"timeZone '{definition.TimeZone}' is not a valid IANA time zone id (e.g. 'Asia/Taipei', 'Europe/Berlin', 'UTC').");
        }

        switch (definition.ScheduleType)
        {
            case ScheduleType.Interval:
                if (definition.IntervalMinutes is not (>= 1 and <= MaxIntervalMinutes))
                {
                    Add("intervalMinutes", $"intervalMinutes is required for interval schedules and must be between 1 and {MaxIntervalMinutes}.");
                }
                break;

            case ScheduleType.Cron:
                ValidateCron(definition, nowUtc, timeZoneValid, Add);
                break;

            default:
                Add("scheduleType", "scheduleType must be 'Interval' or 'Cron'.");
                break;
        }

        if (definition.WebhookUrl is not null
            && !(Uri.TryCreate(definition.WebhookUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
        {
            Add("webhookUrl", $"webhookUrl '{definition.WebhookUrl}' must be an absolute http or https URL.");
        }

        return errors;
    }

    private static void ValidateCron(MonitorDefinition definition, DateTime nowUtc, bool timeZoneValid, Action<string, string> add)
    {
        if (string.IsNullOrWhiteSpace(definition.CronExpression))
        {
            add("cronExpression", "cronExpression is required for cron schedules.");
            return;
        }

        if (!CronExpression.TryParse(definition.CronExpression, CronFormat.Standard, out var cron))
        {
            add("cronExpression", $"cronExpression '{definition.CronExpression}' is not a valid 5-field cron expression (minute hour day-of-month month day-of-week).");
            return;
        }

        if (timeZoneValid && cron.GetNextOccurrence(nowUtc, TimeZoneInfo.FindSystemTimeZoneById(definition.TimeZone)) is null)
        {
            add("cronExpression", $"cronExpression '{definition.CronExpression}' never fires.");
        }
    }

    /// <summary>只接受 IANA ID；Windows ID（如 "Taipei Standard Time"）在某些平台雖然找得到，仍視為不合法。</summary>
    public static bool IsIanaTimeZone(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)
        && zone.HasIanaId;
}
