using HeartbeatWatchdog.Api.Auth;
using HeartbeatWatchdog.Api.Contracts;
using HeartbeatWatchdog.Api.Data;
using HeartbeatWatchdog.Api.Services;
using HeartbeatWatchdog.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeartbeatWatchdog.Api.Controllers;

[ApiController]
[Route("api/monitors")]
[RequireApiKey]
[Produces("application/json")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class MonitorsController(
    WatchdogDbContext db,
    MonitorCommands commands,
    TimeProvider timeProvider) : ControllerBase
{
    private const int MaxPageSize = 200;

    private string BaseUrl => $"{Request.Scheme}://{Request.Host}{Request.PathBase}";

    [HttpGet]
    public async Task<IReadOnlyList<MonitorResponse>> List(CancellationToken ct)
    {
        var monitors = await db.Monitors.AsNoTracking().OrderBy(m => m.Slug).ToListAsync(ct);
        return monitors.Select(m => MonitorResponse.From(m, BaseUrl)).ToList();
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<MonitorResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MonitorResponse>> Get(int id, CancellationToken ct)
    {
        var monitor = await db.Monitors.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct);
        return monitor is null ? NotFoundProblem(id) : MonitorResponse.From(monitor, BaseUrl);
    }

    [HttpPost]
    [ProducesResponseType<MonitorResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MonitorResponse>> Create(MonitorRequest request, CancellationToken ct)
    {
        if (Invalid(request, out var definition) is { } invalid)
        {
            return invalid;
        }

        var (result, monitor) = await commands.CreateAsync(definition, ct);
        return result == CommandResult.SlugConflict
            ? SlugConflict(definition.Slug)
            : CreatedAtAction(nameof(Get), new { id = monitor!.Id }, MonitorResponse.From(monitor, BaseUrl));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<MonitorResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MonitorResponse>> Update(int id, MonitorRequest request, CancellationToken ct)
    {
        if (Invalid(request, out var definition) is { } invalid)
        {
            return invalid;
        }

        var (result, monitor) = await commands.UpdateAsync(id, definition, ct);
        return result switch
        {
            CommandResult.NotFound => NotFoundProblem(id),
            CommandResult.SlugConflict => SlugConflict(definition.Slug),
            _ => MonitorResponse.From(monitor!, BaseUrl),
        };
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await commands.DeleteAsync(id, ct) ? NoContent() : NotFoundProblem(id);

    /// <summary>暫停：不評估、不發通知。重複呼叫不會有副作用。</summary>
    [HttpPost("{id:int}/pause")]
    [ProducesResponseType<MonitorResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MonitorResponse>> Pause(int id, CancellationToken ct) =>
        await commands.PauseAsync(id, ct) is { } monitor ? MonitorResponse.From(monitor, BaseUrl) : NotFoundProblem(id);

    /// <summary>恢復：下一次預期時間從恢復當下重新計算。</summary>
    [HttpPost("{id:int}/resume")]
    [ProducesResponseType<MonitorResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MonitorResponse>> Resume(int id, CancellationToken ct) =>
        await commands.ResumeAsync(id, ct) is { } monitor ? MonitorResponse.From(monitor, BaseUrl) : NotFoundProblem(id);

    /// <summary>事件歷史（ping 與狀態轉換），新到舊排序。</summary>
    [HttpGet("{id:int}/events")]
    [ProducesResponseType<PagedResponse<MonitorEventResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<MonitorEventResponse>>> Events(
        int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (await MissingOrBadPaging(id, page, pageSize, ct) is { } error)
        {
            return error;
        }

        var query = db.Events.AsNoTracking().Where(e => e.MonitorId == id);
        var items = await query
            .OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return new PagedResponse<MonitorEventResponse>(
            items.Select(MonitorEventResponse.From).ToList(), page, pageSize, await query.CountAsync(ct));
    }

    /// <summary>通知紀錄與每一次派送嘗試，新到舊排序。</summary>
    [HttpGet("{id:int}/notifications")]
    [ProducesResponseType<PagedResponse<NotificationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<NotificationResponse>>> Notifications(
        int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (await MissingOrBadPaging(id, page, pageSize, ct) is { } error)
        {
            return error;
        }

        var query = db.Notifications.AsNoTracking().Where(n => n.MonitorId == id);
        var items = await query
            .Include(n => n.Attempts)
            .OrderByDescending(n => n.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return new PagedResponse<NotificationResponse>(
            items.Select(NotificationResponse.From).ToList(), page, pageSize, await query.CountAsync(ct));
    }

    /// <summary>近 N 天（預設 30）可用率；Paused 與 New 期間不計入分母。</summary>
    [HttpGet("{id:int}/availability")]
    [ProducesResponseType<AvailabilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AvailabilityResponse>> Availability(int id, [FromQuery] int days = 30, CancellationToken ct = default)
    {
        if (days is < 1 or > 365)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["days"] = ["days must be between 1 and 365."],
            }));
        }
        if (!await db.Monitors.AnyAsync(m => m.Id == id, ct))
        {
            return NotFoundProblem(id);
        }

        var toUtc = timeProvider.GetUtcNow().UtcDateTime;
        var fromUtc = toUtc.AddDays(-days);
        var periods = await db.StatusPeriods.AsNoTracking()
            .Where(p => p.MonitorId == id && p.StartedAtUtc < toUtc && (p.EndedAtUtc == null || p.EndedAtUtc > fromUtc))
            .ToListAsync(ct);

        var result = AvailabilityCalculator.Calculate(periods, fromUtc, toUtc);
        return new AvailabilityResponse(id, days, fromUtc, toUtc, result.Ratio,
            result.UpTime.TotalSeconds, result.MonitoredTime.TotalSeconds, result.PausedTime.TotalSeconds);
    }

    private ActionResult? Invalid(MonitorRequest request, out MonitorDefinition definition)
    {
        var errors = request.Validate(timeProvider.GetUtcNow().UtcDateTime, out definition);
        return errors.Count == 0 ? null : ValidationProblem(new ValidationProblemDetails(errors.ToDictionary()));
    }

    private async Task<ActionResult?> MissingOrBadPaging(int id, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > MaxPageSize)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["paging"] = [$"page must be >= 1 and pageSize between 1 and {MaxPageSize}."],
            }));
        }
        return await db.Monitors.AnyAsync(m => m.Id == id, ct) ? null : NotFoundProblem(id);
    }

    private ObjectResult NotFoundProblem(int id) =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Monitor not found", detail: $"Monitor {id} does not exist.");

    private ObjectResult SlugConflict(string slug) =>
        Problem(statusCode: StatusCodes.Status409Conflict, title: "Slug already in use", detail: $"A monitor with slug '{slug}' already exists.");
}
