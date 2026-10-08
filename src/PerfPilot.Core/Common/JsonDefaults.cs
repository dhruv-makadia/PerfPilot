using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PerfPilot.Core.Common;

public static class JsonDefaults
{
    /// <summary>Options used for persisted JSON columns and API payloads.</summary>
    public static readonly JsonSerializerOptions Options = Create(indented: false);

    /// <summary>Indented variant used when emitting human-readable JSON (scripts, reports).</summary>
    public static readonly JsonSerializerOptions Indented = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = indented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
