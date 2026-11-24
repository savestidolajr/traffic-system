// =============================================================================
// In-Memory Mock Store — mutable, mutates on POST/PUT/DELETE for realism
// =============================================================================
import {
    JOB_FIXTURES,
    VOICE_FIXTURES,
    VOICE_PROFILE_FIXTURES,
    SCHEDULE_FIXTURES,
    GUIDANCE_FIXTURES,
    GUIDANCE_CANDIDATE_FIXTURES,
    type JobFixture,
    type JobSummaryFixture,
    type VoiceFixture,
    type VoiceProfileFixture,
    type ScheduleFixture,
    type GuidanceFixture,
    type GuidanceCandidateFixture,
    SAFETY_TERM_FIXTURES,
    type SafetyTermFixture,
    DELIVERY_STYLE_FIXTURES,
    type DeliveryStyleFixture,
    BOOKEND_FIXTURES,
    type BookendFixture,
    type PronunciationRuleFixture,
    type PronunciationDictionaryFixture,
    PRONUNCIATION_DICTIONARY_FIXTURES,
    PLAYBOOK_FIXTURES,
    type PlaybookFixture,
} from './fixtures'
import DEV_DATA from './dev-data.json'

/**
 * Thrown by job mutation methods when an optimistic `expectedEtag` precondition
 * fails to match the stored job's current Etag. The adapter maps this to a
 * 409 response carrying the same { Error, Detail, Status } shape the real
 * backend's ErrorResponse produces.
 */
export class MockJobConflictError extends Error {
    constructor(message: string) {
        super(message)
        this.name = 'MockJobConflictError'
    }
}

/**
 * Advances a job's Etag after a mutation, mirroring Azure Table Storage's
 * behaviour of assigning a fresh ETag on every entity update. Increments a
 * trailing "-vN" suffix so repeated mutations produce distinct, orderable values.
 */
function bumpEtag(current: string | null | undefined): string {
    const match = /^(.*)-v(\d+)$/.exec(current ?? '')
    if (match) return `${match[1]}-v${Number(match[2]) + 1}`
    return `${current ?? 'mock-etag'}-v2`
}

interface OperatorDirectoryRecord {
    ObjectId: string
    DisplayName: string
    UserPrincipalName: string | null
    Roles: string[]
    Source: string
    Markets: string[]
    IsAdmin: boolean
    UpdatedAtUtc: string | null
    UpdatedBy: string | null
}

export interface PriorityRoadRecord {
    Name: string
    BoostIncidents: boolean
    FlowTier: 'none' | 'normal' | 'high' | 'critical'
    Enabled?: boolean
}

export interface PriorityRoadsResult {
    Market: string
    Roads: PriorityRoadRecord[]
}
export interface IncidentPriorityRuleRecord {
    IncidentClass: string
    DisplayName?: string
    ScoreBonus: number
    Enabled?: boolean
}

export interface IncidentPrioritiesResult {
    Market: string
    Rules: IncidentPriorityRuleRecord[]
}

export interface IncidentLandscapeEntryRecord {
    Provider: string
    CanonicalClass: string
    EventType: string
    Details: string
    Impact: string
    Codes?: string | null
    Count: number
    FirstSeenUtc: string
    LastSeenUtc: string
}

export interface IncidentLandscapeResult {
    Market: string
    Entries: IncidentLandscapeEntryRecord[]
    TotalCount: number
}

interface IncidentPriorityRuleInput {
    incidentClass: string
    displayName?: string
    scoreBonus?: number
    enabled?: boolean
}

interface PriorityRoadInput {
    name: string
    boostIncidents?: boolean
    flowTier?: 'none' | 'normal' | 'high' | 'critical'
    enabled?: boolean
}

export interface FillerAvailabilityRecord {
    StartDate?: string | null
    EndDate?: string | null
    Days?: string[] | null
    StartLocal?: string | null
    EndLocal?: string | null
}

export interface FillerStatementRecord {
    Id: string
    Text: string
    Enabled?: boolean
    Availability?: FillerAvailabilityRecord | null
}

export interface FillerStatementsResult {
    Market: string
    Statements: FillerStatementRecord[]
}

interface FillerAvailabilityInput {
    startDate?: string | null
    endDate?: string | null
    days?: readonly string[] | null
    startLocal?: string | null
    endLocal?: string | null
}

interface FillerStatementInput {
    id: string
    text: string
    enabled?: boolean
    availability?: FillerAvailabilityInput | null
}

export interface VerifiedPronunciationRecord {
    Word: string
    Note?: string | null
    Enabled?: boolean
    VerifiedBy?: string | null
    VerifiedAt?: string | null
}

export interface VerifiedPronunciationsResult {
    Market: string
    Words: VerifiedPronunciationRecord[]
}

interface VerifiedPronunciationInput {
    word: string
    note?: string | null
    enabled?: boolean
    verifiedBy?: string | null
    verifiedAt?: string | null
}

type ProfileVersionFixture = VoiceProfileFixture['Versions'][number]
type ProfileSettingsFixture = ProfileVersionFixture['Settings']

/** Mock asset every preview/sample points at (the only playable wav in dev). */
const PROFILE_SAMPLE_URI = '/mock/audio-bulletin.wav'
/** Nominal word count of the standard preview script — for duration estimates. */
const NOMINAL_SAMPLE_WORDS = 78
/** Measured read rate at 1.0× speed; preview WPM scales linearly with speed. */
const BASE_WORDS_PER_MINUTE = 165

/** Editable statuses that expose the verbatim re-voice editor (mirrors the
 *  backend IsEditable + web EDITABLE_STATES). */
const MOCK_EDITABLE_STATUSES: Record<string, true> = {
    WAITING_APPROVAL: true,
    AUDIO_READY: true,
}

/** Deterministic editing-guidance band for the mock, mirroring the backend
 *  BulletinEditingBand: target ≈ exact-slot word count at the calibrated WPM,
 *  with the slow-fill (÷1.10) / speed-up (×1.15) band around it. PascalCase so
 *  JobSchema parses it into the camelCase `editingGuidance`. */
function editingGuidanceFor(job: JobFixture): JobFixture['EditingGuidance'] {
    if (job.EditingGuidance) return job.EditingGuidance
    if (MOCK_EDITABLE_STATUSES[job.Status] !== true || !job.Script)
        return undefined
    const speed = job.Input.Voice.Settings?.Speed ?? 1.0
    const wpm = Math.round(BASE_WORDS_PER_MINUTE * speed)
    const slot = job.TargetDurationSec
    const target = Math.round((slot * wpm) / 60)
    return {
        TargetWordCount: target,
        MinWordCount: Math.round(target / 1.1),
        MaxWordCount: Math.round(target * 1.15),
        WordsPerMinute: wpm,
        TargetDurationSeconds: slot,
    }
}

function toSummary(job: JobFixture): JobSummaryFixture {
    return {
        JobId: job.JobId,
        CreatedAt: job.CreatedAt,
        Station: job.Station,
        Market: job.Market,
        TargetDurationSec: job.TargetDurationSec,
        Status: job.Status,
        LastUpdatedAt: job.LastUpdatedAt,
        MeasuredDurationSec: job.MeasuredDurationSec ?? null,
        DeltaSec: job.DeltaSec ?? null,
        Etag: job.Etag ?? null,
    }
}

function delay(ms = 150): Promise<void> {
    return new Promise((r) => setTimeout(r, ms))
}

/**
 * How long the mock pretends a regeneration takes. The deployed pipeline takes
 * ~15s for a re-voice and ~60s for a full regeneration; this only has to be long
 * enough that the REQUESTED window is a real, observable state locally.
 */
const MOCK_REGENERATE_MS = 1800
const MOCK_PROGRESS_STAGES = [
    'Evidence',
    'Select',
    'Write',
    'Compose',
    'FitWords',
    'Edit',
    'Gates',
    'Voice',
    'FitAudio',
] as const

const MOCK_PROGRESS_TYPICAL_SECONDS: Record<string, number> = {
    Evidence: 4,
    Select: 4,
    Write: 4,
    Compose: 4,
    FitWords: 4,
    Edit: 4,
    Gates: 4,
    Voice: 4,
    FitAudio: 4,
}

interface MockProgressState {
    readonly status: string
    readonly startedAtMs: number
}

interface MockProgressDocument {
    JobId: string
    Status: string
    Stage: string
    StageStartedAt: string
    Attempt: number
    MaxAttempts: number
    UpdatedAt: string
    Version: number
    Events: Array<{
        At: string
        Attempt: number
        Stage: string
        Kind: string
        Detail: string
        Counts?: Record<string, number> | null
        DurationMs?: number | null
        Model?: string | null
        Route?: string | null
    }>
    TypicalSeconds: Record<string, number>
}

// ---------------------------------------------------------------------------
// Bookend token helpers — mirror BookendTokenResolver so the mock preview and
// token extraction behave exactly like the deployed backend.
// ---------------------------------------------------------------------------

const BOOKEND_TOKEN_PATTERN = /\{([A-Za-z][A-Za-z0-9_]*)\}/g

/** Distinct token names in first-appearance order (mirrors ExtractTokens). */
function extractBookendTokens(text: string): string[] {
    const names: string[] = []
    for (const match of text.matchAll(BOOKEND_TOKEN_PATTERN)) {
        const name = match[1]
        if (!names.some((n) => n.toLowerCase() === name.toLowerCase())) {
            names.push(name)
        }
    }
    return names
}

/** Best-effort city name from a market id (mirrors the backend DeriveCity). */
function deriveCity(marketId: string): string {
    if (!marketId || !marketId.trim()) return ''
    const head = marketId.trim().split('-')[0]
    if (!head) return ''
    return head
        .toLowerCase()
        .replace(
            /(^|\s)([a-z])/g,
            (_m, pre: string, ch: string) => pre + ch.toUpperCase()
        )
}

/** Collapses repeated/tab whitespace to single spaces and trims (mirrors CollapseWhitespace). */
function collapseWhitespace(text: string): string {
    return text.replace(/[ \t]{2,}/g, ' ').trim()
}

/**
 * Resolves {token} placeholders in BOTH lines of a pair like the backend:
 * built-ins (city, market, station) first, overrides applied on top;
 * unresolved tokens are removed and reported so the studio can flag a pair
 * that would drop text on air.
 */
function resolveBookendTemplate(
    introTemplate: string,
    outroTemplate: string,
    marketId: string,
    station: string,
    overrides: Record<string, string>
): {
    ResolvedIntro: string
    ResolvedOutro: string
    UnresolvedTokens: string[]
    Values: Record<string, string>
} {
    // Mirrors the backend BookendTokenResolver, which merges into a
    // StringComparer.OrdinalIgnoreCase dictionary: an override REPLACES the built-in
    // whatever its casing. A plain object keyed by the raw name would instead add a
    // second entry ("City" beside "city") and the derived built-in would keep winning
    // the lookup below — so demo mode would show the operator a resolved sentence that
    // silently ignored their override.
    const values: Record<string, string> = {}
    const setValue = (key: string, value: string) => {
        const existing = Object.keys(values).find(
            (k) => k.toLowerCase() === key.toLowerCase()
        )
        values[existing ?? key] = value
    }
    setValue('city', deriveCity(marketId))
    setValue('market', marketId.trim())
    setValue('station', station.trim())
    for (const [key, value] of Object.entries(overrides)) {
        if (key.trim() && value.trim()) setValue(key.trim(), value.trim())
    }
    const unresolved: string[] = []
    const resolved: string[] = []
    for (const template of [introTemplate, outroTemplate]) {
        resolved.push(
            template.replace(BOOKEND_TOKEN_PATTERN, (_match, name: string) => {
                const value = Object.entries(values).find(
                    ([k]) => k.toLowerCase() === name.toLowerCase()
                )?.[1]
                if (value && value.trim()) return value
                if (
                    !unresolved.some(
                        (u) => u.toLowerCase() === name.toLowerCase()
                    )
                ) {
                    unresolved.push(name)
                }
                return ''
            })
        )
    }
    return {
        ResolvedIntro: collapseWhitespace(resolved[0]),
        ResolvedOutro: collapseWhitespace(resolved[1]),
        UnresolvedTokens: unresolved,
        Values: values,
    }
}

