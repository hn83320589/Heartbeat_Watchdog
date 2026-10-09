using System.Security.Cryptography;
using System.Text;
using HeartbeatWatchdog.Api.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace HeartbeatWatchdog.Api.Auth;

/// <summary>要求 X-Api-Key header 與設定的金鑰相符，否則回 401。只套用在管理 API，/ping/* 不需要。</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireApiKeyAttribute : Attribute, IAuthorizationFilter
{
    public const string HeaderName = "X-Api-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IOptions<WatchdogOptions>>().Value.ApiKey;
        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();

        // 固定時間比較，避免從回應時間推測金鑰
        if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
        {
            return;
        }

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
            Detail = $"Missing or invalid {HeaderName} header.",
        })
        { StatusCode = StatusCodes.Status401Unauthorized };
    }
}
