using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     REPRODUCTIONS — the edits ARN operators made by hand on test, 2026-08-25 morning
///     (the content operator on sydney, the regional operator on cairns-4ca). Ten of eleven
///     bulletins were rejected, edited and re-voiced; the pipeline's own text survives in
///     <c>Script.Data.Sentences</c> while the aired text is <c>Script.Data.Text</c>, so every pair
///     below is a real before/after rather than a constructed example.
///     <para>
///         Each test pins ONE defect the operators fixed. They are written against the deterministic
///         layer only — the LLM is not in the loop here, so a red test means the pipeline handed the
///         writer contradictory or unusable input, which is the part we can fix without touching
///         a prompt.
///     </para>
/// </summary>
public sealed class OperatorEditReproTests
{
    /// <summary>
    ///     ITEM 1 — the weather line contradicts itself. Four of eleven bulletins hit this and the
    ///     operators rewrote every one.
    ///     <para>
    ///         Live input (job 25d0f3bf, sydney): Conditions "Sunny. Mild.", 20C,
    ///         PrecipitationProbability 57%, Alerts ["Heavy rain anticipated"].
    ///         The writer produced "Sunny skies and 20 degrees, with heavy rain anticipated."
    ///         and the operator replaced it with "Showers today. 20 degrees."
    ///     </para>
    ///     <para>
    ///         <see cref="ContentFormatters.FormatWeather" /> already suppresses an alert when the
    ///         condition ALSO mentions rain, to avoid saying the same fact twice. It does not handle
    ///         the inverse — a dry condition beside a wet alert — so both reach the writer with
    ///         nothing to reconcile them, and it reads as a contradiction on air.
    ///     </para>
    /// </summary>
    [Fact]
    public void Item1_A_dry_condition_beside_a_wet_alert_is_reconciled_before_the_writer_sees_it()
    {
        var sydney = new WeatherReport(
            Conditions: "Sunny. Mild.",
            TemperatureC: 20,
            WindDescription: Option<string>.Some("fresh North wind"),
            PrecipitationProbabilityPercent: 57,
            Alerts: ["Heavy rain anticipated"],
            Outlook: Option<string>.None,
            IsForecast: false);

        string rendered = ContentFormatters.FormatWeather(sydney);

        // The provider's own wording must not reach air verbatim: "anticipated" is not radio English,
        // and at 57% probability "heavy rain" overstates it. Both operators wrote "showers".
        rendered.ShouldNotContain("Heavy rain anticipated", Case.Sensitive,
            "the raw provider alert is what the operators rewrote every time it appeared");
    }

    /// <summary>
    ///     ITEM 1, guardrail — the existing suppression must survive the fix. When the condition
    ///     ALREADY says rain, the alert is redundant and must stay suppressed.
    /// </summary>
    [Fact]
    public void Item1_A_wet_condition_still_suppresses_a_duplicate_wet_alert()
    {
        var raining = new WeatherReport(
            Conditions: "Showers.", TemperatureC: 14, WindDescription: Option<string>.None,
            PrecipitationProbabilityPercent: 90, Alerts: ["Heavy rain anticipated"],
            Outlook: Option<string>.None, IsForecast: false);

        string rendered = ContentFormatters.FormatWeather(raining);

        rendered.ShouldNotContain("ALERT", Case.Sensitive,
            "a rain alert beside a rain condition states the same fact twice");
    }

    /// <summary>
    ///     ITEM 1, guardrail — a NON-weather alert is real information and must still reach the
    ///     writer. The fix must narrow to the rain-vs-dry contradiction, not silence alerts.
    /// </summary>
    [Fact]
    public void Item1_An_unrelated_alert_still_reaches_the_writer()
    {
        var windy = new WeatherReport(
            Conditions: "Sunny. Mild.", TemperatureC: 20, WindDescription: Option<string>.None,
            PrecipitationProbabilityPercent: 5, Alerts: ["Damaging wind warning"],
            Outlook: Option<string>.None, IsForecast: false);

        ContentFormatters.FormatWeather(windy)
            .ShouldContain("Damaging wind warning", Case.Sensitive, "only the rain contradiction is in scope");
    }

