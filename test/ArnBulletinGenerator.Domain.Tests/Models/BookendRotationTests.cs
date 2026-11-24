using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class BookendRotationTests
{
    private static BookendWindow Window(string start, string end, params string[] ids) =>
        new(TimeOnly.Parse(start), TimeOnly.Parse(end), ids);

    // ── Mode invariants ────────────────────────────────────────────────────

    [Fact]
    public void Single_IgnoresSetAndWindows()
    {
        var policy = BookendRotationPolicy.Create(
            BookendSelectionMode.Single, ["bi-1", "bi-2"], [Window("06:00", "09:00", "bi-3")]);

        policy.IsSuccess.ShouldBeTrue();
        policy.Value.BookendIds.ShouldBeEmpty();
        policy.Value.Windows.ShouldBeEmpty();
    }

    [Fact]
    public void RoundRobin_RejectsWindows()
    {
        BookendRotationPolicy.Create(
                BookendSelectionMode.RoundRobin, ["bi-1"], [Window("06:00", "09:00", "bi-2")])
            .IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void RoundRobin_RejectsEmptyOrDuplicateSet()
    {
        BookendRotationPolicy.Create(BookendSelectionMode.RoundRobin, [], null).IsFailure.ShouldBeTrue();
        BookendRotationPolicy.Create(BookendSelectionMode.RoundRobin, ["bi-1", "bi-1"], null)
            .IsFailure.ShouldBeTrue();
        BookendRotationPolicy.Create(BookendSelectionMode.RoundRobin, ["bi-1", "  "], null)
            .IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void RoundRobin_CaseVariantMembers_AreDistinctIds_NotDuplicates()
    {
        // DKI-03 decision (bookend twin): the resolver reads bookends by exact Table RowKey, so
        // "bi-1"/"BI-1" are two DIFFERENT pairs. Construction keeps both, and import/API
        // validation verifies each one resolves EXACTLY.
        var result = BookendRotationPolicy.Create(
            BookendSelectionMode.RoundRobin, ["bi-1", "BI-1"], null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.BookendIds.ShouldBe(["bi-1", "BI-1"]);
    }

    [Fact]
    public void TimeOfDay_RejectsTopLevelSet_AndRequiresAWindow()
    {
        BookendRotationPolicy.Create(
                BookendSelectionMode.TimeOfDay, ["bi-1"], [Window("06:00", "09:00", "bi-2")])
            .IsFailure.ShouldBeTrue();
        BookendRotationPolicy.Create(BookendSelectionMode.TimeOfDay, null, []).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void TimeOfDay_RejectsZeroLengthWindow()
    {
        BookendRotationPolicy.Create(
                BookendSelectionMode.TimeOfDay, null, [Window("06:00", "06:00", "bi-1")])
            .IsFailure.ShouldBeTrue();
    }

    // ── Overlap, including the midnight wrap ───────────────────────────────

    [Fact]
    public void TimeOfDay_RejectsOverlappingWindows()
    {
        BookendRotationPolicy.Create(BookendSelectionMode.TimeOfDay, null,
                [Window("06:00", "10:00", "bi-1"), Window("09:00", "12:00", "bi-2")])
            .IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void TimeOfDay_DetectsOverlapAcrossMidnight()
    {
        // 22:00-06:00 wraps; 05:00-07:00 straddles the wrapped tail. A naive start<end comparison
        // misses this, which is why the arithmetic lives in one shared helper.
        BookendRotationPolicy.Create(BookendSelectionMode.TimeOfDay, null,
                [Window("22:00", "06:00", "bi-1"), Window("05:00", "07:00", "bi-2")])
            .IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void TimeOfDay_AllowsAdjacentHalfOpenWindows()
    {
        // [06:00,09:00) and [09:00,12:00) touch but do not overlap.
        BookendRotationPolicy.Create(BookendSelectionMode.TimeOfDay, null,
                [Window("06:00", "09:00", "bi-1"), Window("09:00", "12:00", "bi-2")])
            .IsSuccess.ShouldBeTrue();
    }

    // ── Candidate resolution at airtime ────────────────────────────────────

    [Fact]
    public void RoundRobin_ReturnsTheWholeSet()
    {
        var policy = BookendRotationPolicy.Create(
            BookendSelectionMode.RoundRobin, ["bi-1", "bi-2", "bi-3"], null).Value;

        policy.CandidatesAt(new TimeOnly(7, 0), "bi-primary").ShouldBe(["bi-1", "bi-2", "bi-3"]);
    }

    [Fact]
    public void TimeOfDay_ReturnsTheCoveringWindow()
    {
        var policy = BookendRotationPolicy.Create(BookendSelectionMode.TimeOfDay, null,
            [Window("06:00", "09:00", "bi-breakfast"), Window("15:00", "18:00", "bi-drive")]).Value;

        policy.CandidatesAt(new TimeOnly(7, 30), "bi-primary").ShouldBe(["bi-breakfast"]);
        policy.CandidatesAt(new TimeOnly(16, 0), "bi-primary").ShouldBe(["bi-drive"]);
    }

    [Fact]
    public void TimeOfDay_UncoveredTime_FallsBackToThePrimary()
    {
        var policy = BookendRotationPolicy.Create(BookendSelectionMode.TimeOfDay, null,
            [Window("06:00", "09:00", "bi-breakfast")]).Value;

        policy.CandidatesAt(new TimeOnly(12, 0), "bi-primary").ShouldBe(["bi-primary"]);
    }

    [Fact]
    public void TimeOfDay_WindowCoversAcrossMidnight()
    {
        var policy = BookendRotationPolicy.Create(BookendSelectionMode.TimeOfDay, null,
            [Window("22:00", "06:00", "bi-overnight")]).Value;

        policy.CandidatesAt(new TimeOnly(23, 30), "bi-primary").ShouldBe(["bi-overnight"]);
        policy.CandidatesAt(new TimeOnly(2, 0), "bi-primary").ShouldBe(["bi-overnight"]);
        policy.CandidatesAt(new TimeOnly(12, 0), "bi-primary").ShouldBe(["bi-primary"]);
    }

    [Fact]
    public void NoPrimary_YieldsNoCandidates_RatherThanABlankId()
    {
        var policy = BookendRotationPolicy.Create(BookendSelectionMode.Single, null, null).Value;

        policy.CandidatesAt(new TimeOnly(7, 0), "").ShouldBeEmpty();
    }

    // ── Referential integrity surface ──────────────────────────────────────

    [Fact]
    public void AllReferencedBookendIds_UnionsSetAndWindows_Distinctly()
    {
        var policy = BookendRotationPolicy.Create(BookendSelectionMode.TimeOfDay, null,
            [Window("06:00", "09:00", "bi-1", "bi-2"), Window("15:00", "18:00", "bi-2", "bi-3")]).Value;

        policy.AllReferencedBookendIds().ShouldBe(["bi-1", "bi-2", "bi-3"]);
    }
}
