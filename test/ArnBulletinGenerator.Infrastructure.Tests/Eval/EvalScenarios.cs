namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     The 10-scenario permutation matrix: size (large/small radius) x density (busy/quiet market)
///     x content (T / T+W / T+F / T+W+F). Shared by the frozen-fetch prompt-iteration harness and
///     the pipeline experiment harness so every eval mode covers the identical conditioning space
///     and results stay comparable across sessions.
/// </summary>
internal static class EvalScenarios
{
    public static readonly (string Label, BulletinScenario Scenario)[] Matrix =
    [
        ("Large/Busy — Traffic only",
            new("sydney-busy", "Sydney", -33.8688, 151.2093, 30, 30, MaxCorridors: 12)),

        ("Large/Busy — Traffic+Weather",
            new("melbourne-busy-w", "Melbourne", -37.8136, 144.9631, 30, 30, MaxCorridors: 12, Weather: true)),

        ("Large/Busy — Traffic+Fuel",
            new("brisbane-busy-f", "Brisbane", -27.4698, 153.0251, 28, 30, MaxCorridors: 12, Fuel: true)),

        ("Large/Busy — Traffic+Weather+Fuel",
            new("goldcoast-busy-wf", "Gold Coast", -28.0167, 153.4000, 25, 30, MaxCorridors: 12, Weather: true, Fuel: true)),

        ("Large/Quiet — Traffic only",
            new("canberra-quiet", "Canberra", -35.2809, 149.1300, 28, 30, MaxCorridors: 5)),

        ("Large/Quiet — Traffic+Weather+Fuel",
            new("perth-quiet-wf", "Perth", -31.9505, 115.8605, 28, 30, MaxCorridors: 5, Weather: true, Fuel: true)),

        ("Small/Busy — Traffic only",
            new("sydney-cbd-busy", "Sydney CBD", -33.8688, 151.2093, 6, 30, MaxCorridors: 10)),

        ("Small/Busy — Traffic+Weather",
            new("melbourne-cbd-busy-w", "Melbourne CBD", -37.8136, 144.9631, 6, 30, MaxCorridors: 10, Weather: true)),

        ("Small/Quiet — Traffic only",
            new("newcastle-quiet", "Newcastle", -32.9283, 151.7817, 5, 30, MaxCorridors: 5)),

        ("Small/Quiet — Traffic+Weather+Fuel",
            new("scottsdale-quiet-wf", "Scottsdale", -41.1667, 147.5167, 8, 30, MaxCorridors: 5, Weather: true, Fuel: true))
    ];
}
