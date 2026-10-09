using HeartbeatWatchdog.Api.Services;
using HeartbeatWatchdog.Core;
using Microsoft.AspNetCore.Mvc;

namespace HeartbeatWatchdog.Api.Controllers;

/// <summary>排程任務回報用的端點；只靠 token 識別，不需要 API key。GET 與 POST 皆可。</summary>
[ApiController]
[Route("ping/{token}")]
[Produces("text/plain")]
public class PingController(MonitorCommands commands) : ControllerBase
{
    /// <summary>任務成功完成。</summary>
    [HttpGet, HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Success(string token, CancellationToken ct) => HandleAsync(token, PingKind.Success, ct);

    /// <summary>任務開始執行；之後的成功 / 失敗 ping 會算出執行時長。</summary>
    [HttpGet("start"), HttpPost("start")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Start(string token, CancellationToken ct) => HandleAsync(token, PingKind.Start, ct);

    /// <summary>任務主動回報失敗，狀態立即變為 Down。</summary>
    [HttpGet("fail"), HttpPost("fail")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Fail(string token, CancellationToken ct) => HandleAsync(token, PingKind.Fail, ct);

    private async Task<IActionResult> HandleAsync(string token, PingKind kind, CancellationToken ct)
    {
        var sourceIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        return await commands.PingAsync(token, kind, sourceIp, ct)
            ? Content("OK", "text/plain")
            : Problem(statusCode: StatusCodes.Status404NotFound, title: "Unknown ping token");
    }
}
