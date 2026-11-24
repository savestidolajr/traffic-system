import { z } from 'zod'
import { camelizeKeys } from '../../../lib/api-utils'
import {
    VoiceSettingsApiSchema,
    ContentSelectionApiSchema,
    CoverageAreaApiSchema,
    CoveragePointApiSchema,
} from '../../../lib/api/schemas/shared'
import {
    STATUS_VALUES,
    FAILURE_STAGE_VALUES,
    TRAFFIC_STATUSES,
    SCRIPT_STATUSES,
    AUDIO_STATUSES,
} from '../config/status-values'

// =============================================================================
// Job Feature Schemas
// Uses camelizeKeys to convert PascalCase (API) to camelCase (Frontend)
// =============================================================================

const TimeWindowSchema = z
    .object({
        From: z.string(),
        To: z.string(),
    })
    .transform(camelizeKeys)

const BrandingSchema = z
    .object({
        StationTag: z.string(),
        SignOff: z.string(),
    })
    .transform(camelizeKeys)

// Manual transform (not pure camelizeKeys) because Settings uses ?? undefined
// to convert null -> undefined, matching VoiceConfig.settings?: VoiceSettings.
// camelizeKeys would preserve null, widening the output type.
const VoiceConfigSchema = z
    .object({
        Provider: z.string(),
        VoiceId: z.string(),
        VoiceName: z.string(),
        Model: z.string(),
        Settings: VoiceSettingsApiSchema.nullable().optional(),
    })
    .transform((api) => ({
        provider: api.Provider,
        voiceId: api.VoiceId,
        voiceName: api.VoiceName,
        model: api.Model,
        settings: api.Settings ?? undefined,
    }))

const JobInputSchema = z
    .object({
        BulletinType: z.string(),
        TimeWindow: TimeWindowSchema,
        Branding: BrandingSchema,
        Voice: VoiceConfigSchema,
    })
    .transform(camelizeKeys)
const SOURCE_IDS = [
    'here',
    'here-weather',
    'here-fuel',
    'livetraffic-nsw',
    'qldtraffic',
    'victraffic',
    'mainroads-wa',
    'trafficsa',
    'tasalert',
    'dataquoll',
] as const

const EmergencyWarningLevelSchema = z.enum([
    'None',
    'Advice',
    'WatchAndAct',
    'EmergencyWarning',
])

const EmergencyEventSchema = z
    .object({
        Id: z.string(),
        SourceFeedId: z.string(),
        Agency: z.string(),
        Title: z.string(),
        EventType: z.string().nullish(),
        EventCategory: z.string().nullish(),
        WarningLevel: EmergencyWarningLevelSchema,
        Severity: z.string().nullish(),
        Urgency: z.string().nullish(),
        Certainty: z.string().nullish(),
        Address: z.string().nullish(),
        Suburb: z.string().nullish(),
        State: z.string().nullish(),
        Latitude: z.number().nullish(),
        Longitude: z.number().nullish(),
        DistanceKm: z.number().nullish(),
        ReportedAtUtc: z.string().nullish(),
        UpdatedAtUtc: z.string().nullish(),
        AirEligible: z.boolean(),
        Licence: z.string().nullish(),
    })
    .transform(camelizeKeys)
const SOURCE_FETCH_OUTCOMES = [
    'Ok',
    'ServedStale',
    'Failed',
    'Skipped',
] as const

export const SourceFetchDiagnosticSchema = z
    .object({
        SourceId: z.enum(SOURCE_IDS),
        Outcome: z.enum(SOURCE_FETCH_OUTCOMES),
        Detail: z.string().nullish(),
        DataAsOfUtc: z.string().nullish(),
        StaleSeconds: z.number().nullish(),
    })
    .transform(camelizeKeys)

const SourceRecordIdentitySchema = z
    .object({
        SourceId: z.string(),
        RecordId: z.string(),
        OriginalRecordId: z.string().nullable().optional(),
    })
    .transform(camelizeKeys)

