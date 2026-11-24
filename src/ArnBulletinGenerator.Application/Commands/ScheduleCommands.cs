using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Commands;

/// <summary>
///     Input DTO for creating a market's broadcast schedule: cron cadence, coverage, voice
///     profile, delivery style, bulletin playbook, dictionary, and notify lead times.
/// </summary>
public sealed record CreateScheduleCommand(
    string MarketId,
    string Name,
    string Station,
    string Timezone,
    string CronExpression,
    bool Enabled,
    CoverageArea Coverage,
    string VoiceProfileId,
    string OutputPath,
    double TargetDurationSeconds,
    int NotifyBeforePlayoutMinutes = 15,
    int EscalationBeforePlayoutMinutes = 5,
    Option<BulletinContentSelection> ContentSelection = default,
    string DeliveryStyleId = "",
    string PlaybookId = "",
    string PronunciationDictionaryId = "",
    Option<VoiceRotationPolicy> VoiceRotation = default,
    Option<IReadOnlyList<FillSource>> FillOrder = default,
    string BookendId = "",
    Option<BookendRotationPolicy> BookendRotation = default,
    Option<IReadOnlyDictionary<string, string>> BookendTokens = default,
    Option<ScheduleCalendar> Calendar = default,
    bool CurationEnabled = false,
    bool PlayoutEnabled = true);

public sealed record UpdateScheduleCommand(
    string MarketId,
    Option<string> Name = default,
    Option<string> Station = default,
    Option<string> Timezone = default,
    Option<string> CronExpression = default,
    Option<bool> Enabled = default,
    Option<double> TargetDurationSeconds = default,
    Option<int> NotifyBeforePlayoutMinutes = default,
    Option<int> EscalationBeforePlayoutMinutes = default,
    Option<string> VoiceProfileId = default,
    Option<string> OutputPath = default,
    Option<CoverageArea> Coverage = default,
    Option<BulletinContentSelection> ContentSelection = default,
    Option<string> DeliveryStyleId = default,
    Option<string> PlaybookId = default,
    Option<string> PronunciationDictionaryId = default,
    Option<VoiceRotationPolicy> VoiceRotation = default,
    Option<IReadOnlyList<FillSource>> FillOrder = default,
    Option<string> BookendId = default,
    Option<BookendRotationPolicy> BookendRotation = default,
    Option<IReadOnlyDictionary<string, string>> BookendTokens = default,
    Option<ScheduleCalendar> Calendar = default,
    Option<bool> CurationEnabled = default,
    Option<bool> PlayoutEnabled = default);
