using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Commands;

/// <summary>Builds create/update schedule commands from the wire request plus resolved domain fields.</summary>
public static class ScheduleCommandBuilder
{
    public static Option<CreateScheduleCommand> BuildCreateCommand(string marketId, UpsertScheduleRequest dto,
        Option<CoverageArea> coverage,
        Option<BulletinContentSelection> contentSelection,
        Option<VoiceRotationPolicy> rotation,
        Option<IReadOnlyList<FillSource>> fillOrder,
        Option<BookendRotationPolicy> bookendRotation = default,
        Option<ScheduleCalendar> calendar = default)
    {
        if (dto.Name is null || dto.Station is null || dto.CronExpression is null ||
            dto.Timezone is null || !coverage.HasValue ||
            dto.TargetDurationSeconds is null or <= 0)
            return Option<CreateScheduleCommand>.None;

        return new CreateScheduleCommand(
            marketId,
            dto.Name!,
            dto.Station!,
            dto.Timezone!,
            dto.CronExpression!,
            dto.Enabled ?? false,
            coverage.Value,
            dto.VoiceProfileId ?? "",
            OutputPath: dto.OutputPath ?? $"{marketId}/traffic/",
            TargetDurationSeconds: dto.TargetDurationSeconds!.Value,
            NotifyBeforePlayoutMinutes: dto.NotifyBeforePlayoutMinutes ?? 15,
            EscalationBeforePlayoutMinutes: dto.EscalationBeforePlayoutMinutes ?? 5,
            ContentSelection: contentSelection,
            DeliveryStyleId: dto.DeliveryStyleId ?? "",
            PlaybookId: dto.PlaybookId ?? "",
            PronunciationDictionaryId: dto.PronunciationDictionaryId ?? "",
            VoiceRotation: rotation,
            FillOrder: fillOrder,
            BookendId: dto.BookendId ?? "",
            BookendRotation: bookendRotation,
            BookendTokens: Option.OfNullable(dto.BookendTokens),
            Calendar: calendar,
            CurationEnabled: dto.CurationEnabled ?? false,
            // A NEW station defaults to armed, matching the domain default and every station that
            // predates the flag. An operator standing one up in a held-back environment turns it
            // off explicitly.
            PlayoutEnabled: dto.PlayoutEnabled ?? true);
    }

    /// <summary>
    ///     Builds the update command from the wire request plus resolved domain fields. An
    ///     explicitly supplied EMPTY value is lifted as Some("") — never as None — for
    ///     VoiceProfileId, Timezone, CronExpression and the other optional ids, so the domain
    ///     validators (MarketSchedule.ApplyUpdates / ScheduleService.ValidateCronAndTimezone)
    ///     run on it exactly as they do on create. Treating "" as "not supplied" used to make
    ///     clearing a required field return 200 with the old value silently kept (the wave-2
    ///     #24 defect, reproduced for cron/timezone/voice on dev 2026-08-05): the caller was
    ///     told the save succeeded while the edit was discarded.
    /// </summary>
    public static UpdateScheduleCommand BuildUpdateCommand(string marketId, UpsertScheduleRequest dto,
        Option<CoverageArea> coverage,
        Option<BulletinContentSelection> contentSelection,
        Option<VoiceRotationPolicy> rotation,
        Option<IReadOnlyList<FillSource>> fillOrder,
        Option<BookendRotationPolicy> bookendRotation = default,
        Option<ScheduleCalendar> calendar = default)
    {
        return new UpdateScheduleCommand(
            marketId,
            Option.OfWhitespace(dto.Name),
            Option.OfWhitespace(dto.Station),
            Option.OfNullable(dto.Timezone),
            Option.OfNullable(dto.CronExpression),
            Option.OfNullable(dto.Enabled),
            Option.OfNullable(dto.TargetDurationSeconds),
            Option.OfNullable(dto.NotifyBeforePlayoutMinutes),
            Option.OfNullable(dto.EscalationBeforePlayoutMinutes),
            Option.OfNullable(dto.VoiceProfileId),
            Option.OfWhitespace(dto.OutputPath),
            coverage,
            contentSelection,
            DeliveryStyleId: Option.OfNullable(dto.DeliveryStyleId),
            PlaybookId: Option.OfNullable(dto.PlaybookId),
            PronunciationDictionaryId: Option.OfNullable(dto.PronunciationDictionaryId),
            VoiceRotation: rotation,
            FillOrder: fillOrder,
            BookendId: Option.OfNullable(dto.BookendId),
            BookendRotation: bookendRotation,
            BookendTokens: Option.OfNullable(dto.BookendTokens),
            Calendar: calendar,
            CurationEnabled: Option.OfNullable(dto.CurationEnabled),
            PlayoutEnabled: Option.OfNullable(dto.PlayoutEnabled));
    }
}
