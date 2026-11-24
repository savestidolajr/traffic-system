namespace ArnBulletinGenerator.Application.ConfigTransfer;

/// <summary>
///     Single source of truth for config-bundle field length/count limits. Shared by the
///     per-library HTTP triggers (request-body validation) and <see cref="ConfigImportService" />
///     (bundle import validation) so the two paths can never drift apart.
/// </summary>
public static class ConfigValidationLimits
{
    public const int MaxTermLength = 120;
    public const int MaxTextLength = 600;
    public const int MaxRoads = 50;
    public const int MaxRoadLength = 80;
    public const int MaxStatements = 50;
    public const int MaxStatementLength = 500;
    public const int MaxVerifiedPronunciations = 200;
    public const int MaxVerifiedPronunciationLength = 80;
    public const int MaxVerifiedPronunciationNoteLength = 200;

    public const int VoiceProfileNameMax = 120;
    public const int VoiceProfileDescriptionMax = 1000;

    /// <summary>
    ///     Upper bound on a voice profile's free-text version note. Mirrors the SPA's
    ///     <c>VOICE_PROFILE_NOTE_MAX</c> (280) — the operator-facing cap. An uncapped note was the
    ///     one realistic way to push a single version past the Azure Table storage wall (the
    ///     note is the only unbounded field a version carries), so it is capped at the same
    ///     place every other operator text is: the HTTP trigger, BEFORE any provider call.
    /// </summary>
    public const int VoiceProfileChangeNoteMax = 280;

    public const int DeliveryStyleNameMax = 120;
    public const int DeliveryStyleDescriptionMax = 1000;
    public const int PronunciationDictionaryNameMax = 120;
    public const int PronunciationDictionaryDescriptionMax = 1000;
    public const int BookendNameMax = 120;
    public const int BookendDescriptionMax = 1000;

    /// <summary>A schedule's display name and station, capped like every other operator-authored name.</summary>
    public const int ScheduleNameMax = 120;
    public const int ScheduleStationMax = 120;

    /// <summary>
    ///     Upper bound on a single ad-hoc synthesis request. ~6 minutes of speech, and at the
    ///     ceiling ElevenLabs itself enforces per request. Without it one call could bill an
    ///     arbitrarily large job (a 15,200-character script was accepted on dev 2026-07-28).
    /// </summary>
    public const int MaxSynthesisScriptLength = 5_000;

    /// <summary>
    ///     Upper bound on an operator's free-text delivery intent (the /direct and /retone
    ///     instruction that is interpolated into the LLM prompt verbatim). The script payloads
    ///     those endpoints accept are capped at 3,000 characters; the intent is prompt input
    ///     rather than spoken text, but it must carry a ceiling too — a 13,000-character intent
    ///     was accepted on dev 2026-08-05, billing an arbitrarily large LLM prompt per call.
    ///     Matches the 3,000-character script caps the same endpoints already enforce.
    /// </summary>
    public const int MaxDeliveryIntentLength = 3_000;

    /// <summary>
    ///     Upper bound on an operator's rejection notes (<c>POST /jobs/{id}/reject</c> and
    ///     <c>/reject-to-fallback</c>). Notes are stored verbatim in every RejectionHistory round
    ///     AND echoed into the job's LogsJson — both share the job row's 32K-char per-column
    ///     ceiling, so an unbounded note lets a single rejection wedge the whole row (the record
    ///     can then never be approved/published/failed because every state write 500s).
    ///     Deliberately looser than the review dialog's 1,000-char field: the reject+regenerate
    ///     review flow joins several change requests into one notes string, so a tight cap would
    ///     400 a legitimate workflow. 10,000 still bounds storage abuse firmly while never being
    ///     reachable from the UI.
    /// </summary>
    public const int MaxRejectionNotesLength = 10_000;

    /// <summary>
    ///     Floor on the gap between two scheduled bulletins for one station. A once-a-minute cron
    ///     across 13 markets is ~19,000 LLM + synthesis jobs a day, so the point is to catch a typo.
    ///     <para>
    ///         Deliberately 2, not 5. Measured against every schedule deployed on dev 2026-07-29:
    ///         adelaide's compound expression fires 15:59 then 16:02 — a real 3-minute gap around a
    ///         news break — with perth next at 8 minutes. A 5-minute floor was drafted first and
    ///         would have REJECTED adelaide's live programming on the next save. Anything at or
    ///         above 2 preserves every real schedule while still refusing every-minute.
    ///     </para>
    /// </summary>
    public const int MinCronIntervalMinutes = 2;
}
