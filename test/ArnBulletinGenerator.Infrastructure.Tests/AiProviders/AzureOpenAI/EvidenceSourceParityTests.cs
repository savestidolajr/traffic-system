using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Domain.Services.Detectors;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.AzureOpenAI;

/// <summary>
///     Guards the single-source-of-truth rule: every role that judges a script must be shown the
///     SAME facts the writer was given.
///     <para>
///         Observed live 2026-08-20 on Heathcote Road. The draft held
///         <c>location:0 = At New Illawarra Road</c>, <c>location:1 = At Macarthur Drive</c> and
///         <c>detail = road closure and roadworks</c>, so "Heathcote Road's closed westbound in
///         Holsworthy at New Illawarra Road and Macarthur Drive" was fully grounded. The reviewer
///         was handed a separately composed prose view that capped the anchors at one and dropped
///         the closure, so it returned FALSE-CLOSURE and "Macarthur Drive fabricated". Neither
///         agent was wrong; the two inputs disagreed. These tests fail if that split returns.
///     </para>
/// </summary>
public sealed class EvidenceSourceParityTests
{
    private static BroadcastWithTraffic HeathcoteContext()
    {
        var incident = new TrafficIncident(
            "here-heathcote", "Closed", 4, "Heathcote Road", "roadClosure",
            Direction: Option<string>.Some("westbound"));

        var corridor = new TrafficCorridor(
            "Heathcote Road", 4, 3, 1800, 45, "roadClosure",
            "road closure and roadworks", [incident],
            Suburb: Option<string>.Some("Holsworthy"),
            Direction: Option<string>.Some("westbound"),
            FlowDelayMinutes: Option<int>.Some(14));

        return new BroadcastWithTraffic
        {
            TargetDurationSeconds = 22,
            JobId = "source-parity",
            Location = "Sydney",
            Traffic = "ignored fallback",
            Corridors = [corridor],
            Incidents = [incident],
            Content = new BulletinContentSelection(Traffic: true, Weather: false, Fuel: false)
        };
    }

    [Fact]
    public void ReviewerData_IsByteIdenticalToTheDraftPayload()
    {
        var context = HeathcoteContext();

        // What the draft is grounded in.
        string draftPayload = PromptLibrary.RenderEvidenceUnits(
            TrafficNarrativeSummarizer.BuildEvidenceUnits(context));

        // What review/rewrite are told the bulletin "may assert only facts present here".
        string reviewerData = BulletinDataComposer.ComposeAllData(context);

        reviewerData.ShouldBe(draftPayload);
    }

    [Fact]
    public void ReviewerData_CarriesEveryAnchorAndTheClosure_SoAGroundedReadCannotReadAsFabricated()
    {
        string reviewerData = BulletinDataComposer.ComposeAllData(HeathcoteContext());

        // The two specific facts the prose view withheld, which produced the false objections.
        reviewerData.ShouldContain("road closure and roadworks");
        reviewerData.ShouldContain("westbound");
        reviewerData.ShouldContain("Holsworthy");
    }

    [Fact]
    public void ReviewerData_StillHonoursContentGating()
    {
        // Parity must not smuggle sources past the station's content selection: a traffic-only
        // station must not have weather quietly appear in the reviewer's allowed-facts block.
        var context = HeathcoteContext() with
        {
            Content = new BulletinContentSelection(Traffic: true, Weather: false, Fuel: false)
        };

        string reviewerData = BulletinDataComposer.ComposeAllData(context);

        reviewerData.ShouldNotContain("weather:condition");
        reviewerData.ShouldNotContain("fuel:");
    }

    [Fact]
    public void DescriptionAssertingAClosureTheTypedDataDenies_IsNotOfferedToTheWriter()
    {
        // The second half of the Heathcote Road defect: HERE returned type=construction with
        // roadClosed=false, yet Description read "road closure and roadworks". The writer voiced the
        // closure from the description and FalseClosureDetector rejected the read for it.
        var incident = new TrafficIncident(
            "here-works", "Closed", 2, "Heathcote Road", "construction");
        var corridor = new TrafficCorridor(
            "Heathcote Road", 3, 3, 1800, 30, "construction",
            "road closure and roadworks", [incident],
            Suburb: Option<string>.Some("Holsworthy"));
        var context = HeathcoteContext() with
        {
            Corridors = [corridor],
            Incidents = [incident],
        };

        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(context);

        IncidentTypeClassifier.DataMarksClosure(corridor).ShouldBeFalse();
        units.ShouldNotContain(u => u.Field == "detail" && u.Value.Contains("closure"));

        // And the detector must agree there is nothing to flag, because nothing offered a closure.
        string grounded = "Heathcote Road's got roadworks at Holsworthy.";
        FalseClosureDetector.Detect(grounded, context).HasFalseClosure.ShouldBeFalse();
    }

    [Fact]
    public void GenuineClosure_IsStillOfferedAndStillVoiceable()
    {
        // The suppression must not silence a real closure: typed as roadClosure, so it stands.
        var incident = new TrafficIncident(
            "here-shut", "Closed", 4, "Heathcote Road", "roadClosure");
        var corridor = new TrafficCorridor(
            "Heathcote Road", 4, 3, 1800, 45, "roadClosure",
            "road closure and roadworks", [incident],
            Suburb: Option<string>.Some("Holsworthy"));
        var context = HeathcoteContext() with
        {
            Corridors = [corridor],
            Incidents = [incident],
        };

        IncidentTypeClassifier.DataMarksClosure(corridor).ShouldBeTrue();
        TrafficNarrativeSummarizer.BuildEvidenceUnits(context)
            .ShouldContain(u => u.Field == "detail" && u.Value.Contains("closure"));
    }
}
