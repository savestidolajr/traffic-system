using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Models;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Application.Tests;

/// <summary>
///     REGRESSION GUARD — dev 9c0938fd's aired read (2026-08-24).
///     <para>
///         The deterministic detector gate must classify a cloned tail as non-clean before the
///         reviewer decision is reached. These counts pin that gate against the complete aired
///         script, including bookend edges.
///     </para>
///     <para>
///         The complete aired script and the station's real configuration are used deliberately.
///         Scored as a bare two-sentence fragment the pair trips
///         <c>RepeatedPrepositionDetector</c> on ratio alone, which is not what production judged.
///     </para>
/// </summary>
public sealed class ClonedReadRoutingTests
{
    /// <summary>Verbatim from job 9c0938fd, bookend edges included as the loop sees them.</summary>
    private const string AiredScript =
        "Now looking over Sydney. "
        + "Westbound on the M4 at Haberfield, a breakdown has one of two lanes closed. "
        + "Southbound on the M7 at Eastern Creek, a breakdown has one of two lanes closed. "
        + "Georges River Road through Croydon Park has a brief two-minute delay. "
        + "Clear skies, 19 degrees.";

    /// <summary>The same read with the second breakdown phrased as the draft also offered it.</summary>
    private const string VariedScript =
        "Now looking over Sydney. "
        + "Westbound on the M4 at Haberfield, a breakdown has one of two lanes closed. "
        + "There's another breakdown southbound on the M7 at Eastern Creek, with one lane closed. "
        + "Georges River Road through Croydon Park has a brief two-minute delay. "
        + "Clear skies, 19 degrees.";

    private static BroadcastWithTraffic Ctx() => new()
    {
        TargetDurationSeconds = 22,
        JobId = "9c0938fd",
        Location = "Sydney",
        Traffic = string.Empty,
        Incidents = [],
        Corridors =
        [
            new TrafficCorridor("M4", 0, 1, 0, 0, "incident", string.Empty, []),
            new TrafficCorridor("M7", 0, 1, 0, 0, "incident", string.Empty, []),
            new TrafficCorridor("Georges River Road", 0, 1, 0, 0, "congestion", string.Empty, [])
        ],
        GoodRunRoads = [],
        FlowPool = [],
        Content = new BulletinContentSelection(Traffic: true, Weather: true, WeatherRequired: true),
        Weather = new WeatherReport("Clear", 19, default, 0, [])
    };

    [Fact]
    public void A_cloned_read_never_presents_to_the_detector_gate_as_clean()
    {
        var (blocking, style) = ScriptReviewLoop.ComputeViolationCounts(AiredScript, Ctx());

        (blocking + style).ShouldBeGreaterThan(0,
            "the unconditional detector gate must flag a cloned tail before reviewer routing, so a "
            + "defect the deterministic detectors cannot see is not silently passed downstream");
    }

    [Fact]
    public void The_same_read_with_varied_phrasing_still_presents_as_clean()
    {
        // The cost optimisation must survive the fix: clean copy must still skip the LLM call.
        var (blocking, style) = ScriptReviewLoop.ComputeViolationCounts(VariedScript, Ctx());

        (blocking + style).ShouldBe(0,
            "the detector gate must leave genuinely varied copy clean, preserving the intended "
            + "review-routing cost boundary instead of flagging every bulletin");
    }
}
