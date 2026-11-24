import type {
    BookendRotation,
    BookendSelectionMode,
    BookendWindow,
    ContentSelection,
    CoverageArea,
    CoveragePoint,
    CoverageShapeType,
    FillSource,
    VoiceSettings,
    VoiceRotation,
    VoiceSelectionMode,
    VoiceWindow,
} from '@/lib/api/schemas/shared'
export type {
    BookendRotation,
    BookendSelectionMode,
    BookendWindow,
    ContentSelection,
    CoverageArea,
    CoveragePoint,
    CoverageShapeType,
    FillSource,
    VoiceSettings,
    VoiceRotation,
    VoiceSelectionMode,
    VoiceWindow,
}

/**
 * Market schedule matching backend ScheduleSummaryDto.
 * Nullable fields from backend are represented as nullable here.
 */
export interface MarketSchedule {
    readonly marketId: string
    readonly name: string | null
    readonly station: string | null
    readonly timezone: string | null
    readonly cronExpression: string | null
    readonly enabled: boolean
    /**
     * Whether this station is armed for playout in THIS environment. A station can be
     * `enabled` (generating and publishing on schedule) with this false, in which case
     * publish keeps the audio in storage and writes nothing to the playout share.
     * Independent of the environment-wide playout switch — delivery needs both.
     */
    readonly playoutEnabled: boolean
    readonly targetDurationSeconds: number
    readonly notifyBeforePlayoutMinutes: number
    readonly escalationBeforePlayoutMinutes: number
    readonly voiceProfileId: string | null
    readonly deliveryStyleId: string | null
    /** Optional station-specific diff over the shipped bulletin writer playbook. */
    readonly playbookId?: string
    readonly pronunciationDictionaryId: string | null
    /** Selected bookend pair; null = no bookends (bulletin opens on the first read, ends on the last filler). */
    readonly bookendId: string | null
    readonly nextRunAt: string | null
    readonly currentJobId: string | null
    readonly createdAt: string
    readonly updatedAt: string
    readonly coverage: CoverageArea | null
    /** Resolved content sources; null = legacy traffic-only schedule. */
    readonly content: ContentSelection | null
    /** Per-station voice selection policy; null = single primary voice (legacy). */
    readonly voiceRotation: VoiceRotation | null
    /** Per-station bookend-pair selection policy; null = the bookend above only. */
    readonly bookendRotation: BookendRotation | null
    /** Per-station token overrides for bookend templates; null = built-ins only. */
    readonly bookendTokens: Readonly<Record<string, string>> | null
    /** Fill priority when a read runs short; null = backend default (weather, fuel, minor traffic, filler). */
    readonly fillOrder: readonly FillSource[] | null
    /** Year calendar of day-type patterns and date assignments; null = base weekly pattern all year. */
    readonly calendar: ScheduleCalendar | null
}

// ---------------------------------------------------------------------------
// Calendar-year scheduling
// ---------------------------------------------------------------------------

/**
 * A named, reusable day-type clock — the "public holiday pattern" ARN asked for.
 * `cronExpression` is the same compound (newline-separated) form the base schedule
 * uses, so a pattern is editable with exactly the weekly editor that already exists.
 */
export interface SchedulePattern {
    readonly id: string
    readonly name: string
    readonly cronExpression: string
}

/**
 * Binds an inclusive station-local date range to a day-type pattern, or suppresses
 * broadcasting entirely for those dates (the "fireworks event, take those out" case).
 * Exactly one of `patternId` / `suppress` is meaningful; the write boundary rejects
 * both-or-neither.
 */
export interface ScheduleAssignment {
    /** Inclusive station-local start date, `YYYY-MM-DD`. */
    readonly startDate: string
    /** Inclusive station-local end date, `YYYY-MM-DD`. Equal to `startDate` for a single day. */
    readonly endDate: string
    readonly patternId: string | null
    readonly suppress: boolean
    readonly note: string | null
}

/**
 * Per-station year calendar. `null` on a schedule means the station runs its base
 * weekly pattern every day of the year — exactly the behaviour that existed before
 * this feature.
 */
export interface ScheduleCalendar {
    readonly patterns: readonly SchedulePattern[]
    readonly assignments: readonly ScheduleAssignment[]
}

