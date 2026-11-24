using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     The contract: an operator-driven LLM pass over an already-composed bulletin must see the
///     traffic body and nothing else. Bookend lines and filler statements are ARN editorial content
///     appended verbatim after every LLM stage during generation, so they carry no traffic evidence
///     and cannot survive a grounding review — peeling them off is what keeps a rewrite of a
///     bookended or filler-topped bulletin possible at all.
/// </summary>
public class StationLineSplitterTests
{
    private const string Body =
        "James Street's at a standstill, with Toogood Road also stopped solid. It's sunny with twenty-six degrees.";

    private const string Council =
        "Roadworks and changed traffic conditions are updated weekly on the Cairns Regional Council website.";

    private const string Promo =
        "Download the iHeartRadio app to take us anywhere, search KIIS or 4CA Cairns.";

    [Fact]
    public void Split_PeelsTheFillerTailAndLeavesTheTrafficBody()
    {
        var split = StationLineSplitter.Split($"{Body} {Council} {Promo}", [Council, Promo]);

        split.Body.ShouldBe(Body);
        split.Leading.ShouldBeEmpty();
        split.Trailing.ShouldBe([Council, Promo]);
        split.PreservedLineCount.ShouldBe(2);
    }

    [Fact]
    public void Split_PeelsBothEndsWhenAStationOpensAndClosesWithFixedLines()
    {
        const string intro = "Traffic across Cairns, brought to you by Bob's Tyres.";
        const string outro = "That's your traffic, back to the music.";

        var split = StationLineSplitter.Split($"{intro} {Body} {Promo} {outro}", [intro, outro, Promo]);

        split.Leading.ShouldBe([intro]);
        split.Body.ShouldBe(Body);
        split.Trailing.ShouldBe([Promo, outro]);
    }

    [Fact]
    public void Split_MatchesATokenisedBookendTemplateOnItsLiteralFragments()
    {
        // The job records a bookend id and version, never the token-substituted text the schedule
        // aired. Matching on the literal fragments around {city} is what lets the trigger peel it
        // without reconstructing the station's token overrides.
        var split = StationLineSplitter.Split(
            $"Looking over Cairns today. {Body}",
            ["Looking over {city} today"]);

        split.Leading.ShouldBe(["Looking over Cairns today."]);
        split.Body.ShouldBe(Body);
    }

    [Fact]
    public void Split_MatchesAcrossRePunctuationAndRecasing()
    {
        // The composer punctuates the join, and an operator may have edited the draft's casing.
        // Neither is a content change, so neither may un-protect a station line.
        var split = StationLineSplitter.Split(
            $"{Body} DOWNLOAD THE IHEARTRADIO APP TO TAKE US ANYWHERE — SEARCH KIIS OR 4CA CAIRNS!",
            [Promo]);

        split.Trailing.Count.ShouldBe(1);
        split.Body.ShouldBe(Body);
    }

    [Fact]
    public void Split_LeavesAStationLineQuotedMidBodyAlone()
    {
        // Only the head and tail are station positions — BulletinBookendComposer never inserts
        // anywhere else. A line that happens to appear mid-script is the writer's and stays editable.
        var split = StationLineSplitter.Split(
            $"James Street's at a standstill. {Council} It's sunny with twenty-six degrees.",
            [Council]);

        split.Leading.ShouldBeEmpty();
        split.Trailing.ShouldBeEmpty();
        split.Body.ShouldContain("Cairns Regional Council");
    }

    [Fact]
    public void Split_WithNoStationLines_IsTheWholeScript()
    {
        var split = StationLineSplitter.Split(Body, []);

        split.Body.ShouldBe(Body);
        split.PreservedLineCount.ShouldBe(0);
    }

    [Fact]
    public void Split_WhenEverySentenceIsAStationLine_KeepsTheScriptAsTheBody()
    {
        // Nothing is the writer's, so there is nothing to protect either — the caller's own
        // "there is no script to rewrite" check stays the single source of that refusal rather
        // than this returning an empty body and a silently-truncated recompose.
        var split = StationLineSplitter.Split($"{Council} {Promo}", [Council, Promo]);

        split.Body.ShouldBe($"{Council} {Promo}");
        split.PreservedLineCount.ShouldBe(0);
    }

    [Fact]
    public void Recompose_PutsARewrittenBodyBackBetweenTheUntouchedStationLines()
    {
        var split = StationLineSplitter.Split($"{Body} {Council} {Promo}", [Council, Promo]);

        split.Recompose("James Street is closed.")
            .ShouldBe($"James Street is closed. {Council} {Promo}");
    }

    [Fact]
    public void Recompose_PunctuatesTheJoinSoTheSynthesiserHearsAPhraseBoundary()
    {
        var split = StationLineSplitter.Split($"{Body} {Promo}", [Promo]);

        split.Recompose("James Street is closed").ShouldBe($"James Street is closed. {Promo}");
    }
}
