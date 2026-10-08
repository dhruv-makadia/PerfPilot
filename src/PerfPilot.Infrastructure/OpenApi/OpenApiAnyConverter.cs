using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.OpenApi.Any;

namespace PerfPilot.Infrastructure.OpenApi;

internal static class OpenApiAnyConverter
{
    public static JsonNode? ToNode(IOpenApiAny? any) => any switch
    {
        null or OpenApiNull => null,
        OpenApiString s => JsonValue.Create(s.Value),
        OpenApiInteger i => JsonValue.Create(i.Value),
        OpenApiLong l => JsonValue.Create(l.Value),
        OpenApiFloat f => JsonValue.Create(f.Value),
        OpenApiDouble d => JsonValue.Create(d.Value),
        OpenApiBoolean b => JsonValue.Create(b.Value),
        OpenApiDate date => JsonValue.Create(date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        OpenApiDateTime dt => JsonValue.Create(dt.Value.ToString("O", CultureInfo.InvariantCulture)),
        OpenApiPassword p => JsonValue.Create(p.Value),
        OpenApiByte by => JsonValue.Create(Convert.ToBase64String(by.Value ?? [])),
        OpenApiBinary bin => JsonValue.Create(Encoding.UTF8.GetString(bin.Value ?? [])),
        OpenApiArray arr => new JsonArray(arr.Select(ToNode).ToArray()),
        OpenApiObject obj => ToObject(obj),
        _ => null,
    };

    private static JsonObject ToObject(OpenApiObject obj)
    {
        var result = new JsonObject();
        foreach (var (key, value) in obj)
        {
            result[key] = ToNode(value);
        }
        return result;
    }

    /// <summary>Scalar rendering used for parameter examples and enum values.</summary>
    public static string? ToDisplayString(IOpenApiAny? any)
    {
        var node = ToNode(any);
        return node switch
        {
            null => null,
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            _ => node.ToJsonString(),
        };
    }
}
