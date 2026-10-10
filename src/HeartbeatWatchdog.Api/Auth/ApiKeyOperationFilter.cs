using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace HeartbeatWatchdog.Api.Auth;

/// <summary>只在標有 <see cref="RequireApiKeyAttribute"/> 的端點標示需要 X-Api-Key，讓 Swagger 正確顯示 /ping/* 不需要金鑰。</summary>
public sealed class ApiKeyOperationFilter : IOperationFilter
{
    public const string SchemeId = "ApiKey";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var requiresApiKey = context.MethodInfo.GetCustomAttributes(typeof(RequireApiKeyAttribute), true).Length > 0
            || context.MethodInfo.DeclaringType?.GetCustomAttributes(typeof(RequireApiKeyAttribute), true).Length > 0;
        if (!requiresApiKey)
        {
            return;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeId, context.Document)] = [],
        });
    }
}
