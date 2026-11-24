using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class TimeOfDayNormalizerTests
{
    [Theory]
    [InlineData("this morning")]
    [InlineData("this afternoon")]
    [InlineData("this evening")]
    [InlineData("this arvo")]
    [InlineData("this midday")]
    [InlineData("this overnight")] // widened regex now recognises "this overnight" too
    [InlineData("tonight")]
    public void Normalize_NullDaypart_RewritesPhraseToRightNow(string phrase)
    {
        string script = $"No major delays across the network {phrase}.";

        string result = TimeOfDayNormalizer.Normalize(script, null);

        result.ShouldBe("No major delays across the network right now.");
    }

    [Theory]
    [InlineData("This Morning", "morning")]
    [InlineData("THIS AFTERNOON", "afternoon")]
    [InlineData("this Evening", "evening")]
    [InlineData("This ARVO", "arvo")]
    // "This Midday"/"Overnight Tonight" are deliberately absent here — their spoken forms
    // ("at midday"/"overnight") drop the "this ..." frame, so they're rewritten even on a
    // match; see Normalize_MatchingDaypartWithoutThisFrame_RewritesToSpokenForm below.
    public void Normalize_MatchingDaypart_KeepsWritersPhraseVerbatim(string phrase, string actualDaypart)
    {
        string script = $"Expect delays {phrase} on the M1.";

        string result = TimeOfDayNormalizer.Normalize(script, actualDaypart);

        // Unchanged, including the writer's own casing - no forced canonicalisation.
        result.ShouldBe(script);
    }

    [Theory]
    // "this overnight"/"this midday" are recognised daypart phrases whose natural spoken form
    // drops the "this ..." frame ("overnight", "at midday") -- unlike morning/afternoon/evening/
    // arvo, an already-matching daypart is still rewritten to the natural form, not kept verbatim.
    [InlineData("this overnight", "overnight", "overnight")]
    [InlineData("this midday", "midday", "at midday")]
    [InlineData("overnight tonight", "overnight", "overnight")]
    public void Normalize_MatchingDaypartWithoutThisFrame_RewritesToSpokenForm(
        string writtenPhrase, string actualDaypart, string expectedPhrase)
    {
        string script = $"Expect delays {writtenPhrase} on the M1.";

        string result = TimeOfDayNormalizer.Normalize(script, actualDaypart);

        result.ShouldBe($"Expect delays {expectedPhrase} on the M1.");
    }

    [Theory]
    [InlineData("this morning", "afternoon", "this afternoon")]
    [InlineData("this afternoon", "arvo", "this arvo")]
    [InlineData("this arvo", "evening", "this evening")]
    [InlineData("this evening", "midday", "at midday")]
    [InlineData("this midday", "overnight", "overnight")]
    [InlineData("tonight", "morning", "this morning")]
    [InlineData("tonight", "evening", "this evening")]
    [InlineData("tonight", "overnight", "overnight")]
    public void Normalize_MismatchedDaypart_RewritesToActualWording(
        string writtenPhrase, string actualDaypart, string expectedPhrase)
    {
        string script = $"Expect delays {writtenPhrase} on the M1.";

        string result = TimeOfDayNormalizer.Normalize(script, actualDaypart);

        result.ShouldBe($"Expect delays {expectedPhrase} on the M1.");
    }

    [Fact]
    public void Normalize_MismatchDetectionIsCaseInsensitive()
    {
        // Phrase detection ignores case; a mismatch still gets rewritten (original casing is
        // discarded rather than preserved, since the whole match is replaced by "spoken").
        string result = TimeOfDayNormalizer.Normalize("Delays continue TONIGHT on the M1.", "afternoon");

        result.ShouldBe("Delays continue this afternoon on the M1.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("afternoon")]
    [InlineData("overnight")]
    public void Normalize_NoDaypartWordsInScript_ReturnsUnchanged(string? actualDaypart)
    {
        const string script = "Expect delays on the M1 near Kwinana Freeway.";

        TimeOfDayNormalizer.Normalize(script, actualDaypart).ShouldBe(script);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Normalize_NullOrEmptyScript_ReturnedAsIs(string? script)
    {
        TimeOfDayNormalizer.Normalize(script!, "morning").ShouldBe(script);
    }

    [Theory]
    [InlineData("Delays continue on this Morningside Drive overpass.")]
    [InlineData("Take care - this Eveningwood Court off-ramp reopens soon.")]
    [InlineData("Drivers should note delays tonightly reported near the bridge.")]
    public void Normalize_CompoundWordsContainingDaypartSubstrings_AreNotMangled(string script)
    {
        // Word-boundary safety: "Morningside"/"Eveningwood"/"tonightly" merely contain a daypart
        // token as a substring - they must never be treated as a daypart phrase, under a null
        // daypart (which would otherwise turn any match into "right now") or a known one.
        TimeOfDayNormalizer.Normalize(script, null).ShouldBe(script);
        TimeOfDayNormalizer.Normalize(script, "morning").ShouldBe(script);
    }
}
