using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class VoiceRotationTests
{
    private static VoiceWindow Window(int startHour, int endHour, params string[] ids) =>
        new(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), ids);

    // ── RoundRobin validation ──

    [Fact]
    public void RoundRobin_WithOrderedSet_Succeeds()
    {
        var result = VoiceRotationPolicy.Create(VoiceSelectionMode.RoundRobin, ["a", "b", "c"], null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Mode.ShouldBe(VoiceSelectionMode.RoundRobin);
        result.Value.VoiceProfileIds.ShouldBe(["a", "b", "c"]);
        result.Value.Windows.ShouldBeEmpty();
    }

    [Fact]
    public void RoundRobin_SingleVoice_IsAllowed()
    {
        VoiceRotationPolicy.Create(VoiceSelectionMode.RoundRobin, ["only"], null).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void RoundRobin_EmptySet_Fails()
    {
        VoiceRotationPolicy.Create(VoiceSelectionMode.RoundRobin, [], null).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void RoundRobin_DuplicateVoice_Fails()
    {
        var result = VoiceRotationPolicy.Create(VoiceSelectionMode.RoundRobin, ["a", "a"], null);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("same voice twice");
    }

    [Fact]
    public void RoundRobin_CaseVariantMembers_AreDistinctIds_NotDuplicates()
    {
        // DKI-03 decision: the resolver (StationConfigResolver) looks rotation members up
        // case-SENSITIVELY (Table RowKey), so "a"/"A" are two DIFFERENT profiles. Construction
        // keeps both, and import/API validation verifies each one resolves EXACTLY — treating
        // them as duplicates here would reject a legitimately distinct pair of profiles.
        var result = VoiceRotationPolicy.Create(VoiceSelectionMode.RoundRobin, ["vp-a", "VP-A"], null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.VoiceProfileIds.ShouldBe(["vp-a", "VP-A"]);
    }

    [Fact]
    public void RoundRobin_BlankVoice_Fails()
    {
        VoiceRotationPolicy.Create(VoiceSelectionMode.RoundRobin, ["a", "  "], null).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void RoundRobin_OverMaxVoices_Fails()
    {
        var ids = Enumerable.Range(0, VoiceRotationPolicy.MaxVoices + 1).Select(i => $"v{i}").ToList();

        VoiceRotationPolicy.Create(VoiceSelectionMode.RoundRobin, ids, null).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void RoundRobin_WithWindows_Fails()
    {
        VoiceRotationPolicy.Create(VoiceSelectionMode.RoundRobin, ["a"], [Window(6, 10, "a")])
            .IsFailure.ShouldBeTrue();
    }

    // ── TimeOfDay validation ──

    [Fact]
    public void TimeOfDay_NonOverlappingWindows_Succeeds()
    {
        var result = VoiceRotationPolicy.Create(VoiceSelectionMode.TimeOfDay, null,
            [Window(6, 10, "morning"), Window(10, 18, "day")]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Windows.Count.ShouldBe(2);
    }

    [Fact]
    public void TimeOfDay_NoWindows_Fails()
    {
        VoiceRotationPolicy.Create(VoiceSelectionMode.TimeOfDay, null, []).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void TimeOfDay_WithTopLevelSet_Fails()
    {
        VoiceRotationPolicy.Create(VoiceSelectionMode.TimeOfDay, ["a"], [Window(6, 10, "a")])
            .IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void TimeOfDay_OverlappingWindows_Fails()
    {
        var result = VoiceRotationPolicy.Create(VoiceSelectionMode.TimeOfDay, null,
            [Window(6, 10, "a"), Window(9, 12, "b")]);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("overlap");
    }

    [Fact]
    public void TimeOfDay_AdjacentWindows_DoNotOverlap()
    {
        VoiceRotationPolicy.Create(VoiceSelectionMode.TimeOfDay, null,
            [Window(6, 10, "a"), Window(10, 14, "b")]).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void TimeOfDay_MidnightWrapOverlap_Fails()
    {
        // 22:00–06:00 wraps midnight and 05:00–07:00 both cover 05:00–06:00.
        var result = VoiceRotationPolicy.Create(VoiceSelectionMode.TimeOfDay, null,
            [Window(22, 6, "night"), Window(5, 7, "dawn")]);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void TimeOfDay_ZeroLengthWindow_Fails()
    {
        VoiceRotationPolicy.Create(VoiceSelectionMode.TimeOfDay, null,
            [new VoiceWindow(new TimeOnly(8, 0), new TimeOnly(8, 0), ["a"])]).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void TimeOfDay_EmptyWindowVoiceSet_Fails()
    {
        VoiceRotationPolicy.Create(VoiceSelectionMode.TimeOfDay, null, [Window(6, 10)]).IsFailure.ShouldBeTrue();
    }

    // ── Single ──

    [Fact]
    public void Single_NormalizesToEmpty()
    {
        var result = VoiceRotationPolicy.Create(VoiceSelectionMode.Single, ["a", "b"], null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.VoiceProfileIds.ShouldBeEmpty();
        result.Value.Windows.ShouldBeEmpty();
    }

    // ── CandidatesAt ──

    [Fact]
    public void CandidatesAt_RoundRobin_ReturnsWholeSet()
    {
        var policy = VoiceRotationPolicy.Create(VoiceSelectionMode.RoundRobin, ["a", "b"], null).Value;

        policy.CandidatesAt(new TimeOnly(3, 0), "primary").ShouldBe(["a", "b"]);
    }

    [Fact]
    public void CandidatesAt_TimeOfDay_ReturnsActiveWindow()
    {
        var policy = VoiceRotationPolicy.Create(VoiceSelectionMode.TimeOfDay, null,
            [Window(6, 10, "morning"), Window(10, 18, "day")]).Value;

        policy.CandidatesAt(new TimeOnly(11, 0), "primary").ShouldBe(["day"]);
    }

    [Fact]
    public void CandidatesAt_TimeOfDay_Gap_FallsBackToPrimary()
    {
        var policy = VoiceRotationPolicy.Create(VoiceSelectionMode.TimeOfDay, null,
            [Window(6, 10, "morning")]).Value;

        policy.CandidatesAt(new TimeOnly(20, 0), "primary").ShouldBe(["primary"]);
    }

    [Fact]
    public void CandidatesAt_Single_ReturnsPrimary()
    {
        var policy = VoiceRotationPolicy.Create(VoiceSelectionMode.Single, null, null).Value;

        policy.CandidatesAt(new TimeOnly(12, 0), "primary").ShouldBe(["primary"]);
    }

    // ── VoiceWindow.Contains ──

    [Theory]
    [InlineData(6, 5, 59, false)]
    [InlineData(6, 6, 0, true)]
    [InlineData(6, 9, 59, true)]
    [InlineData(6, 10, 0, false)] // half-open: end is exclusive
    public void Window_SameDay_Contains(int startHour, int hour, int minute, bool expected)
    {
        var window = new VoiceWindow(new TimeOnly(startHour, 0), new TimeOnly(10, 0), ["a"]);

        window.Contains(new TimeOnly(hour, minute)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(23, 0, true)] // late night
    [InlineData(3, 0, true)] // early morning
    [InlineData(6, 0, false)] // end exclusive
    [InlineData(12, 0, false)] // midday outside
    public void Window_MidnightWrap_Contains(int hour, int minute, bool expected)
    {
        // 22:00 → 06:00 wraps midnight.
        var window = new VoiceWindow(new TimeOnly(22, 0), new TimeOnly(6, 0), ["a"]);

        window.Contains(new TimeOnly(hour, minute)).ShouldBe(expected);
    }

    // ── AllReferencedVoiceIds ──

    [Fact]
    public void AllReferencedVoiceIds_RoundRobin_ReturnsSet()
    {
        var policy = VoiceRotationPolicy.Create(VoiceSelectionMode.RoundRobin, ["a", "b"], null).Value;

        policy.AllReferencedVoiceIds().ShouldBe(["a", "b"]);
    }

    [Fact]
    public void AllReferencedVoiceIds_TimeOfDay_DedupesAcrossWindows()
    {
        var policy = VoiceRotationPolicy.Create(VoiceSelectionMode.TimeOfDay, null,
            [Window(6, 10, "a", "b"), Window(10, 18, "b", "c")]).Value;

        policy.AllReferencedVoiceIds().ShouldBe(["a", "b", "c"]);
    }
}
