// =============================================================================
// Job Stage Machine Types
// =============================================================================

import {
    STATUS_VALUES,
    TRAFFIC_STATUSES,
    SCRIPT_STATUSES,
    AUDIO_STATUSES,
    TERMINAL_STATUSES,
    FAILURE_STAGE_VALUES,
} from './config/status-values'
import type { VoiceSettings } from '@/lib/api/schemas/shared'
import type {
    ContentSelection,
    CoverageArea,
    CoverageShapeType,
    CoveragePoint,
} from '@/lib/api/schemas/shared'
export type { ContentSelection }

export type FailureStage = (typeof FAILURE_STAGE_VALUES)[number]
export interface TimeWindow {
    readonly from: string
    readonly to: string
}

export interface Branding {
    readonly stationTag: string
    readonly signOff: string
}

export interface VoiceConfig {
    readonly provider: string
    readonly voiceId: string
    readonly voiceName: string
    readonly model: string
    readonly settings?: VoiceSettings
}

export interface JobInput {
    readonly bulletinType: string
    readonly timeWindow: TimeWindow
    readonly branding: Branding
    readonly voice: VoiceConfig
}

export interface SourceRecordIdentity {
    readonly sourceId: string
    readonly recordId: string
    readonly originalRecordId?: string | null
}

export type SourceId =
    | 'here'
    | 'here-weather'
    | 'here-fuel'
    | 'livetraffic-nsw'
    | 'qldtraffic'
    | 'victraffic'
    | 'mainroads-wa'
    | 'trafficsa'
    | 'tasalert'
    | 'dataquoll'

export type EmergencyWarningLevel =
    'None' | 'Advice' | 'WatchAndAct' | 'EmergencyWarning'

export interface EmergencyEvent {
    readonly id: string
    readonly sourceFeedId: string
    readonly agency: string
    readonly title: string
    readonly eventType?: string | null
    readonly eventCategory?: string | null
    readonly warningLevel: EmergencyWarningLevel
    readonly severity?: string | null
    readonly urgency?: string | null
    readonly certainty?: string | null
    readonly address?: string | null
    readonly suburb?: string | null
    readonly state?: string | null
    readonly latitude?: number | null
    readonly longitude?: number | null
    readonly distanceKm?: number | null
    readonly reportedAtUtc?: string | null
    readonly updatedAtUtc?: string | null
    readonly airEligible: boolean
    readonly licence?: string | null
}
export type SourceFetchOutcome = 'Ok' | 'ServedStale' | 'Failed' | 'Skipped'

export interface SourceFetchDiagnostic {
    readonly sourceId: SourceId
    readonly outcome: SourceFetchOutcome
    readonly detail?: string | null
    readonly dataAsOfUtc?: string | null
    readonly staleSeconds?: number | null
}

export interface JobIncident {
    readonly key: string
    readonly priority: number
    readonly headline: string
    readonly detail: string
    readonly functionalClass?: number
    readonly lengthMeters?: number
    readonly latitude?: number | null
    readonly longitude?: number | null
    readonly isNew?: boolean
    /** The incident's road geometry (HERE shape, capped) for drawing it as a
     *  line on the review map. Absent for point/shapeless incidents. */
    readonly shape?: readonly CoveragePoint[]
    /** Compass direction of travel, derived from the HERE shape bearing.
     *  Absent for short shapes where a bearing would be unreliable. */
    readonly direction?: string | null
    /** Lane-level state (e.g. "Two lanes blocked") — only carried when it adds
     *  detail beyond `detail`. */
    readonly laneDetail?: string | null
    /** Road authority's free-text reason (e.g. "Sewer Connection"), advisory
     *  language stripped and length-capped upstream. */
    readonly comment?: string | null
    /** HERE's own hard closure flag — more reliable than parsing `detail`. */
    readonly roadClosed?: boolean
    /** When HERE reported this incident (ISO string), for a "reported X ago"
     *  freshness read. */
    readonly entryTimeUtc?: string | null
    /** Evidence-modal fields: the record's own type/window/provenance as parsed. */
    readonly type?: string | null
    readonly startTimeUtc?: string | null
    readonly endTimeUtc?: string | null
    readonly sources?: readonly string[] | null
    /** Authority facts normalized for operator review; absent on legacy jobs. */
    readonly lanesClosed?: number | null
    readonly lanesTotal?: number | null
    readonly otherAssets?: readonly string[] | null
    readonly canonicalClass?: string | null
    /** Source-native record IDs retained for audit and revision-chain tracing. */
    readonly sourceRecordIdentities?: readonly SourceRecordIdentity[] | null
    /** Parsed field names whose values were recovered from source prose. */
    readonly proseFields?: readonly string[] | null
}