/** Whether any schedule still references a bookend (fixed id or rotation ids). */
function scheduleReferencesBookend(
    schedule: ScheduleFixture,
    id: string
): boolean {
    if (schedule.BookendId === id) {
        return true
    }
    const rotation = schedule.BookendRotation
    if (!rotation) return false
    if ((rotation.BookendIds ?? []).includes(id)) return true
    for (const window of rotation.Windows ?? []) {
        if ((window.BookendIds ?? []).includes(id)) return true
    }
    return false
}
export class MockStore {
    private _jobs: Map<string, JobFixture>
    private _voices: Map<string, VoiceFixture>
    private _deliveryStyles: Map<string, DeliveryStyleFixture>
    private _bookends: Map<string, BookendFixture>
    private _pronunciationDictionaries: Map<
        string,
        PronunciationDictionaryFixture
    >
    private _priorityRoads: Map<string, PriorityRoadRecord[]>
    private _incidentPriorities: Map<string, IncidentPriorityRuleRecord[]> =
        new Map()
    private _progressStates: Map<string, MockProgressState>
    private _fillerStatements: Map<string, FillerStatementRecord[]>
    private _verifiedPronunciations: Map<string, VerifiedPronunciationRecord[]>
    private _operatorAssignments: OperatorDirectoryRecord[]
    private _schedules: Map<string, ScheduleFixture>
    private _voiceProfiles: Map<string, VoiceProfileFixture>
    private _guidance: GuidanceFixture[]
    private _candidates: GuidanceCandidateFixture[]
    private _safetyTerms: SafetyTermFixture[]
    private _playbooks: Map<string, PlaybookFixture>
    private _spotChecks: Array<{
        JobId: string
        By: string
        AtUtc: string
    }>
    private _eventVerdicts: Map<
        string,
        { Verdict: string | null; Notes: string | null }
    >

    constructor() {
        this._jobs = new Map(
            JOB_FIXTURES.map((j) => [j.JobId, structuredClone(j)])
        )
        this._voices = new Map(
            VOICE_FIXTURES.map((v) => [v.VoiceId, structuredClone(v)])
        )
        this._deliveryStyles = new Map(
            DELIVERY_STYLE_FIXTURES.map((s) => [s.Id, structuredClone(s)])
        )
        this._bookends = new Map(
            BOOKEND_FIXTURES.map((b) => [b.Id, structuredClone(b)])
        )
        this._pronunciationDictionaries = new Map(
            PRONUNCIATION_DICTIONARY_FIXTURES.map((d) => [
                d.Id,
                structuredClone(d),
            ])
        )
        this._voiceProfiles = new Map(
            VOICE_PROFILE_FIXTURES.map((p) => [p.Id, structuredClone(p)])
        )
        this._schedules = new Map(
            SCHEDULE_FIXTURES.map((s) => [s.MarketId, structuredClone(s)])
        )
        this._guidance = GUIDANCE_FIXTURES.map((g) => structuredClone(g))
        this._candidates = GUIDANCE_CANDIDATE_FIXTURES.map((c) =>
            structuredClone(c)
        )
        this._safetyTerms = SAFETY_TERM_FIXTURES.map((s) => structuredClone(s))
        this._playbooks = new Map(
            PLAYBOOK_FIXTURES.map((playbook) => [
                playbook.Id,
                structuredClone(playbook),
            ])
        )
        this._priorityRoads = new Map(
            (
                DEV_DATA.PriorityRoads as Array<{
                    Market: string
                    Roads: PriorityRoadRecord[]
                }>
            ).map(
                (entry) => [entry.Market, structuredClone(entry.Roads)] as const
            )
        )
        this._fillerStatements = new Map(
            (
                DEV_DATA.FillerStatements as Array<{
                    Market: string
                    Statements: FillerStatementRecord[]
                }>
            ).map(
                (entry) =>
                    [entry.Market, structuredClone(entry.Statements)] as const
            )
        )
        this._verifiedPronunciations = new Map(
            (
                (DEV_DATA.VerifiedPronunciations ?? []) as Array<{
                    Market: string
                    Words: VerifiedPronunciationRecord[]
                }>
            ).map(
                (entry) => [entry.Market, structuredClone(entry.Words)] as const
            )
        )
        this._operatorAssignments = structuredClone(
            DEV_DATA.Operators as OperatorDirectoryRecord[]
        )

        this._spotChecks = []
        this._eventVerdicts = new Map()
        this._progressStates = new Map()
    }

    // ---------------------------------------------------------------------------
    // Jobs
    // ---------------------------------------------------------------------------
    async listJobs(): Promise<{ Items: JobSummaryFixture[]; Total: number }> {
        await delay()
        const items: JobSummaryFixture[] = []
        for (const job of this._jobs.values()) {
            items.push(toSummary(job))
        }
        return { Items: items, Total: items.length }
    }

    async getJob(id: string): Promise<JobFixture | null> {
        await delay()
        const job = this._jobs.get(id)
        if (!job) return null
        const editingGuidance = editingGuidanceFor(job)
        return editingGuidance
            ? { ...job, EditingGuidance: editingGuidance }
            : job
    }
    /** Build a deterministic progress document for a pipeline job. The first
     * request starts the mock clock, so old fixtures still expose a useful
     * live rail instead of jumping straight to a terminal stage.
     */
    getJobProgress(id: string): MockProgressDocument | null {
        const job = this._jobs.get(id)
        if (!job) return null
        const pipelineStatuses = [
            'REQUESTED',
            'HERE_FETCHED',
            'INCIDENTS_SELECTED',
            'AWAITING_CURATION',
            'SCRIPT_READY',
            'SYNTHESIZING_AUDIO',
            'AUDIO_READY',
            'DELIVERY_PENDING',
        ] as const
        if (!(pipelineStatuses as readonly string[]).includes(job.Status)) {
            return null
        }

        let state = this._progressStates.get(id)
        if (!state || state.status !== job.Status) {
            state = { status: job.Status, startedAtMs: Date.now() }
            this._progressStates.set(id, state)
        }

        const elapsedMs = Math.max(0, Date.now() - state.startedAtMs)
        const stageMs = 4000
        const completedCount = Math.min(
            Math.floor(elapsedMs / stageMs),
            MOCK_PROGRESS_STAGES.length
        )
        const currentIndex = Math.min(
            completedCount,
            MOCK_PROGRESS_STAGES.length - 1
        )
        const attempt = Math.max(
            1,
            job.StatusHistory.filter((entry) => entry.Status === 'REQUESTED')
                .length
        )
        const events: MockProgressDocument['Events'] = [
            {
                At: new Date(state.startedAtMs).toISOString(),
                Attempt: attempt,
                Stage: MOCK_PROGRESS_STAGES[0],
                Kind: 'AttemptStarted',
                Detail:
                    attempt > 1
                        ? `Attempt ${attempt} started after an earlier attempt.`
                        : 'First generation attempt started.',
            },
        ]

        for (let index = 0; index < completedCount; index += 1) {
            const stage = MOCK_PROGRESS_STAGES[index]
            const startedAtMs = state.startedAtMs + index * stageMs
            const startedAt = new Date(startedAtMs).toISOString()
            const completedAt = new Date(startedAtMs + stageMs).toISOString()
            const counts =
                stage === 'Evidence'
                    ? {
                          corridors: job.Traffic?.Corridors?.length ?? 0,
                          incidents: job.Traffic?.Incidents?.length ?? 0,
                      }
                    : undefined
            events.push({
                At: startedAt,
                Attempt: attempt,
                Stage: stage,
                Kind: 'StageStarted',
                Detail: `${stage} started.`,
            })
            if (stage === 'Compose') {
                events.push({
                    At: new Date(startedAtMs + 2000).toISOString(),
                    Attempt: attempt,
                    Stage: stage,
                    Kind: 'SubStep',
                    Detail: 'Candidate 1 returned.',
                    Counts: { candidate: 1, words: 84 },
                })
            }
            if (stage === 'FitWords') {
                events.push({
                    At: new Date(startedAtMs + 2000).toISOString(),
                    Attempt: attempt,
                    Stage: stage,
                    Kind: 'SubStep',
                    Detail: 'Round 1 returned.',
                    Counts: { round: 1, words: 86 },
                })
            }
            if (stage === 'Edit') {
                events.push({
                    At: new Date(startedAtMs + 2000).toISOString(),
                    Attempt: attempt,
                    Stage: stage,
                    Kind: 'SubStep',
                    Detail: 'Editor approved.',
                })
            }
            events.push({
                At: completedAt,
                Attempt: attempt,
                Stage: stage,
                Kind: 'StageCompleted',
                Detail: `${stage} completed.`,
                Counts: counts,
                DurationMs: stageMs,
            })
        }

        const terminal = completedCount >= MOCK_PROGRESS_STAGES.length
        const currentStage = terminal
            ? 'Ready'
            : MOCK_PROGRESS_STAGES[currentIndex]
        const currentStageStartedAt = new Date(
            state.startedAtMs + currentIndex * stageMs
        ).toISOString()

        if (!terminal) {
            events.push({
                At: currentStageStartedAt,
                Attempt: attempt,
                Stage: currentStage,
                Kind: 'StageStarted',
                Detail: `${currentStage} is in progress.`,
            })
        } else {
            events.push({
                At: new Date(
                    state.startedAtMs + MOCK_PROGRESS_STAGES.length * stageMs
                ).toISOString(),
                Attempt: attempt,
                Stage: 'Ready',
                Kind: 'Note',
                Detail: 'Generation is ready for review.',
            })
        }

        return {
            JobId: job.JobId,
            Status: job.Status,
            Stage: currentStage,
            StageStartedAt: currentStageStartedAt,
            Attempt: attempt,
            MaxAttempts: 3,
            UpdatedAt:
                events.at(-1)?.At ?? new Date(state.startedAtMs).toISOString(),
            Version: events.length,
            Events: events,
            TypicalSeconds: { ...MOCK_PROGRESS_TYPICAL_SECONDS },
        }
    }

    /** Create a new job from the broadcast endpoint */
    async createJob(): Promise<{ JobId: string }> {
        await delay()
        const now = new Date().toISOString()
        const id = `arn-job-mock-${now.replace(/[^0-9]/g, '').slice(0, 14)}`
        const job: JobFixture = {
            JobId: id,
            Station: '2GB 873 Sydney News/Talk',
            Market: 'Sydney',
            TargetDurationSec: 30,
            CreatedAt: now,
            LastUpdatedAt: now,
            Etag: `mock-etag-${id}-v1`,
            Status: 'REQUESTED',
            Input: {
                BulletinType: 'TRAFFIC_ONLY',
                TimeWindow: {
                    From: now,
                    To: new Date(Date.now() + 3600_000).toISOString(),
                },
                Branding: {
                    StationTag: '2GB 873 Sydney News/Talk',
                    SignOff: 'This traffic report is brought to you by 2GB',
                },
                Voice: {
                    Provider: 'elevenlabs',
                    VoiceId: 'oKDz3nEzNfwKIVvUwPs0',
                    VoiceName: 'ARN Hudson',
                    Model: 'eleven_multilingual_v2',
                    Settings: {
                        Speed: 1.0,
                        Stability: 0.5,
                        SimilarityBoost: 0.75,
                        Style: 0.0,
                        UseSpeakerBoost: false,
                    },
                },
            },
            Logs: [{ At: now, Level: 0, Message: 'Job created via broadcast' }],
            StatusHistory: [
                {
                    Status: 'REQUESTED',
                    At: now,
                    By: 'system',
                    Note: 'Broadcast job created',
                },
            ],
        }
        this._jobs.set(id, job)
        return { JobId: id }
    }

    async approveJob(id: string, expectedEtag?: string): Promise<void> {
        await delay()
        const job = this._jobs.get(id)
        if (!job) return
        if (expectedEtag && job.Etag && expectedEtag !== job.Etag) {
            throw new MockJobConflictError(
                'The bulletin changed after you loaded it (a newer revision exists). Refresh and review the latest version before deciding.'
            )
        }
        const now = new Date().toISOString()
        job.Status = 'APPROVED'
        job.LastUpdatedAt = now
        job.Etag = bumpEtag(job.Etag)
        job.Approval = {
            DecidedAt: now,
            DecidedBy: 'mock.reviewer@arn.com.au',
        }
        job.StatusHistory.push({
            Status: 'APPROVED',
            At: now,
            By: 'mock.reviewer@arn.com.au',
            Note: 'Approved (mock)',
        })
        job.Logs.push({
            At: now,
            Level: 0,
            Message: 'Job approved by mock reviewer',
        })
    }
    async proceedCuration(
        id: string,
        expectedEtag?: string,
        editedIncidents: Array<{
            IncidentId?: string
            Included?: boolean
            Priority?: number
        }> = []
    ): Promise<void> {
        await delay()
        const job = this._jobs.get(id)
        if (!job) return
        if (expectedEtag && job.Etag && expectedEtag !== job.Etag) {
            throw new MockJobConflictError(
                'The bulletin changed after you loaded it (a newer revision exists). Refresh and review the latest traffic.'
            )
        }
        if (job.Traffic && editedIncidents.length > 0) {
            const decisions = new Map(
                editedIncidents
                    .filter((edit) => edit.IncidentId)
                    .map((edit) => [
                        edit.IncidentId as string,
                        {
                            included: edit.Included !== false,
                            priority: edit.Priority ?? 0,
                        },
                    ])
            )
            const incidents = job.Traffic.Incidents.filter(
                (incident) => decisions.get(incident.Key)?.included !== false
            ).map((incident, index) => ({
                ...incident,
                Priority: decisions.get(incident.Key)?.priority ?? index + 1,
            }))
            job.Traffic = { ...job.Traffic, Incidents: incidents }
            job.SelectedIncidents = incidents
        }
        const now = new Date().toISOString()
        job.Status = 'INCIDENTS_SELECTED'
        job.LastUpdatedAt = now
        job.Etag = bumpEtag(job.Etag)
        job.StatusHistory.push({
            Status: 'INCIDENTS_SELECTED',
            At: now,
            By: 'mock.reviewer@arn.com.au',
            Note: 'Curation completed; draft generation started',
        })
        job.Logs.push({
            At: now,
            Level: 0,
            Message: `Curation completed; draft generation started (${job.SelectedIncidents?.length ?? 0} incidents)`,
        })
    }

