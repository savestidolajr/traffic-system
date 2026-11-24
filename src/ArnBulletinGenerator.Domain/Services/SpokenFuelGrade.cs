namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The fuel grade as a presenter says it. HERE labels the grade "Unleaded 91"; no Australian
///     bulletin voices the octane, because it is inventory vocabulary rather than something a driver
///     is told.
///     <para>
///         Single-sourced after the same defect aired from a THIRD path. It was fixed on
///         2026-08-25 in <see cref="ContentFormatters" /> (the prose the writer reads) and in the
///         evidence-unit builder (the structured facts it cites), but
///         <see cref="FixtureLineComposer" /> — which splices a missing weather/fuel line in the
///         Application layer, after both of those have run — kept its own copy and duly aired
///         "Unleaded 91 is a dollar ninety-five in Chermside West" on dev brisbane
///         <c>e91805be</c> the same day. Three hand-copied conditionals is how the incident-type
///         table drifted too; one function is the fix for both.
///     </para>
/// </summary>
public static class SpokenFuelGrade
{
    /// <summary>
    ///     "Unleaded 91" → "unleaded"; every other grade lower-cased as-is ("Diesel" → "diesel").
    ///     Prefix-matched rather than exact so a re-badged grade ("Unleaded 95") cannot reopen this.
    /// </summary>
    public static string Of(string? fuelType)
    {
        string trimmed = (fuelType ?? string.Empty).Trim();
        return trimmed.StartsWith("Unleaded", StringComparison.OrdinalIgnoreCase)
            ? "unleaded"
            : trimmed.ToLowerInvariant();
    }
}
