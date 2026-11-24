using System.Security.Cryptography;
using System.Text;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Application.Jobs;

/// <summary>
///     Tracks incident state across bulletins and produces delta context for scripting.
/// </summary>
public sealed class IncidentStateService(
    IIncidentStateRepository repository,
    ILogger<IncidentStateService> logger,
    IOptions<IncidentStateOptions> options,
    TimeProvider timeProvider) : IIncidentStateService
{
    private readonly IncidentStateOptions _options = options.Value;

    public async Task<Option<IncidentDeltaContext>> ComputeAndPersistDeltaAsync(
        string marketId,
        Option<string> timezoneId,
        IReadOnlyList<TrafficIncident> currentIncidents,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(marketId))
        {
            logger.LogWarning("MarketId is null or whitespace. Skipping incident delta computation.");
            return Option<IncidentDeltaContext>.None;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var timeZoneResult = ResolveTimeZone(timezoneId);
        if (timeZoneResult.IsFailure)
        {
            logger.LogWarning("Failed to resolve timezone '{TimezoneId}': {Error}. Falling back to UTC.",
                timezoneId.GetValueOr("none"), timeZoneResult.Error);
            return Option<IncidentDeltaContext>.None;
        }

        var timeZone = timeZoneResult.Value;
        var startOfDayUtc = TimeZoneHelper.GetLocalDayStartUtc(nowUtc, timeZone);

        // Note: Purge of expired items is skipped here on the latency-sensitive path.

        var previousResult = await repository.GetCurrentByMarketAsync(marketId, cancellationToken);
        if (previousResult.IsFailure)
        {
            logger.LogWarning("Failed to load incident state for market {MarketId}: {Error}", marketId,
                previousResult.Error);
            return Option<IncidentDeltaContext>.None;
        }

        var previousStates = previousResult.Value
            .Where(s => s.LastSeenAt >= startOfDayUtc ||
                        (s.LastReportedAt.HasValue && s.LastReportedAt.Value >= startOfDayUtc))
            .ToList();

        var previousById = previousStates.ToDictionary(s => s.IncidentId, s => s);
        var currentIds = currentIncidents.Select(StateKey).ToHashSet(StringComparer.Ordinal);

        var currentResults = await ClassifyCurrentIncidentsAsync(
            marketId, currentIncidents, previousById, nowUtc, cancellationToken);

        var clearedResults = await ClassifyClearedIncidentsAsync(
            previousStates, currentIds, startOfDayUtc, nowUtc, cancellationToken);

        var newItems = currentResults.Where(r => r.Category is DeltaCategory.New).Select(r => r.Item).ToList();
        var updatedItems = currentResults.Where(r => r.Category is DeltaCategory.Updated).Select(r => r.Item).ToList();
        var unchangedItems = currentResults.Where(r => r.Category is DeltaCategory.Unchanged).Select(r => r.Item)
            .ToList();

        logger.LogInformation(
            "Incident delta for {MarketId} - New: {NewCount}, Updated: {UpdatedCount}, Cleared: {ClearedCount}, Unchanged: {UnchangedCount}",
            marketId, newItems.Count, updatedItems.Count, clearedResults.Count, unchangedItems.Count);

        return Option<IncidentDeltaContext>.Some(new IncidentDeltaContext(
            newItems,
            updatedItems,
            clearedResults,
            unchangedItems));
    }

    private async Task<IReadOnlyList<ClassifiedDelta>> ClassifyCurrentIncidentsAsync(
        string marketId,
        IReadOnlyList<TrafficIncident> currentIncidents,
        IReadOnlyDictionary<string, IncidentState> previousById,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var results = new List<ClassifiedDelta>(currentIncidents.Count);
        var statesToPersist = new List<IncidentState>(currentIncidents.Count);

        foreach (var incident in currentIncidents)
        {
            var freshState = BuildState(marketId, incident, nowUtc);

            string stateKey = StateKey(incident);
            if (!previousById.TryGetValue(stateKey, out var previous))
            {
                results.Add(new ClassifiedDelta(DeltaCategory.New, ToDeltaItem(incident)));
                statesToPersist.Add(freshState);
                continue;
            }

            var category = HasMeaningfulChange(previous, freshState) ? DeltaCategory.Updated : DeltaCategory.Unchanged;
            results.Add(new ClassifiedDelta(category, ToDeltaItem(incident)));

            var mergedState = freshState with
            {
                FirstSeenAt = previous.FirstSeenAt,
                LastSeenAt = nowUtc,
                LastReportedAt = Option<DateTime>.Some(nowUtc),
                Status = IncidentStateStatus.Active,
                UpdatedAt = nowUtc
            };

            statesToPersist.Add(mergedState);
        }

        if (statesToPersist.Count > 0)
        {
            var batchResult = await repository.UpsertBatchAsync(statesToPersist, cancellationToken);
            if (batchResult is not null && batchResult.IsFailure)
            {
                logger.LogWarning("Failed to batch persist incident state for market {MarketId}: {Error}", marketId, batchResult.Error);
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<IncidentDeltaItem>> ClassifyClearedIncidentsAsync(
        IReadOnlyList<IncidentState> previousStates,
        IReadOnlySet<string> currentIds,
        DateTime startOfDayUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var cleared = previousStates
            .Where(s => !currentIds.Contains(s.IncidentId)
                        && s.Status != IncidentStateStatus.Cleared
                        && s.LastReportedAt.HasValue
                        && s.LastReportedAt.Value >= startOfDayUtc)
            .ToList();

        if (cleared.Count > 0)
        {
            var clearedStates = cleared
                .Select(state => state with { Status = IncidentStateStatus.Cleared, UpdatedAt = nowUtc })
                .ToList();

            var batchResult = await repository.UpsertBatchAsync(clearedStates, cancellationToken);
            if (batchResult is not null && batchResult.IsFailure)
            {
                logger.LogWarning("Failed to batch persist cleared incidents: {Error}", batchResult.Error);
            }
        }

        return cleared.Select(ToDeltaItem).ToList();
    }

    // ── Pure helpers ──

    private IncidentState BuildState(string marketId, TrafficIncident incident, DateTime nowUtc)
    {
        return new IncidentState(
            StateKey(incident),
            marketId,
            nowUtc,
            nowUtc,
            IncidentStateStatus.Active,
            incident.Type,
            incident.Severity,
            incident.LengthMeters,
            ComputeHash(incident.Description),
            EvidenceSources.Of(incident.Sources)[0],
            nowUtc,
            Option<DateTime>.Some(nowUtc),
            NormalizeRoadName(incident.Location),
            Direction: ExtractDirection(incident.Description));
    }
    private static string StateKey(TrafficIncident incident) =>
        SourceRecordIdentity.EventKeyFor(incident.SourceRecordIdentities, incident.Id);

    private static bool HasMeaningfulChange(IncidentState previous, IncidentState current)
    {
        return !string.Equals(previous.Type, current.Type, StringComparison.OrdinalIgnoreCase)
               || previous.Criticality != current.Criticality
               || Math.Abs(previous.LengthMeters - current.LengthMeters) >= 50
               || !EqualsOption(previous.RoadName, current.RoadName)
               || !EqualsOption(previous.Suburb, current.Suburb)
               || !EqualsOption(previous.Direction, current.Direction)
               || !string.Equals(previous.DescriptionHash, current.DescriptionHash, StringComparison.Ordinal);
    }

    private static bool EqualsOption(Option<string> left, Option<string> right)
    {
        return string.Equals(left.GetValueOr(string.Empty), right.GetValueOr(string.Empty),
            StringComparison.OrdinalIgnoreCase);
    }

    private Option<string> NormalizeRoadName(string location)
    {
        return string.IsNullOrWhiteSpace(location) || _options.GenericLocations.Contains(location)
            ? Option<string>.None
            : Option<string>.Some(location);
    }

    private static Option<string> ExtractDirection(string description)
    {
        return string.IsNullOrWhiteSpace(description)
            ? Option<string>.None
            : description switch
            {
                var l when l.Contains("northbound", StringComparison.OrdinalIgnoreCase) => Option<string>.Some("northbound"),
                var l when l.Contains("southbound", StringComparison.OrdinalIgnoreCase) => Option<string>.Some("southbound"),
                var l when l.Contains("eastbound", StringComparison.OrdinalIgnoreCase) => Option<string>.Some("eastbound"),
                var l when l.Contains("westbound", StringComparison.OrdinalIgnoreCase) => Option<string>.Some("westbound"),
                var l when l.Contains("city-bound", StringComparison.OrdinalIgnoreCase) || l.Contains("citybound", StringComparison.OrdinalIgnoreCase) => Option<string>.Some("city-bound"),
                var l when l.Contains("inbound", StringComparison.OrdinalIgnoreCase) => Option<string>.Some("inbound"),
                var l when l.Contains("outbound", StringComparison.OrdinalIgnoreCase) => Option<string>.Some("outbound"),
                _ => Option<string>.None
            };
    }

    private static string ComputeHash(string input)
    {
        return string.IsNullOrWhiteSpace(input)
            ? string.Empty
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private IncidentDeltaItem ToDeltaItem(TrafficIncident incident)
    {
        return new IncidentDeltaItem(
            incident.Id,
            incident.Type,
            incident.Severity,
            NormalizeRoadName(incident.Location),
            Option<string>.None,
            ExtractDirection(incident.Description),
            incident.LengthMeters,
            ToOptionalDescription(incident.Description),
            incident.Fact?.Window ?? IncidentWindow.Classify(
                incident.StartTimeUtc, incident.EndTimeUtc, incident.EntryTimeUtc, timeProvider.GetUtcNow()));
    }

    private static IncidentDeltaItem ToDeltaItem(IncidentState state)
    {
        return new IncidentDeltaItem(
            state.IncidentId,
            state.Type,
            state.Criticality,
            state.RoadName,
            state.Suburb,
            state.Direction,
            state.LengthMeters,
            Option<string>.None);
    }

    private static Option<string> ToOptionalDescription(string description)
    {
        return string.IsNullOrWhiteSpace(description) ? Option<string>.None : Option<string>.Some(description);
    }

    private static Result<TimeZoneInfo> ResolveTimeZone(Option<string> timezoneId)
    {
        return timezoneId.Match(
            TimeZoneHelper.TryResolve,
            () => Result<TimeZoneInfo>.Failure("Timezone ID is null or empty"));
    }

    // ── Pure classification (returns categorized items without side effects) ──

    private enum DeltaCategory
    {
        New,
        Updated,
        Unchanged
    }

    private sealed record ClassifiedDelta(DeltaCategory Category, IncidentDeltaItem Item);
}
