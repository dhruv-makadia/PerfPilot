using System.Text.Json.Nodes;
using Microsoft.OpenApi.Models;

namespace PerfPilot.Infrastructure.OpenApi;

/// <summary>Produces a compact, recursion-safe JSON view of a schema for display in the endpoint browser.</summary>
public static class SchemaSummarizer
{
    private const int MaxDepth = 5;

    public static JsonNode? Summarize(OpenApiSchema? schema) =>
        Summarize(schema, 0, new HashSet<OpenApiSchema>(ReferenceEqualityComparer.Instance));

    private static JsonNode? Summarize(OpenApiSchema? schema, int depth, HashSet<OpenApiSchema> ancestors)
    {
        if (schema is null)
        {
            return null;
        }

        var node = new JsonObject();
        if (schema.Reference?.Id is { } refId)
        {
            node["$ref"] = refId;
        }
        if (!ancestors.Add(schema))
        {
            node["recursive"] = true;
            return node;
        }

        try
        {
            if (schema.Type is not null)
            {
                node["type"] = schema.Type;
            }
            if (schema.Format is not null)
            {
                node["format"] = schema.Format;
            }
            if (schema.Nullable)
            {
                node["nullable"] = true;
            }
            if (!string.IsNullOrWhiteSpace(schema.Description))
            {
                node["description"] = schema.Description;
            }
            if (schema.Enum is { Count: > 0 })
            {
                node["enum"] = new JsonArray(schema.Enum.Select(OpenApiAnyConverter.ToNode).ToArray());
            }
            if (schema.Example is not null)
            {
                node["example"] = OpenApiAnyConverter.ToNode(schema.Example);
            }
            if (depth >= MaxDepth)
            {
                if (schema.Properties.Count > 0 || schema.Items is not null)
                {
                    node["truncated"] = true;
                }
                return node;
            }
            if (schema.Required is { Count: > 0 })
            {
                node["required"] = new JsonArray(schema.Required.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray());
            }
            if (schema.Properties.Count > 0)
            {
                var props = new JsonObject();
                foreach (var (name, prop) in schema.Properties)
                {
                    props[name] = Summarize(prop, depth + 1, ancestors);
                }
                node["properties"] = props;
            }
            if (schema.Items is not null)
            {
                node["items"] = Summarize(schema.Items, depth + 1, ancestors);
            }
            if (schema.AdditionalProperties is not null)
            {
                node["additionalProperties"] = Summarize(schema.AdditionalProperties, depth + 1, ancestors);
            }
            AddComposite(node, "allOf", schema.AllOf, depth, ancestors);
            AddComposite(node, "oneOf", schema.OneOf, depth, ancestors);
            AddComposite(node, "anyOf", schema.AnyOf, depth, ancestors);
            return node;
        }
        finally
        {
            ancestors.Remove(schema);
        }
    }

    private static void AddComposite(JsonObject node, string name, IList<OpenApiSchema>? schemas, int depth, HashSet<OpenApiSchema> ancestors)
    {
        if (schemas is { Count: > 0 })
        {
            node[name] = new JsonArray(schemas.Select(s => Summarize(s, depth + 1, ancestors)).ToArray());
        }
    }
}
