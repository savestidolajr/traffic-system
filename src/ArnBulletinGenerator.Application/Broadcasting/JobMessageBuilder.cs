using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Builds rejection feedback strings and regeneration queue messages for job workflows.
/// </summary>
internal static class JobMessageBuilder
{
    public static string BuildRejectionFeedback(
        IReadOnlyList<RejectionEntry> rejectionHistory,
        Option<string> additionalContext,
        Option<string> rejectionReason = default,
        Option<string> rejectionDescription = default)
    {
        var inlineParts = new[]
        {
            rejectionReason.Map(r => $"Rejection reason: {r}"),
            rejectionDescription.Map(d => $"Detailed feedback: {d}")
        }.Where(o => o.HasValue && !string.IsNullOrWhiteSpace(o.Value)).Select(o => o.Value);

        var historyParts = rejectionHistory.Select((entry, i) =>
            rejectionHistory.Count > 1
                ? $"Attempt {i + 1} was rejected: {entry.Notes}"
                : $"Previous version was rejected: {entry.Notes}");

        var feedbackParts = inlineParts.Concat(historyParts).ToList();

        if (feedbackParts.Count == 0)
            return additionalContext.GetValueOr("No specific feedback provided.");

        string aggregatedFeedback = string.Join("\n", feedbackParts);

        return additionalContext
            .Map(ctx => string.IsNullOrWhiteSpace(ctx)
                ? aggregatedFeedback
                : $"{aggregatedFeedback}\n\nAdditional context: {ctx}")
            .GetValueOr(aggregatedFeedback);
    }

    /// <param name="pronunciationOverride">
    ///     When set, the dictionary version to synthesise with instead of the one the job
    ///     pinned — the operator changed a rule during this review.
    /// </param>
    /// <remarks>
    ///     <see cref="RegenerationQueueMessage.PreviousScript" /> is the re-voice's only source of
    ///     words. It reads <see cref="Job.ScriptText" />, which survives the reset only because
    ///     <see cref="Job.RequestRegeneration" /> is called with <c>preserveEvidence</c> for a
    ///     re-voice — without that the words are gone before this runs.
    /// </remarks>
    public static RegenerationQueueMessage BuildRegenerationQueueMessage(
        Job job, string rejectionFeedback, Option<string> editedScript = default,
        Option<IReadOnlyList<string>> selectedCorridors = default, Option<string> deliveryIntent = default,
        Option<string> tonedScript = default,
        Option<IReadOnlyList<string>> selectedGoodRuns = default,
        Option<IReadOnlyList<string>> excludedIncidentKeys = default, bool revoiceOnly = false,
        PronunciationStack? pronunciationOverride = null,
        ResolvedDeliveryStyle delivery = default,
        ResolvedBookends? bookends = null)
    {
        return new RegenerationQueueMessage(
            job.JobId,
            job.Market ?? job.Station,
            (int)job.TargetDurationSeconds,
            job.Market ?? job.Station,
            job.Incidents.Select(i => new RegenerationIncident(
                i.Key,
                i.Headline,
                i.Priority,
                i.Location,
                "incident",
                i.LengthMeters,
                i.FunctionalClass,
                i.Latitude.Map(v => (double?)v).GetValueOr(null),
                i.Longitude.Map(v => (double?)v).GetValueOr(null),
                SourceRecordIdentities: i.SourceRecordIdentities)).ToList(),
            VoiceId: job.VoiceConfig.Match<string?>(vc => vc.VoiceId, () => null),
            Model: job.VoiceConfig.Match<string?>(vc => vc.Model, () => null),
            VoiceSettings: job.VoiceConfig.Match<VoiceSettings?>(vc => vc.Settings.Match<VoiceSettings?>(s => s, () => null), () => null),
            PaceBoostWpm: job.VoiceConfig.Match(vc => vc.PaceBoostWpm, () => 0),
            RejectionFeedback: rejectionFeedback,
            PreviousScript: editedScript.Or(job.ScriptText).Match<string?>(script => script, () => null),
            SelectedCorridors: selectedCorridors.Match<IReadOnlyList<string>?>(s => s, () => null),
            Content: job.ContentSelection.Match<BulletinContentSelection?>(c => c, () => null),
            DeliveryIntent: deliveryIntent.Match<string?>(s => s, () => null),
            TonedScript: tonedScript.Match<string?>(s => s, () => null),
            SelectedGoodRuns: selectedGoodRuns.Match<IReadOnlyList<string>?>(s => s, () => null),
            ExcludedIncidentKeys: excludedIncidentKeys.Match<IReadOnlyList<string>?>(s => s, () => null),
            RevoiceOnly: revoiceOnly,
            PronunciationLocator: pronunciationOverride is not null
                ? pronunciationOverride.Station.Match<PronunciationLocator?>(l => l, () => null)
                : ToLocator(job.PronunciationDictionaryId, job.PronunciationVersionId),
            GlobalPronunciationLocator: pronunciationOverride is not null
                ? pronunciationOverride.Global.Match<PronunciationLocator?>(l => l, () => null)
                : ToLocator(job.GlobalPronunciationDictionaryId, job.GlobalPronunciationVersionId),
            DeliveryStyle: delivery.Policy,
            DeliveryStyleId: delivery.Id,
            DeliveryStyleVersion: delivery.Version,
            Bookends: bookends);
    }

    /// <summary>One persisted dictionary/version pair as a nullable wire locator.</summary>
    private static PronunciationLocator? ToLocator(
        Option<string> dictionaryId, Option<string> versionId) =>
        PronunciationStack.Locator(dictionaryId, versionId).Match<PronunciationLocator?>(l => l, () => null);
}

/// <summary>
///     The delivery style a regeneration should synthesise with — the version the job pinned by
///     default, or the style's current version when the operator changed it during this review.
///     All-null when the job has no style, mapping to the neutral default policy.
/// </summary>
internal readonly record struct ResolvedDeliveryStyle(
    DeliveryStylePolicy? Policy = null, string? Id = null, int? Version = null);