export const OfficialEvidenceSchema = z
    .object({
        SourceId: z.string(),
        Headline: z.string(),
        IsIncidentClass: z.boolean(),
        RoadName: z.string().nullable().optional(),
        CrossStreet: z.string().nullable().optional(),
        Suburb: z.string().nullable().optional(),
        Direction: z.string().nullable().optional(),
        Description: z.string().nullable().optional(),
        StartTime: z.string().nullable().optional(),
        EndTime: z.string().nullable().optional(),
        PublishedTime: z.string().nullable().optional(),
        ShapePointCount: z.number().optional().default(0),
        MatchBasis: z.string().nullable().optional(),
        AuthorityPriority: z.string().nullable().optional(),
        Category: z.string().nullable().optional(),
        SubCategory: z.string().nullable().optional(),
        IsStateRoad: z.boolean().nullable().optional(),
        IsPlanned: z.boolean().nullable().optional(),
        AdviceTags: z.array(z.string()).nullable().optional(),
        ImpactType: z.string().nullable().optional(),
        ImpactSubtype: z.string().nullable().optional(),
        QualitativeDelay: z.string().nullable().optional(),
        SpeedLimitKph: z.number().nullable().optional(),
        LastUpdated: z.string().nullable().optional(),
        SourceRecordId: z.string().nullable().optional(),
    })
    .transform(camelizeKeys)
export const JobIncidentSchema = z
    .object({
        Key: z.string(),
        Priority: z.number(),
        Headline: z.string(),
        Detail: z.string().optional().or(z.literal('')).nullable(),
        FunctionalClass: z.number().optional().default(5),
        LengthMeters: z.number().optional().default(0),
        Latitude: z.number().optional().nullable(),
        Longitude: z.number().optional().nullable(),
        IsNew: z.boolean().optional().default(false),
        Shape: z.array(CoveragePointApiSchema).optional().nullable(),
        // Optional HERE-derived refinements — absent for most incidents.
        Direction: z.string().nullable().optional(),
        LaneDetail: z.string().nullable().optional(),
        Comment: z.string().nullable().optional(),
        RoadClosed: z.boolean().optional().default(false),
        EntryTimeUtc: z.string().nullable().optional(),
        Type: z.string().nullable().optional(),
        StartTimeUtc: z.string().nullable().optional(),
        EndTimeUtc: z.string().nullable().optional(),
        Sources: z.array(z.string()).nullable().optional(),
        LanesClosed: z.number().int().positive().nullable().optional(),
        LanesTotal: z.number().int().positive().nullable().optional(),
        OtherAssets: z.array(z.string()).nullable().optional(),
        CanonicalClass: z.string().nullable().optional(),
        ProseFields: z.array(z.string()).nullable().optional(),
        SourceRecordIdentities: z
            .array(SourceRecordIdentitySchema)
            .nullable()
            .optional(),
    })
    .transform((api) => ({
        ...camelizeKeys(api),
        detail: api.Detail ?? '',
        shape: api.Shape ?? undefined,
    }))

export const TrafficCorridorSchema = z
    .object({
        RoadName: z.string(),
        Severity: z.number(),
        ImpactScore: z.number(),
        PrimaryType: z.string(),
        Description: z.string(),
        IncidentCount: z.number(),
        TotalLengthKm: z.number(),
        Rank: z.number(),
        IsSelected: z
            .boolean()
            .nullish()
            .transform((v) => v ?? false),
        FunctionalClass: z.number().optional().default(5),
        Direction: z.string().nullable().optional(),
        Suburb: z.string().nullable().optional(),
        DriveTimeMinutes: z.number().nullable().optional(),
        NormalDriveTimeMinutes: z.number().nullable().optional(),
        DelayMinutes: z.number().nullable().optional(),
        Latitude: z.number().nullable().optional(),
        Longitude: z.number().nullable().optional(),
        IncidentKeys: z.array(z.string()).nullable().optional(),
        PriorityBoosted: z.boolean().optional().default(false),
        BaseScore: z.number().nullable().optional(),
        BoostAmount: z.number().nullable().optional(),
        Magnitude: z.string().nullable().optional(),
        Sources: z.array(z.string()).nullable().optional(),
        CorroborationCount: z.number().optional().default(1),
        OfficialHeadline: z.string().nullable().optional(),
        OfficialCrossStreet: z.string().nullable().optional(),
        OfficialEvidence: OfficialEvidenceSchema.nullable().optional(),
        OfficialEvidences: z
            .array(OfficialEvidenceSchema)
            .nullable()
            .optional(),
    })
    .transform((api) => {
        const camel = camelizeKeys(api)
        const evidences =
            api.OfficialEvidences ??
            (api.OfficialEvidence ? [api.OfficialEvidence] : null)
        return {
            ...camel,
            officialEvidences: evidences ? evidences.map(camelizeKeys) : null,
        }
    })

