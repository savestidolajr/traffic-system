using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Application.Mapping;

/// <summary>
///     Maps queue messages to domain request types for orchestration.
/// </summary>
public static class QueueMessageMappingExtensions
{
    /// <summary>
    ///     Converts a <see cref="BroadcastQueueMessage" /> to a <see cref="BroadcastRequest" /> domain model.
    ///     Fails when the message carries no usable coverage (corrupt or hand-crafted payload).
    /// </summary>
    public static Result<BroadcastRequest> ToBroadcastRequest(this BroadcastQueueMessage message)
    {
        var coverageResult = message.Coverage is not null
            ? Result<CoverageArea>.Success(message.Coverage)
            : CoverageArea.Create(message.Latitude, message.Longitude, message.RadiusKm);
        if (coverageResult.IsFailure)
            return Result<BroadcastRequest>.Failure($"Queue message has invalid coverage: {coverageResult.Error}");

        if (string.IsNullOrWhiteSpace(message.Model))
            return Result<BroadcastRequest>.Failure(
                "Queue message has no resolved TTS model. Jobs must persist and queue the effective model.");

        return Result<BroadcastRequest>.Success(new BroadcastRequest
        {
            JobId = message.JobId,
            MarketId = message.MarketId,
            StationId = Option.OfWhitespace(message.StationId),
            TimezoneId = Option.OfWhitespace(message.TimezoneId),
            Location = message.Location,
            TargetDurationSeconds = message.TargetDurationSeconds,
            Coverage = coverageResult.Value,
            Phase = message.Phase,
            VoiceId = Option.OfWhitespace(message.VoiceId),
            Model = Option.OfWhitespace(message.Model),
            VoiceSettings = MapVoiceSettings(message.VoiceSettings),
            PaceBoostWpm = message.PaceBoostWpm,
            ScriptOnly = message.ScriptOnly,
            Content = message.Content ?? BulletinContentSelection.Default,
            DeliveryStyle = Option.OfNullable(message.DeliveryStyle),
            DeliveryStyleId = Option.OfWhitespace(message.DeliveryStyleId),
            DeliveryStyleVersion = Option.OfNullable(message.DeliveryStyleVersion),
            Playbook = message.Playbook,
            Bookends = Option.OfNullable(message.Bookends),
            PronunciationStack = PronunciationStack.Of(
                message.PronunciationLocator, message.GlobalPronunciationLocator),
            FillOrder = message.FillOrder is { Count: > 0 }
                ? message.FillOrder
                : [FillSource.Weather, FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler]
        });
    }

    /// <summary>
    ///     Builds a <see cref="BroadcastQueueMessage" /> from a validated ad-hoc request and its
    ///     associated job, layering on the station configuration resolved from the schedule the
    ///     request's location matched.
    ///     <para>
    ///         <paramref name="station" /> is what makes a manual "Trigger now" sound like the
    ///         station instead of like the platform default. It used to be absent entirely, so a
    ///         manual bulletin for a configured station aired with no delivery style, no
    ///         pronunciation dictionary, no bookends and the default fill order. Pass
    ///         <see cref="AdHocStationConfig.None" /> for a location that matched no schedule.
    ///     </para>
    /// </summary>
    public static BroadcastQueueMessage ToBroadcastQueueMessage(this ValidatedOrchestrationRequestDto validated,
        Job job, string marketId, AdHocStationConfig station)
    {
        string? voiceId = job.VoiceConfig.Match(
            vc => vc.VoiceId,
            () => validated.VoiceId.Match(v => (string?)v, () => null));
        string? model = job.VoiceConfig.Match(
            vc => vc.Model,
            () => validated.VoiceModel.Match(v => (string?)v, () => null));
        VoiceSettings? voiceSettings = job.VoiceConfig.Match(
            vc => vc.Settings.Match(v => (VoiceSettings?)v, () => null),
            () => validated.VoiceSettings.Match(v => (VoiceSettings?)v, () => null));
        double paceBoostWpm = job.VoiceConfig.Match(vc => vc.PaceBoostWpm, () => 0);

        return new BroadcastQueueMessage(
            job.JobId,
            marketId,
            validated.Location,
            (int)validated.TargetDurationSeconds,
            validated.Coverage.Latitude,
            validated.Coverage.Longitude,
            validated.Coverage.RadiusKm,
            voiceId,
            Model: model,
            VoiceSettings: voiceSettings,
            ScriptOnly: validated.ScriptOnly,
            Coverage: validated.Coverage,
            Content: validated.Content ?? station.Content,
            PaceBoostWpm: paceBoostWpm,
            // An explicit request fill order wins; otherwise inherit the station's.
            FillOrder: validated.FillOrder ?? station.FillOrder,
            DeliveryStyle: station.DeliveryStyle,
            DeliveryStyleId: station.DeliveryStyleId,
            DeliveryStyleVersion: station.DeliveryStyleVersion,
            Playbook: station.Playbook,
            PronunciationLocator: station.PronunciationLocator,
            GlobalPronunciationLocator: station.GlobalPronunciationLocator,
            Bookends: station.Bookends);
    }

    /// <summary>
    ///     Wraps a nullable <see cref="VoiceSettings" /> into an <see cref="Option{VoiceSettings}" />.
    /// </summary>
    public static Option<VoiceSettings> MapVoiceSettings(VoiceSettings? vs)
    {
        return vs is null ? Option<VoiceSettings>.None : Option<VoiceSettings>.Some(vs);
    }
}
