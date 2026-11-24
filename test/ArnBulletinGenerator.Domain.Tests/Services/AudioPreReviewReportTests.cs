using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     The operator-facing pre-review message and, critically, what an operator's
///     verified-pronunciation clearance is allowed to mute: how a word SOUNDS, never whether it is
///     in the audio at all. Prod job c2894ffb109f4c218c73e274e43665b1 (2026-08-18, cairns-4ca)
///     aired a bulletin cut off mid-"Manoora" and told the operator only "1 suspect
///     pronunciation(s) — hole", because a clearance about Manoora's pronunciation muted the
///     evidence that the word never aired.
/// </summary>
public sealed class AudioPreReviewReportTests
{
    [Fact]
    public void For_VerifiedClearanceOnASuspect_MutesIt()
    {
        var review = new AudioPreReview.Result(0.75, ["Botany"], [], []);

        var report = AudioPreReviewReport.For(review, ["botany"]);

        report.OutstandingSuspects.ShouldBeEmpty();
    }

    [Fact]
    public void For_VerifiedClearanceOnAnUnheardTailWord_StillReportsTheCutOffRead()
    {
        var review = AudioPreReview.Compare(
            "Right now showery and twenty degrees Unleaded's a dollar ninety-eight in Manoora,",
            "Right now, Showery and 20 degrees. Unleaded's $1.98 in");

        var report = AudioPreReviewReport.For(review, ["Manoora"]);

        report.UnheardTailWords.ShouldBe(["Manoora"]);
        report.Message.ShouldContain("cut off");
        report.Message.ShouldContain("Manoora");
        report.IsWarning.ShouldBeTrue();
    }

    [Fact]
    public void For_NothingOutstanding_ProducesTheCleanMessage()
    {
        var review = new AudioPreReview.Result(1.0, [], [], []);

        var report = AudioPreReviewReport.For(review, []);

        // Asserted in parts: the percentage format inserts a culture-dependent space before '%'
        // (the deployed app logs "100 %", a US-culture host logs "100%"), so pinning the whole
        // string would make this test pass or fail on the host's culture rather than on behaviour.
        report.Message.ShouldStartWith("Audio pre-review: transcript matches the script (");
        report.Message.ShouldEndWith(" of significant words heard).");
        report.Message.ShouldNotContain("Listen before approving");
        report.IsWarning.ShouldBeFalse();
    }

    [Fact]
    public void For_UnheardTail_DoesNotAlsoReportItAsASuspectPronunciation()
    {
        var review = AudioPreReview.Compare(
            "Right now showery and twenty degrees Unleaded's a dollar ninety-eight in Manoora,",
            "Right now, Showery and 20 degrees. Unleaded's $1.98 in");

        var report = AudioPreReviewReport.For(review, []);

        report.OutstandingSuspects.ShouldBeEmpty();
        report.Message.ShouldNotContain("suspect pronunciation");
        report.Message.ShouldContain("cut off");
    }
}