export const FlowRoadSchema = z
    .object({
        RoadName: z.string(),
        JamFactor: z.number(),
        LengthKm: z.number(),
        IsWeighted: z.boolean(),
        IsSelected: z
            .boolean()
            .nullish()
            .transform((v) => v ?? false),
        Score: z
            .number()
            .nullish()
            .transform((v) => v ?? 0),
        PriorityBoosted: z
            .boolean()
            .nullish()
            .transform((v) => v ?? false),
        BaseScore: z.number().nullable().optional(),
        BoostAmount: z.number().nullable().optional(),
        FunctionalClass: z
            .number()
            .nullish()
            .transform((v) => v ?? 3),
    })
    .transform(camelizeKeys)
export const MonitoredRouteSchema = z
    .object({
        Name: z.string(),
        DriveTimeMinutes: z.number(),
        NormalDriveTimeMinutes: z.number(),
        DelayMinutes: z.number(),
        DistanceKm: z.number(),
    })
    .transform(camelizeKeys)

const TrafficDataSchema = z
    .object({
        FetchedAt: z.string(),
        Bbox: z.array(z.number()),
        Incidents: z.array(JobIncidentSchema),
        Corridors: z.array(TrafficCorridorSchema).optional().default([]),
        FlowRoads: z
            .array(FlowRoadSchema)
            .nullish()
            .transform((v) => v ?? []),
        MonitoredRoutes: z
            .array(MonitoredRouteSchema)
            .nullish()
            .transform((v) => v ?? []),
    })
    .transform(camelizeKeys)

export const ScriptSentenceSchema = z
    .object({
        Text: z.string(),
        CorridorKeys: z.array(z.string()),
        EvidenceIds: z
            .array(z.string())
            .nullish()
            .transform((v) => v ?? []),
        RouteKeys: z
            .array(z.string())
            .nullish()
            .transform((v) => v ?? []),
    })
    .transform(camelizeKeys)

const ScriptRoadPhraseSchema = z
    .object({
        RoadName: z.string(),
        Start: z.number().int().nonnegative(),
        End: z.number().int().nonnegative(),
        Text: z.string(),
    })
    .refine((span) => span.End > span.Start, {
        message: 'Road phrase span must have a positive range',
    })
    .transform(camelizeKeys)

const ScriptDataSchema = z
    .object({
        Data: z.object({
            Version: z.number(),
            Text: z.string(),
            EstimatedDurationSec: z.number(),
            Sentences: z.array(ScriptSentenceSchema).nullish(),
            RoadPhraseSpans: z.array(ScriptRoadPhraseSchema).nullish(),
            TaggedText: z.string().nullish(),
            DeliveryStyleId: z.string().nullish(),
            DeliveryStyleVersion: z.number().nullish(),
        }),
    })
    .transform((api) => camelizeKeys(api.Data))

const WordTimingSchema = z
    .object({
        Word: z.string(),
        StartSec: z.number(),
        EndSec: z.number(),
    })
    .transform(camelizeKeys)

export const WeatherReportSchema = z
    .object({
        Conditions: z.string(),
        TemperatureC: z.number(),
        WindDescription: z.string().nullish(),
        PrecipitationProbabilityPercent: z.number().optional().default(0),
        Alerts: z.array(z.string()).optional().default([]),
    })
    .transform((api) => ({
        conditions: api.Conditions,
        temperatureC: api.TemperatureC,
        windDescription: api.WindDescription ?? null,
        precipitationProbabilityPercent: api.PrecipitationProbabilityPercent,
        alerts: api.Alerts,
    }))

