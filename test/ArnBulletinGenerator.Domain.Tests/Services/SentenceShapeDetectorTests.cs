using ArnBulletinGenerator.Domain.Services.Detectors;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class SentenceShapeDetectorTests
{
    [Fact]
    public void Flags_the_live_repeated_slower_run_phrase()
    {
        const string script =
            "Now looking over Sydney. Victoria Road is a slower run, with delays adding about four minutes. " +
            "Southern Cross Drive has a slower run, adding about three minutes. There's another brief delay on " +
            "Foreshore Road. Clear at the M4 Service Centre and on the M5 South Western Motorway. Partly cloudy, " +
            "15 degrees. That's KIIS traffic and weather together, powered by";

        var result = SentenceShapeDetector.Detect(script);

        result.HasIssue.ShouldBeTrue();
        result.Feedback.ShouldContain("a slower run");
    }

    [Theory]
    [InlineData("The M1 is running well. The M2 is running well.", "is running well")]
    [InlineData("Parramatta Road is a touch slower. Victoria Road is a touch slower.", "a touch slower")]
    public void Flags_repeated_three_to_five_word_state_phrases(string script, string phrase)
    {
        var result = SentenceShapeDetector.Detect(script);

        result.HasIssue.ShouldBeTrue();
        result.Feedback.ShouldContain(phrase);
    }

    [Fact]
    public void Does_not_flag_the_standard_delay_construction_when_the_number_changes()
    {
        const string script =
            "Victoria Road has delays adding about four minutes. Southern Cross Drive has delays adding about three minutes.";

        SentenceShapeDetector.Detect(script).HasIssue.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_flag_a_repeated_road_name()
    {
        const string script = "Queues are building on the M5 near the tunnel. Clear again on the M5 after the tunnel.";

        SentenceShapeDetector.Detect(script).HasIssue.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_flag_a_correctly_varied_read()
    {
        const string script =
            "Now looking over Sydney. Victoria Road is backed up, adding about five minutes. There is also a slower " +
            "run on Victoria Road South and Southern Cross Drive. Foreshore Road and Pyrmont Bridge Road have " +
            "slight queues. Eastbound on the Warringah Freeway at Cammeray, traffic is a touch slower. Clear on the " +
            "M4 and M5. Partly cloudy, 15 degrees.";

        SentenceShapeDetector.Detect(script).HasIssue.ShouldBeFalse();
    }

    [Fact]
    public void Station_lines_are_excluded_using_the_canonical_splitter()
    {
        const string intro = "Now looking over Sydney.";
        const string filler = "Keep left unless overtaking.";
        const string outro = "That's KIIS traffic and weather together, powered by";
        const string script = $"{intro} Traffic is clear. {filler} {filler} {outro}";

        SentenceShapeDetector.Detect(script, [intro, filler, outro]).HasIssue.ShouldBeFalse();
    }
}
