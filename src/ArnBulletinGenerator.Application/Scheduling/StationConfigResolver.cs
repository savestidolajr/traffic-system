using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Application.Libraries;

namespace ArnBulletinGenerator.Application.Scheduling;

/// <summary>
///     Resolves a schedule's station configuration — voice, delivery style, bulletin playbook,
///     pronunciation dictionary, bookends — into the concrete values a broadcast run needs. Extracted from
///     <see cref="SchedulePollerService" />. Shared by the poller's own dispatch path
///     (<see cref="SchedulePollerService.ProcessScheduleAsync" />), by <see cref="JobWatchdog" />'s stale-job
///     re-resolution, and by <see cref="AdHocStationConfigResolver" /> for a manual trigger on a configured
///     station, so every one of those paths replays the exact same resolution logic.
/// </summary>
internal sealed class StationConfigResolver(
    IModelSelector modelSelector,
    IVoiceProfileService voiceProfileService,
    IVoiceProvider voiceProvider,
    IDeliveryStyleService deliveryStyleService,
    IBulletinPlaybookService playbookService,
    IBookendService bookendService,
    IPronunciationDictionaryService pronunciationDictionaryService,
    IOptions<BroadcastOptions> broadcastOptions,
    TimeProvider timeProvider,
    // Non-generic ILogger so callers outside the poller can pass their own category without
    // borrowing SchedulePollerService's. ILogger<T> converts implicitly, so the poller and
    // watchdog call sites are unchanged.
    ILogger logger)
{
    private readonly BroadcastOptions _broadcastOptions = broadcastOptions.Value;

    /// <summary>
    ///     The concrete voice config resolved from a schedule's assigned voice profile.
    ///     <paramref name="VoiceProfileId" />/<paramref name="VoiceProfileVersion" /> record WHICH
    ///     profile's current tuning produced it, for the job-record provenance pins; null when the
    ///     fallback default voice was used (no profile involved — a pin would be a fiction).
    /// </summary>
    internal readonly record struct ResolvedVoice(
        string VoiceId, string VoiceName, string Model, Option<VoiceSettings> Settings, double PaceBoostWpm,
        string? VoiceProfileId = null, int? VoiceProfileVersion = null);

    /// <summary>The delivery policy + provenance resolved from a schedule's assigned style.</summary>
    internal readonly record struct ResolvedDelivery(
        Option<DeliveryStylePolicy> Policy, Option<string> Id, Option<int> Version);


    /// <summary>
    ///     Resolves the concrete voice config for a schedule. With no rotation policy this is the
    ///     schedule's single voice profile (live current version, exact legacy behaviour). With a
    ///     policy it picks the profile for this run — round-robin via the persisted cursor, or the
    ///     profile whose local-time window is active — falling back to the primary profile (then the
    ///     broadcast default voice) when the chosen profile cannot be loaded.
    /// </summary>
    internal async Task<ResolvedVoice> ResolveVoiceAsync(MarketSchedule schedule, CancellationToken cancellationToken)
    {
        string chosenId = SelectVoiceProfileId(schedule);
        if (!string.IsNullOrWhiteSpace(chosenId))
        {
            var resolved = await TryResolveProfileAsync(chosenId, schedule.MarketId, cancellationToken);
            if (resolved.HasValue) return resolved.Value;

            // Chosen rotation member could not be resolved (e.g. archived after assignment) —
            // fall back to the schedule's primary profile when it is a different id.
            if (!string.Equals(chosenId, schedule.VoiceProfileId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(schedule.VoiceProfileId))
            {
                logger.LogWarning(
                    "Schedule {MarketId} rotation voice {ProfileId} could not be resolved; falling back to the primary voice profile.",
                    schedule.MarketId, chosenId);
                var primary = await TryResolveProfileAsync(schedule.VoiceProfileId, schedule.MarketId, cancellationToken);
                if (primary.HasValue) return primary.Value;
            }

            logger.LogWarning(
                "Schedule {MarketId} voice profile {ProfileId} could not be resolved; using the default voice.",
                schedule.MarketId, chosenId);
        }
        else
        {
            logger.LogWarning("Schedule {MarketId} has no voice profile assigned; using the default voice.",
                schedule.MarketId);
        }

        return await DefaultVoiceAsync(cancellationToken);
    }

    /// <summary>
    ///     Picks the profile id for this run: the schedule's primary when there is no rotation
    ///     policy, otherwise the active candidate set indexed by the round-robin cursor.
    /// </summary>
    private string SelectVoiceProfileId(MarketSchedule schedule)
    {
        if (schedule.VoiceRotation.IsNone)
            return schedule.VoiceProfileId;

        var policy = schedule.VoiceRotation.Value;
        var candidates = policy.CandidatesAt(LocalNow(schedule.Timezone), schedule.VoiceProfileId);
        if (candidates.Count == 0)
            return schedule.VoiceProfileId;
        // Cursor is always >= 0 (MarketSchedule.VoiceRotationCursor clamps negative assignments
        // to 0; AdvanceVoiceRotation wraps at int.MaxValue), so the modulo is safe.
        return candidates[schedule.VoiceRotationCursor % candidates.Count];
    }

    private TimeOnly LocalNow(string timezone)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        return TimeZoneHelper.TryResolve(timezone).Match(
            tz => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz)),
            _ => TimeOnly.FromDateTime(nowUtc));
    }

    private async Task<Option<ResolvedVoice>> TryResolveProfileAsync(string profileId, string marketId,
        CancellationToken ct)
    {
        var profileResult = await voiceProfileService.GetAsync(profileId, ct);
        if (profileResult.IsSuccess && profileResult.Value.Versions.Count > 0)
        {
            var profile = profileResult.Value;
            // Retire-in-place: a profile archived AFTER assignment keeps airing for the stations
            // already pointing at it (silently changing a live station's voice is worse than an
            // archived-but-applied profile), but the archive must be loud — the operator who
            // archived it expects it off air and needs to know it is still being read.
            if (!string.Equals(profile.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Schedule {MarketId} still applies archived voice profile {ProfileId} ({ProfileName}); " +
                    "it keeps airing (retire-in-place) — unassign or replace it on the schedule to retire it.",
                    marketId, profile.Id, profile.Name);
            }

            var current = profile.Versions[^1];
            return Option<ResolvedVoice>.Some(new ResolvedVoice(profile.BaseVoiceId, profile.Name, current.Model,
                Option<VoiceSettings>.Some(current.Settings), current.PaceBoostWpm,
                profile.Id, current.Version));
        }

        return Option<ResolvedVoice>.None;
    }

    /// <summary>
    ///     Last resort for a schedule whose assigned profile vanished after assignment
    ///     (MarketSchedule.Create requires a VoiceProfileId and ScheduleHttpTrigger rejects a
    ///     dangling one, so this is only reachable via a profile deleted or unreadable later).
    ///     The display name is read from the TTS provider rather than config: a hardcoded default
    ///     name drifts out of sync with the provider and mislabels every bulletin that uses it.
    ///     Falls back to the bare voice id, which is at least never a fiction.
    /// </summary>
    private async Task<ResolvedVoice> DefaultVoiceAsync(CancellationToken cancellationToken) => new(
        _broadcastOptions.DefaultVoiceId,
        await DefaultVoiceNameAsync(cancellationToken),
        await modelSelector.SelectModelAsync(_broadcastOptions.DefaultVoiceId,
            Option<string>.Some(_broadcastOptions.DefaultLanguage), cancellationToken: cancellationToken),
        Option<VoiceSettings>.None,
        0);

    private async Task<string> DefaultVoiceNameAsync(CancellationToken cancellationToken)
    {
        string voiceId = _broadcastOptions.DefaultVoiceId;
        var voicesResult = await voiceProvider.GetVoicesAsync(cancellationToken);
        if (voicesResult.IsFailure)
        {
            logger.LogWarning("Could not resolve a display name for the default voice {VoiceId}: {Error}.",
                voiceId, voicesResult.Error);
            return voiceId;
        }

        return voicesResult.Value
            .FirstOrDefault(v => string.Equals(v.VoiceId, voiceId, StringComparison.Ordinal))?.Name ?? voiceId;
    }

    /// <summary>
    ///     Resolves the station's delivery style (live current version) into a policy + provenance.
    ///     Returns all-None when the schedule has no style or it cannot be loaded, so the orchestrator
    ///     falls back to the global config default (neutral unless turned on).
    /// </summary>
    internal async Task<ResolvedDelivery> ResolveDeliveryStyleAsync(MarketSchedule schedule, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(schedule.DeliveryStyleId))
            return default;

        var styleResult = await deliveryStyleService.GetAsync(schedule.DeliveryStyleId, cancellationToken);
        if (styleResult.IsSuccess && styleResult.Value.Versions.Count > 0)
        {
            var style = styleResult.Value;
            // Retire-in-place (same rule as the archived voice profile): a style archived AFTER
            // assignment keeps applying for the stations already pointing at it — silently
            // changing a live station's read is worse than an archived-but-applied style — but
            // the archive must be loud so the operator who retired it knows it is still airing.
            if (!string.Equals(style.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Schedule {MarketId} still applies archived delivery style {StyleId} ({StyleName}); " +
                    "it keeps airing (retire-in-place) — unassign or replace it on the schedule to retire it.",
                    schedule.MarketId, style.Id, style.Name);
            }

            return new ResolvedDelivery(
                Option<DeliveryStylePolicy>.Some(style.CurrentPolicy),
                Option<string>.Some(style.Id),
                Option<int>.Some(style.CurrentVersion));
        }

        logger.LogWarning(
            "Schedule {MarketId} references delivery style {StyleId} which could not be resolved; using the default delivery.",
            schedule.MarketId, schedule.DeliveryStyleId);
        return default;
    }
    /// <summary>
    ///     Resolves the station's bulletin playbook (live current version) into the overlay applied
    ///     to writer instructions. Returns null when no playbook is assigned or it cannot be loaded.
    /// </summary>
    internal async Task<PlaybookOverlay?> ResolvePlaybookAsync(
        MarketSchedule schedule, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(schedule.PlaybookId))
            return null;

        var playbookResult = await playbookService.GetAsync(schedule.PlaybookId, cancellationToken);
        if (playbookResult.IsSuccess && playbookResult.Value.Versions.Count > 0)
        {
            var playbook = playbookResult.Value;
            if (!string.Equals(playbook.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Schedule {MarketId} still applies archived bulletin playbook {PlaybookId} ({PlaybookName}); "
                    + "it keeps applying until the schedule is changed.",
                    schedule.MarketId, playbook.Id, playbook.Name);
            }

            var overlay = playbook.CurrentOverlay;
            return overlay.IsEmpty ? null : overlay;
        }

        logger.LogWarning(
            "Schedule {MarketId} references bulletin playbook {PlaybookId} which could not be resolved; "
            + "using the default writer instructions.",
            schedule.MarketId, schedule.PlaybookId);
        return null;
    }

    /// <summary>
    ///     Resolves the pronunciation dictionaries this bulletin reads with: the station's own
    ///     assigned dictionary plus the active platform-wide one, station layer FIRST.
    ///     <para>
    ///         Order is precedence. ElevenLabs applies locators sequentially and the first one to
    ///         match a word wins it, while non-colliding rules from every layer still apply
    ///         (verified live 2026-07-31), so emitting the station first is what lets a station
    ///         override a platform-wide respelling per word and inherit the rest.
    ///     </para>
    ///     <para>
    ///         Each layer is phoneme-guarded INDEPENDENTLY: a dictionary holding phoneme rules is
    ///         dropped when the resolved voice model is eleven_multilingual_v2, where ElevenLabs
    ///         silently DELETES every phoneme-matched word from the audio (verified live
    ///         2026-07-19). Per-layer means a phoneme-carrying platform-wide dictionary cannot
    ///         take a clean alias-only station dictionary off the read. Better to air an
    ///         un-respelled road name than to drop it entirely.
    ///     </para>
    /// </summary>
    internal async Task<PronunciationStack> ResolvePronunciationStackAsync(
        MarketSchedule schedule, string voiceModel, CancellationToken cancellationToken)
    {
        // One list read serves both layers: the platform-wide dictionary is only discoverable by
        // scanning the library anyway, so a per-layer GetAsync would cost a second round trip.
        var listResult = await pronunciationDictionaryService.ListAsync(cancellationToken);
        if (listResult.IsFailure)
        {
            logger.LogWarning(
                "Schedule {MarketId}: the pronunciation library could not be read ({Error}); no dictionary attached.",
                schedule.MarketId, listResult.Error);
            return PronunciationStack.Empty;
        }

        var library = listResult.Value;
        var station = Option<PronunciationLocator>.None;

        if (!string.IsNullOrWhiteSpace(schedule.PronunciationDictionaryId))
        {
            var assigned = library.FirstOrDefault(d =>
                string.Equals(d.Id, schedule.PronunciationDictionaryId, StringComparison.Ordinal));
            if (assigned is null)
            {
                logger.LogWarning(
                    "Schedule {MarketId} references pronunciation dictionary {DictionaryId} which could not be resolved; using the default.",
                    schedule.MarketId, schedule.PronunciationDictionaryId);
            }
            else
            {
                // Retire-in-place (same rule as the archived voice profile and delivery style):
                // a dictionary archived AFTER assignment keeps applying for stations already
                // pointing at it, but the archive must be loud so the operator who retired it
                // knows it is still being read.
                if (!assigned.IsActive)
                {
                    logger.LogWarning(
                        "Schedule {MarketId} still applies archived pronunciation dictionary {DictionaryId} ({DictionaryName}); " +
                        "it keeps being read (retire-in-place) — unassign or replace it on the schedule to retire it.",
                        schedule.MarketId, assigned.Id, assigned.Name);
                }

                station = UsableLocator(assigned, voiceModel, "station", schedule.MarketId);
            }
        }

        var activeGlobal = library.FirstOrDefault(d => d.IsGlobal && d.IsActive);
        var global = activeGlobal is null
            ? Option<PronunciationLocator>.None
            : UsableLocator(activeGlobal, voiceModel, "platform-wide", schedule.MarketId);

        return PronunciationStack.Of(station, global);
    }

    /// <summary>
    ///     The dictionary's attachable locator for the resolved voice model: the full current
    ///     version when the model can safely take it, the ALIAS-ONLY version when the model
    ///     (eleven_multilingual_v2) would silently delete phoneme-matched words, and None when
    ///     no safe attachable version exists. Applied per layer so one bad layer cannot
    ///     disqualify the other, and so a single phoneme rule never takes a station's alias
    ///     corrections off the read. The decision itself lives on the domain
    ///     (<see cref="PronunciationDictionary.LocatorFor" />) so the preview path and the
    ///     airtime path can never disagree.
    /// </summary>
    private Option<PronunciationLocator> UsableLocator(
        PronunciationDictionary dictionary, string voiceModel, string layer, string marketId)
    {
        var dropped = dictionary.UnsupportedRuleWordsFor(voiceModel);
        if (dropped.Count > 0)
        {
            var locator = dictionary.LocatorFor(voiceModel);
            if (locator.HasValue)
            {
                logger.LogWarning(
                    "Schedule {MarketId}: the {Layer} pronunciation dictionary {DictionaryId} contains phoneme " +
                    "rule(s) ({Words}) that {Model} would silently DROP from the audio; attaching the " +
                    "alias-only version so every alias correction still airs.",
                    marketId, layer, dictionary.Id, string.Join(", ", dropped), voiceModel);
                return locator;
            }

            logger.LogError(
                "Schedule {MarketId}: the {Layer} pronunciation dictionary {DictionaryId} contains phoneme " +
                "rule(s) ({Words}) but {Model} silently DROPS phoneme-matched words and no alias-only " +
                "version exists — that layer was NOT attached. Convert its phoneme rules to alias " +
                "respellings or assign a turbo/flash v2 voice.",
                marketId, layer, dictionary.Id, string.Join(", ", dropped), voiceModel);
            return Option<PronunciationLocator>.None;
        }

        return dictionary.CurrentLocator;
    }

    /// <summary>
    ///     Resolves the station's bookend pair into the exact lines that will air, with
    ///     <c>{token}</c> placeholders already substituted for this station. None = the station has
    ///     no bookend and the bulletin reads exactly as every station did before the feature
    ///     existed; either line inside a resolved pair may still be empty.
    ///     <para>
    ///         Fail-soft at every step, and deliberately so: a bookend is branding, not content. An
    ///         unresolvable id, an archived entry, or a token with no value drops the pair and the
    ///         bulletin still airs. The alternative — airing "Looking over today" with a word
    ///         silently missing, or failing the slot outright over a decorative phrase — is worse
    ///         than simply not saying it. The schedule API rejects all three at assignment time, so
    ///         reaching any of these branches means the library changed under a live station.
    ///     </para>
    /// </summary>
    internal async Task<Option<ResolvedBookends>> ResolveBookendsAsync(
        MarketSchedule schedule, CancellationToken cancellationToken)
    {
        string chosenId = SelectBookendId(schedule);
        if (string.IsNullOrWhiteSpace(chosenId))
            return Option<ResolvedBookends>.None;

        var resolved = await TryResolveBookendAsync(schedule, chosenId, cancellationToken);
        if (resolved.HasValue)
            return resolved;

        // The chosen rotation member is unusable (archived or deleted after assignment) — fall back
        // to the station's primary pair when that is a different entry.
        if (!string.Equals(chosenId, schedule.BookendId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(schedule.BookendId))
        {
            logger.LogWarning(
                "Schedule {MarketId} rotation bookend {BookendId} could not be resolved; falling back to the primary.",
                schedule.MarketId, chosenId);
            return await TryResolveBookendAsync(schedule, schedule.BookendId, cancellationToken);
        }

        return Option<ResolvedBookends>.None;
    }

    /// <summary>
    ///     Picks the bookend id for this run: the station's primary when there is no rotation
    ///     policy, otherwise the active candidate set indexed by the bookend cursor.
    /// </summary>
    private string SelectBookendId(MarketSchedule schedule)
    {
        if (schedule.BookendRotation.IsNone)
            return schedule.BookendId;

        var candidates = schedule.BookendRotation.Value
            .CandidatesAt(LocalNow(schedule.Timezone), schedule.BookendId);
        if (candidates.Count == 0)
            return schedule.BookendId;
        // Cursor is always >= 0 (MarketSchedule.BookendRotationCursor clamps negative assignments
        // to 0; AdvanceBookendRotation wraps at int.MaxValue), so the modulo is safe.
        return candidates[schedule.BookendRotationCursor % candidates.Count];
    }

    private async Task<Option<ResolvedBookends>> TryResolveBookendAsync(
        MarketSchedule schedule, string bookendId, CancellationToken cancellationToken)
    {
        var result = await bookendService.GetAsync(bookendId, cancellationToken);
        if (result.IsFailure || result.Value.Versions.Count == 0)
        {
            logger.LogWarning(
                "Schedule {MarketId} references bookend {BookendId} which could not be resolved; no bookend will be read.",
                schedule.MarketId, bookendId);
            return Option<ResolvedBookends>.None;
        }

        var bookend = result.Value;
        if (!string.Equals(bookend.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Schedule {MarketId} references archived bookend {BookendId}; no bookend will be read.",
                schedule.MarketId, bookendId);
            return Option<ResolvedBookends>.None;
        }

        var template = bookend.CurrentTemplate;
        var intro = BookendTokenResolver.Resolve(
            template.IntroText, schedule.MarketId, schedule.Station, schedule.BookendTokens);
        var outro = BookendTokenResolver.Resolve(
            template.OutroText, schedule.MarketId, schedule.Station, schedule.BookendTokens);

        // A token with no value would air as a sentence with a word silently missing. Dropping the
        // whole pair is the honest outcome — and the API already refuses to save this state.
        if (!intro.IsComplete || !outro.IsComplete)
        {
            var unresolved = intro.UnresolvedTokens.Concat(outro.UnresolvedTokens)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            logger.LogError(
                "Schedule {MarketId} bookend {BookendId} has unresolved token(s) {Tokens} and was DROPPED from the " +
                "bulletin — give the station a value for each, or remove them from the template.",
                schedule.MarketId, bookendId, string.Join(", ", unresolved));
            return Option<ResolvedBookends>.None;
        }

        // Older rows can predate the domain admission rule. Do not let an incomplete handoff reach
        // TTS, but retain a valid intro so one bad decorative line does not discard the pair.
        var outroValidation = BookendTemplate.ValidateOutro(outro.Text);
        if (outroValidation.IsFailure)
        {
            logger.LogWarning(
                "Schedule {MarketId} bookend {BookendId} has an invalid outro and it will be omitted " +
                "while preserving the intro: {Error}",
                schedule.MarketId, bookendId, outroValidation.Error);
            outro = new BookendTokenResolver.Resolution("", []);
        }

        if (string.IsNullOrWhiteSpace(intro.Text) && string.IsNullOrWhiteSpace(outro.Text))
            return Option<ResolvedBookends>.None;

        return Option<ResolvedBookends>.Some(
            new ResolvedBookends(intro.Text, outro.Text, bookend.Id, bookend.CurrentVersion));
    }
}