/** The matched authority record, bounded and verbatim-as-parsed — the receipt behind a chip. */
export interface OfficialEvidence {
    readonly sourceId: string
    readonly headline: string
    readonly isIncidentClass: boolean
    readonly roadName?: string | null
    readonly crossStreet?: string | null
    readonly suburb?: string | null
    readonly direction?: string | null
    readonly description?: string | null
    readonly startTime?: string | null
    readonly endTime?: string | null
    readonly publishedTime?: string | null
    readonly shapePointCount?: number
    readonly matchBasis?: string | null
    readonly authorityPriority?: string | null
    readonly category?: string | null
    readonly subCategory?: string | null
    readonly isStateRoad?: boolean | null
    readonly isPlanned?: boolean | null
    readonly adviceTags?: readonly string[] | null
    readonly impactType?: string | null
    readonly impactSubtype?: string | null
    readonly qualitativeDelay?: string | null
    readonly speedLimitKph?: number | null
    readonly lastUpdated?: string | null
    readonly sourceRecordId?: string | null
}
export interface TrafficCorridor {
    readonly roadName: string
    readonly severity: number
    readonly impactScore: number
    readonly primaryType: string
    readonly description: string
    readonly incidentCount: number
    readonly totalLengthKm: number
    readonly rank: number
    readonly isSelected: boolean
    readonly functionalClass: number
    readonly direction?: string | null
    readonly suburb?: string | null
    readonly driveTimeMinutes?: number | null
    readonly normalDriveTimeMinutes?: number | null
    readonly delayMinutes?: number | null
    readonly latitude?: number | null
    readonly longitude?: number | null
    /** Keys of the incidents aggregated into this corridor — the link that
     *  tells the UI which incidents actually back the bulletin. */
    readonly incidentKeys?: readonly string[] | null
    /** True when a per-market priority road boosted this corridor past the selection cap. */
    readonly priorityBoosted?: boolean
    readonly baseScore?: number | null
    readonly boostAmount?: number | null
    readonly magnitude?: string | null
    /** Provenance: the feeds this corridor's evidence came from ("here", "livetraffic-nsw", …).
     *  Undefined/empty = legacy HERE-only row. Replaces the HERE-centric corroboration statuses. */
    readonly sources?: readonly string[] | null
    readonly corroborationCount: number
    readonly officialHeadline?: string | null
    readonly officialCrossStreet?: string | null
    readonly officialEvidences?: readonly OfficialEvidence[] | null
}

export interface FlowRoad {
    readonly roadName: string
    /** Worst HERE jam factor (0 clear … 10 blocked) across the road's segments. */
    readonly jamFactor: number
    readonly lengthKm: number
    /** True when the road is on the station good-run list or market priority roads. */
    readonly isWeighted: boolean
    /** True when this road aired as a good run in the current bulletin. */
    readonly isSelected: boolean
    /** Comparable rank score (road importance + length, priority boost folded in) on the same
     *  scale as a corridor's impactScore — lets a flowing road be ranked beside incidents. */
    readonly score: number
    /** True when a market priority road (boost-flowing) lifted this road's score. */
    readonly priorityBoosted: boolean
    readonly baseScore?: number | null
    readonly boostAmount?: number | null
    readonly functionalClass: number
}

export interface MonitoredRoute {
    readonly name: string
    readonly driveTimeMinutes: number
    readonly normalDriveTimeMinutes: number
    /** Drive time minus typical; may be zero or negative when running to or quicker than usual. */
    readonly delayMinutes: number
    readonly distanceKm: number
}

export interface TrafficData {
    readonly fetchedAt: string
    readonly bbox: readonly number[]
    readonly incidents: readonly JobIncident[]
    readonly corridors: readonly TrafficCorridor[]
    /** Data-backed pool of roads HERE flow reports as flowing freely (the Flow tab). */
    readonly flowRoads: readonly FlowRoad[]
    /** Configured point-to-point commuter runs measured live against their typical time. */
    readonly monitoredRoutes: readonly MonitoredRoute[]
}

/** A single sentence with backend-determined lineage (from Script.Data.Sentences). */
export interface ScriptSentenceData {
    readonly text: string
    readonly corridorKeys: readonly string[]
    /** Atomic evidence committed by the structured Draft, retained for legacy-job recovery. */
    readonly evidenceIds: readonly string[]
    readonly routeKeys: readonly string[]
}