    async rejectJob(
        id: string,
        reason: string,
        notes: string,
        futureLesson?: { category?: string; text?: string }
    ): Promise<void> {
        await delay()
        const job = this._jobs.get(id)
        if (!job) return
        const now = new Date().toISOString()
        job.Status = 'REJECTED'
        job.LastUpdatedAt = now
        job.Etag = bumpEtag(job.Etag)
        job.Rejection = {
            Reason: reason,
            DecidedAt: now,
            DecidedBy: 'mock.reviewer@arn.com.au',
        }
        const history = job.RejectionHistory ?? []
        history.push({
            Reason: reason,
            Notes: notes,
            RejectedScript: job.Script?.Data.Text ?? null,
            At: now,
            By: 'mock.reviewer@arn.com.au',
        })
        job.RejectionHistory = history
        job.StatusHistory.push({
            Status: 'REJECTED',
            At: now,
            By: 'mock.reviewer@arn.com.au',
            Note: reason,
        })
        job.Logs.push({ At: now, Level: 1, Message: `Job rejected: ${reason}` })
        if (futureLesson?.text) {
            job.Logs.push({
                At: now,
                Level: 0,
                Message: `Future ${futureLesson.category ?? 'editorial'} lesson saved: ${futureLesson.text}`,
            })
        }
    }

    async rejectToFallbackJob(
        id: string,
        reason: string,
        notes: string,
        futureLesson?: { category?: string; text?: string }
    ): Promise<void> {
        await delay()
        const job = this._jobs.get(id)
        if (!job) return
        const now = new Date().toISOString()
        job.Status = 'FALLBACK_USED'
        job.LastUpdatedAt = now
        job.Etag = bumpEtag(job.Etag)
        job.Fallback = {
            SourceJobId: 'arn-job-00009',
            AppliedAt: now,
            Reason: 'operator-rejected',
            Streak: 1,
        }
        const history = job.RejectionHistory ?? []
        history.push({
            Reason: reason,
            Notes: notes,
            RejectedScript: job.Script?.Data.Text ?? null,
            At: now,
            By: 'mock.reviewer@arn.com.au',
        })
        job.RejectionHistory = history
        job.StatusHistory.push({
            Status: 'FALLBACK_USED',
            At: now,
            By: 'system',
            Note: `Fallback applied: ${reason}`,
        })
        job.Logs.push({
            At: now,
            Level: 1,
            Message: `Fallback used: ${reason}`,
        })
        if (futureLesson?.text) {
            job.Logs.push({
                At: now,
                Level: 0,
                Message: `Future ${futureLesson.category ?? 'editorial'} lesson saved: ${futureLesson.text}`,
            })
        }
    }

    /**
     * An operator choosing what airs in a slot: an earlier bulletin from today
     * (`repeat`) or the network standby cart (`cart`).
     *
     * Lands on `FALLBACK_USED` with a fallback record, exactly as the deadline
     * sweep's own coverage does — the difference is only that a human picked the
     * source. The mock also stamps the publish record, because the real endpoint
     * delivers to the drop in the same call rather than leaving the operator to
     * publish a second time.
     */
    async setAirSource(
        id: string,
        kind: 'repeat' | 'cart',
        sourceJobId?: string | null,
        expectedEtag?: string
    ): Promise<void> {
        await delay()
        const job = this._jobs.get(id)
        if (!job) return
        if (expectedEtag && job.Etag && expectedEtag !== job.Etag) {
            throw new MockJobConflictError(
                'The bulletin changed after you loaded it (a newer revision exists). Refresh and review the latest version before deciding.'
            )
        }
        const now = new Date().toISOString()
        const what =
            kind === 'cart' ? 'the standby cart' : 'an earlier bulletin'
        job.Status = 'FALLBACK_USED'
        job.LastUpdatedAt = now
        job.Etag = bumpEtag(job.Etag)
        job.Fallback = {
            // The cart is not a job, so the wire carries the slot's own id here
            // rather than a dangling reference the UI would try to open.
            SourceJobId: (kind === 'cart' ? null : sourceJobId) ?? id,
            AppliedAt: now,
            Reason: 'operator-chose',
            Streak: 1,
        }
        job.Publish = {
            Destination: 'Zetta Playout System',
            PublishedAt: now,
            PublishedBy: 'mock.reviewer@arn.com.au',
        }
        job.StatusHistory.push({
            Status: 'FALLBACK_USED',
            At: now,
            By: 'mock.reviewer@arn.com.au',
            Note: `Operator chose ${what} for this slot`,
        })
        job.Logs.push({
            At: now,
            Level: 1,
            Message: `Operator chose ${what}; written to the playout drop.`,
        })
    }
    async publishJob(id: string, expectedEtag?: string): Promise<void> {
        await delay()
        const job = this._jobs.get(id)
        if (!job) return
        if (job.Status === 'FALLBACK_USED') {
            throw new MockJobConflictError(
                'This fallback bulletin must be approved before it can be published. Approve it first.'
            )
        }
        if (expectedEtag && job.Etag && expectedEtag !== job.Etag) {
            throw new MockJobConflictError(
                'The bulletin changed after you loaded it (a newer revision exists). Refresh and review the latest version before deciding.'
            )
        }
        const now = new Date().toISOString()
        job.Status = 'PUBLISHED'
        job.LastUpdatedAt = now
        job.Etag = bumpEtag(job.Etag)
        job.Publish = {
            Destination: 'Zetta Playout System',
            PublishedAt: now,
            PublishedBy: 'mock.reviewer@arn.com.au',
        }
        // A published job always carries an approval record on the real wire:
        // `JobMappingExtensions.MapApproval` reads the APPROVED status-history
        // entry and falls back to `system`, never null. The mock left it unset
        // when a job was published without going through `approveJob`, so mock
        // mode rendered a Published record card with no "Approved by" line —
        // and the parse-boundary invariant now rejects that payload outright.
        job.Approval ??= {
            DecidedAt:
                job.StatusHistory.findLast((h) => h.Status === 'APPROVED')
                    ?.At ?? now,
            DecidedBy:
                job.StatusHistory.findLast((h) => h.Status === 'APPROVED')
                    ?.By ?? 'system',
        }
        job.StatusHistory.push({
            Status: 'PUBLISHED',
            At: now,
            By: 'system',
            Note: 'Published to playout',
        })
        job.Logs.push({
            At: now,
            Level: 0,
            Message: 'Bulletin published to Zetta Playout System',
        })
    }

    async regenerateJob(
        id: string,
        body: {
            targetDurationSec?: number
            editedScript?: string
            editedIncidents?: {
                incidentId: string
                included: boolean
                priority: number
            }[]
            revoiceOnly?: boolean
            tonedScript?: string
            selectedCorridors?: string[]
        }
    ): Promise<void> {
        await delay()
        const job = this._jobs.get(id)
        if (!job) return
        const now = new Date().toISOString()

        // Apply an operator's direct script edit, bumping the script version.
        // The tagged read must not outlive the words it was built on: a
        // verbatim re-voice stores the operator's tags, or null for a neutral
        // read (the delivery director takes over).
        if (body.editedScript && job.Script) {
            job.Script.Data = {
                ...job.Script.Data,
                Version: job.Script.Data.Version + 1,
                Text: body.editedScript,
                TaggedText: body.tonedScript ?? null,
            }
        }

        // Rebuild the selected incidents from the full pool + included flags,
        // so incidents dragged in from "available" are added and dropped ones
        // are removed, in the operator's chosen airing order.
        if (body.editedIncidents) {
            const pool = new Map(
                [
                    ...(job.Traffic?.Incidents ?? []),
                    ...(job.SelectedIncidents ?? []),
                ].map((inc) => [inc.Key, inc])
            )
            job.SelectedIncidents = body.editedIncidents
                .filter((e) => e.included)
                .sort((a, b) => a.priority - b.priority)
                .map((e) => {
                    const inc = pool.get(e.incidentId)
                    return inc ? { ...inc, Priority: e.priority } : null
                })
                .filter((inc): inc is NonNullable<typeof inc> => inc !== null)
        }

        // Mirrors the backend's `Job.RequestRegeneration` + the async pipeline
        // that follows it: the job loses its audio and goes back to REQUESTED,
        // and the re-voiced bulletin only reappears when the pipeline finishes.
        // Jumping straight to WAITING_APPROVAL hid the entire handoff locally —
        // and that handoff (REJECTED, then REQUESTED, then back) is exactly where
        // the review workspace used to flash the as-aired record card at an
        // operator who had just pressed Re-voice.
        const audio = job.Audio
        job.Status = 'REQUESTED'
        job.Audio = null
        job.MeasuredDurationSec = null
        job.DeltaSec = null
        // The decision record describes a status the job no longer has. The
        // deployed API omits it for every non-REJECTED status; keeping it here
        // would hand the client a shape the real backend never emits.
        job.Rejection = null
        job.LastUpdatedAt = now
        job.Etag = bumpEtag(job.Etag)
        job.StatusHistory.push({
            Status: 'REQUESTED',
            At: now,
            By: 'operator',
            Note: body.revoiceOnly
                ? 'Re-voice requested'
                : 'Regeneration requested',
        })
        job.Logs.push({
            At: now,
            Level: 0,
            Message: body.revoiceOnly
                ? 'Re-voice requested (TTS only)'
                : 'Job regenerated with operator changes',
        })
        setTimeout(() => {
            const current = this._jobs.get(id)
            if (!current || current.Status !== 'REQUESTED') return
            const finishedAt = new Date().toISOString()
            current.Status = 'WAITING_APPROVAL'
            current.Audio = audio
            current.LastUpdatedAt = finishedAt
            current.Etag = bumpEtag(current.Etag)
            current.StatusHistory.push({
                Status: 'WAITING_APPROVAL',
                At: finishedAt,
                By: 'system',
                Note: body.revoiceOnly
                    ? 'Re-voiced from edited script'
                    : 'Regenerated with operator changes',
            })
        }, MOCK_REGENERATE_MS)
    }

    async deleteJob(id: string): Promise<void> {
        await delay()
        this._jobs.delete(id)
        this._progressStates.delete(id)
    }

    // ---------------------------------------------------------------------------
    // Voices
    // ---------------------------------------------------------------------------
    async listVoices(): Promise<{ Voices: VoiceFixture[] }> {
        await delay()
        return { Voices: [...this._voices.values()] }
    }

    async calibrateVoice(): Promise<{
        status: string
        wordsPerMinute: number
        syllablesPerSecond: number
    }> {
        await delay()
        return {
            status: 'success',
            wordsPerMinute: 150,
            syllablesPerSecond: 5.8,
        }
    }

    // ---------------------------------------------------------------------------
    // Voice profiles (shared voice library)
    // ---------------------------------------------------------------------------
    async listVoiceProfiles(): Promise<{
        Items: VoiceProfileFixture[]
        Total: number
    }> {
        await delay()
        const items = [...this._voiceProfiles.values()]
        return { Items: items, Total: items.length }
    }

    async getVoiceProfile(id: string): Promise<VoiceProfileFixture | null> {
        await delay()
        return this._voiceProfiles.get(id) ?? null
    }

    async deleteVoiceProfile(id: string): Promise<boolean> {
        await delay()
        return this._voiceProfiles.delete(id)
    }

    async archiveVoiceProfile(id: string): Promise<VoiceProfileFixture | null> {
        await delay()
        const profile = this._voiceProfiles.get(id)
        if (!profile) return null
        profile.Status = 'archived'
        profile.UpdatedAt = new Date().toISOString()
        return profile
    }

    async unarchiveVoiceProfile(
        id: string
    ): Promise<VoiceProfileFixture | null> {
        await delay()
        const profile = this._voiceProfiles.get(id)
        if (!profile) return null
        profile.Status = 'active'
        profile.UpdatedAt = new Date().toISOString()
        return profile
    }

