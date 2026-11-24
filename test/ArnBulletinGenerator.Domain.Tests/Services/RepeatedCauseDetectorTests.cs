using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services.Detectors;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class RepeatedCauseDetectorTests
{
    private static BroadcastWithTraffic Context() => new()
    {
        TargetDurationSeconds = 30,
        JobId = "repeated-cause",
        Location = "Gold Coast",
        Traffic = string.Empty,
        Incidents = [],
        Corridors = []
    };

    [Fact]
    public void Flags_the_real_gold_coast_shared_event_read()
    {
        const string script =
            "Outbound Gold Coast Highway is closed between Nerang-Broadbeach Road and Heron Avenue for the Gold Coast Marathon. "
            + "It is adding three minutes. Margaret Avenue is closed outbound between Nerang-Broadbeach Road and Markeri Street for the Gold Coast Marathon. "
            + "Marine Parade is closed westbound at Scarborough Street North for the event. Fern Street is closed citybound at Surfers Paradise Boulevard for the Gold Coast Marathon.";

        var result = RepeatedCauseDetector.Detect(script, Context());
        result.Feedback.ShouldContain("Group");
        result.HasRepeatedCause.ShouldBeTrue();
        result.Cause.ShouldBe("gold coast marathon");
        result.Count.ShouldBe(3);
        result.Issues.Single().Scope.ShouldBe(RefineScope.Script);
    }

    [Fact]
    public void Passes_when_causes_are_distinct()
    {
        const string script =
            "The road is closed for the Gold Coast Marathon. A lane is blocked because of a crash. "
            + "Traffic is slow due to roadworks.";

        RepeatedCauseDetector.Detect(script, Context()).HasRepeatedCause.ShouldBeFalse();
    }

    [Fact]
    public void Passes_when_a_cause_is_used_once()
    {
        const string script = "Fern Street is closed for the Gold Coast Marathon. Marine Parade is slow.";

        RepeatedCauseDetector.Detect(script, Context()).HasRepeatedCause.ShouldBeFalse();
    }

    [Fact]
    public void Passes_empty_script()
    {
        RepeatedCauseDetector.Detect(string.Empty, Context()).HasRepeatedCause.ShouldBeFalse();
    }
}
