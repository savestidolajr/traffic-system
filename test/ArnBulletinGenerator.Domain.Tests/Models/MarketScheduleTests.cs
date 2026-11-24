using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class MarketScheduleTests
{
    private static MarketSchedule CreateSchedule()
    {
        return MarketSchedule.Create(
            "mkt-1",
            "Test Market",
            "TST-FM",
            "Australia/Sydney",
            "0 8 * * *",
            true,
            CoverageArea.Create(-33.87, 151.21, 25).Value,
            "voice-1",
            "/output",
            60,
            15,
            5,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Value;
    }

    // ── Create validation ──

    [Theory]
    [InlineData("marketId", null)]
    [InlineData("marketId", "")]
    [InlineData("name", null)]
    [InlineData("name", "")]
    [InlineData("station", null)]
    [InlineData("station", "")]
    [InlineData("timezone", null)]
    [InlineData("timezone", "")]
    [InlineData("cronExpression", null)]
    [InlineData("cronExpression", "")]
    [InlineData("voiceProfileId", null)]
    [InlineData("voiceProfileId", "")]
    [InlineData("outputPath", null)]
    [InlineData("outputPath", "")]
    public void Create_RequiredFieldMissing_Fails(string field, string? value)
    {
        var result = (field) switch
        {
            "marketId" => MarketSchedule.Create(value!, "Test Market", "TST-FM", "Australia/Sydney", "0 8 * * *",
                true, CoverageArea.Create(-33.87, 151.21, 25).Value, "voice-1", "/output",
                60, 15, 5, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            "name" => MarketSchedule.Create("mkt-1", value!, "TST-FM", "Australia/Sydney", "0 8 * * *",
                true, CoverageArea.Create(-33.87, 151.21, 25).Value, "voice-1", "/output",
                60, 15, 5, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            "station" => MarketSchedule.Create("mkt-1", "Test Market", value!, "Australia/Sydney", "0 8 * * *",
                true, CoverageArea.Create(-33.87, 151.21, 25).Value, "voice-1", "/output",
                60, 15, 5, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            "timezone" => MarketSchedule.Create("mkt-1", "Test Market", "TST-FM", value!, "0 8 * * *",
                true, CoverageArea.Create(-33.87, 151.21, 25).Value, "voice-1", "/output",
                60, 15, 5, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            "cronExpression" => MarketSchedule.Create("mkt-1", "Test Market", "TST-FM", "Australia/Sydney", value!,
                true, CoverageArea.Create(-33.87, 151.21, 25).Value, "voice-1", "/output",
                60, 15, 5, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            "voiceProfileId" => MarketSchedule.Create("mkt-1", "Test Market", "TST-FM", "Australia/Sydney", "0 8 * * *",
                true, CoverageArea.Create(-33.87, 151.21, 25).Value, value!, "/output",
                60, 15, 5, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            "outputPath" => MarketSchedule.Create("mkt-1", "Test Market", "TST-FM", "Australia/Sydney", "0 8 * * *",
                true, CoverageArea.Create(-33.87, 151.21, 25).Value, "voice-1", value!,
                60, 15, 5, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
        };

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe($"{field switch
        {
            "marketId" => "MarketId",
            "name" => "Name",
            "station" => "Station",
            "timezone" => "Timezone",
            "cronExpression" => "CronExpression",
            "voiceProfileId" => "VoiceProfileId",
            "outputPath" => "OutputPath",
            _ => field
        }} is required.");
    }


    // ── CoverageArea ──

    [Fact]
    public void CoverageArea_ValidValues_CreatesInstance()
    {
        var area = CoverageArea.Create(45.0, 90.0, 10.0).Value;

        area.Latitude.ShouldBe(45.0);
        area.Longitude.ShouldBe(90.0);
        area.RadiusKm.ShouldBe(10.0);
    }

    [Fact]
    public void CoverageArea_BoundaryValues_Succeeds()
    {
        var area = CoverageArea.Create(-90.0, -180.0, 0.001).Value;

        area.Latitude.ShouldBe(-90.0);
        area.Longitude.ShouldBe(-180.0);
    }

    [Theory]
    [InlineData(-91.0, 0.0, 10.0)]
    [InlineData(91.0, 0.0, 10.0)]
    public void CoverageArea_LatitudeOutOfRange_Fails(double lat, double lng, double radius)
    {
        CoverageArea.Create(lat, lng, radius).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0.0, -181.0, 10.0)]
    [InlineData(0.0, 181.0, 10.0)]
    public void CoverageArea_LongitudeOutOfRange_Fails(double lat, double lng, double radius)
    {
        CoverageArea.Create(lat, lng, radius).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void CoverageArea_RadiusZeroOrNegative_Fails(double radius)
    {
        CoverageArea.Create(0, 0, radius).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void CoverageArea_RadiusOverMaxCircleRadius_Fails()
    {
        var result = CoverageArea.Create(0, 0, 100000.0);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(CoverageArea.MaxCircleRadiusKm.ToString());
    }

    [Fact]
    public void CoverageArea_RadiusWithinMaxCircleRadius_Succeeds()
    {
        var result = CoverageArea.Create(0, 0, 50.0);

        result.IsSuccess.ShouldBeTrue();
    }

    // ── State queries ──

    [Fact]
    public void NewSchedule_HasNoRunState()
    {
        var schedule = CreateSchedule();

        schedule.HasEverRun.ShouldBeFalse();
        schedule.IsRunning.ShouldBeFalse();
        schedule.HasNextRun.ShouldBeFalse();
    }

    // ── StartRun ──

    [Fact]
    public void StartRun_SetsJobIdAndUpdatedAt()
    {
        var schedule = CreateSchedule();
        var now = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        schedule.StartRun("job-42", now);

        schedule.IsRunning.ShouldBeTrue();
        schedule.CurrentJobId.Value.ShouldBe("job-42");
        schedule.UpdatedAt.ShouldBe(now);
    }

    // ── CompleteRun ──

    [Fact]
    public void CompleteRun_ClearsJobId_SetsLastAndNextRun()
    {
        var schedule = CreateSchedule();
        var startTime = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var completeTime = new DateTime(2025, 6, 1, 12, 5, 0, DateTimeKind.Utc);
        var nextRun = new DateTime(2025, 6, 2, 8, 0, 0, DateTimeKind.Utc);

        schedule.StartRun("job-1", startTime);
        schedule.CompleteRun(completeTime, Option<DateTime>.Some(nextRun));

        schedule.IsRunning.ShouldBeFalse();
        schedule.HasEverRun.ShouldBeTrue();
        schedule.LastRunUtc.Value.ShouldBe(completeTime);
        schedule.NextRunUtc.Value.ShouldBe(nextRun);
        schedule.UpdatedAt.ShouldBe(completeTime);
    }

    [Fact]
    public void CompleteRun_WithNoNextRun_SetsNextRunToNone()
    {
        var schedule = CreateSchedule();
        var now = DateTime.UtcNow;

        schedule.CompleteRun(now, Option<DateTime>.None);

        schedule.HasNextRun.ShouldBeFalse();
    }

    // ── ScheduleNextRun ──

    [Fact]
    public void ScheduleNextRun_SetsNextRunUtc()
    {
        var schedule = CreateSchedule();
        var next = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        schedule.ScheduleNextRun(Option<DateTime>.Some(next));

        schedule.HasNextRun.ShouldBeTrue();
        schedule.NextRunUtc.Value.ShouldBe(next);
    }

    [Fact]
    public void ScheduleNextRun_None_ClearsNextRun()
    {
        var schedule = CreateSchedule();
        schedule.ScheduleNextRun(Option<DateTime>.Some(DateTime.UtcNow));

        schedule.ScheduleNextRun(Option<DateTime>.None);

        schedule.HasNextRun.ShouldBeFalse();
    }

    // ── Voice rotation cursor ──

    [Fact]
    public void CompleteRun_WithRotation_AdvancesCursor()
    {
        var schedule = CreateSchedule();
        schedule.VoiceRotation = Option<VoiceRotationPolicy>.Some(
            VoiceRotationPolicy.Create(VoiceSelectionMode.RoundRobin, ["a", "b"], null).Value);

        schedule.CompleteRun(DateTime.UtcNow, Option<DateTime>.None);

        schedule.VoiceRotationCursor.ShouldBe(1);
    }

    [Fact]
    public void CompleteRun_WithoutRotation_DoesNotAdvanceCursor()
    {
        var schedule = CreateSchedule();

        schedule.CompleteRun(DateTime.UtcNow, Option<DateTime>.None);

        schedule.VoiceRotationCursor.ShouldBe(0);
    }

    [Fact]
    public void AdvanceVoiceRotation_AtMaxValue_WrapsToZero()
    {
        var schedule = CreateSchedule();
        schedule.VoiceRotationCursor = int.MaxValue;

        schedule.AdvanceVoiceRotation();

        schedule.VoiceRotationCursor.ShouldBe(0);
    }

    [Fact]
    public void VoiceRotationCursor_NegativeAssignment_ClampsToZero()
    {
        var schedule = CreateSchedule();

        // The poller indexes candidates[cursor % count] and relies on the cursor
        // never being negative — a negative value (corrupt storage row, buggy
        // caller) would make the modulo negative and throw IndexOutOfRangeException.
        schedule.VoiceRotationCursor = -1;

        schedule.VoiceRotationCursor.ShouldBe(0);
    }

    // ── Bookend rotation cursor ──

    [Fact]
    public void CompleteRun_WithBookendRotation_AdvancesBookendCursor()
    {
        // A station that rotates bookends but NOT voices must still cycle its bookends.
        var schedule = CreateSchedule();
        schedule.BookendRotation = Option<BookendRotationPolicy>.Some(
            BookendRotationPolicy.Create(BookendSelectionMode.RoundRobin, ["bk-1", "bk-2"], null).Value);

        schedule.CompleteRun(DateTime.UtcNow, Option<DateTime>.None);

        schedule.BookendRotationCursor.ShouldBe(1);
        schedule.VoiceRotationCursor.ShouldBe(0); // voice cursor untouched
    }

    [Fact]
    public void CompleteRun_WithBookendAndVoiceRotations_AdvancesBothCursors()
    {
        var schedule = CreateSchedule();
        schedule.VoiceRotation = Option<VoiceRotationPolicy>.Some(
            VoiceRotationPolicy.Create(VoiceSelectionMode.RoundRobin, ["vp-a", "vp-b"], null).Value);
        schedule.BookendRotation = Option<BookendRotationPolicy>.Some(
            BookendRotationPolicy.Create(BookendSelectionMode.RoundRobin, ["bk-1"], null).Value);

        schedule.CompleteRun(DateTime.UtcNow, Option<DateTime>.None);

        schedule.VoiceRotationCursor.ShouldBe(1);
        schedule.BookendRotationCursor.ShouldBe(1);
    }

    [Fact]
    public void CompleteRun_WithoutBookendRotation_DoesNotAdvanceBookendCursor()
    {
        var schedule = CreateSchedule();

        schedule.CompleteRun(DateTime.UtcNow, Option<DateTime>.None);

        schedule.BookendRotationCursor.ShouldBe(0);
    }

    [Fact]
    public void AdvanceBookendRotation_AtMaxValue_WrapsToZero()
    {
        var schedule = CreateSchedule();
        schedule.BookendRotationCursor = int.MaxValue;

        schedule.AdvanceBookendRotation();

        schedule.BookendRotationCursor.ShouldBe(0);
    }

    [Fact]
    public void BookendRotationCursor_NegativeAssignment_ClampsToZero()
    {
        var schedule = CreateSchedule();

        schedule.BookendRotationCursor = -1;

        schedule.BookendRotationCursor.ShouldBe(0);
    }

    // ── Bookend assignment fields ──

    [Fact]
    public void Create_WithBookendId_StoresNormalizedId()
    {
        var schedule = MarketSchedule.Create("mkt-1", "Test Market", "TST-FM", "Australia/Sydney", "0 8 * * *",
            true, CoverageArea.Create(-33.87, 151.21, 25).Value, "voice-1", "/output",
            60, 15, 5, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            bookendId: "bk-1").Value;

        schedule.BookendId.ShouldBe("bk-1");
    }

    [Fact]
    public void Create_WithBlankBookendId_DefaultsToEmpty()
    {
        var schedule = MarketSchedule.Create("mkt-1", "Test Market", "TST-FM", "Australia/Sydney", "0 8 * * *",
            true, CoverageArea.Create(-33.87, 151.21, 25).Value, "voice-1", "/output",
            60, 15, 5, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            bookendId: "  ").Value;

        schedule.BookendId.ShouldBe("");
    }

    [Fact]
    public void BookendTokens_Assignment_NormalizesTrimsAndDropsBlanks()
    {
        var schedule = CreateSchedule();

        schedule.BookendTokens = new Dictionary<string, string>
        {
            ["  brand "] = "  Sunrise FM ",
            [""] = "blank-key",
            ["blank-value"] = "  ",
            ["City"] = "Cairns"
        };

        schedule.BookendTokens.Count.ShouldBe(2);
        schedule.BookendTokens["brand"].ShouldBe("Sunrise FM");
        // Case-insensitive: the key stored as "City" resolves via lowercase lookup.
        schedule.BookendTokens["city"].ShouldBe("Cairns");
        schedule.BookendTokens["CITY"].ShouldBe("Cairns");
    }

    [Fact]
    public void BookendTokens_NullAssignment_DefaultsToEmptyDictionary()
    {
        var schedule = CreateSchedule();

        schedule.BookendTokens = null!;

        schedule.BookendTokens.ShouldNotBeNull();
        schedule.BookendTokens.Count.ShouldBe(0);
    }

    [Fact]
    public void ApplyUpdates_BookendFields_AreApplied()
    {
        var schedule = CreateSchedule();

        var result = schedule.ApplyUpdates(
            Option<string>.None, Option<string>.None, Option<string>.None, Option<string>.None,
            Option<bool>.None, Option<CoverageArea>.None, Option<string>.None, Option<string>.None,
            Option<double>.None, Option<int>.None, Option<int>.None,
            Option<string>.None, Option<string>.None,
            Option<BulletinContentSelection>.None, Option<IReadOnlyList<FillSource>>.None,
            Option<VoiceRotationPolicy>.None,
            "bk-9",
            Option<BookendRotationPolicy>.Some(
                BookendRotationPolicy.Create(BookendSelectionMode.RoundRobin, ["bk-1", "bk-2"], null).Value),
            new Dictionary<string, string> { ["brand"] = "Nine Radio" },
            Option<ScheduleCalendar>.None,
            new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        result.IsSuccess.ShouldBeTrue();
        schedule.BookendId.ShouldBe("bk-9");
        schedule.BookendRotation.HasValue.ShouldBeTrue();
        schedule.BookendRotation.Value.Mode.ShouldBe(BookendSelectionMode.RoundRobin);
        schedule.BookendTokens["brand"].ShouldBe("Nine Radio");
    }

    [Fact]
    public void ApplyUpdates_RotationModeChange_ResetsBookendCursor()
    {
        var schedule = CreateSchedule();
        schedule.BookendRotation = Option<BookendRotationPolicy>.Some(
            BookendRotationPolicy.Create(BookendSelectionMode.RoundRobin, ["bk-1", "bk-2"], null).Value);
        schedule.BookendRotationCursor = 3;

        // Switching to TimeOfDay is a mode change: the old round-robin position is meaningless.
        schedule.ApplyUpdates(
            Option<string>.None, Option<string>.None, Option<string>.None, Option<string>.None,
            Option<bool>.None, Option<CoverageArea>.None, Option<string>.None, Option<string>.None,
            Option<double>.None, Option<int>.None, Option<int>.None,
            Option<string>.None, Option<string>.None,
            Option<BulletinContentSelection>.None, Option<IReadOnlyList<FillSource>>.None,
            Option<VoiceRotationPolicy>.None,
            Option<string>.None,
            Option<BookendRotationPolicy>.Some(BookendRotationPolicy.Create(
                BookendSelectionMode.TimeOfDay,
                null,
                [new BookendWindow(new TimeOnly(6, 0), new TimeOnly(18, 0), ["bk-1"])]).Value),
            Option<IReadOnlyDictionary<string, string>>.None,
            Option<ScheduleCalendar>.None,
            new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        schedule.BookendRotationCursor.ShouldBe(0);
    }

    [Fact]
    public void ApplyUpdates_SameRotationMode_PreservesBookendCursor()
    {
        var schedule = CreateSchedule();
        schedule.BookendRotation = Option<BookendRotationPolicy>.Some(
            BookendRotationPolicy.Create(BookendSelectionMode.RoundRobin, ["bk-1", "bk-2"], null).Value);
        schedule.BookendRotationCursor = 3;

        // Editing the round-robin SET (still RoundRobin) keeps the cursor position.
        schedule.ApplyUpdates(
            Option<string>.None, Option<string>.None, Option<string>.None, Option<string>.None,
            Option<bool>.None, Option<CoverageArea>.None, Option<string>.None, Option<string>.None,
            Option<double>.None, Option<int>.None, Option<int>.None,
            Option<string>.None, Option<string>.None,
            Option<BulletinContentSelection>.None, Option<IReadOnlyList<FillSource>>.None,
            Option<VoiceRotationPolicy>.None,
            Option<string>.None,
            Option<BookendRotationPolicy>.Some(
                BookendRotationPolicy.Create(BookendSelectionMode.RoundRobin, ["bk-1", "bk-2", "bk-3"], null).Value),
            Option<IReadOnlyDictionary<string, string>>.None,
            Option<ScheduleCalendar>.None,
            new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        schedule.BookendRotationCursor.ShouldBe(3);
    }

    [Fact]
    public void Clone_CopiesBookendFields()
    {
        var schedule = CreateSchedule();
        schedule.BookendId = "bk-1";
        schedule.BookendRotation = Option<BookendRotationPolicy>.Some(
            BookendRotationPolicy.Create(BookendSelectionMode.RoundRobin, ["bk-1", "bk-2"], null).Value);
        schedule.BookendRotationCursor = 4;
        schedule.BookendTokens = new Dictionary<string, string> { ["brand"] = "Nine" };

        var clone = schedule.Clone();

        clone.BookendId.ShouldBe("bk-1");
        clone.BookendRotation.HasValue.ShouldBeTrue();
        clone.BookendRotationCursor.ShouldBe(4);
        clone.BookendTokens["brand"].ShouldBe("Nine");
    }

    // ── Bulletin duration bounds ─────────────────────────────────────

    private static Result<MarketSchedule> CreateWithDuration(double seconds) =>
        MarketSchedule.Create("mkt-1", "Test Market", "TST-FM", "Australia/Sydney", "0 8 * * *",
            true, CoverageArea.Create(-33.87, 151.21, 25).Value, "voice-1", "/output",
            seconds, 15, 5,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Theory]
    [InlineData(15)]   // shortest real ARN slot
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(600)]  // upper bound inclusive
    [InlineData(5)]    // lower bound inclusive
    public void Create_WithARealisticDuration_Succeeds(double seconds)
    {
        CreateWithDuration(seconds).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(99999)]  // 27 hours — accepted and stored on dev before the bound
    [InlineData(601)]
    [InlineData(0.001)]  // a one-millisecond slot, also accepted before
    [InlineData(4.9)]
    public void Create_WithAnImpossibleDuration_IsRejected(double seconds)
    {
        var result = CreateWithDuration(seconds);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("TargetDurationSeconds");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Create_WithANonPositiveDuration_StillSaysPositive(double seconds)
    {
        // Pre-existing wording for the zero/negative case is kept: it is a different mistake from
        // "in range", and operators (and tests) already rely on it.
        CreateWithDuration(seconds).Error.ShouldContain("must be positive");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Alert-tier ordering (2026-08-10)
    //
    // The rule "escalation must fire closer to playout than the first notify"
    // lived ONLY in the console's zod schema, so the API, config import and
    // seeds could all persist a pair the console then refused to open: the
    // adelaide station on dev stored notify 5 / escalate 5, and its whole
    // studio was unsaveable behind a disabled button reading "Fix the
    // highlighted steps first". These lock the rule at the domain boundary,
    // AND lock the deliberate asymmetry that keeps a legacy row loadable.
    // ══════════════════════════════════════════════════════════════════════

    private static Result<MarketSchedule> CreateWithAlerts(int notify, int escalation)
    {
        return MarketSchedule.Create(
            "mkt-1", "Test Market", "TST-FM", "Australia/Sydney", "0 8 * * *", true,
            CoverageArea.Create(-33.87, 151.21, 25).Value, "voice-1", "/output", 60,
            notify, escalation,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private static Result MutateAlerts(MarketSchedule schedule, Option<int> notify, Option<int> escalation)
    {
        return schedule.ApplyUpdates(
            Option<string>.None, Option<string>.None, Option<string>.None, Option<string>.None,
            Option<bool>.None, Option<CoverageArea>.None, Option<string>.None, Option<string>.None,
            Option<double>.None, notify, escalation,
            Option<string>.None, Option<string>.None,
            Option<BulletinContentSelection>.None, Option<IReadOnlyList<FillSource>>.None,
            Option<VoiceRotationPolicy>.None, Option<string>.None,
            Option<BookendRotationPolicy>.None, Option<IReadOnlyDictionary<string, string>>.None,
            Option<ScheduleCalendar>.None,
            new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(15, 5)]
    [InlineData(2, 1)]
    [InlineData(120, 119)]
    public void Create_AcceptsEscalationStrictlyCloserToPlayout(int notify, int escalation)
    {
        CreateWithAlerts(notify, escalation).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(5, 5)]   // the exact adelaide row
    [InlineData(5, 15)]  // inverted
    [InlineData(0, 0)]
    public void Create_RejectsAlertTiersThatCollapseOrInvert(int notify, int escalation)
    {
        var result = CreateWithAlerts(notify, escalation);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("must be less than");
        // "must " is what ScheduleHttpTrigger keys on to answer 400 rather than 500.
        result.Error.ShouldContain("must ");
    }

    [Fact]
    public void Rehydrate_StillLoadsALegacyRowThatCreateWouldNowReject()
    {
        // Deliberate asymmetry. The repository maps stored rows through Rehydrate and treats a
        // failure as "corrupt, skip and log", so enforcing an admission rule here would make the
        // adelaide station silently vanish from the console AND the poller instead of showing up
        // where an operator can fix it. Do not "tidy" this into Create.
        var result = MarketSchedule.Rehydrate(
            "adelaide", "Adelaide Drive Traffic", "KIIS 102.3", "Australia/Adelaide", "0 8 * * *", true,
            CoverageArea.Create(-34.93, 138.6, 25).Value, "voice-1", "/output", 30,
            5, 5,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        result.IsSuccess.ShouldBeTrue();
        result.Value.NotifyBeforePlayoutMinutes.ShouldBe(5);
        result.Value.EscalationBeforePlayoutMinutes.ShouldBe(5);
    }

    [Fact]
    public void ApplyUpdates_RejectsLoweringNotifyOntoTheEscalationTier()
    {
        var schedule = CreateSchedule(); // 15 / 5

        var result = MutateAlerts(schedule, Option<int>.Some(5), Option<int>.None);

        result.IsFailure.ShouldBeTrue();
        // The pair must be judged on its RESULT, not on the one field supplied.
        result.Error.ShouldContain("must be less than");
        schedule.NotifyBeforePlayoutMinutes.ShouldBe(15);
        schedule.EscalationBeforePlayoutMinutes.ShouldBe(5);
    }

    [Fact]
    public void ApplyUpdates_RejectsRaisingEscalationPastNotify()
    {
        var schedule = CreateSchedule(); // 15 / 5

        var result = MutateAlerts(schedule, Option<int>.None, Option<int>.Some(20));

        result.IsFailure.ShouldBeTrue();
        schedule.EscalationBeforePlayoutMinutes.ShouldBe(5, "a rejected update must not half-apply");
    }

    [Fact]
    public void ApplyUpdates_AcceptsAValidPairSuppliedTogether()
    {
        var schedule = CreateSchedule();

        var result = MutateAlerts(schedule, Option<int>.Some(30), Option<int>.Some(10));

        result.IsSuccess.ShouldBeTrue();
        schedule.NotifyBeforePlayoutMinutes.ShouldBe(30);
        schedule.EscalationBeforePlayoutMinutes.ShouldBe(10);
    }

    [Fact]
    public void ApplyUpdates_CanRepairALegacyRowByRaisingNotifyAlone()
    {
        // The escape hatch an operator needs: adelaide loads as 5/5 and one edit fixes it.
        var legacy = MarketSchedule.Rehydrate(
            "adelaide", "Adelaide Drive Traffic", "KIIS 102.3", "Australia/Adelaide", "0 8 * * *", true,
            CoverageArea.Create(-34.93, 138.6, 25).Value, "voice-1", "/output", 30, 5, 5,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Value;

        var result = MutateAlerts(legacy, Option<int>.Some(15), Option<int>.None);

        result.IsSuccess.ShouldBeTrue();
        legacy.NotifyBeforePlayoutMinutes.ShouldBe(15);
        legacy.EscalationBeforePlayoutMinutes.ShouldBe(5);
    }

    // ── Playout arming ──

    /// <summary>
    ///     Every station that existed before this flag was delivering, and an unset Table column
    ///     rehydrates through this default. A false default would have taken live stations off air
    ///     the moment the build carrying the flag deployed.
    /// </summary>
    [Fact]
    public void PlayoutEnabled_DefaultsToArmed_OnCreateAndRehydrate()
    {
        CreateSchedule().PlayoutEnabled.ShouldBeTrue();

        var rehydrated = MarketSchedule.Rehydrate(
            "sydney", "Sydney Traffic", "KIIS 90s", "Australia/Sydney", "0 8 * * *", true,
            CoverageArea.Create(-33.87, 151.21, 25).Value, "voice-1", "/output", 30, 15, 5,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Value;

        rehydrated.PlayoutEnabled.ShouldBeTrue();
    }

    /// <summary>
    ///     Holding a station back must NOT pause it: it keeps generating, reviewing and publishing
    ///     here while another environment airs it. The two flags are independent on purpose.
    /// </summary>
    [Fact]
    public void ApplyUpdates_CanHoldPlayoutBackWithoutPausingTheStation()
    {
        var schedule = CreateSchedule();

        var result = MutatePlayout(schedule, Option<bool>.Some(false));

        result.IsSuccess.ShouldBeTrue();
        schedule.PlayoutEnabled.ShouldBeFalse();
        schedule.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void ApplyUpdates_OmittingPlayoutLeavesArmingUntouched()
    {
        var schedule = CreateSchedule();
        MutatePlayout(schedule, Option<bool>.Some(false));

        var result = MutateAlerts(schedule, Option<int>.Some(20), Option<int>.None);

        result.IsSuccess.ShouldBeTrue();
        schedule.PlayoutEnabled.ShouldBeFalse();
    }

    private static Result MutatePlayout(MarketSchedule schedule, Option<bool> playoutEnabled)
    {
        return schedule.ApplyUpdates(
            Option<string>.None, Option<string>.None, Option<string>.None, Option<string>.None,
            Option<bool>.None, Option<CoverageArea>.None, Option<string>.None, Option<string>.None,
            Option<double>.None, Option<int>.None, Option<int>.None,
            Option<string>.None, Option<string>.None,
            Option<BulletinContentSelection>.None, Option<IReadOnlyList<FillSource>>.None,
            Option<VoiceRotationPolicy>.None, Option<string>.None,
            Option<BookendRotationPolicy>.None, Option<IReadOnlyDictionary<string, string>>.None,
            Option<ScheduleCalendar>.None,
            new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            playoutEnabled: playoutEnabled);
    }
    [Fact]
    public void ContentSelection_UsesClassicOrderWhenSegmentOrderIsAbsent()
    {
        var selection = new BulletinContentSelection(Traffic: true, Weather: true, Fuel: true);

        selection.EffectiveSegmentOrder.ShouldBe(
            [BulletinSegment.Traffic, BulletinSegment.Weather, BulletinSegment.Fuel]);
    }

    [Fact]
    public void Create_RejectsDuplicateSegmentOrder()
    {
        var result = CreateWithSelection(new BulletinContentSelection(
            Traffic: true, Weather: true, Fuel: true,
            SegmentOrder: [BulletinSegment.Traffic, BulletinSegment.Traffic, BulletinSegment.Fuel]));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("SegmentOrder");
    }

    [Fact]
    public void Create_RejectsSegmentOrderNamingDisabledSource()
    {
        var result = CreateWithSelection(new BulletinContentSelection(
            Traffic: true, Weather: false, Fuel: true,
            SegmentOrder: [BulletinSegment.Traffic, BulletinSegment.Weather, BulletinSegment.Fuel]));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("SegmentOrder");
    }

    [Fact]
    public void Create_RejectsUnknownSegmentOrderValue()
    {
        var result = CreateWithSelection(new BulletinContentSelection(
            Traffic: true,
            SegmentOrder: [(BulletinSegment)99]));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("SegmentOrder");
    }

    [Fact]
    public void Rehydrate_AllowsLegacySelectionWithoutSegmentOrder()
    {
        var result = MarketSchedule.Rehydrate(
            "legacy", "Legacy", "LEG", "Australia/Sydney", "0 8 * * *", true,
            CoverageArea.Create(-33.87, 151.21, 25).Value, "voice-1", "/output", 30, 15, 5,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            contentSelection: Option<BulletinContentSelection>.Some(
                new BulletinContentSelection(Traffic: true, Weather: true)));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ContentSelection.Value.SegmentOrder.ShouldBeNull();
    }

    private static Result<MarketSchedule> CreateWithSelection(BulletinContentSelection selection) =>
        MarketSchedule.Create(
            "mkt-1", "Test Market", "TST-FM", "Australia/Sydney", "0 8 * * *", true,
            CoverageArea.Create(-33.87, 151.21, 25).Value, "voice-1", "/output", 60, 15, 5,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            contentSelection: Option<BulletinContentSelection>.Some(selection));
}