    async createVoiceProfile(body: {
        Name: string
        Description?: string | null
        BaseVoiceId: string
        BaseVoiceName: string
        Model: string
        Settings: ProfileSettingsFixture
        ChangeNote?: string | null
        PaceBoostWpm?: number
    }): Promise<VoiceProfileFixture> {
        await delay()
        const now = new Date().toISOString()
        const slug =
            body.Name.toLowerCase()
                .replace(/[^a-z0-9]+/g, '-')
                .replace(/^-+|-+$/g, '')
                .slice(0, 24) || 'variant'
        const id = `vp-${slug}-${Math.random().toString(36).slice(2, 7)}`
        const profile: VoiceProfileFixture = {
            Id: id,
            Name: body.Name,
            Description: body.Description ?? null,
            BaseVoiceId: body.BaseVoiceId,
            BaseVoiceName: body.BaseVoiceName,
            Provider: 'elevenlabs',
            Status: 'active',
            CurrentVersion: 1,
            CreatedAt: now,
            UpdatedAt: now,
            CreatedBy: 'admin@arn.com.au',
            AssignedStations: [],
            Versions: [
                this.makeVersion(
                    1,
                    body.BaseVoiceId,
                    body.Model,
                    body.Settings,
                    body.ChangeNote ?? 'Initial variant.',
                    now,
                    body.PaceBoostWpm ?? 0
                ),
            ],
        }
        this._voiceProfiles.set(id, profile)
        return profile
    }

    async updateVoiceProfile(
        id: string,
        body: {
            Name: string
            Description?: string | null
            Model: string
            Settings: ProfileSettingsFixture
            ChangeNote?: string | null
            PaceBoostWpm?: number
        }
    ): Promise<VoiceProfileFixture | null> {
        await delay()
        const profile = this._voiceProfiles.get(id)
        if (!profile) return null
        const now = new Date().toISOString()
        profile.Name = body.Name
        profile.Description = body.Description ?? null

        const latest = profile.Versions[profile.Versions.length - 1]
        const s = body.Settings
        const p = latest.Settings
        const nextPaceBoostWpm = body.PaceBoostWpm ?? 0
        const settingsChanged =
            s.Speed !== p.Speed ||
            s.Stability !== p.Stability ||
            s.SimilarityBoost !== p.SimilarityBoost ||
            s.Style !== p.Style ||
            s.UseSpeakerBoost !== p.UseSpeakerBoost ||
            body.Model !== latest.Model ||
            nextPaceBoostWpm !== (latest.PaceBoostWpm ?? 0)
        if (settingsChanged) {
            const nextVersion = profile.CurrentVersion + 1
            profile.Versions.push(
                this.makeVersion(
                    nextVersion,
                    profile.BaseVoiceId,
                    body.Model,
                    body.Settings,
                    body.ChangeNote ?? null,
                    now,
                    nextPaceBoostWpm
                )
            )
            profile.CurrentVersion = nextVersion
        }
        profile.UpdatedAt = now
        return profile
    }

    async restoreVoiceProfileVersion(
        id: string,
        version: number
    ): Promise<VoiceProfileFixture | null> {
        await delay()
        const profile = this._voiceProfiles.get(id)
        if (!profile) return null
        const source = profile.Versions.find((v) => v.Version === version)
        if (!source) return null
        const now = new Date().toISOString()
        const nextVersion = profile.CurrentVersion + 1
        profile.Versions.push(
            this.makeVersion(
                nextVersion,
                profile.BaseVoiceId,
                source.Model,
                source.Settings,
                `Restored from version ${version}.`,
                now,
                source.PaceBoostWpm ?? 0
            )
        )
        profile.CurrentVersion = nextVersion
        profile.UpdatedAt = now
        return profile
    }

    async previewVoice(body: {
        BaseVoiceId: string
        Model: string
        Settings: ProfileSettingsFixture
        PaceBoostWpm?: number
    }): Promise<{
        Url: string
        EstimatedDurationSec: number
        WordsPerMinute: number
        EffectivePaceBoostWpm: number
    }> {
        // Slower than other endpoints: a preview "synthesizes" audio.
        await delay(450)
        const naturalWpm = Math.round(
            BASE_WORDS_PER_MINUTE * body.Settings.Speed
        )
        const paceBoostWpm = body.PaceBoostWpm ?? 0
        // Mirror the real endpoint's clamp: widen toward the boost, capped at 1.35x.
        const effectiveFactor =
            paceBoostWpm > 0
                ? Math.min((naturalWpm + paceBoostWpm) / naturalWpm, 1.35)
                : 1
        const wpm = Math.round(naturalWpm * effectiveFactor)
        return {
            Url: PROFILE_SAMPLE_URI,
            EstimatedDurationSec: Math.round((NOMINAL_SAMPLE_WORDS / wpm) * 60),
            WordsPerMinute: wpm,
            EffectivePaceBoostWpm: Math.round(
                (effectiveFactor - 1) * naturalWpm
            ),
        }
    }

    /**
     * A voiceover render. Mock mode has no synthesiser, so this returns the standing sample
     * clip under a realistic file name — enough to exercise playback and the download anchor.
     */
    async renderVoiceover(
        profileId: string,
        body: { Script?: string }
    ): Promise<{
        Url: string
        FileName: string
        DurationSec: number
        WordCount: number
    } | null> {
        const profile = this._voiceProfiles.get(profileId)
        if (!profile) return null
        // Synthesis is the slow step everywhere else in this store; keep the wait honest so
        // the UI's pending state is exercised.
        await delay(700)
        const words = (body.Script ?? '').trim().split(/\s+/).filter(Boolean)
        return {
            Url: PROFILE_SAMPLE_URI,
            FileName: `voiceover_${profileId.replace(/[^a-z0-9]/gi, '')}_20260812_101500_000.wav`,
            DurationSec: Math.max(1, Math.round((words.length / 150) * 60)),
            WordCount: words.length,
        }
    }

    /**
     * A fresh version: new settings, but no calibration or run samples yet —
     * those populate from real runs, so a just-saved version has empty memory.
     */
    private makeVersion(
        version: number,
        voiceId: string,
        model: string,
        settings: ProfileSettingsFixture,
        changeNote: string | null,
        createdAt: string,
        paceBoostWpm = 0
    ): ProfileVersionFixture {
        const n = (x: number) => x.toFixed(2)
        const configurationKey = [
            voiceId,
            n(settings.Speed),
            n(settings.Stability),
            n(settings.SimilarityBoost),
            n(settings.Style),
            settings.UseSpeakerBoost ? 'true' : 'false',
        ].join('_')
        return {
            Version: version,
            Settings: settings,
            Model: model,
            ChangeNote: changeNote,
            CreatedAt: createdAt,
            CreatedBy: 'admin@arn.com.au',
            SampleUrl: PROFILE_SAMPLE_URI,
            ConfigurationKey: configurationKey,
            Calibration: null,
            RunSamples: [],
            PaceBoostWpm: paceBoostWpm,
        }
    }

    // ---------------------------------------------------------------------------
    // Schedules
    // ---------------------------------------------------------------------------
    async listSchedules(): Promise<{
        Items: ScheduleFixture[]
        Total: number
    }> {
        await delay()
        const items = [...this._schedules.values()]
        return { Items: items, Total: items.length }
    }

    async getSchedule(id: string): Promise<ScheduleFixture | null> {
        await delay()
        return this._schedules.get(id) ?? null
    }

    async upsertSchedule(
        id: string,
        data: Partial<ScheduleFixture>
    ): Promise<ScheduleFixture> {
        await delay()
        const now = new Date().toISOString()
        const existing = this._schedules.get(id)
        if (existing) {
            Object.assign(existing, data, { UpdatedAt: now })
            return existing
        }
        const created: ScheduleFixture = {
            MarketId: id,
            Name: data.Name ?? null,
            Station: data.Station ?? null,
            Timezone: data.Timezone ?? null,
            CronExpression: data.CronExpression ?? null,
            Enabled: data.Enabled ?? true,
            TargetDurationSeconds: data.TargetDurationSeconds ?? 30,
            NotifyBeforePlayoutMinutes: data.NotifyBeforePlayoutMinutes ?? 10,
            EscalationBeforePlayoutMinutes:
                data.EscalationBeforePlayoutMinutes ?? 5,
            VoiceProfileId: data.VoiceProfileId ?? null,
            DeliveryStyleId: data.DeliveryStyleId ?? null,
            PronunciationDictionaryId: data.PronunciationDictionaryId ?? null,
            Content: data.Content ?? {
                Traffic: true,
                Weather: false,
                Fuel: false,
            },
            NextRunAt: data.NextRunAt ?? null,
            CurrentJobId: data.CurrentJobId ?? null,
            CreatedAt: now,
            UpdatedAt: now,
            Coverage:
                data.Coverage === undefined
                    ? {
                          Latitude: -27.4698,
                          Longitude: 153.0251,
                          RadiusKm: 40,
                          ShapeType: 'circle' as const,
                      }
                    : data.Coverage,
        }
        this._schedules.set(id, created)
        return created
    }

    // ---------------------------------------------------------------------------
    // Event ledger and spot checks
    // ---------------------------------------------------------------------------
    async getLedger(market: string) {
        await delay()
        const latest = [...this._jobs.values()]
            .filter((job) => job.Market === market)
            .sort((a, b) => b.LastUpdatedAt.localeCompare(a.LastUpdatedAt))[0]
        const records =
            latest?.SelectedIncidents ?? latest?.Traffic?.Incidents ?? []
        return {
            Market: market,
            Events: records.map((record) => {
                const verdict = this._eventVerdicts.get(
                    `${market}|${record.Key}`
                )
                return {
                    Key: record.Key,
                    RoadName: record.Headline,
                    Headline: record.Headline,
                    Detail: record.Detail ?? null,
                    Sources: record.Sources ?? [],
                    FirstSeenUtc:
                        record.EntryTimeUtc ??
                        latest?.CreatedAt ??
                        new Date().toISOString(),
                    LastSeenUtc:
                        latest?.LastUpdatedAt ?? new Date().toISOString(),
                    StartTimeUtc: record.StartTimeUtc ?? null,
                    EndTimeUtc: record.EndTimeUtc ?? null,
                    RoadClosed: record.RoadClosed ?? false,
                    Lane: record.IsNew ? 'New' : 'Active',
                    SightingCount: 1,
                    Observations: [],
                    Airings: [],
                    Verdict: verdict?.Verdict ?? null,
                    VerdictNotes: verdict?.Notes ?? null,
                    VerdictBy: verdict ? 'mock.user' : null,
                    VerdictAtUtc: verdict ? new Date().toISOString() : null,
                    Record: record,
                    OfficialEvidences: null,
                }
            }),
            FeedHealth: [],
        }
    }

    async setEventVerdict(
        market: string,
        eventKey: string,
        verdict: string | null,
        notes: string | null
    ): Promise<void> {
        await delay()
        this._eventVerdicts.set(`${market}|${eventKey}`, {
            Verdict: verdict,
            Notes: notes,
        })
    }

    async getJobDelta(jobId: string) {
        await delay()
        const job = this._jobs.get(jobId)
        const previous = job
            ? [...this._jobs.values()]
                  .filter(
                      (candidate) =>
                          candidate.Market === job.Market &&
                          candidate.JobId !== job.JobId &&
                          candidate.LastUpdatedAt < job.LastUpdatedAt
                  )
                  .sort((a, b) =>
                      b.LastUpdatedAt.localeCompare(a.LastUpdatedAt)
                  )[0]
            : undefined
        return {
            JobId: jobId,
            PreviousJobId: previous?.JobId ?? null,
            PreviousJobAtUtc: previous?.LastUpdatedAt ?? null,
            Deltas: [],
        }
    }
    async deleteSchedule(id: string): Promise<boolean> {
        await delay()
        return this._schedules.delete(id)
    }

    async listSpotChecks() {
        await delay()
        return [...this._spotChecks]
    }

    async markSpotCheck(jobId: string, by = 'mock.user'): Promise<void> {
        await delay()
        this._spotChecks = this._spotChecks.filter(
            (review) => review.JobId !== jobId
        )
        this._spotChecks.push({
            JobId: jobId,
            By: by,
            AtUtc: new Date().toISOString(),
        })
    }

    // ---------------------------------------------------------------------------
    // Priority Roads
    // ---------------------------------------------------------------------------
    async getPriorityRoads(market: string): Promise<PriorityRoadsResult> {
        await delay()
        return {
            Market: market,
            Roads: this._priorityRoads.get(market) ?? [],
        }
    }

    async setPriorityRoads(
        market: string,
        roads: readonly PriorityRoadInput[]
    ): Promise<void> {
        await delay()
        this._priorityRoads.set(
            market,
            roads.map((r) => ({
                Name: r.name,
                BoostIncidents: r.boostIncidents ?? true,
                FlowTier: r.flowTier ?? 'high',
                Enabled: r.enabled ?? true,
            }))
        )
    }
    // ---------------------------------------------------------------------------
    // Incident Priorities
    // ---------------------------------------------------------------------------
    async getIncidentPriorities(
        market: string
    ): Promise<IncidentPrioritiesResult> {
        await delay()
        return {
            Market: market,
            Rules: this._incidentPriorities.get(market) ?? [],
        }
    }