/** A server-resolved, data-backed road phrase in rendered-script coordinates. */
export interface ScriptRoadPhrase {
    readonly roadName: string
    readonly start: number
    readonly end: number
    readonly text: string
}

/** A server-resolved, data-backed road phrase in rendered-script coordinates. */
export interface ScriptRoadPhrase {
    readonly roadName: string
    readonly start: number
    readonly end: number
    readonly text: string
}

export interface ScriptData {
    readonly version: number
    readonly text: string
    readonly estimatedDurationSec: number
    readonly sentences?: readonly ScriptSentenceData[] | null
    /** UTF-16 ranges into taggedText when it is a valid aired read, otherwise text. */
    readonly roadPhraseSpans?: readonly ScriptRoadPhrase[] | null
    /** The synthesised read with ElevenLabs v3 audio tags; absent = plain (today). */
    readonly taggedText?: string | null
    readonly deliveryStyleId?: string | null
    readonly deliveryStyleVersion?: number | null
}

/** Weather snapshot the bulletin drew from (WS1), for review verification. */
export interface WeatherReport {
    readonly conditions: string
    readonly temperatureC: number
    readonly windDescription: string | null
    readonly precipitationProbabilityPercent: number
    readonly alerts: readonly string[]
}

export interface FuelSummary {
    readonly fuelType: string
    readonly minPrice: number
    readonly averagePrice: number
    readonly stationCount: number
    readonly latestModifiedUtc?: string | null
}

/** Fuel price snapshot the bulletin drew from (WS1), for review verification. */
export interface FuelReport {
    readonly summaries: readonly FuelSummary[]
}

/** Backend-measured per-word audio timing (seconds). */
export interface AudioWordTiming {
    readonly word: string
    readonly startSec: number
    readonly endSec: number
}

/** Transcript-vs-script audio pre-review verdict; absent on jobs generated
 *  before this feature or when transcription was unavailable. */
export interface PreReviewData {
    /** Fraction (0..1) of the script the transcript matched. */
    readonly matchRatio: number
    /** Words the transcript couldn't confidently match — worth an ear check. */
    readonly suspectWords: readonly string[]
    /** Aired quantities (e.g. "$1.63") the transcript couldn't confirm. */
    readonly numberMismatches: readonly string[]
}

interface AudioDataInner {
    readonly format: 'mp3' | 'wav'
    readonly sampleRateHz: number
    readonly durationSec: number
    readonly url: string
}

export interface AudioData {
    readonly data: AudioDataInner
    readonly measuredDurationSec: number
    readonly deltaSec: number
    /** Real per-word timings from synthesis alignment; absent on legacy jobs. */
    readonly wordTimings?: readonly AudioWordTiming[]
}

/** The bulletin this job generated before a fallback replaced it, retained read-only so the
 *  operator can still read and hear what the pipeline produced — not just what aired. Absent
 *  when the job never generated a script or never fell back. */
export interface SupersededTake {
    readonly capturedAt: string
    /** The recorded fallback reason token (e.g. `missed-window`), or a default sentence. */
    readonly reason: string
    readonly text: string
    readonly taggedText?: string | null
    /** Resolved URL for the take's own audio; empty when the blob could not be resolved. */
    readonly audioUrl: string
    readonly measuredDurationSec?: number | null
    readonly estimatedDurationSec?: number | null
    readonly sentences: readonly ScriptSentenceData[]
    readonly wordTimings?: readonly AudioWordTiming[] | null
    readonly preReview?: PreReviewData | null
    readonly deliveryStyleId?: string | null
    readonly deliveryStyleVersion?: number | null
}

export interface ApprovalDecision {
    readonly decidedAt: string
    readonly decidedBy: string
}

export interface RejectionData {
    readonly reason: string
    readonly decidedAt: string
    readonly decidedBy: string
}

export interface RejectionHistoryEntry {
    readonly reason: string
    readonly notes: string
    readonly rejectedScript: string | null
    readonly at: string
    readonly by: string
}

export interface PublishData {
    readonly destination: string
    readonly publishedAt: string
    readonly publishedBy: string
}

export interface FallbackData {
    readonly sourceJobId: string
    readonly appliedAt: string
    readonly reason: string
    readonly streak: number
}