    /// <summary>
    ///     ITEM 3b — THE REAL DEFECT. ARN report seeing "M1" and "Pacific Mwy" as SEPARATE incident
    ///     and flow entries in the adjust tab. They are one physical road, so the slot is being
    ///     offered the same motorway twice and its impact score is split across two rows.
    ///     <para>
    ///         String similarity is exactly backwards for this problem, which is why these three
    ///         cases belong together. "Pacific Hwy" and "Pacific Mwy" differ by ONE letter and are
    ///         DIFFERENT roads (the A1 surface road and the M1 motorway; dev carries 19 jobs naming
    ///         one and 13 the other). "M1" and "Pacific Mwy" share no characters at all and are the
    ///         SAME road. No edit-distance or token rule can separate those two facts.
    ///     </para>
    /// </summary>
    [Fact]
    public void Item3b_The_alias_table_already_knows_the_designation_when_the_route_code_survives()
    {
        // This is the machinery that already works — for M4/M5/M2/M7/M8, whose bare gazetted names
        // are also in the table. Dev's FlowRoadsJson shows "M5" reaching air correctly today.
        RoadDesignationAliases.Canonicalize("M1 Pacific Mwy").ShouldBe("M1");
        RoadDesignationAliases.Canonicalize("M1 Pacific Motorway").ShouldBe("M1");
    }

    [Fact]
    public void Item3b_But_the_route_code_is_destroyed_before_the_table_ever_sees_it()
    {
        // HERE hands us "M1 / Pacific Mwy". PreferredRoadName drops everything before the slash, so
        // the string that reaches the alias table is "Pacific Mwy" — which is DELIBERATELY absent
        // from it, because bare "Pacific Motorway" is the M3 in Brisbane. The designation is lost
        // not because we lack the rule, but because we discarded the evidence for it.
        string afterStrip = RoadNameFormatter.PreferredRoadName("M1 / Pacific Mwy");

        RoadDesignationAliases.Canonicalize(afterStrip).ShouldBe("M1",
            "the route code HERE supplied is exactly what the alias table needs");
    }

    /// <summary>
    ///     ITEM 3b — the BOUNDARY of what the code fix can reach, and therefore the exact job of the
    ///     per-market road registry.
    ///     <para>
    ///         When HERE supplies the route code we now resolve the designation, so map-derived rows
    ///         converge on "M1" and stop splitting. When a record arrives carrying ONLY the gazetted
    ///         name — an official-feed incident, or free text with no map geometry — nothing in the
    ///         data says it is the M1, and no global table may say so either: bare "Pacific Motorway"
    ///         is the M3 in Brisbane. That is not a defect to fix in code; it is irreducibly local
    ///         knowledge, and it is why the registry is market-scoped.
    ///     </para>
    ///     <para>
    ///         This test pins the boundary so it is deliberate rather than forgotten. It must be
    ///         REVISITED when the registry lands: with a sydney entry mapping "Pacific Mwy" to "M1",
    ///         the two keys below become equal and this assertion should be inverted.
    ///     </para>
    /// </summary>
    [Fact]
    public void Item3b_A_supplied_route_code_resolves_but_a_bare_gazetted_name_cannot()
    {
        // Resolvable: HERE gave us the code, so the curated list can act on it.
        RoadNameFormatter.PreferredRoadName("M1 / Pacific Mwy").ShouldBe("M1");

        // Not resolvable without market knowledge — the registry's job, not the resolver's.
        CorridorAggregator.CanonicalRoadKey("Pacific Mwy")
            .ShouldNotBe(CorridorAggregator.CanonicalRoadKey("M1"),
                "until a market registry says otherwise, a bare gazetted name cannot claim a designation");
    }

    [Fact]
    public void Item3b_Guardrail_two_roads_one_letter_apart_must_stay_separate()
    {
        // The trap any fuzzy rule falls into. The Pacific HIGHWAY is not the Pacific MOTORWAY.
        CorridorAggregator.CanonicalRoadKey("Pacific Hwy")
            .ShouldNotBe(CorridorAggregator.CanonicalRoadKey("Pacific Mwy"),
                "the A1 surface road and the M1 motorway are different roads");
        RoadNameFormatter.IsSameRoad("Pacific Hwy", "Pacific Mwy").ShouldBeFalse();
    }

