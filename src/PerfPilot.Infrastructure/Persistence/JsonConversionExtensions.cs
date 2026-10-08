using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PerfPilot.Core.Common;

namespace PerfPilot.Infrastructure.Persistence;

internal static class JsonConversionExtensions
{
    /// <summary>Stores a property as a JSON nvarchar(max) column, with value-based change tracking.</summary>
    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> builder) where T : class, new()
    {
        var comparer = new ValueComparer<T>(
            (a, b) => Serialize(a) == Serialize(b),
            v => Serialize(v).GetHashCode(),
            v => Deserialize<T>(Serialize(v)));

        builder.HasConversion(v => Serialize(v), v => Deserialize<T>(v), comparer)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        return builder;
    }

    private static string Serialize<T>(T? value) => JsonSerializer.Serialize(value, JsonDefaults.Options);

    private static T Deserialize<T>(string? json) where T : new() =>
        string.IsNullOrEmpty(json) ? new T() : JsonSerializer.Deserialize<T>(json, JsonDefaults.Options) ?? new T();
}