export interface AttemptResult {
    readonly attemptNumber: number
    readonly estimatedDurationSec: number | null
    readonly actualDurationSec: number | null
    readonly succeeded: boolean
    readonly failureReason: string | null
    readonly at: string
}

export interface FailureData {
    readonly stage: FailureStage
    readonly message: string
    readonly occurredAt: string
    readonly attemptCount: number
    readonly lastEstimatedDurationSec: number | null
    readonly targetDurationSec: number
    readonly attemptHistory: readonly AttemptResult[]
}

export interface LogEntry {
    readonly at: string
    readonly level: 'INFO' | 'WARN' | 'ERROR'
    readonly message: string
}

export interface StatusHistoryItem {
    readonly status: JobSummaryStatus
    readonly at: string
    readonly by: string
    readonly note: string
}

// =============================================================================
// Job State Models
// =============================================================================

export interface BulletinEditingGuidance {
    readonly targetWordCount: number
    readonly minWordCount: number
    readonly maxWordCount: number
    readonly wordsPerMinute: number
    readonly targetDurationSeconds: number
}

interface JobBase {
    readonly jobId: string
    readonly station: string
    readonly market: string
    readonly targetDurationSec: number
    readonly createdAt: string
    readonly lastUpdatedAt: string
    readonly input: JobInput
    readonly logs: readonly LogEntry[]
    readonly statusHistory: readonly StatusHistoryItem[]
    /** Content sources this bulletin used; undefined = legacy traffic-only. */
    readonly content?: ContentSelection
    /** Weather data the bulletin drew from, for review verification. */
    readonly weather?: WeatherReport
    /** Fuel price data the bulletin drew from, for review verification. */
    readonly fuel?: FuelReport
    /** Outcomes observed while fetching each upstream source for this bulletin. */
    readonly sourceDiagnostics?: readonly SourceFetchDiagnostic[]
    /** Emergency events retrieved for this bulletin's coverage area. */
    readonly emergencyEvents?: readonly EmergencyEvent[]
    /** Priority roads reported as flowing well in this bulletin. */
    readonly goodRuns: readonly string[]
    /** Point-in-time snapshot of the coverage shape this bulletin was generated
     *  with; undefined for legacy circle-only jobs (map falls back to bbox). */
    readonly coverage?: CoverageArea
    /** ElevenLabs pronunciation dictionary applied to this bulletin (the
     *  dictionary's ElevenLabs id); resolved to a name in the config view. */
    readonly pronunciationDictionaryId?: string | null
    /** Historical ElevenLabs dictionary version used for this bulletin; null on legacy jobs. */
    readonly pronunciationVersionId?: string | null
    /** The platform-wide dictionary that also applied, underneath the station's; null = none. */
    readonly globalPronunciationDictionaryId?: string | null
    readonly globalPronunciationVersionId?: string | null
    /** Word/time editing band for a verbatim re-voice edit (target + min/max the
     *  audio pipeline can stretch/compress to the slot); present only when editable. */
    readonly editingGuidance?: BulletinEditingGuidance
    /** Transcript-vs-script audio verification; absent pre-feature jobs or
     *  when transcription was unavailable. */
    readonly preReview?: PreReviewData
    /** Fixed slot-owned deadline for the five-minute operator curation window. */
    readonly curationDeadlineAt?: string | null
    /** Azure Table Storage ETag of the row this job was mapped from; used to
     *  bind approve/publish decisions to the exact revision reviewed. */
    readonly etag?: string | null
    /** The bulletin a fallback replaced on this job. On JobBase, not a leaf: a FALLBACK_USED
     *  job, the PUBLISHED job that delivered the cover, and a FAILED job all carry it. */
    readonly supersededTake?: SupersededTake | null
}

export interface RequestedJob extends JobBase {
    readonly status: 'REQUESTED'
}

export interface TrafficFetchedJob extends JobBase {
    readonly status: 'HERE_FETCHED'
    readonly traffic: TrafficData
}

export interface IncidentsSelectedJob extends JobBase {
    readonly status: 'INCIDENTS_SELECTED'
    readonly traffic: TrafficData
    readonly selectedIncidents: readonly JobIncident[]
}
export interface AwaitingCurationJob extends JobBase {
    readonly status: 'AWAITING_CURATION'
    readonly traffic: TrafficData
    readonly selectedIncidents: readonly JobIncident[]
}

export interface ScriptReadyJob extends JobBase {
    readonly status: 'SCRIPT_READY'
    readonly traffic: TrafficData
    readonly selectedIncidents: readonly JobIncident[]
    readonly script: ScriptData
}