    async setIncidentPriorities(
        market: string,
        rules: readonly IncidentPriorityRuleInput[]
    ): Promise<void> {
        await delay()
        this._incidentPriorities.set(
            market,
            rules.map((r) => ({
                IncidentClass: r.incidentClass,
                DisplayName: r.displayName ?? r.incidentClass,
                ScoreBonus: r.scoreBonus ?? 0,
                Enabled: r.enabled ?? true,
            }))
        )
    }

    async getIncidentLandscape(
        market: string
    ): Promise<IncidentLandscapeResult> {
        await delay()
        return {
            Market: market,
            Entries: [],
            TotalCount: 0,
        }
    }
    // ---------------------------------------------------------------------------
    // Filler Statements
    // ---------------------------------------------------------------------------
    async getFillerStatements(market: string): Promise<FillerStatementsResult> {
        await delay()
        return {
            Market: market,
            Statements: this._fillerStatements.get(market) ?? [],
        }
    }
    async setFillerStatements(
        market: string,
        statements: readonly FillerStatementInput[]
    ): Promise<void> {
        await delay()
        this._fillerStatements.set(
            market,
            statements.map((s) => ({
                Id: s.id,
                Text: s.text,
                Enabled: s.enabled ?? true,
                Availability: s.availability
                    ? {
                          StartDate: s.availability.startDate ?? null,
                          EndDate: s.availability.endDate ?? null,
                          Days: s.availability.days
                              ? [...s.availability.days]
                              : null,
                          StartLocal: s.availability.startLocal ?? null,
                          EndLocal: s.availability.endLocal ?? null,
                      }
                    : null,
            }))
        )
    }

    // ---------------------------------------------------------------------------
    // Verified Pronunciations
    // ---------------------------------------------------------------------------
    async getVerifiedPronunciations(
        market: string
    ): Promise<VerifiedPronunciationsResult> {
        await delay()
        return {
            Market: market,
            Words: this._verifiedPronunciations.get(market) ?? [],
        }
    }

    async setVerifiedPronunciations(
        market: string,
        words: readonly VerifiedPronunciationInput[]
    ): Promise<void> {
        await delay()
        // Mirrors the real trigger: existing entries keep their original
        // verifier/timestamp, so a toggle never reattributes another
        // operator's verdict.
        const prior = new Map(
            (this._verifiedPronunciations.get(market) ?? []).map(
                (w) =>
                    [
                        w.Word.replace(/[^\p{L}\p{N}]+/gu, '').toLowerCase(),
                        w,
                    ] as const
            )
        )
        this._verifiedPronunciations.set(
            market,
            words.map((w) => {
                const key = w.word.replace(/[^\p{L}\p{N}]+/gu, '').toLowerCase()
                const existing = prior.get(key)
                return {
                    Word: w.word,
                    Note: w.note ?? null,
                    Enabled: w.enabled ?? true,
                    VerifiedBy: existing?.VerifiedBy ?? 'Dev Operator',
                    VerifiedAt:
                        existing?.VerifiedAt ?? new Date().toISOString(),
                }
            })
        )
    }

    // ---------------------------------------------------------------------------
    // Operators
    // ---------------------------------------------------------------------------
    async getOperatorScope(): Promise<{
        Scoped: boolean
        Markets: string[] | null
    }> {
        await delay()
        return { Scoped: false, Markets: null }
    }

    async listOperators(): Promise<OperatorDirectoryRecord[]> {
        await delay()
        return [...this._operatorAssignments]
    }

    async upsertOperator(
        objectId: string,
        data: { Markets: string[]; DisplayName?: string }
    ): Promise<OperatorDirectoryRecord> {
        await delay()
        const now = new Date().toISOString()
        const existing = this._operatorAssignments.find(
            (operator) => operator.ObjectId === objectId
        )
        if (existing) {
            existing.Markets = data.Markets
            if (data.DisplayName !== undefined)
                existing.DisplayName = data.DisplayName
            existing.UpdatedAtUtc = now
            existing.UpdatedBy = 'mock.admin@arn.com.au'
            return existing
        }
        const created: OperatorDirectoryRecord = {
            ObjectId: objectId,
            DisplayName: data.DisplayName ?? `Operator ${objectId}`,
            UserPrincipalName: `${objectId}@mock.arn.com.au`,
            Roles: ['Operator'],
            Source: 'Direct',
            Markets: data.Markets,
            IsAdmin: false,
            UpdatedAtUtc: now,
            UpdatedBy: 'mock.admin@arn.com.au',
        }
        this._operatorAssignments.push(created)
        return created
    }

    async deleteOperator(objectId: string): Promise<void> {
        await delay()
        const idx = this._operatorAssignments.findIndex(
            (operator) => operator.ObjectId === objectId
        )
        if (idx >= 0) this._operatorAssignments.splice(idx, 1)
    }
    // ---- Guidance ----

    async listMarketGuidance(market: string): Promise<GuidanceFixture[]> {
        await delay()
        return this._guidance.filter(
            (g) => g.Scope === 'Market' && g.ScopeId === market
        )
    }

    async listGlobalGuidance(): Promise<GuidanceFixture[]> {
        await delay()
        return this._guidance.filter((g) => g.Scope === 'Global')
    }

    async upsertGuidance(
        scope: string,
        scopeId: string,
        body: { id?: string; type: string; text: string; enabled: boolean }
    ): Promise<GuidanceFixture> {
        await delay()
        const now = new Date().toISOString()
        const existing = body.id
            ? this._guidance.find((g) => g.Id === body.id)
            : undefined
        if (existing) {
            existing.Type = body.type
            existing.Text = body.text
            existing.Enabled = body.enabled
            existing.UpdatedAtUtc = now
            return existing
        }
        const created: GuidanceFixture = {
            Id: `g-${Math.random().toString(36).slice(2, 10)}`,
            Scope: scope,
            ScopeId: scope === 'Global' ? '' : scopeId,
            Type: body.type,
            Text: body.text,
            Enabled: body.enabled,
            CreatedBy: 'mock.user',
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            Source: 'manual',
            ReviewAfterUtc: null,
        }
        this._guidance.push(created)
        return created
    }

    async deleteGuidance(id: string): Promise<void> {
        await delay()
        this._guidance = this._guidance.filter((g) => g.Id !== id)
    }

    async composedGuidance(
        market: string,
        station?: string
    ): Promise<{ inEffect: GuidanceFixture[]; droppedCount: number }> {
        await delay()
        const RANK: Record<string, number> = {
            Station: 0,
            Market: 1,
            Global: 2,
        }
        const matched = this._guidance
            .filter((g) => g.Enabled && g.Text.trim().length > 0)
            .filter(
                (g) =>
                    g.Scope === 'Global' ||
                    (g.Scope === 'Market' && g.ScopeId === market) ||
                    (g.Scope === 'Station' &&
                        station != null &&
                        g.ScopeId === station)
            )
            .sort(
                (a, b) =>
                    (RANK[a.Scope] ?? 2) - (RANK[b.Scope] ?? 2) ||
                    b.UpdatedAtUtc.localeCompare(a.UpdatedAtUtc)
            )
        const cap = 8
        return {
            inEffect: matched.slice(0, cap),
            droppedCount: Math.max(0, matched.length - cap),
        }
    }

    async listCandidates(market: string) {
        await delay()
        const station =
            [...this._schedules.values()].find((s) => s.MarketId === market)
                ?.Station ?? market
        return this._candidates
            .filter(
                (candidate) =>
                    candidate.Market === market &&
                    candidate.Category?.toLowerCase() !== 'revision'
            )
            .map((candidate) => {
                const category = candidate.Category?.toLowerCase() ?? ''
                return {
                    Id: `lesson-${candidate.JobId}`,
                    Station: station,
                    JobId: candidate.JobId,
                    Category:
                        category === 'ordering'
                            ? 'PriorityOrdering'
                            : category === 'wording' || category === 'naming'
                              ? 'WordingStyle'
                              : category === 'pace'
                                ? 'StructureRepetition'
                                : 'ContentSelection',
                    Text: candidate.Feedback,
                    CreatedAtUtc: candidate.CreatedAtUtc,
                    LastSeenAtUtc: candidate.CreatedAtUtc,
                    OccurrenceCount: 1,
                    ExpiresAtUtc: new Date(
                        new Date(candidate.CreatedAtUtc).getTime() +
                            90 * 24 * 60 * 60 * 1000
                    ).toISOString(),
                    Promoted: false,
                    Status: 'Active',
                    Origin: 'Operator',
                    ConflictWithLessonId: null,
                }
            })
    }

    async dismissCandidate(id: string): Promise<void> {
        await delay()
        this._candidates = this._candidates.filter(
            (candidate) => `lesson-${candidate.JobId}` !== id
        )
    }

    async promoteGuidance(body: {
        scope: string
        scopeId: string
        type: string
        text: string
    }): Promise<GuidanceFixture> {
        return this.upsertGuidance(body.scope, body.scopeId, {
            type: body.type,
            text: body.text,
            enabled: true,
        })
    }

    // ---- Safety terms ----

    async listSafetyTerms(): Promise<SafetyTermFixture[]> {
        await delay()
        return this._safetyTerms
    }

    async upsertSafetyTerm(body: {
        id?: string
        kind: string
        scope: string
        scopeId?: string
        term: string
        note?: string
    }): Promise<SafetyTermFixture> {
        await delay()
        const now = new Date().toISOString()
        const existing = body.id
            ? this._safetyTerms.find((s) => s.Id === body.id)
            : undefined
        if (existing) {
            existing.Kind = body.kind
            existing.Term = body.term
            existing.Note = body.note ?? null
            existing.UpdatedAtUtc = now
            return existing
        }
        const created: SafetyTermFixture = {
            Id: `st-${Math.random().toString(36).slice(2, 10)}`,
            Kind: body.kind,
            Scope: body.scope,
            ScopeId: body.scope === 'Global' ? '' : (body.scopeId ?? ''),
            Term: body.term,
            Note: body.note ?? null,
            CreatedBy: 'admin@arn.com.au',
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
        }
        this._safetyTerms.push(created)
        return created
    }

    // ---------------------------------------------------------------------------
    // Bulletin playbooks (station overlays over the shipped writer prompt)
    // ---------------------------------------------------------------------------
    async listPlaybooks(): Promise<{
        Items: PlaybookFixture[]
        Total: number
    }> {
        await delay()
        const items = [...this._playbooks.values()]
        return { Items: items, Total: items.length }
    }

    async getPlaybook(id: string): Promise<PlaybookFixture | null> {
        await delay()
        return this._playbooks.get(id) ?? null
    }

    async createPlaybook(body: {
        Id?: string
        Name: string
        Description?: string | null
        Role: string
        Overlay: PlaybookFixture['CurrentOverlay']
        ChangeNote?: string | null
    }): Promise<PlaybookFixture> {
        await delay()
        const now = new Date().toISOString()
        const slug =
            body.Name.toLowerCase()
                .replace(/[^a-z0-9]+/g, '-')
                .replace(/^-+|-+$/g, '')
                .slice(0, 24) || 'playbook'
        const id =
            body.Id || `pb-${slug}-${Math.random().toString(36).slice(2, 7)}`
        const playbook: PlaybookFixture = {
            Id: id,
            Name: body.Name,
            Description: body.Description ?? null,
            Role: body.Role,
            Status: 'active',
            CurrentVersion: 1,
            CurrentOverlay: structuredClone(body.Overlay),
            CreatedAt: now,
            UpdatedAt: now,
            CreatedBy: 'mock.reviewer@arn.com.au',
            AssignedStations: [],
            Versions: [
                {
                    Version: 1,
                    Overlay: structuredClone(body.Overlay),
                    ChangeNote: body.ChangeNote ?? null,
                    CreatedAt: now,
                    CreatedBy: 'mock.reviewer@arn.com.au',
                },
            ],
        }
        this._playbooks.set(id, playbook)
        return playbook
    }

    async updatePlaybook(
        id: string,
        body: {
            Name: string
            Description?: string | null
            Overlay: PlaybookFixture['CurrentOverlay']
            ChangeNote?: string | null
        }
    ): Promise<PlaybookFixture | null> {
        await delay()
        const playbook = this._playbooks.get(id)
        if (!playbook) return null
        const now = new Date().toISOString()
        const nextVersion = playbook.CurrentVersion + 1
        const overlay = structuredClone(body.Overlay)
        playbook.Versions.push({
            Version: nextVersion,
            Overlay: overlay,
            ChangeNote: body.ChangeNote ?? null,
            CreatedAt: now,
            CreatedBy: 'mock.reviewer@arn.com.au',
        })
        playbook.CurrentVersion = nextVersion
        playbook.CurrentOverlay = overlay
        playbook.Name = body.Name
        playbook.Description = body.Description ?? null
        playbook.UpdatedAt = now
        return playbook
    }