const FuelSummarySchema = z
    .object({
        FuelType: z.string(),
        MinPrice: z.number(),
        AveragePrice: z.number(),
        StationCount: z.number(),
        LatestModifiedUtc: z.string().nullish(),
    })
    .transform(camelizeKeys)

export const FuelReportSchema = z
    .object({
        Summaries: z.array(FuelSummarySchema).optional().default([]),
    })
    .transform((api) => ({ summaries: api.Summaries }))

const AudioDataSchema = z
    .object({
        Data: z
            .object({
                Format: z.enum(['mp3', 'wav']).nullish(),
                SampleRateHz: z.number().nullish(),
                DurationSec: z.number().nullish(),
                Url: z.string().nullish(),
            })
            .nullish(),
        MeasuredDurationSec: z.number().nullish(),
        DeltaSec: z.number().nullish(),
        WordTimings: z.array(WordTimingSchema).nullish(),
    })
    .transform((api) => ({
        data: {
            format: api.Data?.Format ?? 'mp3',
            sampleRateHz: api.Data?.SampleRateHz ?? 24000,
            durationSec: api.Data?.DurationSec ?? 0,
            url: api.Data?.Url ?? '',
        },
        measuredDurationSec: api.MeasuredDurationSec ?? 0,
        deltaSec: api.DeltaSec ?? 0,
        wordTimings: api.WordTimings ?? undefined,
    }))

const PreReviewSchema = z
    .object({
        MatchRatio: z.number(),
        SuspectWords: z.array(z.string()).optional().default([]),
        NumberMismatches: z.array(z.string()).optional().default([]),
    })
    .transform(camelizeKeys)

// The bulletin a fallback replaced, retained on the job so the operator can still read and hear
// what the pipeline produced. Declared AFTER PreReviewSchema: it composes it, and a const
// referenced before its declaration is a TDZ throw at module init, not a hoisted forward ref.
const SupersededTakeSchema = z
    .object({
        CapturedAt: z.string(),
        Reason: z.string(),
        Text: z.string(),
        TaggedText: z.string().nullish(),
        AudioUrl: z.string(),
        MeasuredDurationSec: z.number().nullish(),
        EstimatedDurationSec: z.number().nullish(),
        Sentences: z.array(ScriptSentenceSchema),
        WordTimings: z.array(WordTimingSchema).nullish(),
        PreReview: PreReviewSchema.nullish(),
        DeliveryStyleId: z.string().nullish(),
        DeliveryStyleVersion: z.number().nullish(),
    })
    .transform(camelizeKeys)

const ApprovalDecisionSchema = z
    .object({
        DecidedAt: z.string(),
        DecidedBy: z.string(),
    })
    .transform(camelizeKeys)

const RejectionDataSchema = z
    .object({
        Reason: z.string(),
        DecidedAt: z.string(),
        DecidedBy: z.string(),
    })
    .transform(camelizeKeys)

const RejectionHistoryEntrySchema = z
    .object({
        Reason: z.string(),
        Notes: z.string(),
        RejectedScript: z.string().nullable(),
        At: z.string(),
        By: z.string(),
    })
    .transform(camelizeKeys)

const PublishDataSchema = z
    .object({
        Destination: z.string(),
        PublishedAt: z.string(),
        PublishedBy: z.string(),
    })
    .transform(camelizeKeys)

const FallbackDataSchema = z
    .object({
        SourceJobId: z.string(),
        AppliedAt: z.string(),
        Reason: z.string(),
        Streak: z.number(),
    })
    .transform(camelizeKeys)
const DeliveryPendingDataSchema = z
    .object({
        Kind: z.string(),
        IdempotencyKey: z.string(),
        ReviewedEtag: z.string(),
        AttemptCount: z.number(),
        RequestedBy: z.string(),
    })
    .transform(camelizeKeys)

const MissedDataSchema = z
    .object({
        DeadlineAt: z.string(),
        At: z.string(),
    })
    .transform(camelizeKeys)

// Unknown stage values degrade to UNKNOWN instead of failing the whole
// job parse — a new backend stage must never blank the review screen.
const FailureStageSchema = z.enum(FAILURE_STAGE_VALUES).catch('UNKNOWN')

