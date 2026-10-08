using PerfPilot.Core.Contracts;
using PerfPilot.Core.Domain;

namespace PerfPilot.Core.Services;

/// <summary>Built-in load profiles. Each fills editable k6 stages in the scenario builder.</summary>
public static class LoadProfilePresets
{
    public static IReadOnlyList<LoadPresetDto> All { get; } =
    [
        new(LoadPreset.Smoke, "Minimal load to verify the scenario works: 1 VU for 1 minute.",
            [new LoadStage { Duration = "1m", Target = 1 }], 1, 2),

        new(LoadPreset.Load, "Typical expected traffic: ramp to 20 VUs, hold 5 minutes, ramp down.",
            [
                new LoadStage { Duration = "2m", Target = 20 },
                new LoadStage { Duration = "5m", Target = 20 },
                new LoadStage { Duration = "1m", Target = 0 },
            ], 1, 3),

        new(LoadPreset.Stress, "Beyond normal load in steps to find the breaking point: 50 then 100 VUs.",
            [
                new LoadStage { Duration = "2m", Target = 50 },
                new LoadStage { Duration = "5m", Target = 50 },
                new LoadStage { Duration = "2m", Target = 100 },
                new LoadStage { Duration = "5m", Target = 100 },
                new LoadStage { Duration = "2m", Target = 0 },
            ], 1, 2),

        new(LoadPreset.Spike, "Sudden surge from 5 to 100 VUs and back, to test elasticity and recovery.",
            [
                new LoadStage { Duration = "30s", Target = 5 },
                new LoadStage { Duration = "30s", Target = 100 },
                new LoadStage { Duration = "1m", Target = 100 },
                new LoadStage { Duration = "30s", Target = 5 },
                new LoadStage { Duration = "1m", Target = 5 },
                new LoadStage { Duration = "30s", Target = 0 },
            ], 0.5, 1.5),

        new(LoadPreset.Soak, "Moderate load for an extended period to surface leaks: 30 VUs for 1 hour.",
            [
                new LoadStage { Duration = "5m", Target = 30 },
                new LoadStage { Duration = "1h", Target = 30 },
                new LoadStage { Duration = "5m", Target = 0 },
            ], 1, 3),
    ];

    public static LoadPresetDto? Get(LoadPreset preset) => All.FirstOrDefault(p => p.Preset == preset);

    public static TimeSpan TotalDuration(IEnumerable<LoadStage> stages) =>
        stages.Aggregate(TimeSpan.Zero, (sum, s) => sum + (DurationParser.TryParse(s.Duration, out var d) ? d : TimeSpan.Zero));

    public static int MaxTarget(IEnumerable<LoadStage> stages) => stages.Select(s => s.Target).DefaultIfEmpty(0).Max();
}
