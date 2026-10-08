using System.Text.Json.Nodes;
using Microsoft.OpenApi.Models;

namespace PerfPilot.Infrastructure.OpenApi;

/// <summary>Builds a sample JSON value from an OpenAPI schema, preferring examples, defaults and enums.</summary>
public static class SchemaSampleGenerator
{
    private const int MaxDepth = 6;

    public static JsonNode? Generate(OpenApiSchema? schema) => Generate(schema, 0, new HashSet<OpenApiSchema>(ReferenceEqualityComparer.Instance));

    private static JsonNode? Generate(OpenApiSchema? schema, int depth, HashSet<OpenApiSchema> ancestors)
    {
        if (schema is null || depth > MaxDepth)
        {
            return null;
        }
        if (schema.Example is not null)
        {
            return OpenApiAnyConverter.ToNode(schema.Example);
        }
        if (schema.Default is not null)
        {
            return OpenApiAnyConverter.ToNode(schema.Default);
        }
        if (schema.Enum is { Count: > 0 })
        {
            return OpenApiAnyConverter.ToNode(schema.Enum[0]);
        }
        if (!ancestors.Add(schema))
        {
            // Recursive reference (e.g. a tree node containing children of the same type).
            return schema.Type == "array" ? new JsonArray() : schema.Type is "object" or null ? new JsonObject() : null;
        }

        try
        {
            if (schema.AllOf is { Count: > 0 })
            {
                var merged = new JsonObject();
                foreach (var part in schema.AllOf)
                {
                    if (Generate(part, depth + 1, ancestors) is JsonObject obj)
                    {
                        foreach (var (key, value) in obj.ToList())
                        {
                            obj.Remove(key);
                            merged[key] = value;
                        }
                    }
                }
                AddProperties(schema, merged, depth, ancestors);
                return merged;
            }
            if (schema.OneOf is { Count: > 0 })
            {
                return Generate(schema.OneOf[0], depth + 1, ancestors);
            }
            if (schema.AnyOf is { Count: > 0 })
            {
                return Generate(schema.AnyOf[0], depth + 1, ancestors);
            }

            switch (schema.Type)
            {
                case "array":
                    var item = Generate(schema.Items, depth + 1, ancestors);
                    return item is null ? new JsonArray() : new JsonArray(item);
                case "string":
                    return JsonValue.Create(StringSample(schema.Format));
                case "integer":
                    return JsonValue.Create(schema.Minimum is { } min ? (long)Math.Ceiling(min) : 0L);
                case "number":
                    return JsonValue.Create(schema.Minimum is { } nmin ? (double)nmin : 0.0);
                case "boolean":
                    return JsonValue.Create(true);
            }

            if (schema.Type == "object" || schema.Properties.Count > 0 || schema.AdditionalProperties is not null)
            {
                var obj = new JsonObject();
                AddProperties(schema, obj, depth, ancestors);
                if (schema.Properties.Count == 0 && schema.AdditionalProperties is { } additional)
                {
                    obj["key"] = Generate(additional, depth + 1, ancestors);
                }
                return obj;
            }
            return null;
        }
        finally
        {
            ancestors.Remove(schema);
        }
    }

    private static void AddProperties(OpenApiSchema schema, JsonObject target, int depth, HashSet<OpenApiSchema> ancestors)
    {
        foreach (var (name, property) in schema.Properties)
        {
            if (property.ReadOnly)
            {
                continue;
            }
            target[name] = Generate(property, depth + 1, ancestors);
        }
    }

    private static string StringSample(string? format) => format switch
    {
        "date-time" => "2025-01-01T00:00:00Z",
        "date" => "2025-01-01",
        "time" => "12:00:00",
        "email" => "user@example.com",
        "uuid" => "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "uri" or "url" => "https://example.com",
        "hostname" => "example.com",
        "ipv4" => "192.168.0.1",
        "ipv6" => "::1",
        "byte" => "U3dhZ2dlciByb2Nrcw==",
        "password" => "P@ssw0rd!",
        _ => "string",
    };
}