/** Wire shape for a voice-rotation policy (windows carry a voice set; the UI uses one each). */
export interface VoiceRotationPayload {
    readonly mode: VoiceSelectionMode
    readonly voiceProfileIds?: readonly string[]
    readonly windows?: ReadonlyArray<{
        readonly start: string
        readonly end: string
        readonly voiceProfileIds: readonly string[]
    }>
}

/** Wire shape for a bookend-rotation policy (windows carry a bookend set; the UI uses one each). */
export interface BookendRotationPayload {
    readonly mode: BookendSelectionMode
    readonly bookendIds?: readonly string[]
    readonly windows?: ReadonlyArray<{
        readonly start: string
        readonly end: string
        readonly bookendIds: readonly string[]
    }>
}

/**
 * Request body for creating/updating a schedule.
 * Matches backend UpsertScheduleRequest.
 */
export interface UpsertScheduleRequest {
    readonly name?: string
    readonly station?: string
    readonly timezone?: string
    readonly cronExpression?: string
    readonly enabled?: boolean
    readonly playoutEnabled?: boolean
    readonly targetDurationSeconds?: number
    readonly notifyBeforePlayoutMinutes?: number
    readonly escalationBeforePlayoutMinutes?: number
    readonly voiceProfileId?: string
    readonly pronunciationDictionaryId?: string
    readonly deliveryStyleId?: string
    /** Station-specific writer instructions; an empty string means shipped defaults. */
    readonly playbookId?: string
    /** Bookend-pair assignment; omitted = leave unchanged (update) / none (create). */
    readonly bookendId?: string
    readonly bookendRotation?: BookendRotationPayload
    /** Token overrides resolved against the bookend templates; omitted = built-ins only. */
    readonly bookendTokens?: Readonly<Record<string, string>>
    readonly coverage?: CoverageArea
    /** Per-bulletin content sources for this station's bulletins. */
    readonly content?: ContentSelection
    readonly voiceRotation?: VoiceRotationPayload
    /** Fill priority; omitted = keep existing (update) / backend default (create). */
    readonly fillOrder?: readonly FillSource[]
    /** Year calendar; omitted = leave unchanged. Send an empty calendar to clear it. */
    readonly calendar?: ScheduleCalendar
}

export interface SchedulesListResponse {
    readonly schedules: readonly MarketSchedule[]
    readonly total: number
}

export type ScheduleMode = 'interval' | 'specific'

export interface ScheduleFormData {
    marketId: string
    name: string
    station: string
    timezone: string
    enabled: boolean
    playoutEnabled: boolean
    latitude: number
    longitude: number
    radiusKm: number
    notifyBeforePlayoutMinutes: number
    targetDurationSeconds: number
    escalationBeforePlayoutMinutes: number
    voiceProfileId: string
    deliveryStyleId: string
    /** Empty means the station uses the shipped writer defaults. */
    playbookId?: string
    pronunciationDictionaryId: string
    bookendId: string
    bookendRotationMode: BookendSelectionMode
    bookendRotationIds: string[]
    bookendWindows: BookendWindow[]
    /** Token overrides as editable rows; converted to a record in the submit payload. */
    bookendTokens: Array<{ key: string; value: string }>
    intervalMinutes: number
    s3Path?: string | null
    daysOfWeek: number[]
    scheduleMode: ScheduleMode
    startTime: string
    endTime: string
    shapeType?: CoverageShapeType
    west?: number
    south?: number
    east?: number
    north?: number
    corridorPoints?: CoveragePoint[]
    corridorRadiusMeters?: number
    polygonPoints?: CoveragePoint[]
    /** Optional circle override used by the matching HERE source. */
    weatherCoverage?: CoverageArea
    fuelCoverage?: CoverageArea
    specificTimes: string[]
    /** Per-bulletin content sources — defaults to traffic-only on legacy. */
    content: ContentSelection
    /** Fill priority order; excluded sources are never used to fill a short read. */
    fillOrder: FillSource[]
    /**
     * Year calendar edited alongside the weekly clock in the same studio step, so a
     * station's whole schedule — normal week plus its date exceptions — saves in ONE PUT.
     * `null` = this station runs its weekly clock every day of the year.
     */
    calendar: ScheduleCalendar | null
    voiceRotationMode: VoiceSelectionMode
    voiceRotationIds: string[]
    voiceWindows: VoiceWindow[]
}
