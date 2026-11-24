using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.AzureOpenAI;

/// <summary>
///     Context we already held and never handed the writer, plus the register it is voiced in.
///     <para>
///         ARN, 2026-08-25, reviewing a panel of generated reads: "they are currently suffering
///         from repetition, and do not sound aussie when they should... we should never say 500
///         metres, we should say half a kay."
///     </para>
///     <para>
///         Both halves of that are covered here. The register is <c>FormatExtentLength</c>. The
///         repetition is largely a data problem: several roadworks items in one read collapse into
///         the same sentence because every one of them arrives carrying nothing but the word
///         "roadworks", while the authority's own headline - populated on 31% of live dev corridors
///         - was parsed, stored, and never emitted.
///     </para>
/// </summary>
public sealed class VoicedContextEvidenceTests
{
    private static BroadcastWithTraffic ContextFor(TrafficCorridor corridor) => new()
    {
        TargetDurationSeconds = 30,
        JobId = "voiced-context",
        Location = "Brisbane",
        Traffic = "ignored fallback",
        Corridors = [corridor],
        Incidents = [.. corridor.Incidents],
        Content = new BulletinContentSelection(Traffic: true, Weather: false, Fuel: false)
    };

    private static TrafficCorridor Works(
        double lengthMeters,
        Option<string> headline = default,
        Option<string> traversability = default)
    {
        var incident = new TrafficIncident(
            "here-1", "Road construction", 3, "Gympie Road", "construction",
            JunctionTraversability: traversability);

        return new TrafficCorridor(
            "Gympie Road", 3, 3, lengthMeters, 40, "construction", "roadworks", [incident],
            Suburb: Option<string>.Some("Chermside"),
            OfficialHeadline: headline);
    }

    private static string? ValueOf(TrafficCorridor corridor, string field) =>
        TrafficNarrativeSummarizer.BuildEvidenceUnits(ContextFor(corridor))
            .FirstOrDefault(u => u.Field == field)?.Value;

    // ── Register: kays, never metres ────────────────────────────────────

    [Theory]
    [InlineData(476.0, "about half a kay")]   // the adelaide Salisbury Highway closure
    [InlineData(515.0, "about half a kay")]   // the cairns Kennedy Highway hazard
    [InlineData(1_000.0, "about a kay")]
    [InlineData(1_500.0, "about one and a half kays")]
    [InlineData(7_000.0, "about seven kays")]
    public void ExtentIsVoicedInKays(double meters, string expected) =>
        ValueOf(Works(meters), "extentLength").ShouldBe(expected);

    [Fact]
    public void NoLengthEverReachesTheWriterAsAMetreCountOrANumeral()
    {
        // Two defects, one rule. First: 476m rendered as "500 metres" and aired as "a 500-metre
        // road closure". Then the kays fix itself emitted "about {n:0.#} kays", which aired as
        // "roadworks span about 1.5 kays" — a numeral in a field whose only purpose is to be
        // spoken, exactly the "$2.23" defect in another costume. The value is read aloud, so it
        // must contain no digits at all.
        foreach (double meters in new[]
                 { 300.0, 476.0, 515.0, 999.0, 1_240.0, 1_500.0, 1_600.0, 2_600.0, 7_000.0, 23_400.0, 31_000.0 })
        {
            string? extent = ValueOf(Works(meters), "extentLength");
            extent.ShouldNotBeNull();
            extent.ShouldNotContain("metre", Case.Insensitive);
            extent.ShouldNotContain("km", Case.Insensitive);
            extent.ShouldAllBe(c => !char.IsDigit(c), $"\"{extent}\" is read aloud and must be spelled");
        }
    }

    [Fact]
    public void ShortWorksStaySayablyVague()
    {
        // Source shapes are HERE polylines, so sub-half-kay precision was always spurious.
        ValueOf(Works(320.0), "extentLength").ShouldBe("a short stretch");
    }

    // ── The authority's own reporting ───────────────────────────────────

    [Fact]
    public void WorksWindowIsLiftedFromTheOfficialHeadline()
    {
        // Verbatim from live dev. The window exists NOWHERE else in our payload, and without it the
        // writer can only manage "roadworks are slowing Gympie Road" - which is what shipped.
        var corridor = Works(800.0, Option<string>.Some(
            "Roadworks: At Gympie and Webster Road intersection, 9.30pm to 4am "
            + "between Murphy Road and Ledbury Street"));

        ValueOf(corridor, "worksWindow").ShouldBe("9.30pm to 4am");
    }

    [Fact]
    public void TheHeadlineItselfIsNeverHandedOverAsAPhrasing()
    {
        // The "40 through the works" defect: a rendered phrase in an evidence value gets read back
        // almost verbatim. Only closed-form data may be lifted out of the prose.
        var corridor = Works(800.0, Option<string>.Some(
            "Roadworks: Nightwork asphalt resurfacing on the Ipswich Motorway "
            + "between the Warrego Highway and Logan Motorway"));

        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(ContextFor(corridor));

        units.ShouldNotContain(u => u.Value.Contains("Nightwork asphalt resurfacing"),
            "the headline is prose and must never become an evidence value");
    }

    [Fact]
    public void AHeadlineWithoutAWindowEmitsNothing()
    {
        ValueOf(Works(800.0, Option<string>.Some("Roadworks: Bremer River bridge Works")), "worksWindow")
            .ShouldBeNull();
    }

    [Fact]
    public void ACrossStreetNumberIsNotMistakenForATime()
    {
        // Both halves must carry a meridiem, or "between 9 and 4" style copy would become a window.
        ValueOf(Works(800.0, Option<string>.Some("Roadworks: between Exit 9 and Exit 4")), "worksWindow")
            .ShouldBeNull();
    }


    // ── The invariant these must not break ──────────────────────────────

    [Fact]
    public void EveryNewFactAlsoAppearsInTheProseTheReviewerSees()
    {
        // A citable unit with no prose line lets the writer voice something the operator and the
        // reviewer never saw. That divergence is what EvidenceSourceParityTests exists to catch,
        // so the works window is yielded from the SHARED AdditionalFactLines, not straight to
        // evidence.
        var corridor = Works(
            800.0,
            Option<string>.Some("Roadworks: works 9.30pm to 4am between Murphy Road and Ledbury Street"));
        var context = ContextFor(corridor);

        string draftPayload = PromptLibrary.RenderEvidenceUnits(
            TrafficNarrativeSummarizer.BuildEvidenceUnits(context));
        string reviewerData = BulletinDataComposer.ComposeAllData(context);

        reviewerData.ShouldBe(draftPayload);
        draftPayload.ShouldContain("9.30pm to 4am");
    }
}
