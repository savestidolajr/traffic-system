using ArnBulletinGenerator.Application.Access;
using System.Text.Json;
using ArnBulletinGenerator.Application.ConfigImport;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Application.ConfigTransfer;

/// <summary>
///     Plans and applies config-bundle imports. Planning validates every entity through the
///     same domain/service rules the ordinary endpoints use and diffs against current state
///     (Create / Update / Unchanged / Error per entity); applying executes the planned actions
///     in dependency order — libraries before the schedules that reference them. Upsert-only:
///     nothing is ever deleted. Unchanged entities are skipped entirely, so re-importing an
///     untouched export is a no-op (and never round-trips the ElevenLabs dictionary provider).
///     Per-section planning logic lives in <see cref="ArnBulletinGenerator.Application.ConfigImport" />;
///     this class wires the shared dependencies into a <see cref="ConfigImport.ConfigImportContext" />
///     and coordinates the sections in dependency order.
/// </summary>
public sealed class ConfigImportService(
    IScheduleRepository scheduleRepository,
    IScheduleService scheduleService,
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
    IFillerStatementsRepository fillerStatements,
    IFillerAudioCacheService fillerAudioCache,
    TimeProvider timeProvider,
    ILogger<ConfigImportService> logger,
    IMarketAccessCacheInvalidator? marketAccessCacheInvalidator = null)
{
    private readonly ConfigImportSnapshotReader snapshotReader = new(
        scheduleRepository, voiceProfiles, deliveryStyles, playbooks, bookends, pronunciationDictionaries,
        safetyTerms, guidance, priorityRoads, verifiedPronunciations, operatorAssignments,
        voiceCalibrations, fillerStatements);
    private ConfigImportContext Context => new(
        scheduleRepository, scheduleService, voiceProfiles, deliveryStyles, playbooks, bookends, pronunciationDictionaries,
        safetyTerms, guidance, priorityRoads, verifiedPronunciations,
        operatorAssignments, voiceCalibrations, fillerStatements, fillerAudioCache,
        timeProvider, logger, marketAccessCacheInvalidator);

    public async Task<Result<ConfigImportPlan>> PlanAsync(
        ConfigBundleDto bundle, string actor, CancellationToken ct,
        IReadOnlyList<string>? unknownSections = null)
    {
        var ctx = Context;
        var items = new List<PlannedConfigItem>();

        // A bundle from another schema era must not be validated against this era's rules —
        // reject the version up front, naming the supported one (absent/0 deserialize to the
        // record's default, so pre-versioning exports still import cleanly).
        if (bundle.SchemaVersion != ConfigSections.CurrentSchemaVersion)
        {
            items.Add(ConfigImportHelpers.ErrorItem(
                "SchemaVersion", bundle.SchemaVersion.ToString(), "SchemaVersion",
                $"Unsupported SchemaVersion {bundle.SchemaVersion}. Supported versions: {ConfigSections.CurrentSchemaVersion}."));
            return Result<ConfigImportPlan>.Success(new ConfigImportPlan(items));
        }

        // A typo'd top-level key (e.g. 'Schedule' instead of 'Schedules') would otherwise be a
        // silent no-op — surface it as a blocking error so the operator sees it in the report.
        if (unknownSections is { Count: > 0 })
            foreach (var name in unknownSections)
                items.Add(ConfigImportHelpers.ErrorItem("Unknown", name, name,
                    $"Unrecognized top-level property '{name}' is not a known config section. " +
                    $"Valid sections: {string.Join(", ", ConfigSections.All)}."));

        var snapshotResult = await snapshotReader.ReadAsync(bundle, ct);
        if (snapshotResult.IsFailure)
            return Result<ConfigImportPlan>.Failure(snapshotResult.Error);
        var snapshot = snapshotResult.Value;

        // Library id universes (existing ∪ created-by-this-bundle) for schedule referential
        // checks. ORDINAL on purpose: at airtime every id resolves through a case-SENSITIVE
        // Table RowKey lookup / StringComparison.Ordinal (StationConfigResolver), so a bundle
        // assigning 'My-Dict' to a library called 'my-dict' must fail import loudly here — a
        // case-insensitive universe would import clean, show as assigned, and air the default
        // voice / no dictionary.
        var voiceProfileIds = new HashSet<string>(StringComparer.Ordinal);
        var deliveryStyleIds = new HashSet<string>(StringComparer.Ordinal);
        var playbookIds = new HashSet<string>(StringComparer.Ordinal);
        var dictionaryIds = new HashSet<string>(StringComparer.Ordinal);
        var globalDictionaryIds = new HashSet<string>(StringComparer.Ordinal);
        var bookendIds = new HashSet<string>(StringComparer.Ordinal);
        var existingProfiles = snapshot.VoiceProfiles ?? [];
        var existingStyles = snapshot.DeliveryStyles ?? [];
        var existingPlaybooks = snapshot.Playbooks ?? [];
        var existingBookends = snapshot.Bookends ?? [];
        var existingDictionaries = snapshot.PronunciationDictionaries ?? [];
        foreach (var p in existingProfiles) voiceProfileIds.Add(p.Id);
        foreach (var s in existingStyles) deliveryStyleIds.Add(s.Id);
        foreach (var p in existingPlaybooks) playbookIds.Add(p.Id);
        foreach (var b in existingBookends) bookendIds.Add(b.Id);
        foreach (var d in existingDictionaries)
        {
            dictionaryIds.Add(d.Id);
            if (d.IsGlobal && d.IsActive)
                globalDictionaryIds.Add(d.Id);
        }

        if (bundle.VoiceProfiles is not null)
            VoiceProfilePlanBuilder.Plan(
                ctx, bundle.VoiceProfiles, existingProfiles, actor, items, voiceProfileIds);

        if (bundle.DeliveryStyles is not null)
            DeliveryStylePlanBuilder.Plan(
                ctx, bundle.DeliveryStyles, existingStyles, actor, items, deliveryStyleIds);

        if (bundle.PronunciationDictionaries is not null)
            PronunciationDictionaryPlanBuilder.Plan(
                ctx, bundle.PronunciationDictionaries, existingDictionaries, actor, items, dictionaryIds,
                globalDictionaryIds);

        if (bundle.SafetyTerms is not null)
        {
            var result = await SafetyTermsPlanBuilder.PlanAsync(
                ctx, bundle.SafetyTerms, actor, items, ct, snapshot.SafetyTerms);
            if (result.IsFailure)
                return Result<ConfigImportPlan>.Failure(result.Error);
        }

        if (bundle.Guidance is not null)
        {
            var result = await GuidancePlanBuilder.PlanAsync(
                ctx, bundle.Guidance, actor, items, ct, snapshot.Guidance);
            if (result.IsFailure)
                return Result<ConfigImportPlan>.Failure(result.Error);
        }

        if (bundle.PriorityRoads is not null)
        {
            var result = await PriorityRoadsPlanBuilder.PlanAsync(
                ctx, bundle.PriorityRoads, actor, items, ct, snapshot.PriorityRoads);
            if (result.IsFailure)
                return Result<ConfigImportPlan>.Failure(result.Error);
        }

        if (bundle.VerifiedPronunciations is not null)
        {
            var result = await VerifiedPronunciationsPlanBuilder.PlanAsync(
                ctx, bundle.VerifiedPronunciations, actor, items, ct, snapshot.VerifiedPronunciations);
            if (result.IsFailure)
                return Result<ConfigImportPlan>.Failure(result.Error);
        }

        if (bundle.OperatorAssignments is not null)
        {
            var result = await OperatorAssignmentsPlanBuilder.PlanAsync(
                ctx, bundle.OperatorAssignments, actor, items, ct, snapshot.OperatorAssignments);
            if (result.IsFailure)
                return Result<ConfigImportPlan>.Failure(result.Error);
        }

        if (bundle.VoiceCalibrations is not null)
        {
            var result = await VoiceCalibrationsPlanBuilder.PlanAsync(
                ctx, bundle.VoiceCalibrations, actor, items, ct, snapshot.VoiceCalibrations);
            if (result.IsFailure)
                return Result<ConfigImportPlan>.Failure(result.Error);
        }

        if (bundle.FillerStatements is not null)
        {
            var result = await FillerStatementsPlanBuilder.PlanAsync(
                ctx, bundle.FillerStatements, actor, items, ct, snapshot.FillerStatements);
            if (result.IsFailure)
                return Result<ConfigImportPlan>.Failure(result.Error);
        }

        if (bundle.Bookends is not null)
            BookendPlanBuilder.Plan(ctx, bundle.Bookends, existingBookends, actor, items, bookendIds);

        if (bundle.Schedules is not null)
        {
            var result = await SchedulePlanBuilder.PlanAsync(
                ctx, bundle.Schedules, voiceProfileIds, deliveryStyleIds, playbookIds, dictionaryIds,
                globalDictionaryIds, bookendIds, items, ct, snapshot.Schedules);
            if (result.IsFailure)
                return Result<ConfigImportPlan>.Failure(result.Error);
        }

        return Result<ConfigImportPlan>.Success(new ConfigImportPlan(items));
    }

    /// <summary>
    ///     Executes a validated plan in order. Individual apply failures are recorded per item
    ///     (the report makes any partial application visible) rather than aborting the batch —
    ///     later items do not silently depend on earlier failures because referential targets
    ///     were validated against the merged universe up front.
    /// </summary>
    public async Task<ConfigImportReportDto> ApplyAsync(
        ConfigImportPlan plan, bool requiresApproval, CancellationToken ct)
    {
        var resolved = new List<ConfigImportItemDto>(plan.Items.Count);
        var failures = 0;

        foreach (var item in plan.Items)
        {
            if (item.Apply is null)
            {
                resolved.Add(item.Report);
                continue;
            }

            var result = await item.Apply(ct);
            if (result.IsSuccess)
            {
                resolved.Add(item.Report with { Outcome = ConfigImportOutcomes.Applied });
            }
            else
            {
                failures++;
                logger.LogError("[ConfigImport] {Section} '{Key}' failed to apply: {Error}",
                    item.Report.Section, item.Report.Key, result.Error);
                resolved.Add(item.Report with { Outcome = ConfigImportOutcomes.Failed, Detail = result.Error });
            }
        }

        logger.LogInformation(
            "[ConfigImport] Applied plan: {Total} item(s), {Failures} failure(s)", plan.Items.Count, failures);
        return plan.ToReport(dryRun: false, requiresApproval, applied: true, resolved);
    }

    /// <summary>Top-level bundle properties that are export metadata, not a config section.</summary>
    private static readonly IReadOnlySet<string> KnownBundleMetadataKeys =
        new HashSet<string>(["Schema", "SchemaVersion", "ExportedAtUtc", "Source"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Scans the raw import JSON for top-level property names that are neither a known
    ///     config section nor bundle metadata. Deserializing straight to <see cref="ConfigBundleDto" />
    ///     silently drops unrecognized properties, so a typo'd section name (e.g. 'Schedule' instead
    ///     of 'Schedules') would otherwise become an invisible no-op.
    /// </summary>
    public static IReadOnlyList<string> DetectUnknownSections(string rawBundleJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBundleJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return [];

            var known = new HashSet<string>(ConfigSections.All, StringComparer.OrdinalIgnoreCase);
            var unknown = new List<string>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                if (!known.Contains(prop.Name) && !KnownBundleMetadataKeys.Contains(prop.Name))
                    unknown.Add(prop.Name);
            return unknown;
        }
        catch (JsonException)
        {
            // Malformed JSON is already rejected by the caller's own deserialization step.
            return [];
        }
    }
}