export interface SynthesizingAudioJob extends JobBase {
    readonly status: 'SYNTHESIZING_AUDIO'
    readonly traffic: TrafficData
    readonly selectedIncidents: readonly JobIncident[]
    readonly script: ScriptData
    readonly audio: AudioData
}

export interface AudioReadyJob extends JobBase {
    readonly status: 'AUDIO_READY'
    readonly traffic: TrafficData
    readonly selectedIncidents: readonly JobIncident[]
    readonly script: ScriptData
    readonly audio: AudioData
}

export interface WaitingApprovalJob extends JobBase {
    readonly status: 'WAITING_APPROVAL'
    readonly traffic: TrafficData
    readonly selectedIncidents: readonly JobIncident[]
    // Required, not optional: WAITING_APPROVAL is in both SCRIPT_STATUSES and
    // AUDIO_STATUSES, so `JobSchema`'s superRefine already refuses a payload
    // without them. Declaring them optional made the type WEAKER than the
    // parse and hid that a waiting bulletin always has a script and audio.
    readonly script: ScriptData
    readonly audio: AudioData
}

export interface ApprovedJob extends JobBase {
    readonly status: 'APPROVED'
    readonly traffic: TrafficData
    readonly selectedIncidents: readonly JobIncident[]
    readonly script: ScriptData
    readonly audio: AudioData
    readonly approval: ApprovalDecision
}

export interface RejectedJob extends JobBase {
    readonly status: 'REJECTED'
    readonly traffic: TrafficData
    readonly selectedIncidents: readonly JobIncident[]
    readonly script: ScriptData
    readonly audio: AudioData
    readonly rejection: RejectionData
    readonly rejectionHistory: readonly RejectionHistoryEntry[]
}

export interface PublishedJob extends JobBase {
    readonly status: 'PUBLISHED'
    readonly traffic: TrafficData
    readonly selectedIncidents: readonly JobIncident[]
    readonly script: ScriptData
    readonly audio: AudioData
    readonly approval: ApprovalDecision
    readonly publish: PublishData

    /**
     * Set when the audio that aired was NOT this slot's own bulletin: a repeat of
     * an earlier one today, or the standby cart. Present because coverage now
     * delivers, so a covered slot ends here rather than resting at FALLBACK_USED.
     */
    readonly fallback?: FallbackData
}
export interface DeliveryPendingData {
    readonly kind: string
    readonly idempotencyKey: string
    readonly reviewedEtag: string
    readonly attemptCount: number
    readonly requestedBy: string
}

export interface DeliveryPendingJob extends JobBase {
    readonly status: 'DELIVERY_PENDING'
    readonly traffic: TrafficData
    readonly selectedIncidents: readonly JobIncident[]
    readonly script: ScriptData
    readonly audio: AudioData
    readonly delivery: DeliveryPendingData
}

export interface FallbackJob extends JobBase {
    readonly status: 'FALLBACK_USED'
    readonly traffic: TrafficData
    readonly selectedIncidents: readonly JobIncident[]
    readonly script: ScriptData
    readonly audio: AudioData
    readonly fallback: FallbackData
}

export interface FailedJob extends JobBase {
    readonly status: 'FAILED'
    readonly failure?: FailureData
    /** Traffic data available at time of failure, when fetched before failure. */
    readonly traffic?: TrafficData
    readonly selectedIncidents?: readonly JobIncident[]
    /** Best script retained after retries, for operator inspection only. */
    readonly script?: ScriptData
    /** Best unpublished audio retained after retries, for operator inspection only. */
    readonly audio?: AudioData
}
export interface CancelledJob extends JobBase {
    readonly status: 'CANCELLED'
}

export interface MissedJob extends JobBase {
    readonly status: 'MISSED'
    readonly traffic: TrafficData
    readonly selectedIncidents: readonly JobIncident[]
    readonly script: ScriptData
    readonly audio: AudioData
    readonly missed: { readonly deadlineAt: string; readonly at: string }
}

export type Job =
    | RequestedJob
    | TrafficFetchedJob
    | IncidentsSelectedJob
    | AwaitingCurationJob
    | ScriptReadyJob
    | SynthesizingAudioJob
    | AudioReadyJob
    | WaitingApprovalJob
    | ApprovedJob
    | DeliveryPendingJob
    | RejectedJob
    | PublishedJob
    | FallbackJob
    | FailedJob
    | CancelledJob
    | MissedJob

export type Status = Job['status']