const AttemptResultSchema = z
    .object({
        AttemptNumber: z.number(),
        EstimatedDurationSec: z.number().nullable(),
        ActualDurationSec: z.number().nullable(),
        Succeeded: z.boolean(),
        FailureReason: z.string().nullable(),
        At: z.string(),
    })
    .transform(camelizeKeys)

const FailureDataSchema = z
    .object({
        Stage: FailureStageSchema,
        Message: z.string(),
        OccurredAt: z.string(),
        AttemptCount: z.number(),
        LastEstimatedDurationSec: z.number().nullable(),
        TargetDurationSec: z.number(),
        AttemptHistory: z.array(AttemptResultSchema),
    })
    .transform(camelizeKeys)

/**
 * `JobLogLevel` on the wire, as an int. The backend enum is `{ Info = 0, Warn = 1,
 * Error = 2 }` (`Domain/Models/JobLogLevel.cs`) and the response serialiser has no
 * `JsonStringEnumConverter`, so those three ordinals are the whole alphabet — there
 * is no DEBUG level and nothing ever emits 3.
 *
 * This used to read 0 as DEBUG and shift every level down one: an INFO line printed
 * the word "DEBUG", and — worse — a WARN line arrived as INFO, so `isFlaggedLog`
 * stopped flagging it. The pre-review warning an operator must act on ("1 suspect
 * pronunciation — Listen before approving") lost its amber row, its place in the
 * "N flagged" count, and the auto-expanded log panel. The mock fixtures encoded the
 * same off-by-one, so mock mode looked correct while the deployed app did not
 * (confirmed against dev job d94648ed, 2026-08-10).
 */
const LogLevelSchema = z
    .number()
    .transform((val): 'INFO' | 'WARN' | 'ERROR' => {
        switch (val) {
            case 1:
                return 'WARN'
            case 2:
                return 'ERROR'
            default:
                return 'INFO'
        }
    })

const LogEntrySchema = z
    .object({
        At: z.string(),
        Level: LogLevelSchema,
        Message: z.string(),
    })
    .transform(camelizeKeys)

const JobStatusSchema = z.enum(STATUS_VALUES)

const StatusHistoryItemSchema = z
    .object({
        Status: JobStatusSchema,
        At: z.string(),
        By: z.string(),
        Note: z.string(),
    })
    .transform(camelizeKeys)

const EditingGuidanceSchema = z
    .object({
        TargetWordCount: z.number(),
        MinWordCount: z.number(),
        MaxWordCount: z.number(),
        WordsPerMinute: z.number(),
        TargetDurationSeconds: z.number(),
    })
    .transform((g) => ({
        targetWordCount: g.TargetWordCount,
        minWordCount: g.MinWordCount,
        maxWordCount: g.MaxWordCount,
        wordsPerMinute: g.WordsPerMinute,
        targetDurationSeconds: g.TargetDurationSeconds,
    }))
const JobBaseFields = {
    JobId: z.string(),
    Station: z.string(),
    Market: z.string(),
    TargetDurationSec: z.number(),
    CreatedAt: z.string(),
    LastUpdatedAt: z.string(),
    Input: JobInputSchema,
    Logs: z.array(LogEntrySchema),
    StatusHistory: z.array(StatusHistoryItemSchema),
    CurationDeadlineUtc: z.string().nullish(),
}

