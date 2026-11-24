using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.ConfigTransfer;

/// <summary>
///     Reads the current configuration needed by one export. Section reads are independent unless
///     they need the schedule-derived market/scope universe; those dependencies are loaded first.
///     Every storage call shares one small semaphore, so a large per-market section cannot fan out
///     an unbounded number of table requests. The resulting DTO remains import-shaped and contains
///     current state only.
/// </summary>
internal sealed class ConfigSnapshotReader(
    IScheduleRepository scheduleRepository,
    IVoiceProfileService voiceProfiles,
    IDeliveryStyleService deliveryStyles,
    IBookendService bookends,
    IPronunciationDictionaryService pronunciationDictionaries,
    ISafetyTermRepository safetyTerms,
    IGuidanceRepository guidance,
    IPriorityRoadsRepository priorityRoads,
    IVerifiedPronunciationRepository verifiedPronunciations,
    IOperatorAssignmentRepository operatorAssignments,
    IVoiceCalibrationRepository voiceCalibrations,
    IFillerStatementsRepository fillerStatements,
    TimeProvider timeProvider)
{
    private const int MaxParallelReads = 4;

    public async Task<Result<ConfigBundleDto>> ReadAsync(
        IReadOnlySet<string> sections, string schemaUrl, CancellationToken ct)
    {
        var gate = new SemaphoreSlim(MaxParallelReads, MaxParallelReads);
        var dependentOnSchedules = sections.Contains(ConfigSections.Guidance)
            || sections.Contains(ConfigSections.PriorityRoads)
            || sections.Contains(ConfigSections.VerifiedPronunciations)
            || sections.Contains(ConfigSections.FillerStatements);
        var needsSchedules = dependentOnSchedules || sections.Contains(ConfigSections.Schedules);

        Task<Result<IReadOnlyList<MarketSchedule>>>? schedulesTask = needsSchedules
            ? ReadSchedulesAsync(gate, ct)
            : null;

        // Start every section that does not need the schedule-derived scope universe immediately.
        // The schedule task is intentionally started above as well when it is an independent
        // requested section, so e.g. Schedules + VoiceProfiles do not serialize their reads.
        var reads = new Dictionary<string, Task<Result<object>>>(StringComparer.OrdinalIgnoreCase);
        if (sections.Contains(ConfigSections.VoiceProfiles))
            reads[ConfigSections.VoiceProfiles] = Box(ReadVoiceProfilesAsync(gate, ct));
        if (sections.Contains(ConfigSections.DeliveryStyles))
            reads[ConfigSections.DeliveryStyles] = Box(ReadDeliveryStylesAsync(gate, ct));
        if (sections.Contains(ConfigSections.Bookends))
            reads[ConfigSections.Bookends] = Box(ReadBookendsAsync(gate, ct));
        if (sections.Contains(ConfigSections.PronunciationDictionaries))
            reads[ConfigSections.PronunciationDictionaries] = Box(ReadPronunciationDictionariesAsync(gate, ct));
        if (sections.Contains(ConfigSections.SafetyTerms))
            reads[ConfigSections.SafetyTerms] = Box(ReadSafetyTermsAsync(gate, ct));
        if (sections.Contains(ConfigSections.OperatorAssignments))
            reads[ConfigSections.OperatorAssignments] = Box(ReadOperatorAssignmentsAsync(gate, ct));
        if (sections.Contains(ConfigSections.VoiceCalibrations))
            reads[ConfigSections.VoiceCalibrations] = Box(ReadVoiceCalibrationsAsync(gate, ct));

        IReadOnlyList<MarketSchedule> schedules = [];
        if (dependentOnSchedules)
        {
            // Do not start schedule-dependent reads until the dependency has succeeded. Any
            // already-started independent reads are drained before returning a dependency failure,
            // avoiding background storage work after this method has completed.
            var scheduleResult = await schedulesTask!;
            if (scheduleResult.IsFailure)
            {
                await DrainAsync(reads.Values);
                return Result<ConfigBundleDto>.Failure($"Failed to list schedules: {scheduleResult.Error}");
            }

            schedules = scheduleResult.Value.ToList();
            AddScheduleDependentReads(reads, schedules, sections, gate, ct);
        }
        else if (schedulesTask is not null)
        {
            // Schedules is itself requested but no other section needs its market universe.
            reads[ConfigSections.Schedules] = Box(schedulesTask);
        }

        await DrainAsync(reads.Values);

        // Inspect results in the stable section order rather than completion order. This keeps
        // failure selection and field ordering deterministic while the I/O itself is concurrent.
        foreach (var section in ConfigSections.All)
        {
            if (!reads.TryGetValue(section, out var task))
                continue;
            var result = await task;
            if (result.IsFailure)
                return Result<ConfigBundleDto>.Failure(result.Error);
        }

        List<ConfigVoiceProfileDto>? voiceProfileDtos = Get<List<ConfigVoiceProfileDto>>(reads, ConfigSections.VoiceProfiles);
        List<ConfigDeliveryStyleDto>? deliveryStyleDtos = Get<List<ConfigDeliveryStyleDto>>(reads, ConfigSections.DeliveryStyles);
        List<ConfigBookendDto>? bookendDtos = Get<List<ConfigBookendDto>>(reads, ConfigSections.Bookends);
        List<ConfigPronunciationDictionaryDto>? dictionaryDtos =
            Get<List<ConfigPronunciationDictionaryDto>>(reads, ConfigSections.PronunciationDictionaries);
        List<ConfigSafetyTermDto>? safetyTermDtos = Get<List<ConfigSafetyTermDto>>(reads, ConfigSections.SafetyTerms);
        List<ConfigGuidanceDto>? guidanceDtos = Get<List<ConfigGuidanceDto>>(reads, ConfigSections.Guidance);
        List<PriorityRoadsDto>? roadsDtos = Get<List<PriorityRoadsDto>>(reads, ConfigSections.PriorityRoads);
        List<ConfigVerifiedPronunciationsDto>? verifiedPronunciationDtos =
            Get<List<ConfigVerifiedPronunciationsDto>>(reads, ConfigSections.VerifiedPronunciations);
        List<ConfigOperatorAssignmentDto>? operatorAssignmentDtos =
            Get<List<ConfigOperatorAssignmentDto>>(reads, ConfigSections.OperatorAssignments);
        List<ConfigVoiceCalibrationDto>? voiceCalibrationDtos =
            Get<List<ConfigVoiceCalibrationDto>>(reads, ConfigSections.VoiceCalibrations);
        List<FillerStatementsDto>? fillerDtos = Get<List<FillerStatementsDto>>(reads, ConfigSections.FillerStatements);
        List<ConfigScheduleDto>? scheduleDtos = Get<List<ConfigScheduleDto>>(reads, ConfigSections.Schedules);

        return Result<ConfigBundleDto>.Success(new ConfigBundleDto(
            Schema: schemaUrl,
            SchemaVersion: 1,
            ExportedAtUtc: timeProvider.GetUtcNow().UtcDateTime.ToString("o"),
            Source: Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "local",
            VoiceProfiles: voiceProfileDtos,
            DeliveryStyles: deliveryStyleDtos,
            Bookends: bookendDtos,
            PronunciationDictionaries: dictionaryDtos,
            SafetyTerms: safetyTermDtos,
            Guidance: guidanceDtos,
            PriorityRoads: roadsDtos,
            VerifiedPronunciations: verifiedPronunciationDtos,
            OperatorAssignments: operatorAssignmentDtos,
            VoiceCalibrations: voiceCalibrationDtos,
            FillerStatements: fillerDtos,
            Schedules: scheduleDtos));
    }

    private void AddScheduleDependentReads(
        IDictionary<string, Task<Result<object>>> reads,
        IReadOnlyList<MarketSchedule> schedules,
        IReadOnlySet<string> sections,
        SemaphoreSlim gate,
        CancellationToken ct)
    {
        if (sections.Contains(ConfigSections.Guidance))
            reads[ConfigSections.Guidance] = Box(ReadGuidanceAsync(schedules, gate, ct));
        if (sections.Contains(ConfigSections.PriorityRoads))
            reads[ConfigSections.PriorityRoads] = Box(ReadPriorityRoadsAsync(schedules, gate, ct));
        if (sections.Contains(ConfigSections.VerifiedPronunciations))
            reads[ConfigSections.VerifiedPronunciations] = Box(ReadVerifiedPronunciationsAsync(schedules, gate, ct));
        if (sections.Contains(ConfigSections.FillerStatements))
            reads[ConfigSections.FillerStatements] = Box(ReadFillerStatementsAsync(schedules, gate, ct));
        if (sections.Contains(ConfigSections.Schedules))
            reads[ConfigSections.Schedules] = Box(Task.FromResult(Result<List<ConfigScheduleDto>>.Success(
                schedules.OrderBy(s => s.MarketId, StringComparer.OrdinalIgnoreCase)
                    .Select(s => s.ToConfigScheduleDto()).ToList())));
    }

    private async Task<Result<IReadOnlyList<MarketSchedule>>> ReadSchedulesAsync(
        SemaphoreSlim gate, CancellationToken ct)
    {
        var result = await BoundedAsync(gate, c => scheduleRepository.ListSchedulesAsync(c), ct);
        return result.IsFailure
            ? Result<IReadOnlyList<MarketSchedule>>.Failure($"Failed to list schedules: {result.Error}")
            : Result<IReadOnlyList<MarketSchedule>>.Success(result.Value.ToList());
    }

    private async Task<Result<List<ConfigVoiceProfileDto>>> ReadVoiceProfilesAsync(
        SemaphoreSlim gate, CancellationToken ct)
    {
        var result = await BoundedAsync(gate,
            c => voiceProfiles.ListAsync(VersionedLibraryListQuery.CurrentSummary, c), ct);
        if (result.IsFailure)
            return Result<List<ConfigVoiceProfileDto>>.Failure($"Failed to list voice profiles: {result.Error}");
        return Result<List<ConfigVoiceProfileDto>>.Success(result.Value.Items.Select(ToDto).ToList());
    }

    private async Task<Result<List<ConfigDeliveryStyleDto>>> ReadDeliveryStylesAsync(
        SemaphoreSlim gate, CancellationToken ct)
    {
        var result = await BoundedAsync(gate,
            c => deliveryStyles.ListAsync(VersionedLibraryListQuery.CurrentSummary, c), ct);
        if (result.IsFailure)
            return Result<List<ConfigDeliveryStyleDto>>.Failure($"Failed to list delivery styles: {result.Error}");
        return Result<List<ConfigDeliveryStyleDto>>.Success(result.Value.Items.Select(ToDto).ToList());
    }

    private async Task<Result<List<ConfigBookendDto>>> ReadBookendsAsync(
        SemaphoreSlim gate, CancellationToken ct)
    {
        var result = await BoundedAsync(gate,
            c => bookends.ListAsync(VersionedLibraryListQuery.CurrentSummary, c), ct);
        if (result.IsFailure)
            return Result<List<ConfigBookendDto>>.Failure($"Failed to list bookends: {result.Error}");
        return Result<List<ConfigBookendDto>>.Success(result.Value.Items.Select(ToDto).ToList());
    }

    private async Task<Result<List<ConfigPronunciationDictionaryDto>>> ReadPronunciationDictionariesAsync(
        SemaphoreSlim gate, CancellationToken ct)
    {
        var result = await BoundedAsync(gate,
            c => pronunciationDictionaries.ListAsync(VersionedLibraryListQuery.CurrentSummary, c), ct);
        if (result.IsFailure)
            return Result<List<ConfigPronunciationDictionaryDto>>.Failure(
                $"Failed to list pronunciation dictionaries: {result.Error}");
        return Result<List<ConfigPronunciationDictionaryDto>>.Success(result.Value.Items.Select(ToDto).ToList());
    }

    private async Task<Result<List<ConfigSafetyTermDto>>> ReadSafetyTermsAsync(
        SemaphoreSlim gate, CancellationToken ct)
    {
        var result = await BoundedAsync(gate, c => safetyTerms.ListAllAsync(c), ct);
        if (result.IsFailure)
            return Result<List<ConfigSafetyTermDto>>.Failure($"Failed to list safety terms: {result.Error}");
        return Result<List<ConfigSafetyTermDto>>.Success(result.Value.Select(t => new ConfigSafetyTermDto(
            t.Id, t.Kind.ToString(), t.Scope.ToString(),
            string.IsNullOrEmpty(t.ScopeId) ? null : t.ScopeId,
            t.Term, t.Note.Match(n => (string?)n, () => null), t.Enabled)).ToList());
    }

    private async Task<Result<List<ConfigOperatorAssignmentDto>>> ReadOperatorAssignmentsAsync(
        SemaphoreSlim gate, CancellationToken ct)
    {
        var result = await BoundedAsync(gate, c => operatorAssignments.ListAsync(c), ct);
        if (result.IsFailure)
            return Result<List<ConfigOperatorAssignmentDto>>.Failure(
                $"Failed to list operator assignments: {result.Error}");
        return Result<List<ConfigOperatorAssignmentDto>>.Success(result.Value.Select(a => new ConfigOperatorAssignmentDto(
            a.ObjectId, a.DisplayName, a.Markets.ToList(), a.UpdatedAtUtc, a.UpdatedBy)).ToList());
    }

    private async Task<Result<List<ConfigVoiceCalibrationDto>>> ReadVoiceCalibrationsAsync(
        SemaphoreSlim gate, CancellationToken ct)
    {
        var result = await BoundedAsync(gate, c => voiceCalibrations.ListAsync(c), ct);
        if (result.IsFailure)
            return Result<List<ConfigVoiceCalibrationDto>>.Failure(
                $"Failed to list voice calibrations: {result.Error}");
        return Result<List<ConfigVoiceCalibrationDto>>.Success(result.Value.Select(c => new ConfigVoiceCalibrationDto(
            c.ConfigurationKey, c.VoiceId,
            new VoiceSettingsDto(c.Settings.Speed, c.Settings.Stability, c.Settings.SimilarityBoost,
                c.Settings.Style, c.Settings.UseSpeakerBoost),
            c.Provider, c.SyllablesPerSecond, c.SyllablesPerWord, c.WordsPerMinute,
            c.SyllablesPerMinute, c.RunCount, c.ReferenceSyllableCount,
            c.ReferenceDurationSeconds, c.CalibratedAt, c.ValidFor.TotalDays)).ToList());
    }

    private async Task<Result<List<ConfigGuidanceDto>>> ReadGuidanceAsync(
        IReadOnlyList<MarketSchedule> schedules, SemaphoreSlim gate, CancellationToken ct)
    {
        var scopes = new List<(GuidanceScope Scope, string ScopeId)> { (GuidanceScope.Global, string.Empty) };
        scopes.AddRange(schedules
            .Select(s => s.MarketId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(m => (GuidanceScope.Market, m)));
        scopes.AddRange(schedules
            .Select(s => GuidanceEntry.NormalizeScopeId(GuidanceScope.Station, s.Station))
            .Where(st => !string.IsNullOrWhiteSpace(st))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(st => (GuidanceScope.Station, st)));

        var tasks = scopes.Select(async scope =>
        {
            var result = await BoundedAsync(gate, c => guidance.ListAsync(scope.Scope, scope.ScopeId, c), ct);
            if (result.IsFailure)
                return Result<(GuidanceScope Scope, string ScopeId, IReadOnlyList<GuidanceEntry> Entries)>.Failure(
                    $"Failed to list {scope.Scope} guidance ('{scope.ScopeId}'): {result.Error}");
            return Result<(GuidanceScope Scope, string ScopeId, IReadOnlyList<GuidanceEntry> Entries)>.Success(
                (scope.Scope, scope.ScopeId, result.Value));
        }).ToArray();
        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
            if (result.IsFailure)
                return Result<List<ConfigGuidanceDto>>.Failure(result.Error);

        var dtos = new List<ConfigGuidanceDto>();
        foreach (var result in results)
            dtos.AddRange(result.Value.Entries.Select(e => new ConfigGuidanceDto(
                e.Id, e.Scope.ToString(),
                string.IsNullOrEmpty(e.ScopeId) ? null : e.ScopeId,
                e.Type.ToString(), e.Text, e.Enabled,
                e.ReviewAfterUtc.Match(d => (DateTime?)d, () => null))));
        return Result<List<ConfigGuidanceDto>>.Success(dtos);
    }

    private async Task<Result<List<PriorityRoadsDto>>> ReadPriorityRoadsAsync(
        IReadOnlyList<MarketSchedule> schedules, SemaphoreSlim gate, CancellationToken ct)
    {
        var marketsResult = await BoundedAsync(gate, c => priorityRoads.ListMarketsAsync(c), ct);
        if (marketsResult.IsFailure)
            return Result<List<PriorityRoadsDto>>.Failure(
                $"Failed to list markets with priority roads: {marketsResult.Error}");

        var markets = SectionMarkets(schedules, marketsResult.Value);
        var rowsResult = await BoundedAsync(
            gate, c => priorityRoads.GetRoadsForMarketsAsync(markets, c), ct);
        if (rowsResult.IsFailure)
            return Result<List<PriorityRoadsDto>>.Failure(
                $"Failed to read priority roads: {rowsResult.Error}");

        var dtos = new List<PriorityRoadsDto>();
        foreach (var market in markets)
        {
            if (!rowsResult.Value.TryGetValue(market, out var result))
                return Result<List<PriorityRoadsDto>>.Failure(
                    $"Priority roads reader omitted requested market '{market}'.");
            if (result.IsFailure)
                return Result<List<PriorityRoadsDto>>.Failure(
                    $"Failed to read priority roads for '{market}': {result.Error}");
            if (result.Value.Count > 0)
                dtos.Add(new PriorityRoadsDto(market, PriorityRoadsHttpTrigger.ToDtos(result.Value)));
        }
        return Result<List<PriorityRoadsDto>>.Success(dtos);
    }

    private async Task<Result<List<ConfigVerifiedPronunciationsDto>>> ReadVerifiedPronunciationsAsync(
        IReadOnlyList<MarketSchedule> schedules, SemaphoreSlim gate, CancellationToken ct)
    {
        var marketsResult = await BoundedAsync(gate, c => verifiedPronunciations.ListMarketsAsync(c), ct);
        if (marketsResult.IsFailure)
            return Result<List<ConfigVerifiedPronunciationsDto>>.Failure(
                $"Failed to list markets with verified pronunciations: {marketsResult.Error}");

        var markets = SectionMarkets(schedules, marketsResult.Value);
        var rowsResult = await BoundedAsync(
            gate, c => verifiedPronunciations.GetForMarketsAsync(markets, c), ct);
        if (rowsResult.IsFailure)
            return Result<List<ConfigVerifiedPronunciationsDto>>.Failure(
                $"Failed to read verified pronunciations: {rowsResult.Error}");

        var dtos = new List<ConfigVerifiedPronunciationsDto>();
        foreach (var market in markets)
        {
            if (!rowsResult.Value.TryGetValue(market, out var result))
                return Result<List<ConfigVerifiedPronunciationsDto>>.Failure(
                    $"Verified pronunciations reader omitted requested market '{market}'.");
            if (result.IsFailure)
                return Result<List<ConfigVerifiedPronunciationsDto>>.Failure(
                    $"Failed to read verified pronunciations for '{market}': {result.Error}");
            if (result.Value.Count > 0)
                dtos.Add(new ConfigVerifiedPronunciationsDto(
                    market,
                    result.Value.Select(w => new VerifiedPronunciationDto(
                        w.Word, w.Note, w.Enabled, w.VerifiedBy,
                        w.VerifiedAtUtc == default ? null : w.VerifiedAtUtc.ToString("O"))).ToList()));
        }
        return Result<List<ConfigVerifiedPronunciationsDto>>.Success(dtos);
    }

    private async Task<Result<List<FillerStatementsDto>>> ReadFillerStatementsAsync(
        IReadOnlyList<MarketSchedule> schedules, SemaphoreSlim gate, CancellationToken ct)
    {
        var marketsResult = await BoundedAsync(gate, c => fillerStatements.ListMarketsAsync(c), ct);
        if (marketsResult.IsFailure)
            return Result<List<FillerStatementsDto>>.Failure(
                $"Failed to list markets with filler statements: {marketsResult.Error}");

        var markets = SectionMarkets(schedules, marketsResult.Value);
        var rowsResult = await BoundedAsync(
            gate, c => fillerStatements.GetStatementsForMarketsAsync(markets, c), ct);
        if (rowsResult.IsFailure)
            return Result<List<FillerStatementsDto>>.Failure(
                $"Failed to read filler statements: {rowsResult.Error}");

        var dtos = new List<FillerStatementsDto>();
        foreach (var market in markets)
        {
            if (!rowsResult.Value.TryGetValue(market, out var result))
                return Result<List<FillerStatementsDto>>.Failure(
                    $"Filler statements reader omitted requested market '{market}'.");
            if (result.IsFailure)
                return Result<List<FillerStatementsDto>>.Failure(
                    $"Failed to read filler statements for '{market}': {result.Error}");
            if (result.Value.Count > 0)
                dtos.Add(new FillerStatementsDto(
                    market, FillerStatementsHttpTrigger.ToDtos(result.Value)));
        }
        return Result<List<FillerStatementsDto>>.Success(dtos);
    }

    private static List<string> SectionMarkets(
        IReadOnlyList<MarketSchedule> schedules, IReadOnlyList<string> extraMarkets) =>
        schedules.Select(s => s.MarketId)
            .Concat(extraMarkets)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static ConfigVoiceProfileDto ToDto(VoiceProfile profile)
    {
        var current = profile.Versions[^1];
        return new ConfigVoiceProfileDto(
            profile.Id, profile.Name, profile.Description, profile.BaseVoiceId, profile.BaseVoiceName,
            current.Model,
            new VoiceSettingsDto(current.Settings.Speed, current.Settings.Stability,
                current.Settings.SimilarityBoost, current.Settings.Style, current.Settings.UseSpeakerBoost),
            current.PaceBoostWpm);
    }

    private static ConfigDeliveryStyleDto ToDto(DeliveryStyle style)
    {
        var policy = style.CurrentPolicy;
        return new ConfigDeliveryStyleDto(
            style.Id, style.Name, style.Description,
            new DeliveryStylePolicyDto(policy.ToneDescription, policy.AllowedTags.ToList(), policy.Intensity));
    }

    private static ConfigBookendDto ToDto(Bookend bookend)
    {
        var template = bookend.CurrentTemplate;
        return new ConfigBookendDto(bookend.Id, bookend.Name, bookend.Description,
            template.IntroText, template.OutroText);
    }

    private static ConfigPronunciationDictionaryDto ToDto(PronunciationDictionary dictionary) => new(
        dictionary.Id, dictionary.Name, dictionary.Description,
        dictionary.CurrentRules.Select(PronunciationDictionariesHttpTrigger.ToRuleDto).ToList(),
        Scope: dictionary.Scope.ToString());

    private static async Task<Result<T>> BoundedAsync<T>(
        SemaphoreSlim gate,
        Func<CancellationToken, Task<Result<T>>> operation,
        CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            return await operation(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<Result<object>> Box<T>(Task<Result<T>> task)
    {
        var result = await task;
        return result.IsFailure
            ? Result<object>.Failure(result.Error)
            : Result<object>.Success(result.Value!);
    }

    private static async Task DrainAsync(IEnumerable<Task<Result<object>>> tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private static T? Get<T>(IReadOnlyDictionary<string, Task<Result<object>>> reads, string section)
    {
        if (!reads.TryGetValue(section, out var task))
            return default;
        var result = task.GetAwaiter().GetResult();
        return result.Value is null ? default : (T)result.Value;
    }
}