// =============================================================================
// Stage Groups
// =============================================================================

export type JobWithTraffic =
    | TrafficFetchedJob
    | IncidentsSelectedJob
    | AwaitingCurationJob
    | ScriptReadyJob
    | SynthesizingAudioJob
    | AudioReadyJob
    | WaitingApprovalJob
    | ApprovedJob
    | DeliveryPendingJob
    | RejectedJob
    | PublishedJob
    | FallbackJob
    | MissedJob

export type JobWithScript =
    | ScriptReadyJob
    | SynthesizingAudioJob
    | AudioReadyJob
    | WaitingApprovalJob
    | ApprovedJob
    | DeliveryPendingJob
    | RejectedJob
    | PublishedJob
    | FallbackJob
    | MissedJob

export type JobWithAudio =
    | AudioReadyJob
    | WaitingApprovalJob
    | ApprovedJob
    | RejectedJob
    | PublishedJob
    | FallbackJob
    | DeliveryPendingJob
    | MissedJob
export function hasTraffic(
    job: Job
): job is JobWithTraffic | (FailedJob & { readonly traffic: TrafficData }) {
    return (
        (TRAFFIC_STATUSES as readonly string[]).includes(job.status) ||
        (job.status === 'FAILED' && Boolean(job.traffic))
    )
}

export function isFailed(job: Job): job is FailedJob {
    return job.status === 'FAILED'
}

export function hasScript(
    job: Job
): job is JobWithScript | (FailedJob & { readonly script: ScriptData }) {
    return (
        (SCRIPT_STATUSES as readonly string[]).includes(job.status) ||
        (job.status === 'FAILED' && Boolean(job.script))
    )
}

export function hasAudio(
    job: Job
): job is JobWithAudio | (FailedJob & { readonly audio: AudioData }) {
    return (
        (AUDIO_STATUSES as readonly string[]).includes(job.status) ||
        (job.status === 'FAILED' && Boolean(job.audio))
    )
}

export function isTerminal(
    job: Job
): job is
    | PublishedJob
    | RejectedJob
    | FailedJob
    | FallbackJob
    | MissedJob
    | CancelledJob {
    return (TERMINAL_STATUSES as readonly string[]).includes(job.status)
}

// =============================================================================
// Summaries
// =============================================================================

export type JobSummaryStatus = (typeof STATUS_VALUES)[number]

export interface JobSummary {
    readonly jobId: string
    readonly createdAt: string
    readonly station: string
    readonly market: string
    readonly targetDurationSec: number
    readonly status: JobSummaryStatus
    readonly lastUpdatedAt: string
    readonly measuredDurationSec?: number
    readonly deltaSec?: number
    readonly etag?: string | null
}

// =============================================================================
// Requests
// =============================================================================

export interface OrchestrateRequest {
    readonly station?: string
    readonly market: string
    readonly targetDurationSec: number
    readonly voiceProvider?: string
    readonly voiceId?: string
    readonly voiceName?: string
    readonly voiceModel?: string
    readonly voiceSettings?: VoiceSettings
    /** Schedule's assigned profile; lets the backend resolve full profile tuning (incl. pace boost). */
    readonly voiceProfileId?: string
    readonly testMode?: boolean
    readonly latitude: number
    readonly longitude: number
    readonly radiusKm: number
    readonly bulletinType?: 'traffic'
    readonly shapeType?: CoverageShapeType
    readonly west?: number
    readonly south?: number
    readonly east?: number
    readonly north?: number
    readonly corridorPoints?: CoveragePoint[]
    readonly corridorRadiusMeters?: number
    readonly polygonPoints?: CoveragePoint[]
    /** Per-bulletin content sources; omitted = station/schedule default. */
    readonly content?: ContentSelection
}

/**
 * `status` is the only filter the backend's unpaged `GET /api/jobs` path reads.
 * `from`/`to`/`limit` select the bounded, newest-first path (see
 * `JobsHttpTrigger.ListJobsPagedAsync`). UI calls through `useJobs` always supply
 * `from` (the rolling 24-hour lower bound) and `limit`; History supplies its own
 * cursor and page size to load older settled jobs on demand.
 */
export interface JobFilters {
    readonly status?: string
    readonly from?: string
    readonly to?: string
    readonly limit?: number
}

export interface JobsListResponse {
    readonly jobs: readonly JobSummary[]
    readonly total: number
    /** Only meaningful when `limit` was set on the request: a full page came back, so an older page likely exists. */
    readonly hasMore: boolean
}