    async restorePlaybookVersion(
        id: string,
        version: number
    ): Promise<PlaybookFixture | null> {
        await delay()
        const playbook = this._playbooks.get(id)
        if (!playbook) return null
        const source = playbook.Versions.find(
            (entry) => entry.Version === version
        )
        if (!source) return null
        const now = new Date().toISOString()
        const nextVersion = playbook.CurrentVersion + 1
        const overlay = structuredClone(source.Overlay)
        playbook.Versions.push({
            Version: nextVersion,
            Overlay: overlay,
            ChangeNote: `Restored from version ${version}.`,
            CreatedAt: now,
            CreatedBy: 'mock.reviewer@arn.com.au',
        })
        playbook.CurrentVersion = nextVersion
        playbook.CurrentOverlay = overlay
        playbook.UpdatedAt = now
        return playbook
    }

    async archivePlaybook(id: string): Promise<PlaybookFixture | null> {
        await delay()
        const playbook = this._playbooks.get(id)
        if (!playbook) return null
        playbook.Status = 'archived'
        playbook.UpdatedAt = new Date().toISOString()
        return playbook
    }

    async unarchivePlaybook(id: string): Promise<PlaybookFixture | null> {
        await delay()
        const playbook = this._playbooks.get(id)
        if (!playbook) return null
        playbook.Status = 'active'
        playbook.UpdatedAt = new Date().toISOString()
        return playbook
    }

    async deletePlaybook(id: string): Promise<boolean> {
        await delay()
        return this._playbooks.delete(id)
    }

    async deleteSafetyTerm(id: string): Promise<void> {
        await delay()
        this._safetyTerms = this._safetyTerms.filter((s) => s.Id !== id)
    }

    // ---------------------------------------------------------------------------
    // Delivery styles (shared delivery library)
    // ---------------------------------------------------------------------------
    async listDeliveryStyles(): Promise<{
        Items: DeliveryStyleFixture[]
        Total: number
    }> {
        await delay()
        const items = [...this._deliveryStyles.values()]
        return { Items: items, Total: items.length }
    }

    async getDeliveryStyle(id: string): Promise<DeliveryStyleFixture | null> {
        await delay()
        return this._deliveryStyles.get(id) ?? null
    }

    async deleteDeliveryStyle(id: string): Promise<boolean> {
        await delay()
        return this._deliveryStyles.delete(id)
    }

    async createDeliveryStyle(body: {
        Name: string
        Description?: string | null
        Policy: {
            ToneDescription: string
            AllowedTags: string[]
            Intensity: number
        }
        ChangeNote?: string | null
    }): Promise<DeliveryStyleFixture> {
        await delay()
        const now = new Date().toISOString()
        const slug =
            body.Name.toLowerCase()
                .replace(/[^a-z0-9]+/g, '-')
                .replace(/^-+|-+$/g, '')
                .slice(0, 24) || 'style'
        const id = `ds-${slug}-${Math.random().toString(36).slice(2, 7)}`
        const style: DeliveryStyleFixture = {
            Id: id,
            Name: body.Name,
            Description: body.Description ?? null,
            Status: 'active',
            CurrentVersion: 1,
            CreatedAt: now,
            UpdatedAt: now,
            CreatedBy: 'mock.admin@arn.com.au',
            AssignedStations: [],
            Versions: [
                {
                    Version: 1,
                    Policy: body.Policy,
                    ChangeNote: body.ChangeNote ?? null,
                    CreatedAt: now,
                    CreatedBy: 'mock.admin@arn.com.au',
                },
            ],
        }
        this._deliveryStyles.set(id, style)
        return style
    }

    async updateDeliveryStyle(
        id: string,
        body: {
            Name: string
            Description?: string | null
            Policy: {
                ToneDescription: string
                AllowedTags: string[]
                Intensity: number
            }
            ChangeNote?: string | null
        }
    ): Promise<DeliveryStyleFixture | null> {
        await delay()
        const style = this._deliveryStyles.get(id)
        if (!style) return null
        const now = new Date().toISOString()
        const latest = style.Versions[style.Versions.length - 1]
        const p = body.Policy
        const policyChanged =
            p.ToneDescription !== latest.Policy.ToneDescription ||
            JSON.stringify(p.AllowedTags) !==
                JSON.stringify(latest.Policy.AllowedTags) ||
            p.Intensity !== latest.Policy.Intensity
        if (policyChanged) {
            const nextVersion = style.CurrentVersion + 1
            style.Versions.push({
                Version: nextVersion,
                Policy: body.Policy,
                ChangeNote: body.ChangeNote ?? null,
                CreatedAt: now,
                CreatedBy: 'mock.admin@arn.com.au',
            })
            style.CurrentVersion = nextVersion
        }
        style.Name = body.Name
        style.Description = body.Description ?? null
        style.UpdatedAt = now
        return style
    }

    async restoreDeliveryStyleVersion(
        id: string,
        version: number
    ): Promise<DeliveryStyleFixture | null> {
        await delay()
        const style = this._deliveryStyles.get(id)
        if (!style) return null
        const source = style.Versions.find((v) => v.Version === version)
        if (!source) return null
        const now = new Date().toISOString()
        const nextVersion = style.CurrentVersion + 1
        style.Versions.push({
            Version: nextVersion,
            Policy: { ...source.Policy },
            ChangeNote: `Restored from version ${version}.`,
            CreatedAt: now,
            CreatedBy: 'mock.admin@arn.com.au',
        })
        style.CurrentVersion = nextVersion
        style.UpdatedAt = now
        return style
    }

    async archiveDeliveryStyle(
        id: string
    ): Promise<DeliveryStyleFixture | null> {
        await delay()
        const style = this._deliveryStyles.get(id)
        if (!style) return null
        style.Status = 'archived'
        style.UpdatedAt = new Date().toISOString()
        return style
    }

    async unarchiveDeliveryStyle(
        id: string
    ): Promise<DeliveryStyleFixture | null> {
        await delay()
        const style = this._deliveryStyles.get(id)
        if (!style) return null
        style.Status = 'active'
        style.UpdatedAt = new Date().toISOString()
        return style
    }

    async previewDeliveryStyle(body: {
        Policy: {
            ToneDescription: string
            AllowedTags: string[]
            Intensity: number
        }
    }): Promise<{
        Url: string
        EstimatedDurationSec: number
        TagCount: number
        TagsSupported: boolean
    }> {
        await delay(350)
        return {
            Url: '/mock/audio-bulletin.wav',
            EstimatedDurationSec: 12,
            TagCount: body.Policy.AllowedTags.length || 0,
            TagsSupported: true,
        }
    }

    // ---------------------------------------------------------------------------
    // Bookends (shared intro+outro pair library)
    // ---------------------------------------------------------------------------
    async listBookends(): Promise<{
        Items: BookendFixture[]
        Total: number
    }> {
        await delay()
        const items = [...this._bookends.values()]
        return { Items: items, Total: items.length }
    }

    async getBookend(id: string): Promise<BookendFixture | null> {
        await delay()
        return this._bookends.get(id) ?? null
    }

    async createBookend(body: {
        Name: string
        Description?: string | null
        IntroText: string
        OutroText: string
        ChangeNote?: string | null
    }): Promise<BookendFixture> {
        await delay()
        const now = new Date().toISOString()
        const slug =
            body.Name.toLowerCase()
                .replace(/[^a-z0-9]+/g, '-')
                .replace(/^-+|-+$/g, '')
                .slice(0, 24) || 'bookend'
        const id = `bk-${slug}-${Math.random().toString(36).slice(2, 7)}`
        const bookend: BookendFixture = {
            Id: id,
            Name: body.Name,
            Description: body.Description ?? null,
            Status: 'active',
            IntroText: body.IntroText,
            OutroText: body.OutroText,
            CurrentVersion: 1,
            CreatedAt: now,
            UpdatedAt: now,
            CreatedBy: 'mock.admin@arn.com.au',
            AssignedStations: [],
            Tokens: extractBookendTokens(`${body.IntroText} ${body.OutroText}`),
            Versions: [
                {
                    Version: 1,
                    IntroText: body.IntroText,
                    OutroText: body.OutroText,
                    ChangeNote: body.ChangeNote ?? null,
                    CreatedAt: now,
                    CreatedBy: 'mock.admin@arn.com.au',
                },
            ],
        }
        this._bookends.set(id, bookend)
        return bookend
    }

    async updateBookend(
        id: string,
        body: {
            Name: string
            Description?: string | null
            IntroText: string
            OutroText: string
            ChangeNote?: string | null
        }
    ): Promise<BookendFixture | null> {
        await delay()
        const bookend = this._bookends.get(id)
        if (!bookend) return null
        const now = new Date().toISOString()
        const latest = bookend.Versions[bookend.Versions.length - 1]
        if (
            body.IntroText !== latest.IntroText ||
            body.OutroText !== latest.OutroText
        ) {
            const nextVersion = bookend.CurrentVersion + 1
            bookend.Versions.push({
                Version: nextVersion,
                IntroText: body.IntroText,
                OutroText: body.OutroText,
                ChangeNote: body.ChangeNote ?? null,
                CreatedAt: now,
                CreatedBy: 'mock.admin@arn.com.au',
            })
            bookend.CurrentVersion = nextVersion
        }
        bookend.Name = body.Name
        bookend.Description = body.Description ?? null
        bookend.IntroText = body.IntroText
        bookend.OutroText = body.OutroText
        bookend.Tokens = extractBookendTokens(
            `${body.IntroText} ${body.OutroText}`
        )
        bookend.UpdatedAt = now
        return bookend
    }

    async restoreBookendVersion(
        id: string,
        version: number
    ): Promise<BookendFixture | null> {
        await delay()
        const bookend = this._bookends.get(id)
        if (!bookend) return null
        const source = bookend.Versions.find((v) => v.Version === version)
        if (!source) return null
        const now = new Date().toISOString()
        const nextVersion = bookend.CurrentVersion + 1
        bookend.Versions.push({
            Version: nextVersion,
            IntroText: source.IntroText,
            OutroText: source.OutroText,
            ChangeNote: `Restored from version ${version}.`,
            CreatedAt: now,
            CreatedBy: 'mock.admin@arn.com.au',
        })
        bookend.CurrentVersion = nextVersion
        bookend.IntroText = source.IntroText
        bookend.OutroText = source.OutroText
        bookend.Tokens = extractBookendTokens(
            `${source.IntroText} ${source.OutroText}`
        )
        bookend.UpdatedAt = now
        return bookend
    }

    async archiveBookend(id: string): Promise<BookendFixture | null> {
        await delay()
        const bookend = this._bookends.get(id)
        if (!bookend) return null
        bookend.Status = 'archived'
        bookend.UpdatedAt = new Date().toISOString()
        return bookend
    }

    async unarchiveBookend(id: string): Promise<BookendFixture | null> {
        await delay()
        const bookend = this._bookends.get(id)
        if (!bookend) return null
        bookend.Status = 'active'
        bookend.UpdatedAt = new Date().toISOString()
        return bookend
    }

    /**
     * Deletes a bookend unless any schedule still references it (fixed id or a
     * rotation policy's ids) — the mock mirrors the backend's 409 library.in_use.
     */
    async deleteBookend(id: string): Promise<'deleted' | 'in_use' | 'missing'> {
        await delay()
        for (const schedule of this._schedules.values()) {
            if (scheduleReferencesBookend(schedule, id)) return 'in_use'
        }
        if (this._bookends.delete(id)) return 'deleted'
        return 'missing'
    }

    async previewBookend(body: {
        IntroText?: string | null
        OutroText?: string | null
        MarketId?: string | null
        Station?: string | null
        Tokens?: Record<string, string> | null
    }): Promise<{
        ResolvedIntro: string
        ResolvedOutro: string
        UnresolvedTokens: string[]
        Values: Record<string, string>
    }> {
        await delay(250)
        return resolveBookendTemplate(
            body.IntroText ?? '',
            body.OutroText ?? '',
            body.MarketId ?? 'sydney',
            body.Station ?? 'the station',
            body.Tokens ?? {}
        )
    }

    // ---------------------------------------------------------------------------
    // Pronunciation dictionaries (shared pronunciation library)
    // ---------------------------------------------------------------------------
    async listPronunciationDictionaries(): Promise<{
        Items: PronunciationDictionaryFixture[]
        Total: number
    }> {
        await delay()
        const items = [...this._pronunciationDictionaries.values()]
        return { Items: items, Total: items.length }
    }

    async getPronunciationDictionary(
        id: string
    ): Promise<PronunciationDictionaryFixture | null> {
        await delay()
        return this._pronunciationDictionaries.get(id) ?? null
    }

    async deletePronunciationDictionary(id: string): Promise<boolean> {
        await delay()
        return this._pronunciationDictionaries.delete(id)
    }

