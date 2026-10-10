using System.Net.Http.Json;
using System.Text.Json;

namespace HeartbeatWatchdog.Tests.Api;

public class OpenApiDocumentTests : IDisposable
{
    private readonly WatchdogApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static IEnumerable<(string Path, string Method, JsonElement Operation)> Operations(JsonElement document) =>
        from path in document.GetProperty("paths").EnumerateObject()
        from operation in path.Value.EnumerateObject()
        select (path.Name, operation.Name, operation.Value);

    private static bool RequiresApiKey(JsonElement operation) =>
        operation.TryGetProperty("security", out var security)
        && security.EnumerateArray().Any(requirement => requirement.TryGetProperty("ApiKey", out _));

    [Fact]
    public async Task Swagger_MarksOnlyAdminEndpointsAsRequiringApiKey()
    {
        var document = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var operations = Operations(document).ToList();

        Assert.NotEmpty(operations);
        Assert.All(operations.Where(o => o.Path.StartsWith("/api/")),
            o => Assert.True(RequiresApiKey(o.Operation), $"{o.Method.ToUpper()} {o.Path} should require X-Api-Key"));
        Assert.All(operations.Where(o => o.Path.StartsWith("/ping/")),
            o => Assert.False(RequiresApiKey(o.Operation), $"{o.Method.ToUpper()} {o.Path} must not require X-Api-Key"));
    }
}
