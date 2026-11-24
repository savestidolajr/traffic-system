using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>Deterministic tests for the script-sanity gate (placeholder/ID junk in the bulletin).</summary>
public sealed class ScriptSanityScorerTests
{
    [Fact]
    public void PlaceholderRoadName_IsFlagged()
    {
        var result = ScriptSanityScorer.Evaluate(
            "A planned event is causing heavy delays on that Unknown-4386185267451476265 location.");

        result.Passed.ShouldBeFalse();
        result.JunkTokens.ShouldContain("Unknown-4386185267451476265");
    }

    [Fact]
    public void BareLongDigitRun_IsFlagged()
    {
        var result = ScriptSanityScorer.Evaluate("Incident reference 1234567 on the M4.");

        result.Passed.ShouldBeFalse();
        result.JunkTokens.ShouldContain("1234567");
    }

    [Fact]
    public void CleanBroadcast_WithShortNumbers_Passes()
    {
        var result = ScriptSanityScorer.Evaluate(
            "Heavy delays on the M4, queue about 700 metres back, drive time up around 9 minutes.");

        result.Passed.ShouldBeTrue($"unexpected junk: {string.Join(", ", result.JunkTokens)}");
    }
}