    async createPronunciationDictionary(body: {
        Name: string
        Description?: string | null
        Rules: PronunciationRuleFixture[]
        ChangeNote?: string | null
        Scope?: 'Station' | 'Global'
    }): Promise<PronunciationDictionaryFixture> {
        await delay()
        const now = new Date().toISOString()
        const slug =
            body.Name.toLowerCase()
                .replace(/[^a-z0-9]+/g, '-')
                .replace(/^-+|-+$/g, '')
                .slice(0, 24) || 'pd'
        const id = `pd-${slug}-${Math.random().toString(36).slice(2, 7)}`
        const elVersionId = `el-ver-${Math.random().toString(36).slice(2, 10)}`
        const dict: PronunciationDictionaryFixture = {
            Id: id,
            DictionaryId: `el-dict-${Math.random().toString(36).slice(2, 10)}`,
            Name: body.Name,
            Description: body.Description ?? null,
            Status: 'active',
            Scope: body.Scope ?? 'Station',
            CurrentVersion: 1,
            CreatedAt: now,
            UpdatedAt: now,
            CreatedBy: 'mock.admin@arn.com.au',
            AssignedStations: [],
            Rules: body.Rules,
            Versions: [
                {
                    Version: 1,
                    ElevenLabsVersionId: elVersionId,
                    Rules: body.Rules,
                    ChangeNote: body.ChangeNote ?? null,
                    CreatedAt: now,
                    CreatedBy: 'mock.admin@arn.com.au',
                },
            ],
        }
        this._pronunciationDictionaries.set(id, dict)
        return dict
    }

    async updatePronunciationDictionary(
        id: string,
        body: {
            Name: string
            Description?: string | null
            Rules: PronunciationRuleFixture[]
            ChangeNote?: string | null
            Scope?: 'Station' | 'Global'
        }
    ): Promise<PronunciationDictionaryFixture | null> {
        await delay()
        const dict = this._pronunciationDictionaries.get(id)
        if (!dict) return null
        const now = new Date().toISOString()
        const latest = dict.Versions[dict.Versions.length - 1]
        const rulesChanged =
            JSON.stringify(body.Rules) !== JSON.stringify(latest.Rules)
        if (rulesChanged) {
            const nextVersion = dict.CurrentVersion + 1
            const newElVersionId = `el-ver-${Math.random().toString(36).slice(2, 10)}`
            dict.Versions.push({
                Version: nextVersion,
                ElevenLabsVersionId: newElVersionId,
                Rules: body.Rules,
                ChangeNote: body.ChangeNote ?? null,
                CreatedAt: now,
                CreatedBy: 'mock.admin@arn.com.au',
            })
            dict.CurrentVersion = nextVersion
        }
        dict.Name = body.Name
        dict.Description = body.Description ?? null
        // Reach is not rule content, so a scope change mints no version.
        if (body.Scope) dict.Scope = body.Scope
        dict.UpdatedAt = now
        return dict
    }

    /**
     * The review cockpit's job-addressed one-word fix. Mirrors the server: resolve the dictionary
     * the bulletin AIRED with (matched on the provider id the job recorded), then REPLACE that
     * word's alias rule rather than appending a second one.
     */
    async addJobPronunciationRule(
        jobId: string,
        body: { Word: string; Replacement: string }
    ): Promise<{
        Id: string
        Outcome: 'added' | 'changed' | 'alreadyCorrect'
        Dictionary: PronunciationDictionaryFixture
    } | null> {
        const job = this._jobs.get(jobId)
        const airedId = job?.PronunciationDictionaryId
        if (!airedId) return null
        const dict = [...this._pronunciationDictionaries.values()].find(
            (d) => d.DictionaryId === airedId
        )
        if (!dict) return null
        // Mirrors the server: an operator's word fix never edits the platform-wide dictionary.
        if (dict.Scope === 'Global') return null

        const target = body.Word.trim().toLowerCase()
        const rules = dict.Rules.map((r) => ({ ...r }))
        const index = rules.findIndex(
            (r) =>
                r.Type === 'alias' &&
                r.StringToReplace.trim().toLowerCase() === target
        )
        let outcome: 'added' | 'changed' | 'alreadyCorrect'
        if (index < 0) {
            rules.push({
                Type: 'alias',
                StringToReplace: body.Word.trim(),
                Replacement: body.Replacement.trim(),
                Alphabet: null,
                CaseSensitive: false,
                WordBoundaries: true,
                AppliesOnAllModels: true,
            })
            outcome = 'added'
        } else if (rules[index].Replacement === body.Replacement.trim()) {
            outcome = 'alreadyCorrect'
        } else {
            rules[index] = {
                ...rules[index],
                Replacement: body.Replacement.trim(),
            }
            outcome = 'changed'
        }

        if (outcome === 'alreadyCorrect')
            return { Id: dict.Id, Outcome: outcome, Dictionary: dict }

        const updated = await this.updatePronunciationDictionary(dict.Id, {
            Name: dict.Name,
            Description: dict.Description,
            Rules: rules,
            ChangeNote:
                outcome === 'changed'
                    ? `Changed "${body.Word.trim()}" from bulletin review`
                    : `Added "${body.Word.trim()}" from bulletin review`,
        })
        if (!updated) return null
        updated.Rules = rules
        return { Id: updated.Id, Outcome: outcome, Dictionary: updated }
    }

    async restorePronunciationDictionaryVersion(
        id: string,
        version: number
    ): Promise<PronunciationDictionaryFixture | null> {
        await delay()
        const dict = this._pronunciationDictionaries.get(id)
        if (!dict) return null
        const source = dict.Versions.find((v) => v.Version === version)
        if (!source) return null
        const now = new Date().toISOString()
        const nextVersion = dict.CurrentVersion + 1
        const newElVersionId = `el-ver-${Math.random().toString(36).slice(2, 10)}`
        dict.Versions.push({
            Version: nextVersion,
            ElevenLabsVersionId: newElVersionId,
            Rules: source.Rules.map((r) => ({ ...r })),
            ChangeNote: `Restored from version ${version}.`,
            CreatedAt: now,
            CreatedBy: 'mock.admin@arn.com.au',
        })
        dict.CurrentVersion = nextVersion
        dict.UpdatedAt = now
        return dict
    }

    async archivePronunciationDictionary(
        id: string
    ): Promise<PronunciationDictionaryFixture | null> {
        await delay()
        const dict = this._pronunciationDictionaries.get(id)
        if (!dict) return null
        dict.Status = 'archived'
        dict.UpdatedAt = new Date().toISOString()
        return dict
    }

    async unarchivePronunciationDictionary(
        id: string
    ): Promise<PronunciationDictionaryFixture | null> {
        await delay()
        const dict = this._pronunciationDictionaries.get(id)
        if (!dict) return null
        dict.Status = 'active'
        dict.UpdatedAt = new Date().toISOString()
        return dict
    }

    /**
     * POST /pronunciation-dictionaries/preview-rule — ONE candidate rule (saved or not) in a
     * chosen voice. Mirrors the real trigger: an alias rule reads its replacement verbatim
     * (PhonemeApplied=false); a phoneme rule carries an inline tag (PhonemeApplied=true).
     */
    async previewPronunciationRule(body: {
        Type?: string
        StringToReplace?: string
        Replacement?: string
        Alphabet?: string
        BaseVoiceId?: string
        Model?: string
    }): Promise<{
        Url: string
        DurationSeconds: number
        SpokenText: string
        PhonemeApplied: boolean
    }> {
        await delay(300)
        const isPhoneme = (body.Type ?? 'alias') === 'phoneme'
        return {
            Url: '/mock/audio-bulletin.wav',
            DurationSeconds: 2.1,
            SpokenText: isPhoneme
                ? `<phoneme alphabet="${body.Alphabet === 'cmu' ? 'cmu-arpabet' : 'ipa'}" ph="${body.Replacement ?? ''}">${body.StringToReplace ?? ''}</phoneme>`
                : (body.Replacement ?? ''),
            PhonemeApplied: isPhoneme,
        }
    }

    // ---------------------------------------------------------------------------
    // Config transfer (export / import / pending approvals)
    // ---------------------------------------------------------------------------

    async exportConfig(sections?: string[]): Promise<Record<string, unknown>> {
        await delay()
        const allSections = new Set([
            'VoiceProfiles',
            'DeliveryStyles',
            'PronunciationDictionaries',
            'SafetyTerms',
            'Guidance',
            'PriorityRoads',
            'FillerStatements',
            'Schedules',
        ])
        const wanted = sections?.length
            ? sections.filter((s) => allSections.has(s))
            : Array.from(allSections)
        const bundle: Record<string, unknown> = {
            Schema: '/api/config/schema',
            SchemaVersion: 1,
            ExportedAtUtc: new Date().toISOString(),
            Source: 'mock',
        }
        if (wanted.includes('VoiceProfiles'))
            bundle.VoiceProfiles = [...this._voiceProfiles.values()]
        if (wanted.includes('DeliveryStyles'))
            bundle.DeliveryStyles = [...this._deliveryStyles.values()]
        if (wanted.includes('PronunciationDictionaries'))
            bundle.PronunciationDictionaries = [
                ...this._pronunciationDictionaries.values(),
            ]
        if (wanted.includes('SafetyTerms'))
            bundle.SafetyTerms = [...this._safetyTerms]
        if (wanted.includes('Guidance')) bundle.Guidance = [...this._guidance]
        if (wanted.includes('PriorityRoads')) {
            bundle.PriorityRoads = Array.from(
                this._priorityRoads.entries()
            ).map(([market, roads]) => ({ Market: market, Roads: roads }))
        }
        if (wanted.includes('FillerStatements')) {
            bundle.FillerStatements = Array.from(
                this._fillerStatements.entries()
            ).map(([market, statements]) => ({
                Market: market,
                Statements: statements,
            }))
        }
        if (wanted.includes('Schedules'))
            bundle.Schedules = [...this._schedules.values()]
        return bundle
    }