    /// <summary>
    ///     ITEM 3c — FOUND WHILE SIZING THE ALIAS LIBRARY, AND WORSE THAN THE M1 CASE.
    ///     <para>
    ///         <see cref="CorridorAggregator.CanonicalRoadKey" /> strips a leading route code because
    ///         "M1 Eastern Distributor" and "Eastern Distributor" are one road. That holds while the
    ///         remainder is distinctive. It collapses when the remainder is nothing but the generic
    ///         road type: "M2 Motorway" and "M8 Motorway" both reduce to "Motorway".
    ///     </para>
    ///     <para>
    ///         These are not hypothetical strings. Four days of dev sydney traffic carry
    ///         "M2 Motorway" x55, "M8 Motorway" x15, "M7 Motorway" x14, "M4 Motorway" x8,
    ///         "M5 Motorway" x7, "M12 Motorway" and "M11 Motorway (NorthConnex)" — seven distinct
    ///         motorways landing on ONE grouping key, where <c>CreateCongestionCorridor</c> SUMS
    ///         their delays and lengths and voices the total under a single name.
    ///     </para>
    ///     <para>
    ///         <see cref="RoadDesignationAliases" /> already maps every one of these correctly
    ///         ("M2 Motorway" to "M2"), and its own summary says it is "applied at the road-identity
    ///         boundary (RoadIdentityResolver winners and CorridorAggregator name normalization)".
    ///         CanonicalRoadKey never calls it.
    ///     </para>
    /// </summary>
    [Fact]
    public void Item3c_Distinct_motorways_must_not_collapse_onto_one_grouping_key()
    {
        // Deliberately the codes the alias table does NOT carry. M2/M4/M8 have explicit
        // "M<n> Motorway" entries and resolve correctly; M5, M7, M11 and M12 do not, so the strip
        // erases them and four different motorways land on the key "Motorway". Observed in dev
        // sydney over four days: M7 x14, M5 x7, M11 (NorthConnex) x2, M12 x1.
        string m7 = CorridorAggregator.CanonicalRoadKey("M7 Motorway");
        string m5 = CorridorAggregator.CanonicalRoadKey("M5 Motorway");
        string m12 = CorridorAggregator.CanonicalRoadKey("M12 Motorway");

        m7.ShouldNotBe("Motorway", "the route code was the entire identity and stripping it erased the road");
        m7.ShouldNotBe(m5, "the M7 and the M5 are different motorways");
        m7.ShouldNotBe(m12, "the M7 and the M12 are different motorways");
    }

    /// <summary>
    ///     ITEM 3c, guardrail — the behaviour the strip exists for must survive. When the remainder
    ///     IS distinctive, the code is decoration and the two forms are one road.
    /// </summary>
    [Fact]
    public void Item3c_Guardrail_a_route_code_on_a_distinctive_name_is_still_decoration()
    {
        CorridorAggregator.CanonicalRoadKey("M1 Eastern Distributor")
            .ShouldBe(CorridorAggregator.CanonicalRoadKey("Eastern Distributor"));
    }

    /// <summary>
    ///     ITEM 3 — we discard the road designation Sydney drivers use.
    ///     <para>
    ///         Operators rewrote "the Pacific Motorway at Cowan" to "the M1 at Cowan" twice this
    ///         morning (jobs c4544250 and 591cdaa8). HERE returns motorway names in the form
    ///         "&lt;route code&gt; / &lt;name&gt;", and <c>HereMapAttributesProvider.StripRouteCode</c>
    ///         keeps only the part AFTER the slash — correct for "A28 / Hume Hwy", wrong for a
    ///         motorway whose route code IS its everyday name.
    ///     </para>
    ///     <para>
    ///         Sydney's own priority-roads config already uses the designation form — "M4 Western
    ///         Motorway", "M5 South West Motorway" — so the two sources disagree with each other,
    ///         and the M1 is absent from that list entirely.
    ///     </para>
    /// </summary>
    [Theory]
    [InlineData("M1 / Pacific Mwy", "M1")]
    [InlineData("M4 / Western Mwy", "M4")]
    [InlineData("A28 / Hume Hwy", "Hume Hwy")]   // unchanged: an alphanumeric route code is not spoken
    [InlineData("1 / Kennedy Hwy", "Kennedy Hwy")] // unchanged: a bare number is not spoken
    public void Item3_A_motorway_designation_survives_while_other_route_codes_are_still_dropped(
        string hereName, string expectedToBeVoiced)
    {
        // RoadNameFormatter is the shared seam; StripRouteCode lives in Infrastructure and is
        // exercised through it so the rule has one home rather than two.
        string voiced = RoadNameFormatter.PreferredRoadName(hereName);

        voiced.ShouldContain(expectedToBeVoiced);
    }
}