const OptionalDataFields = {
    Traffic: TrafficDataSchema.nullish(),
    SelectedIncidents: z.array(JobIncidentSchema).nullish(),
    Script: ScriptDataSchema.nullish(),
    Audio: AudioDataSchema.nullish(),
    MeasuredDurationSec: z.number().nullish(),
    DeltaSec: z.number().nullish(),
    Approval: ApprovalDecisionSchema.nullish(),
    Rejection: RejectionDataSchema.nullish(),
    RejectionHistory: z.array(RejectionHistoryEntrySchema).nullish(),
    Publish: PublishDataSchema.nullish(),
    Fallback: FallbackDataSchema.nullish(),
    Delivery: DeliveryPendingDataSchema.nullish(),
    Missed: MissedDataSchema.nullish(),
    Failure: FailureDataSchema.nullish(),
    SourceDiagnostics: z.array(SourceFetchDiagnosticSchema).nullish(),
    EmergencyEvents: z.array(EmergencyEventSchema).nullish(),
    Content: ContentSelectionApiSchema.nullish(),
    Weather: WeatherReportSchema.nullish(),
    Fuel: FuelReportSchema.nullish(),
    GoodRuns: z.array(z.string()).nullish(),
    Coverage: CoverageAreaApiSchema.nullish(),
    // (the dictionary's ElevenLabs id); null = none attached. camelizeKeys maps it.
    PronunciationDictionaryId: z.string().nullish(),
    // Historical ElevenLabs dictionary version used for this bulletin; null on legacy jobs.
    PronunciationVersionId: z.string().nullish(),
    // The platform-wide dictionary that also applied, underneath the station's; null = none active.
    GlobalPronunciationDictionaryId: z.string().nullish(),
    GlobalPronunciationVersionId: z.string().nullish(),
    EditingGuidance: EditingGuidanceSchema.nullish(),
    PreReview: PreReviewSchema.nullish(),
    // The bulletin a fallback replaced. Nullish is required, not cosmetic: the response
    // serialiser writes every key (DefaultIgnoreCondition = Never), so a job that never
    // generated a script sends `SupersededTake: null` rather than omitting it.
    SupersededTake: SupersededTakeSchema.nullish(),
    // Azure Table Storage ETag of the row this job was mapped from; used to
    // bind approve/publish decisions to the exact revision reviewed.
    Etag: z.string().nullish(),
}

export const JobSchema = z
    .object({
        ...JobBaseFields,
        Status: JobStatusSchema,
        ...OptionalDataFields,
    })
    .transform((api) => ({
        ...camelizeKeys(api),
        status: api.Status,
        traffic: api.Traffic ?? undefined,
        selectedIncidents: api.SelectedIncidents ?? [],
        script: api.Script ?? undefined,
        audio: api.Audio ?? undefined,
        measuredDurationSec: api.MeasuredDurationSec ?? 0,
        deltaSec: api.DeltaSec ?? 0,
        approval: api.Approval ?? undefined,
        rejection: api.Rejection ?? undefined,
        rejectionHistory: api.RejectionHistory ?? [],
        publish: api.Publish ?? undefined,
        fallback: api.Fallback ?? undefined,
        delivery: api.Delivery ?? undefined,
        missed: api.Missed ?? undefined,
        failure: api.Failure ?? undefined,
        content: api.Content ?? undefined,
        weather: api.Weather ?? undefined,
        fuel: api.Fuel ?? undefined,
        goodRuns: api.GoodRuns ?? [],
        coverage: api.Coverage ?? undefined,
        editingGuidance: api.EditingGuidance ?? undefined,
        preReview: api.PreReview ?? undefined,
        sourceDiagnostics: api.SourceDiagnostics ?? [],
        // The current JobDetailDtoBase omits EmergencyEvents while
        // TrafficEvidenceBuilder.ApplyMarketContent drops them. Preserve absence
        // so the review panel is not shown for real DTOs until that projection lands.
        emergencyEvents: api.EmergencyEvents ?? undefined,
        curationDeadlineAt: api.CurationDeadlineUtc ?? undefined,
    }))
    // Enforce the Job union's per-status invariants. The stage-group guards
    // (hasTraffic/hasScript/hasAudio) only check STATUS membership, so a job
    // claiming SCRIPT_READY with no script would crash deep in the cockpit —
    // failing here surfaces the contract violation at the boundary instead,
    // and it is what justifies job-hooks' `parse(data) as Job` narrowing.
    //
    // The decision fields belong here for the same reason and used not to be:
    // the union declares `rejection` on RejectedJob, `approval` on Approved/
    // Published, `publish` on PublishedJob and `fallback` on FallbackJob as
    // NON-OPTIONAL, and `AsAiredCard` reads `job.rejection.reason` on that
    // promise the moment a status turns terminal. Leaving them unchecked meant
    // `parse(data) as Job` asserted a union the parse had not proved, so a
    // terminal payload missing its decision record surfaced as the error
    // boundary's "Cannot read properties of undefined (reading 'reason')"
    // instead of a named contract violation at the boundary.
    .superRefine((job, ctx) => {
        const requirements = [
            [TRAFFIC_STATUSES, 'traffic'],
            [SCRIPT_STATUSES, 'script'],
            [AUDIO_STATUSES, 'audio'],
            [['APPROVED', 'PUBLISHED'], 'approval'],
            [['DELIVERY_PENDING'], 'delivery'],
            [['REJECTED'], 'rejection'],
            [['PUBLISHED'], 'publish'],
            [['FALLBACK_USED'], 'fallback'],
            [['MISSED'], 'missed'],
        ] as const
        for (const [statuses, field] of requirements) {
            if (
                (statuses as readonly string[]).includes(job.status) &&
                job[field] == null
            ) {
                ctx.addIssue({
                    code: 'custom',
                    path: [field],
                    message: `status ${job.status} requires ${field} data, but the payload carries none`,
                })
            }
        }
    })

