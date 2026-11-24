using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Application.ConfigImport;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Application.Tests;

/// <summary>
///     Regression tests for the duplicate-id guards in the config-import plan builders (bug-bash
///     2026-08-05 F3): two entries sharing one id inside a single bundle section must be a plan
///     ERROR, never a silent collapse where the report claims Creates=N but only one record
///     survives. Each builder is exercised directly (the builders are internal; these tests are
///     the Application-layer home for their behaviour).
/// </summary>
public sealed class ConfigImportDuplicateIdTests
{
    private static readonly DateTime Now = new(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Lenient cron stub: parses everything and yields constant 30-minute gaps, so the
    /// schedule builder's cron-interval walk passes. The duplicate-market guard is the subject of
    /// these tests, not cron semantics (the real Cronos-backed parser is exercised elsewhere).</summary>
    private sealed class LenientCronParser : ICronParser
    {
        public bool TryParseCron(string expression) => true;

        public Option<DateTime> CalculateNextRun(string cronExpression, string timezone) =>
            Option<DateTime>.Some(new DateTime(2026, 1, 5, 0, 30, 0, DateTimeKind.Utc));

        public Option<DateTime> CalculateNextRun(string cronExpression, string timezone, DateTime baseTimeUtc) =>
            Option<DateTime>.Some(baseTimeUtc.AddMinutes(30));
    }

    // ── Fakes for the async builders' read paths ────────────────────

    private sealed class EmptyScheduleRepository : IScheduleRepository
    {
        public Task<Result<IEnumerable<MarketSchedule>>> ListSchedulesAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IEnumerable<MarketSchedule>>.Success([]));

        public Task<Result<MarketSchedule>> GetScheduleAsync(string marketId, CancellationToken ct = default) =>
            Task.FromResult(Result<MarketSchedule>.Failure($"Schedule '{marketId}' not found."));

        public Task<Result> SaveScheduleAsync(MarketSchedule schedule, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> DeleteScheduleAsync(string marketId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<IEnumerable<MarketSchedule>>> GetDueSchedulesAsync(
            DateTime thresholdUtc, CancellationToken ct = default) =>
            Task.FromResult(Result<IEnumerable<MarketSchedule>>.Success([]));

        public Task<Result> TryStartJobAsync(string marketId, string jobId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> CompleteJobAsync(
            string marketId, Option<DateTime> nextRunUtc, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class EmptyPriorityRoadsRepository : IPriorityRoadsRepository
    {
        public Task<Result<IReadOnlyList<PriorityRoad>>> GetRoadsAsync(
            string market, CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<PriorityRoad>>.Success([]));

        public Task<Result<IReadOnlyList<string>>> ListMarketsAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<string>>.Success([]));

        public Task<Result> SetRoadsAsync(
            string market, IReadOnlyList<PriorityRoad> roads, string updatedBy, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class EmptyFillerStatementsRepository : IFillerStatementsRepository
    {
        public Task<Result<IReadOnlyList<FillerStatement>>> GetStatementsAsync(
            string market, CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<FillerStatement>>.Success([]));

        public Task<Result<IReadOnlyList<string>>> ListMarketsAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<string>>.Success([]));

        public Task<Result> SetStatementsAsync(
            string market, IReadOnlyList<FillerStatement> statements, string updatedBy,
            CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class EmptySafetyTermRepository : ISafetyTermRepository
    {
        public Task<Result<IReadOnlyList<SafetyTerm>>> GetForMarketAsync(
            string? market, CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<SafetyTerm>>.Success([]));

        public Task<Result<IReadOnlyList<SafetyTerm>>> ListAllAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<SafetyTerm>>.Success([]));

        public Task<Result> UpsertAsync(SafetyTerm term, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> DeleteAsync(
            SafetyScope scope, string scopeId, string id, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class EmptyGuidanceRepository : IGuidanceRepository
    {
        public Task<Result<IReadOnlyList<GuidanceEntry>>> GetForScopesAsync(
            string market, Option<string> station, CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<GuidanceEntry>>.Success([]));

        public Task<Result<IReadOnlyList<GuidanceEntry>>> ListAsync(
            GuidanceScope scope, string scopeId, CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<GuidanceEntry>>.Success([]));

        public Task<Result> UpsertAsync(GuidanceEntry entry, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<bool>> DeleteAsync(
            GuidanceScope scope, string scopeId, string id, CancellationToken ct = default) =>
            Task.FromResult(Result<bool>.Success(true));
    }

    // A context with every field null except the ones the section under test touches at plan
    // time. The sync library builders never dereference the context during planning (their apply
    // thunks are deferred), so nulls are safe there; the async builders get the fakes above.
    private static ConfigImportContext Context(
        IScheduleRepository? scheduleRepository = null,
        IScheduleService? scheduleService = null,
        ISafetyTermRepository? safetyTerms = null,
        IGuidanceRepository? guidance = null,
        IPriorityRoadsRepository? priorityRoads = null,
        IFillerStatementsRepository? fillerStatements = null) => new(
        scheduleRepository ?? null!,
        scheduleService ?? null!,
        VoiceProfiles: null!, DeliveryStyles: null!, Playbooks: null!, Bookends: null!, PronunciationDictionaries: null!,
        SafetyTerms: safetyTerms ?? null!,
        Guidance: guidance ?? null!,
        PriorityRoads: priorityRoads ?? null!,
        VerifiedPronunciations: null!, OperatorAssignments: null!, VoiceCalibrations: null!,
        FillerStatements: fillerStatements ?? null!,
        FillerAudioCache: null!,
        TimeProvider: new FixedTimeProvider(Now),
        Logger: NullLogger<ConfigImportService>.Instance);

    // ── Library builders ────────────────────────────────────────────

    [Fact]
    public void VoiceProfilePlanBuilder_DuplicatedId_IsAPlanError()
    {
        var items = new List<PlannedConfigItem>();
        var dtos = new List<ConfigVoiceProfileDto>
        {
            new(Id: "vp-dup", Name: "One", BaseVoiceId: "eleven_base", Model: "eleven_v3",
                Settings: new VoiceSettingsDto()),
            new(Id: "vp-dup", Name: "Two", BaseVoiceId: "eleven_base", Model: "eleven_v3",
                Settings: new VoiceSettingsDto())
        };

        VoiceProfilePlanBuilder.Plan(Context(), dtos, [], "test", items, new HashSet<string>());

        items[0].Report.Action.ShouldBe(ConfigImportActions.Create);
        items[1].Report.Action.ShouldBe(ConfigImportActions.Error);
        items[1].Report.Detail.ShouldBe("Each voice profile id may appear only once.");
    }

    [Fact]
    public void DeliveryStylePlanBuilder_DuplicatedId_IsAPlanError()
    {
        var items = new List<PlannedConfigItem>();
        var dtos = new List<ConfigDeliveryStyleDto>
        {
            new(Id: "ds-dup", Name: "One", Policy: new DeliveryStylePolicyDto()),
            new(Id: "ds-dup", Name: "Two", Policy: new DeliveryStylePolicyDto())
        };

        DeliveryStylePlanBuilder.Plan(Context(), dtos, [], "test", items, new HashSet<string>());

        items[0].Report.Action.ShouldBe(ConfigImportActions.Create);
        items[1].Report.Action.ShouldBe(ConfigImportActions.Error);
        items[1].Report.Detail.ShouldBe("Each delivery style id may appear only once.");
    }

    [Fact]
    public void BookendPlanBuilder_DuplicatedId_IsAPlanError()
    {
        var items = new List<PlannedConfigItem>();
        var dtos = new List<ConfigBookendDto>
        {
            new(Id: "bk-dup", Name: "One", IntroText: "Intro", OutroText: "Outro"),
            new(Id: "bk-dup", Name: "Two", IntroText: "Intro", OutroText: "Outro")
        };

        BookendPlanBuilder.Plan(Context(), dtos, [], "test", items, new HashSet<string>());

        items[0].Report.Action.ShouldBe(ConfigImportActions.Create);
        items[1].Report.Action.ShouldBe(ConfigImportActions.Error);
        items[1].Report.Detail.ShouldBe("Each bookend id may appear only once.");
    }

    [Fact]
    public void PronunciationDictionaryPlanBuilder_DuplicatedId_IsAPlanError()
    {
        var items = new List<PlannedConfigItem>();
        var dtos = new List<ConfigPronunciationDictionaryDto>
        {
            new(Id: "pd-dup", Name: "One", Rules:
                [new PronunciationRuleDto(StringToReplace: "x", Replacement: "y")]),
            new(Id: "pd-dup", Name: "Two", Rules:
                [new PronunciationRuleDto(StringToReplace: "x", Replacement: "y")])
        };

        PronunciationDictionaryPlanBuilder.Plan(
            Context(), dtos, [], "test", items, new HashSet<string>(), new HashSet<string>());

        items[0].Report.Action.ShouldBe(ConfigImportActions.Create);
        items[1].Report.Action.ShouldBe(ConfigImportActions.Error);
        items[1].Report.Detail.ShouldBe("Each pronunciation dictionary id may appear only once.");
    }

    // ── Schedules ───────────────────────────────────────────────────

    [Fact]
    public async Task SchedulePlanBuilder_DuplicatedMarketId_IsAPlanError()
    {
        var scheduleService = new ScheduleService(
            new EmptyScheduleRepository(), new LenientCronParser(), new FixedTimeProvider(Now),
            NullLogger<ScheduleService>.Instance);
        var items = new List<PlannedConfigItem>();
        var dtos = new List<ConfigScheduleDto>
        {
            ValidSchedule("probe-market"),
            ValidSchedule("probe-market") with { Name = "Probe Two" }
        };

        var result = await SchedulePlanBuilder.PlanAsync(
            Context(scheduleRepository: new EmptyScheduleRepository(), scheduleService: scheduleService),
            dtos, new HashSet<string> { "vp-1" }, new HashSet<string>(), new HashSet<string>(),
            new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), items, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        items[0].Report.Action.ShouldBe(ConfigImportActions.Create);
        items[1].Report.Action.ShouldBe(ConfigImportActions.Error);
        items[1].Report.Detail.ShouldBe("Each schedule market id may appear only once.");
    }

    private static ConfigScheduleDto ValidSchedule(string marketId) => new(
        MarketId: marketId, Name: "Probe", Station: "ZZZ 100.1",
        Timezone: "Australia/Sydney", CronExpression: "0 7 * * 1-5", Enabled: false,
        TargetDurationSeconds: 30, NotifyBeforePlayoutMinutes: 15, EscalationBeforePlayoutMinutes: 5,
        VoiceProfileId: "vp-1",
        Coverage: new CoverageDto(-33.85, 151.20, 5.0));

    [Fact]
    public async Task SchedulePlanBuilder_CoverageOutsideOperatingRegion_IsAPlanError()
    {
        // Parity with the interactive PUT (bug-bash #16 class): the PUT rejects coverage
        // outside the operating region; the import path must too, so a hand/AI-authored
        // bundle can't persist geometry the interactive API would refuse (a Paris polygon
        // would silently fetch and air the wrong region's incidents).
        var scheduleService = new ScheduleService(
            new EmptyScheduleRepository(), new LenientCronParser(), new FixedTimeProvider(Now),
            NullLogger<ScheduleService>.Instance);
        var items = new List<PlannedConfigItem>();
        var dtos = new List<ConfigScheduleDto>
        {
            ValidSchedule("probe-market") with { Coverage = new CoverageDto(48.85, 2.35, 50) }
        };

        var result = await SchedulePlanBuilder.PlanAsync(
            Context(scheduleRepository: new EmptyScheduleRepository(), scheduleService: scheduleService),
            dtos, new HashSet<string>(), new HashSet<string>(), new HashSet<string>(),
            new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), items, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var item = items.Single();
        item.Report.Action.ShouldBe(ConfigImportActions.Error);
        item.Report.Detail.ShouldNotBeNull();
        item.Report.Detail!.ShouldContain("operating region");
    }

    // ── Per-market replace-list sections ────────────────────────────

    [Fact]
    public async Task PriorityRoadsPlanBuilder_DuplicatedMarket_IsAPlanError()
    {
        var items = new List<PlannedConfigItem>();
        var dtos = new List<PriorityRoadsDto>
        {
            new("sydney", [new PriorityRoadDto("M1 Pacific Motorway", true, "high", true)]),
            new("sydney", [new PriorityRoadDto("M2 Hills Motorway", true, "normal", true)])
        };

        var result = await PriorityRoadsPlanBuilder.PlanAsync(
            Context(priorityRoads: new EmptyPriorityRoadsRepository()), dtos, "test", items,
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        items[0].Report.Action.ShouldBe(ConfigImportActions.Create);
        items[1].Report.Action.ShouldBe(ConfigImportActions.Error);
        items[1].Report.Detail.ShouldBe("Each market may appear only once.");
    }

    [Fact]
    public async Task FillerStatementsPlanBuilder_DuplicatedMarket_IsAPlanError()
    {
        var items = new List<PlannedConfigItem>();
        var dtos = new List<FillerStatementsDto>
        {
            new("sydney", [new FillerStatementDto("s1", "Stay safe.")]),
            new("sydney", [new FillerStatementDto("s2", "Take it easy.")])
        };

        var result = await FillerStatementsPlanBuilder.PlanAsync(
            Context(fillerStatements: new EmptyFillerStatementsRepository()), dtos, "test", items,
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        items[0].Report.Action.ShouldBe(ConfigImportActions.Create);
        items[1].Report.Action.ShouldBe(ConfigImportActions.Error);
        items[1].Report.Detail.ShouldBe("Each market may appear only once.");
    }

    [Fact]
    public async Task FillerStatementsPlanBuilder_DuplicatedStatementIdWithinMarket_IsAPlanError()
    {
        // The previous build silently deduped two statements sharing an id (first one won) —
        // the same silent-collapse class as a duplicated section id.
        var items = new List<PlannedConfigItem>();
        var dtos = new List<FillerStatementsDto>
        {
            new("sydney",
            [
                new FillerStatementDto("s1", "Stay safe."),
                new FillerStatementDto("s1", "Take it easy.")
            ])
        };

        var result = await FillerStatementsPlanBuilder.PlanAsync(
            Context(fillerStatements: new EmptyFillerStatementsRepository()), dtos, "test", items,
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var item = items.Single();
        item.Report.Action.ShouldBe(ConfigImportActions.Error);
        var detail = item.Report.Detail;
        detail.ShouldNotBeNull();
        detail.ShouldContain("Each filler statement id may appear only once");
        detail.ShouldContain("s1");
    }

    [Fact]
    public async Task SafetyTermsPlanBuilder_DuplicatedId_IsAPlanError()
    {
        var items = new List<PlannedConfigItem>();
        var dtos = new List<ConfigSafetyTermDto>
        {
            new(Id: "st-dup", Kind: "Banned", Scope: "Global", Term: "word one"),
            new(Id: "st-dup", Kind: "Banned", Scope: "Global", Term: "word two")
        };

        var result = await SafetyTermsPlanBuilder.PlanAsync(
            Context(safetyTerms: new EmptySafetyTermRepository()), dtos, "test", items,
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        items[0].Report.Action.ShouldBe(ConfigImportActions.Create);
        items[1].Report.Action.ShouldBe(ConfigImportActions.Error);
        items[1].Report.Detail.ShouldBe("Each safety term id may appear only once.");
    }

    [Fact]
    public async Task GuidancePlanBuilder_DuplicatedIdInSameScope_IsAPlanError()
    {
        var items = new List<PlannedConfigItem>();
        var dtos = new List<ConfigGuidanceDto>
        {
            new(Id: "g-dup", Scope: "Global", Type: "Style", Text: "First directive"),
            new(Id: "g-dup", Scope: "Global", Type: "Style", Text: "Second directive")
        };

        var result = await GuidancePlanBuilder.PlanAsync(
            Context(guidance: new EmptyGuidanceRepository()), dtos, "test", items, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        items[0].Report.Action.ShouldBe(ConfigImportActions.Create);
        items[1].Report.Action.ShouldBe(ConfigImportActions.Error);
        items[1].Report.Detail.ShouldBe("Each guidance id may appear only once.");
    }

    [Fact]
    public async Task GuidancePlanBuilder_SameIdInDifferentScopes_IsNotADuplicate()
    {
        // Guidance rows are keyed (scope partition, id), so an id reused across scopes is two
        // distinct entries — the guard must be per scope bucket.
        var items = new List<PlannedConfigItem>();
        var dtos = new List<ConfigGuidanceDto>
        {
            new(Id: "g-1", Scope: "Global", Type: "Style", Text: "Global directive"),
            new(Id: "g-1", Scope: "Market", ScopeId: "sydney", Type: "Style", Text: "Sydney directive")
        };

        var result = await GuidancePlanBuilder.PlanAsync(
            Context(guidance: new EmptyGuidanceRepository()), dtos, "test", items, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        items.Count.ShouldBe(2);
        items.ShouldAllBe(i => i.Report.Action == ConfigImportActions.Create);
    }
}
