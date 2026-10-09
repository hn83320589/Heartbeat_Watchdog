using HeartbeatWatchdog.Core;

namespace HeartbeatWatchdog.Tests.Core;

public class MonitorDefinitionValidatorTests
{
    private static readonly DateTime Now = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    private static MonitorDefinition ValidCron() => new(
        Name: "Nightly ETL",
        Slug: "nightly-etl",
        ScheduleType: ScheduleType.Cron,
        IntervalMinutes: null,
        CronExpression: "0 2 * * *",
        GraceMinutes: 30,
        TimeZone: "Asia/Taipei",
        WebhookUrl: null);

    [Fact]
    public void ValidDefinition_HasNoErrors()
    {
        Assert.Empty(MonitorDefinitionValidator.Validate(ValidCron(), Now));
    }

    [Theory]
    [InlineData("not a cron")]
    [InlineData("61 * * * *")]
    [InlineData("* * * * * *")] // 6 欄位（含秒）不是標準 5 欄位
    public void InvalidCron_IsRejectedWithMessage(string cron)
    {
        var errors = MonitorDefinitionValidator.Validate(ValidCron() with { CronExpression = cron }, Now);

        Assert.Contains("cronExpression", errors.Keys);
        Assert.Contains(cron, errors["cronExpression"][0]);
    }

    [Fact]
    public void CronThatNeverFires_IsRejected()
    {
        var errors = MonitorDefinitionValidator.Validate(ValidCron() with { CronExpression = "0 0 30 2 *" }, Now);

        Assert.Contains("cronExpression", errors.Keys);
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("Taipei Standard Time")] // Windows ID，不是 IANA
    [InlineData("")]
    public void InvalidTimeZone_IsRejected(string timeZone)
    {
        var errors = MonitorDefinitionValidator.Validate(ValidCron() with { TimeZone = timeZone }, Now);

        Assert.Contains("timeZone", errors.Keys);
    }

    [Fact]
    public void NegativeGrace_IsRejected()
    {
        var errors = MonitorDefinitionValidator.Validate(ValidCron() with { GraceMinutes = -1 }, Now);

        Assert.Equal(["graceMinutes must be greater than or equal to 0."], errors["graceMinutes"]);
    }

    [Fact]
    public void IntervalSchedule_RequiresPositiveInterval()
    {
        var definition = ValidCron() with { ScheduleType = ScheduleType.Interval, CronExpression = null, IntervalMinutes = 0 };

        var errors = MonitorDefinitionValidator.Validate(definition, Now);

        Assert.Contains("intervalMinutes", errors.Keys);
    }

    [Theory]
    [InlineData("Has Space")]
    [InlineData("UPPER")]
    [InlineData("")]
    public void InvalidSlug_IsRejected(string slug)
    {
        var errors = MonitorDefinitionValidator.Validate(ValidCron() with { Slug = slug }, Now);

        Assert.Contains("slug", errors.Keys);
    }

    [Fact]
    public void RelativeWebhookUrl_IsRejected()
    {
        var errors = MonitorDefinitionValidator.Validate(ValidCron() with { WebhookUrl = "/hook" }, Now);

        Assert.Contains("webhookUrl", errors.Keys);
    }
}