    /** Naive diff: compare incoming bundle against current store state. */
    planImport(bundle: Record<string, unknown>): {
        items: Array<{
            Section: string
            Key: string
            Name: string | null
            Action: 'Create' | 'Update' | 'Unchanged' | 'Error'
            Detail: string | null
            Outcome: null
        }>
        creates: number
        updates: number
        unchanged: number
        errors: number
    } {
        const items: Array<{
            Section: string
            Key: string
            Name: string | null
            Action: 'Create' | 'Update' | 'Unchanged' | 'Error'
            Detail: string | null
            Outcome: null
        }> = []
        let creates = 0
        let updates = 0
        let unchanged = 0
        let errors = 0

        const addItem = (
            section: string,
            key: string,
            name: string | null,
            action: 'Create' | 'Update' | 'Unchanged' | 'Error',
            detail: string | null = null
        ) => {
            items.push({
                Section: section,
                Key: key,
                Name: name,
                Action: action,
                Detail: detail,
                Outcome: null,
            })
            if (action === 'Create') creates++
            else if (action === 'Update') updates++
            else if (action === 'Unchanged') unchanged++
            else errors++
        }

        // Safety terms
        const incomingSafety = bundle.SafetyTerms as
            | Array<{
                  Id?: string
                  Kind?: string
                  Scope?: string
                  Term?: string
              }>
            | undefined
        if (Array.isArray(incomingSafety)) {
            const existingMap = new Map(this._safetyTerms.map((s) => [s.Id, s]))
            for (const term of incomingSafety) {
                const id = term.Id ?? ''
                if (!id) {
                    addItem(
                        'SafetyTerms',
                        '(missing)',
                        term.Term ?? null,
                        'Error',
                        'Missing required Id'
                    )
                    continue
                }
                const existing = existingMap.get(id)
                if (!existing) {
                    addItem('SafetyTerms', id, term.Term ?? null, 'Create')
                } else if (
                    existing.Term !== term.Term ||
                    existing.Kind !== term.Kind ||
                    existing.Scope !== term.Scope
                ) {
                    addItem('SafetyTerms', id, term.Term ?? null, 'Update')
                } else {
                    addItem('SafetyTerms', id, term.Term ?? null, 'Unchanged')
                }
            }
        }

        // Priority roads
        const incomingRoads = bundle.PriorityRoads as
            Array<{ Market?: string; Roads?: unknown[] }> | undefined
        if (Array.isArray(incomingRoads)) {
            for (const market of incomingRoads) {
                const mkt = market.Market ?? '(unknown)'
                const existing = this._priorityRoads.get(mkt)
                if (
                    !existing ||
                    JSON.stringify(existing) !== JSON.stringify(market.Roads)
                ) {
                    addItem(
                        'PriorityRoads',
                        mkt,
                        mkt,
                        existing ? 'Update' : 'Create'
                    )
                } else {
                    addItem('PriorityRoads', mkt, mkt, 'Unchanged')
                }
            }
        }

        // Filler statements
        const incomingFiller = bundle.FillerStatements as
            Array<{ Market?: string; Statements?: unknown[] }> | undefined
        if (Array.isArray(incomingFiller)) {
            for (const market of incomingFiller) {
                const mkt = market.Market ?? '(unknown)'
                const existing = this._fillerStatements.get(mkt)
                if (
                    !existing ||
                    JSON.stringify(existing) !==
                        JSON.stringify(market.Statements)
                ) {
                    addItem(
                        'FillerStatements',
                        mkt,
                        mkt,
                        existing ? 'Update' : 'Create'
                    )
                } else {
                    addItem('FillerStatements', mkt, mkt, 'Unchanged')
                }
            }
        }

        // Guidance
        const incomingGuidance = bundle.Guidance as
            | Array<{
                  Id?: string
                  Scope?: string
                  ScopeId?: string
                  Text?: string
              }>
            | undefined
        if (Array.isArray(incomingGuidance)) {
            const existingMap = new Map(this._guidance.map((g) => [g.Id, g]))
            for (const g of incomingGuidance) {
                const id = g.Id ?? ''
                if (!id) {
                    addItem(
                        'Guidance',
                        '(missing)',
                        g.Text ?? null,
                        'Error',
                        'Missing required Id'
                    )
                    continue
                }
                const existing = existingMap.get(id)
                if (!existing) {
                    addItem('Guidance', id, g.Text ?? null, 'Create')
                } else if (
                    existing.Text !== g.Text ||
                    existing.Scope !== g.Scope
                ) {
                    addItem('Guidance', id, g.Text ?? null, 'Update')
                } else {
                    addItem('Guidance', id, g.Text ?? null, 'Unchanged')
                }
            }
        }

        // Voice profiles (flatten to items by Id/Name)
        const incomingVP = bundle.VoiceProfiles as
            Array<{ Id?: string; Name?: string }> | undefined
        if (Array.isArray(incomingVP)) {
            for (const vp of incomingVP) {
                const id = vp.Id ?? ''
                const existing = this._voiceProfiles.get(id)
                if (!existing) {
                    addItem('VoiceProfiles', id, vp.Name ?? null, 'Create')
                } else {
                    addItem('VoiceProfiles', id, vp.Name ?? null, 'Unchanged')
                }
            }
        }

        // Delivery styles
        const incomingDS = bundle.DeliveryStyles as
            Array<{ Id?: string; Name?: string }> | undefined
        if (Array.isArray(incomingDS)) {
            for (const ds of incomingDS) {
                const id = ds.Id ?? ''
                const existing = this._deliveryStyles.get(id)
                if (!existing) {
                    addItem('DeliveryStyles', id, ds.Name ?? null, 'Create')
                } else {
                    addItem('DeliveryStyles', id, ds.Name ?? null, 'Unchanged')
                }
            }
        }

        // Pronunciation dictionaries
        const incomingPD = bundle.PronunciationDictionaries as
            Array<{ Id?: string; Name?: string }> | undefined
        if (Array.isArray(incomingPD)) {
            for (const pd of incomingPD) {
                const id = pd.Id ?? ''
                const existing = this._pronunciationDictionaries.get(id)
                if (!existing) {
                    addItem(
                        'PronunciationDictionaries',
                        id,
                        pd.Name ?? null,
                        'Create'
                    )
                } else {
                    addItem(
                        'PronunciationDictionaries',
                        id,
                        pd.Name ?? null,
                        'Unchanged'
                    )
                }
            }
        }

        // Schedules
        const incomingSchedules = bundle.Schedules as
            Array<{ MarketId?: string; Name?: string }> | undefined
        if (Array.isArray(incomingSchedules)) {
            for (const s of incomingSchedules) {
                const id = s.MarketId ?? ''
                const existing = this._schedules.get(id)
                if (!existing) {
                    addItem('Schedules', id, s.Name ?? null, 'Create')
                } else {
                    addItem('Schedules', id, s.Name ?? null, 'Unchanged')
                }
            }
        }

        return { items, creates, updates, unchanged, errors }
    }

    /** Apply import: mutate store sections that are understood. */
    applyImport(bundle: Record<string, unknown>): {
        items: Array<{
            Section: string
            Key: string
            Name: string | null
            Action: 'Create' | 'Update' | 'Unchanged' | 'Error'
            Detail: string | null
            Outcome: 'Applied' | 'Failed' | null
        }>
        creates: number
        updates: number
        unchanged: number
        errors: number
    } {
        const plan = this.planImport(bundle)
        const applied = plan.items.map((item) => ({
            ...item,
            Outcome:
                item.Action === 'Error'
                    ? ('Failed' as const)
                    : item.Action !== 'Unchanged'
                      ? ('Applied' as const)
                      : null,
        }))

        // Mutate safety terms (naive replace for items in bundle)
        const incomingSafety = bundle.SafetyTerms as
            | Array<{
                  Id?: string
                  Kind?: string
                  Scope?: string
                  ScopeId?: string
                  Term?: string
                  Note?: string | null
                  Enabled?: boolean
              }>
            | undefined
        if (Array.isArray(incomingSafety)) {
            const existingMap = new Map(this._safetyTerms.map((s) => [s.Id, s]))
            for (const term of incomingSafety) {
                const id = term.Id
                if (!id) continue
                const existing = existingMap.get(id)
                if (existing) {
                    existing.Term = term.Term ?? existing.Term
                    existing.Kind = term.Kind ?? existing.Kind
                    existing.Scope = term.Scope ?? existing.Scope
                    existing.ScopeId = term.ScopeId ?? existing.ScopeId
                    existing.Note = term.Note ?? existing.Note
                    existing.UpdatedAtUtc = new Date().toISOString()
                } else {
                    this._safetyTerms.push({
                        Id: id,
                        Kind: term.Kind ?? 'Banned',
                        Scope: term.Scope ?? 'Global',
                        ScopeId: term.ScopeId ?? '',
                        Term: term.Term ?? '',
                        Note: term.Note ?? null,
                        CreatedBy: 'admin@arn.com.au',
                        CreatedAtUtc: new Date().toISOString(),
                        UpdatedAtUtc: new Date().toISOString(),
                    })
                }
            }
        }

        // Mutate priority roads (per-market whole-list replace)
        const incomingRoads = bundle.PriorityRoads as
            Array<{ Market?: string; Roads?: PriorityRoadRecord[] }> | undefined
        if (Array.isArray(incomingRoads)) {
            for (const market of incomingRoads) {
                if (market.Market && market.Roads) {
                    this._priorityRoads.set(market.Market, market.Roads)
                }
            }
        }

        // Mutate filler statements (per-market whole-list replace)
        const incomingFiller = bundle.FillerStatements as
            | Array<{
                  Market?: string
                  Statements?: FillerStatementRecord[]
              }>
            | undefined
        if (Array.isArray(incomingFiller)) {
            for (const market of incomingFiller) {
                if (market.Market && market.Statements) {
                    this._fillerStatements.set(market.Market, market.Statements)
                }
            }
        }

        return {
            items: applied,
            creates: plan.creates,
            updates: plan.updates,
            unchanged: plan.unchanged,
            errors: plan.errors,
        }
    }
}

const editorialSettings = new Map<string, Record<string, number | null>>()
// Kept apart from the numeric map above so the coercion in upsertEditorialSettings cannot
// flatten the denylist array to null. null/absent = the scope inherits.
const storedSources = new Map<string, string[]>()

/** Mirrors the backend's OfficialTrafficSources.All. */
const MOCK_TRAFFIC_SOURCES = [
    'livetraffic-nsw',
    'qldtraffic',
    'victraffic',
    'mainroads-wa',
    'trafficsa',
    'tasalert',
]

const editorialScopeKey = (scope: string, scopeId: string) =>
    scope === 'Global' ? 'Global' : `${scope}:${scopeId}`

export const mockEditorialApi = {
    getEditorialSettings: (scope: string, scopeId: string) => {
        const stored =
            editorialSettings.get(editorialScopeKey(scope, scopeId)) ?? {}
        return {
            Scope: scope,
            ScopeId: scopeId,
            DriveTimeHeadlineThresholdMinutes:
                stored.DriveTimeHeadlineThresholdMinutes ?? null,
            MaxCorridors: stored.MaxCorridors ?? null,
            MajorTierCap: stored.MajorTierCap ?? null,
            ModerateTierCap: stored.ModerateTierCap ?? null,
            MinorTierCap: stored.MinorTierCap ?? null,
            ContinuityNewCap: stored.ContinuityNewCap ?? null,
            ContinuityOngoingCap: stored.ContinuityOngoingCap ?? null,
            ContinuityClearedCap: stored.ContinuityClearedCap ?? null,
            ContinuityUpdatedCap: stored.ContinuityUpdatedCap ?? null,
            GuidanceMaxDirectives: stored.GuidanceMaxDirectives ?? null,
            MaxSlowDownFactor: stored.MaxSlowDownFactor ?? null,
            MaxSpeedUpFactor: stored.MaxSpeedUpFactor ?? null,
            MaxSilencePadSeconds: stored.MaxSilencePadSeconds ?? null,
            MaxSafeOverrunTrimSeconds: stored.MaxSafeOverrunTrimSeconds ?? null,
            DisabledTrafficSources:
                storedSources.get(editorialScopeKey(scope, scopeId)) ?? null,
            UpdatedAtUtc: new Date().toISOString(),
            UpdatedBy: 'mock.editor@arn.com.au',
        }
    },

    upsertEditorialSettings: (body: Record<string, unknown>) => {
        const scope = String(body.Scope ?? 'Global')
        const scopeId = scope === 'Global' ? '' : String(body.ScopeId ?? '')
        const row: Record<string, number | null> = {}
        for (const [field, value] of Object.entries(body)) {
            if (field === 'Scope' || field === 'ScopeId') continue
            // The denylist is the one non-numeric field; it lives in its own map so the numeric
            // coercion below cannot flatten an array to null.
            if (field === 'DisabledTrafficSources') continue
            row[field] = typeof value === 'number' ? value : null
        }
        editorialSettings.set(editorialScopeKey(scope, scopeId), row)
        const sources = body.DisabledTrafficSources
        if (Array.isArray(sources))
            storedSources.set(
                editorialScopeKey(scope, scopeId),
                sources.map(String)
            )
        else storedSources.delete(editorialScopeKey(scope, scopeId))
        return mockEditorialApi.getEditorialSettings(scope, scopeId)
    },

    deleteEditorialSettings: (scope: string, scopeId: string) => {
        editorialSettings.delete(editorialScopeKey(scope, scopeId))
        storedSources.delete(editorialScopeKey(scope, scopeId))
    },

    effectiveEditorialSettings: (market: string, station: string | null) => {
        const global = editorialSettings.get('Global') ?? {}
        const marketRow = editorialSettings.get(`Market:${market}`) ?? {}
        const stationRow = station
            ? (editorialSettings.get(`Station:${station}`) ?? {})
            : {}
        const merged = { ...global, ...marketRow, ...stationRow }
        const configured = Object.entries(merged)
            .filter(([, value]) => typeof value === 'number')
            .map(([field]) => field)
        // Most-specific-wins per field, matching EditorialSettings.OverlayWith: a scope that set
        // nothing (absent key) inherits rather than contributing an empty denylist.
        const mergedSources =
            (station ? storedSources.get(`Station:${station}`) : undefined) ??
            storedSources.get(`Market:${market}`) ??
            storedSources.get('Global') ??
            []
        if (
            (station && storedSources.has(`Station:${station}`)) ||
            storedSources.has(`Market:${market}`) ||
            storedSources.has('Global')
        )
            configured.push('DisabledTrafficSources')
        return {
            Market: market,
            Station: station,
            DriveTimeHeadlineThresholdMinutes:
                merged.DriveTimeHeadlineThresholdMinutes ?? 5,
            MaxCorridors: merged.MaxCorridors ?? 5,
            MajorTierCap: merged.MajorTierCap ?? 4,
            ModerateTierCap: merged.ModerateTierCap ?? 3,
            MinorTierCap: merged.MinorTierCap ?? 3,
            ContinuityNewCap: merged.ContinuityNewCap ?? 3,
            ContinuityOngoingCap: merged.ContinuityOngoingCap ?? 2,
            ContinuityClearedCap: merged.ContinuityClearedCap ?? 2,
            ContinuityUpdatedCap: merged.ContinuityUpdatedCap ?? 1,
            GuidanceMaxDirectives: merged.GuidanceMaxDirectives ?? 8,
            MaxSlowDownFactor: merged.MaxSlowDownFactor ?? 1.0,
            MaxSpeedUpFactor: merged.MaxSpeedUpFactor ?? 1.15,
            MaxSilencePadSeconds: merged.MaxSilencePadSeconds ?? 0.6,
            MaxSafeOverrunTrimSeconds: merged.MaxSafeOverrunTrimSeconds ?? 1.5,
            ConfiguredByArn: configured,
            EnabledTrafficSources: MOCK_TRAFFIC_SOURCES.filter(
                (id) => !mergedSources.includes(id)
            ),
        }
    },
}
