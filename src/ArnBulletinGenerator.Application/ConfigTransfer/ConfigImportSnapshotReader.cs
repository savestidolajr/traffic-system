using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.ConfigTransfer;

/// <summary>
///     Loads only the current state needed to plan one bundle. Library lists needed for schedule
///     referential validation are treated as dependencies; unrelated sections are not touched.
///     Independent section and per-market reads share one bounded gate, while the returned snapshot
///     keeps all data needed by the planners so they do not issue duplicate reads.
/// </summary>
public sealed class ConfigImportSnapshotReader(
    IScheduleRepository scheduleRepository,
    IVoiceProfileService voiceProfiles,
    IDeliveryStyleService deliveryStyles,
    IBulletinPlaybookService playbooks,
    IBookendService bookends,
    IPronunciationDictionaryService pronunciationDictionaries,
    ISafetyTermRepository safetyTerms,
    IGuidanceRepository guidance,
    IPriorityRoadsRepository priorityRoads,
    IVerifiedPronunciationRepository verifiedPronunciations,
    IOperatorAssignmentRepository operatorAssignments,
    IVoiceCalibrationRepository voiceCalibrations,
    IFillerStatementsRepository fillerStatements)
{
    private const int MaxParallelReads = 4;

    public async Task<Result<ConfigImportSnapshot>> ReadAsync(ConfigBundleDto bundle, CancellationToken ct)
    {
        var gate = new SemaphoreSlim(MaxParallelReads, MaxParallelReads);
        var reads = new Dictionary<string, Task<Result<object>>>(StringComparer.OrdinalIgnoreCase);

        bool hasSchedules = bundle.Schedules is not null;
        bool hasVoiceProfiles = bundle.VoiceProfiles is not null || hasSchedules;
        bool hasDeliveryStyles = bundle.DeliveryStyles is not null || hasSchedules;
        bool hasPlaybooks = hasSchedules;
        bool hasBookends = bundle.Bookends is not null || hasSchedules;
        bool hasDictionaries = bundle.PronunciationDictionaries is not null || hasSchedules;

        if (hasVoiceProfiles)
            reads[ConfigSections.VoiceProfiles] = Box(ReadVoiceProfilesAsync(gate, ct));
        if (hasDeliveryStyles)
            reads[ConfigSections.DeliveryStyles] = Box(ReadDeliveryStylesAsync(gate, ct));
        if (hasPlaybooks)
            reads["__playbooks"] = Box(ReadPlaybooksAsync(gate, ct));
        if (hasBookends)
            reads[ConfigSections.Bookends] = Box(ReadBookendsAsync(gate, ct));
        if (hasDictionaries)
            reads[ConfigSections.PronunciationDictionaries] = Box(ReadDictionariesAsync(gate, ct));
        if (bundle.SafetyTerms is not null)
            reads[ConfigSections.SafetyTerms] = Box(ReadSafetyTermsAsync(gate, ct));
        if (bundle.OperatorAssignments is not null)
            reads[ConfigSections.OperatorAssignments] = Box(ReadOperatorAssignmentsAsync(gate, ct));
        if (bundle.VoiceCalibrations is not null)
            reads[ConfigSections.VoiceCalibrations] = Box(ReadVoiceCalibrationsAsync(gate, ct));

        if (hasSchedules)
            reads[ConfigSections.Schedules] = Box(ReadSchedulesAsync(gate, ct));

        if (bundle.Guidance is not null)
            reads[ConfigSections.Guidance] = Box(ReadGuidanceAsync(bundle.Guidance, gate, ct));
        if (bundle.PriorityRoads is not null)
            reads[ConfigSections.PriorityRoads] = Box(ReadPriorityRoadsAsync(bundle.PriorityRoads, gate, ct));
        if (bundle.VerifiedPronunciations is not null)
            reads[ConfigSections.VerifiedPronunciations] = Box(
                ReadVerifiedPronunciationsAsync(bundle.VerifiedPronunciations, gate, ct));
        if (bundle.FillerStatements is not null)
            reads[ConfigSections.FillerStatements] = Box(ReadFillerStatementsAsync(bundle.FillerStatements, gate, ct));

        await DrainAsync(reads.Values);
        foreach (var section in ConfigSections.All)
        {
            if (!reads.TryGetValue(section, out var task))
                continue;
            var result = await task;
            if (result.IsFailure)
                return Result<ConfigImportSnapshot>.Failure(result.Error);
        }
        if (reads.TryGetValue("__playbooks", out var playbooksTask))
        {
            var result = await playbooksTask;
            if (result.IsFailure)
                return Result<ConfigImportSnapshot>.Failure(result.Error);
        }

        var schedules = Get<IReadOnlyList<MarketSchedule>>(reads, ConfigSections.Schedules);
        var voiceProfiles = Get<IReadOnlyList<VoiceProfile>>(reads, ConfigSections.VoiceProfiles);
        var deliveryStyles = Get<IReadOnlyList<DeliveryStyle>>(reads, ConfigSections.DeliveryStyles);
        var playbooks = Get<IReadOnlyList<BulletinPlaybook>>(reads, "__playbooks");
        var bookends = Get<IReadOnlyList<Bookend>>(reads, ConfigSections.Bookends);
        var dictionaries = Get<IReadOnlyList<PronunciationDictionary>>(
            reads, ConfigSections.PronunciationDictionaries);
        var safety = Get<IReadOnlyList<SafetyTerm>>(reads, ConfigSections.SafetyTerms);
        var operators = Get<IReadOnlyList<OperatorAssignment>>(reads, ConfigSections.OperatorAssignments);
        var calibrations = Get<IReadOnlyList<VoiceCalibration>>(reads, ConfigSections.VoiceCalibrations);
        var guidanceRows = Get<IReadOnlyList<ConfigImportGuidanceScope>>(reads, ConfigSections.Guidance);
        var roads = Get<IReadOnlyDictionary<string, IReadOnlyList<PriorityRoad>>>(
            reads, ConfigSections.PriorityRoads);
        var verified = Get<IReadOnlyDictionary<string, IReadOnlyList<VerifiedPronunciation>>>(
            reads, ConfigSections.VerifiedPronunciations);
        var fillers = Get<IReadOnlyDictionary<string, IReadOnlyList<FillerStatement>>>(
            reads, ConfigSections.FillerStatements);

        return Result<ConfigImportSnapshot>.Success(new ConfigImportSnapshot(
            schedules, voiceProfiles, deliveryStyles, playbooks, bookends, dictionaries,
            safety, guidanceRows, roads, verified, operators, calibrations, fillers));
    }

    private async Task<Result<IReadOnlyList<MarketSchedule>>> ReadSchedulesAsync(
        SemaphoreSlim gate, CancellationToken ct)
    {
        var result = await BoundedAsync(gate, c => scheduleRepository.ListSchedulesAsync(c), ct);
        return result.IsFailure
            ? Result<IReadOnlyList<MarketSchedule>>.Failure($"Failed to list schedules: {result.Error}")
            : Result<IReadOnlyList<MarketSchedule>>.Success(result.Value.ToList());
    }

    private async Task<Result<IReadOnlyList<VoiceProfile>>> ReadVoiceProfilesAsync(
        SemaphoreSlim gate, CancellationToken ct)
    {
        var result = await BoundedAsync(
            gate, c => voiceProfiles.ListAsync(VersionedLibraryListQuery.CurrentSummary, c), ct);
        return result.IsFailure
            ? Result<IReadOnlyList<VoiceProfile>>.Failure($"Failed to list voice profiles: {result.Error}")
            : Result<IReadOnlyList<VoiceProfile>>.Success(result.Value.Items);
    }

    private async Task<Result<IReadOnlyList<DeliveryStyle>>> ReadDeliveryStylesAsync(
        SemaphoreSlim gate, CancellationToken ct)
    {
        var result = await BoundedAsync(
            gate, c => deliveryStyles.ListAsync(VersionedLibraryListQuery.CurrentSummary, c), ct);
        return result.IsFailure
            ? Result<IReadOnlyList<DeliveryStyle>>.Failure($"Failed to list delivery styles: {result.Error}")
            : Result<IReadOnlyList<DeliveryStyle>>.Success(result.Value.Items);
    }

    private async Task<Result<IReadOnlyList<BulletinPlaybook>>> ReadPlaybooksAsync(
        SemaphoreSlim gate, CancellationToken ct) =>
        await BoundedAsync(gate, c => playbooks.ListAsync(c), ct);

    private async Task<Result<IReadOnlyList<Bookend>>> ReadBookendsAsync(
        SemaphoreSlim gate, CancellationToken ct)
    {
        var result = await BoundedAsync(
            gate, c => bookends.ListAsync(VersionedLibraryListQuery.CurrentSummary, c), ct);
        return result.IsFailure
            ? Result<IReadOnlyList<Bookend>>.Failure($"Failed to list bookends: {result.Error}")
            : Result<IReadOnlyList<Bookend>>.Success(result.Value.Items);
    }

    private async Task<Result<IReadOnlyList<PronunciationDictionary>>> ReadDictionariesAsync(
        SemaphoreSlim gate, CancellationToken ct)
    {
        var result = await BoundedAsync(
            gate, c => pronunciationDictionaries.ListAsync(VersionedLibraryListQuery.CurrentSummary, c), ct);
        return result.IsFailure
            ? Result<IReadOnlyList<PronunciationDictionary>>.Failure(
                $"Failed to list pronunciation dictionaries: {result.Error}")
            : Result<IReadOnlyList<PronunciationDictionary>>.Success(result.Value.Items);
    }

    private async Task<Result<IReadOnlyList<SafetyTerm>>> ReadSafetyTermsAsync(
        SemaphoreSlim gate, CancellationToken ct) =>
        await BoundedAsync(gate, c => safetyTerms.ListAllAsync(c), ct);

    private async Task<Result<IReadOnlyList<OperatorAssignment>>> ReadOperatorAssignmentsAsync(
        SemaphoreSlim gate, CancellationToken ct) =>
        await BoundedAsync(gate, c => operatorAssignments.ListAsync(c), ct);

    private async Task<Result<IReadOnlyList<VoiceCalibration>>> ReadVoiceCalibrationsAsync(
        SemaphoreSlim gate, CancellationToken ct) =>
        await BoundedAsync(gate, c => voiceCalibrations.ListAsync(c), ct);

    private async Task<Result<IReadOnlyList<ConfigImportGuidanceScope>>> ReadGuidanceAsync(
        IReadOnlyList<ConfigGuidanceDto> dtos, SemaphoreSlim gate, CancellationToken ct)
    {
        var scopes = dtos
            .Select(dto => TryGuidanceScope(dto, out var scope)
                ? ((GuidanceScope Scope, string ScopeId)?)scope
                : null)
            .Where(scope => scope.HasValue)
            .Select(scope => scope!.Value)
            .Distinct()
            .ToArray();
        var tasks = scopes.Select(async scope =>
        {
            var result = await BoundedAsync(gate, c => guidance.ListAsync(scope.Scope, scope.ScopeId, c), ct);
            return result.IsFailure
                ? Result<ConfigImportGuidanceScope>.Failure(
                    $"Failed to list {scope.Scope} guidance ('{scope.ScopeId}'): {result.Error}")
                : Result<ConfigImportGuidanceScope>.Success(
                    new ConfigImportGuidanceScope(scope.Scope, scope.ScopeId, result.Value));
        }).ToArray();
        var results = await Task.WhenAll(tasks);
        foreach (var result in results)
            if (result.IsFailure)
                return Result<IReadOnlyList<ConfigImportGuidanceScope>>.Failure(result.Error);
        return Result<IReadOnlyList<ConfigImportGuidanceScope>>.Success(results.Select(result => result.Value).ToList());
    }

    private async Task<Result<IReadOnlyDictionary<string, IReadOnlyList<PriorityRoad>>>> ReadPriorityRoadsAsync(
        IReadOnlyList<PriorityRoadsDto> dtos, SemaphoreSlim gate, CancellationToken ct)
    {
        var markets = dtos.Select(dto => dto.Market?.Trim() ?? string.Empty)
            .Where(market => !string.IsNullOrWhiteSpace(market))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var tasks = markets.Select(async market =>
        {
            var result = await BoundedAsync(gate, c => priorityRoads.GetRoadsAsync(market, c), ct);
            return result.IsFailure
                ? Result<(string Market, IReadOnlyList<PriorityRoad> Rows)>.Failure(
                    $"Failed to read priority roads for '{market}': {result.Error}")
                : Result<(string Market, IReadOnlyList<PriorityRoad> Rows)>.Success((market, result.Value));
        }).ToArray();
        var results = await Task.WhenAll(tasks);
        foreach (var result in results)
            if (result.IsFailure)
                return Result<IReadOnlyDictionary<string, IReadOnlyList<PriorityRoad>>>.Failure(result.Error);
        var rows = new Dictionary<string, IReadOnlyList<PriorityRoad>>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
            rows[result.Value.Market] = result.Value.Rows;
        return Result<IReadOnlyDictionary<string, IReadOnlyList<PriorityRoad>>>.Success(rows);
    }

    private async Task<Result<IReadOnlyDictionary<string, IReadOnlyList<VerifiedPronunciation>>>>
        ReadVerifiedPronunciationsAsync(
            IReadOnlyList<ConfigVerifiedPronunciationsDto> dtos, SemaphoreSlim gate, CancellationToken ct)
    {
        var markets = dtos.Select(dto => dto.Market?.Trim() ?? string.Empty)
            .Where(market => !string.IsNullOrWhiteSpace(market))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var tasks = markets.Select(async market =>
        {
            var result = await BoundedAsync(gate, c => verifiedPronunciations.GetForMarketAsync(market, c), ct);
            return result.IsFailure
                ? Result<(string Market, IReadOnlyList<VerifiedPronunciation> Rows)>.Failure(
                    $"Failed to read verified pronunciations for '{market}': {result.Error}")
                : Result<(string Market, IReadOnlyList<VerifiedPronunciation> Rows)>.Success((market, result.Value));
        }).ToArray();
        var results = await Task.WhenAll(tasks);
        foreach (var result in results)
            if (result.IsFailure)
                return Result<IReadOnlyDictionary<string, IReadOnlyList<VerifiedPronunciation>>>.Failure(result.Error);
        var rows = new Dictionary<string, IReadOnlyList<VerifiedPronunciation>>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
            rows[result.Value.Market] = result.Value.Rows;
        return Result<IReadOnlyDictionary<string, IReadOnlyList<VerifiedPronunciation>>>.Success(rows);
    }

    private async Task<Result<IReadOnlyDictionary<string, IReadOnlyList<FillerStatement>>>> ReadFillerStatementsAsync(
        IReadOnlyList<FillerStatementsDto> dtos, SemaphoreSlim gate, CancellationToken ct)
    {
        var markets = dtos.Select(dto => dto.Market?.Trim() ?? string.Empty)
            .Where(market => !string.IsNullOrWhiteSpace(market))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var tasks = markets.Select(async market =>
        {
            var result = await BoundedAsync(gate, c => fillerStatements.GetStatementsAsync(market, c), ct);
            return result.IsFailure
                ? Result<(string Market, IReadOnlyList<FillerStatement> Rows)>.Failure(
                    $"Failed to read filler statements for '{market}': {result.Error}")
                : Result<(string Market, IReadOnlyList<FillerStatement> Rows)>.Success((market, result.Value));
        }).ToArray();
        var results = await Task.WhenAll(tasks);
        foreach (var result in results)
            if (result.IsFailure)
                return Result<IReadOnlyDictionary<string, IReadOnlyList<FillerStatement>>>.Failure(result.Error);
        var rows = new Dictionary<string, IReadOnlyList<FillerStatement>>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
            rows[result.Value.Market] = result.Value.Rows;
        return Result<IReadOnlyDictionary<string, IReadOnlyList<FillerStatement>>>.Success(rows);
    }

    private static bool TryGuidanceScope(ConfigGuidanceDto dto, out (GuidanceScope Scope, string ScopeId) scope)
    {
        scope = default;
        if (!Enum.TryParse<GuidanceScope>(dto.Scope, true, out var parsed))
            return false;
        scope = (parsed, GuidanceEntry.NormalizeScopeId(parsed, dto.ScopeId));
        return true;
    }

    private static async Task<Result<T>> BoundedAsync<T>(
        SemaphoreSlim gate, Func<CancellationToken, Task<Result<T>>> operation, CancellationToken ct)
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
        await Task.WhenAll(tasks);
    }

    private static T? Get<T>(IReadOnlyDictionary<string, Task<Result<object>>> reads, string key)
    {
        if (!reads.TryGetValue(key, out var task))
            return default;
        var result = task.GetAwaiter().GetResult();
        return result.Value is null ? default : (T)result.Value;
    }
}

public sealed record ConfigImportSnapshot(
    IReadOnlyList<MarketSchedule>? Schedules,
    IReadOnlyList<VoiceProfile>? VoiceProfiles,
    IReadOnlyList<DeliveryStyle>? DeliveryStyles,
    IReadOnlyList<BulletinPlaybook>? Playbooks,
    IReadOnlyList<Bookend>? Bookends,
    IReadOnlyList<PronunciationDictionary>? PronunciationDictionaries,
    IReadOnlyList<SafetyTerm>? SafetyTerms,
    IReadOnlyList<ConfigImportGuidanceScope>? Guidance,
    IReadOnlyDictionary<string, IReadOnlyList<PriorityRoad>>? PriorityRoads,
    IReadOnlyDictionary<string, IReadOnlyList<VerifiedPronunciation>>? VerifiedPronunciations,
    IReadOnlyList<OperatorAssignment>? OperatorAssignments,
    IReadOnlyList<VoiceCalibration>? VoiceCalibrations,
    IReadOnlyDictionary<string, IReadOnlyList<FillerStatement>>? FillerStatements);

public sealed record ConfigImportGuidanceScope(
    GuidanceScope Scope,
    string ScopeId,
    IReadOnlyList<GuidanceEntry> Entries);