// ROUND2-AUDIT: Both JobSchema and JobSummarySchema redundantly re-declare
// `status: api.Status` after `...camelizeKeys(api)` already converts PascalCase
// Status→status. The transforms differ enough (different fallback patterns,
// different field sets) that extracting a shared helper isn't clean. Intentional.

// API contract is PascalCase. Accepting the camelCase fallback silently masked
// real API shape changes (a backend rename to lowercase would have produced
// "unknown"/"Unknown" placeholders instead of a hard schema error). Require
// PascalCase only — let zod raise loudly if the API drifts.
export const JobSummarySchema = z
    .object({
        JobId: z.string(),
        CreatedAt: z.string(),
        Station: z.string(),
        Market: z.string(),
        TargetDurationSec: z.number(),
        Status: JobStatusSchema,
        LastUpdatedAt: z.string().nullish(),
        MeasuredDurationSec: z.number().nullish(),
        DeltaSec: z.number().nullish(),
        Etag: z.string().nullish(),
    })
    .transform((api) => ({
        ...camelizeKeys(api),
        jobId: api.JobId,
        createdAt: api.CreatedAt,
        station: api.Station,
        market: api.Market,
        targetDurationSec: api.TargetDurationSec,
        status: api.Status,
        lastUpdatedAt: api.LastUpdatedAt ?? api.CreatedAt,
        measuredDurationSec: api.MeasuredDurationSec ?? 0,
        deltaSec: api.DeltaSec ?? 0,
    }))

export const JobsListResponseSchema = z
    .object({
        Items: z.array(JobSummarySchema).nullish(),
        Jobs: z.array(JobSummarySchema).nullish(),
        jobs: z.array(JobSummarySchema).nullish(),
        Total: z.number().nullish(),
        HasMore: z.boolean().nullish(),
    })
    .transform((api) => ({
        jobs: api.Items ?? api.Jobs ?? api.jobs ?? [],
        total: api.Total ?? 0,
        hasMore: api.HasMore ?? false,
    }))

// =============================================================================
// Delivery iteration — audition and rewrite-for-register.
// These MUST go through a schema like everything else: the API serialises
// PascalCase and the first version of these hooks read `res.data.script`
// directly, so both calls returned 200 and were then silently ignored by the UI.
// The mock adapter answers in camelCase, which hid it until a deployed run.
// =============================================================================

export const AuditionResponseSchema = z
    .object({
        Url: z.string().default(''),
        DurationSec: z.number().default(0),
        TagCount: z.number().default(0),
    })
    .transform(camelizeKeys)

// `PreservedLines` counts the station-authored lines (bookend intro/outro, filler statements) the
// backend held out of the rewrite and put back verbatim. Defaulted so a pre-deploy backend, or the
// mock adapter, still parses — zero reads as "nothing was withheld", which is the old behaviour.
export const RetoneResponseSchema = z
    .object({
        Script: z.string().default(''),
        WordsBefore: z.number().default(0),
        WordsAfter: z.number().default(0),
        Tags: z.array(z.string()).default([]),
        PreservedLines: z.number().default(0),
    })
    .transform(camelizeKeys)

export const RewriteResponseSchema = z
    .object({
        Script: z.string().default(''),
        WordsBefore: z.number().default(0),
        WordsAfter: z.number().default(0),
        PreservedLines: z.number().default(0),
    })
    .transform(camelizeKeys)
