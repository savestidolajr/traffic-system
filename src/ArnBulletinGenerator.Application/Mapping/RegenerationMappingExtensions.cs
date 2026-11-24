using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Mapping;

/// <summary>
///     Maps <see cref="RegenerationQueueMessage" /> to domain types for orchestration.
/// </summary>
public static class RegenerationMappingExtensions
{
    /// <summary>
    ///     Converts a <see cref="RegenerationQueueMessage" /> to a <see cref="BroadcastWithTraffic" /> domain model.
    /// </summary>
    public static BroadcastWithTraffic ToBroadcastWithTraffic(this RegenerationQueueMessage msg)
    {
        return new BroadcastWithTraffic
        {
            TargetDurationSeconds = msg.TargetDurationSeconds,
            JobId = msg.JobId,
            Location = msg.Location,
            Traffic = msg.Traffic,
            Incidents = msg.Incidents.Select(i => new TrafficIncident(
                i.Id, i.Description, i.Severity,
                i.Location, i.Type, i.LengthMeters,
                i.FunctionalClass,
                Option.OfNullable(i.Latitude),
                Option.OfNullable(i.Longitude),
                SourceRecordIdentities: i.SourceRecordIdentities)).ToList(),
            VoiceId = Option.OfWhitespace(msg.VoiceId),
            Model = Option.OfWhitespace(msg.Model),
            VoiceSettings = QueueMessageMappingExtensions.MapVoiceSettings(msg.VoiceSettings),
            PaceBoostWpm = msg.PaceBoostWpm,
            RejectionFeedback = Option.OfWhitespace(msg.RejectionFeedback),
            PreviousScript = Option.OfWhitespace(msg.PreviousScript),
            CuratedCorridorRoads = msg.SelectedCorridors is { Count: > 0 }
                ? Option<IReadOnlyList<string>>.Some(msg.SelectedCorridors)
                : Option<IReadOnlyList<string>>.None,
            Content = msg.Content ?? BulletinContentSelection.Default,
            DeliveryIntent = Option.OfWhitespace(msg.DeliveryIntent),
            TonedScript = Option.OfWhitespace(msg.TonedScript),
            CuratedGoodRunRoads = msg.SelectedGoodRuns is { Count: > 0 }
                ? Option<IReadOnlyList<string>>.Some(msg.SelectedGoodRuns)
                : Option<IReadOnlyList<string>>.None,
            ExcludedIncidentKeys = msg.ExcludedIncidentKeys is { Count: > 0 }
                ? Option<IReadOnlyList<string>>.Some(msg.ExcludedIncidentKeys)
                : Option<IReadOnlyList<string>>.None,
            RevoiceOnly = msg.RevoiceOnly,
            // Everything on the regeneration queue was put there by an operator's regenerate
            // action — there is no other producer — so the flag needs no wire field.
            OperatorAttended = true,
            PronunciationStack = PronunciationStack.Of(
                msg.PronunciationLocator, msg.GlobalPronunciationLocator),
            DeliveryStyle = Option.OfNullable(msg.DeliveryStyle),
            DeliveryStyleId = Option.OfNullable(msg.DeliveryStyleId),
            DeliveryStyleVersion = Option.OfNullable(msg.DeliveryStyleVersion),
            Bookends = Option.OfNullable(msg.Bookends)
        };
    }
}
