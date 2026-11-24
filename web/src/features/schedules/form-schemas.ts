import { z } from 'zod'
import {
    CoveragePointSchema,
    SourceCoverageSchema,
    COVERAGE_SHAPE_TYPES,
    MIN_CORRIDOR_RADIUS_M,
    MAX_CORRIDOR_RADIUS_M,
    ContentSelectionSchema,
    VOICE_SELECTION_MODES,
    BOOKEND_SELECTION_MODES,
    FILL_SOURCES,
} from '@/lib/api/schemas/shared'
import type { BookendSelectionMode } from '@/lib/api/schemas/shared'
import { validateCoverageShape } from '@/components/map-picker/coverage-validation'
import { buildCronFromCalendar } from './lib/cron-calendar'

export const TARGET_DURATION_MAX = 600
/** Server floor (MarketSchedule.MinDurationSeconds) — 0-4s passes the UI but 400s on save. */
export const TARGET_DURATION_MIN = 5
/** Server cap (ConfigValidationLimits.ScheduleNameMax/StationMax). */
export const NAME_MAX_CHARS = 120
/** Server cron gap floor (ConfigValidationLimits.MinCronIntervalMinutes) — a 5-min client
 *  floor would reject adelaide's real 3-minute around-news-break gap. */
export const INTERVAL_MIN_MINUTES = 2
export const INTERVAL_MAX_MINUTES = 60
export const RADIUS_KM_MIN = 1

/** HH:MM 24-hour time, matching the backend window time format. */
export const TIME_HHMM_REGEX = /^([01]\d|2[0-3]):[0-5]\d$/

/** Minutes-since-midnight ranges for a window, splitting a midnight wrap into two. */
function windowMinuteRanges(
    start: string,
    end: string
): Array<[number, number]> {
    const toMin = (t: string) => {
        const [h, m] = t.split(':').map(Number)
        return h * 60 + m
    }
    const s = toMin(start)
    const e = toMin(end)
    return s < e
        ? [[s, e]]
        : [
              [s, 1440],
              [0, e],
          ]
}

/** True when any two windows share an instant (wrap-aware, half-open ranges). */
export function voiceWindowsOverlap(
    windows: ReadonlyArray<{ start: string; end: string }>
): boolean {
    const ranges = windows.flatMap((w, i) =>
        windowMinuteRanges(w.start, w.end).map(([lo, hi]) => ({ lo, hi, i }))
    )
    for (let a = 0; a < ranges.length; a++) {
        for (let b = a + 1; b < ranges.length; b++) {
            if (
                ranges[a].i !== ranges[b].i &&
                ranges[a].lo < ranges[b].hi &&
                ranges[b].lo < ranges[a].hi
            ) {
                return true
            }
        }
    }
    return false
}

/**
 * The bookend-rotation cross-field rules — the exact mirror of the
 * voice-rotation block in the schema's superRefine, for the single pair.
 */
function validateBookendRotation(
    mode: BookendSelectionMode,
    ids: readonly string[],
    windows: ReadonlyArray<{
        start: string
        end: string
        bookendId: string
    }>,
    ctx: z.RefinementCtx
): void {
    const idsPath = 'bookendRotationIds'
    const windowsPath = 'bookendWindows'
    if (mode === 'roundRobin') {
        if (ids.length === 0) {
            ctx.addIssue({
                code: z.ZodIssueCode.custom,
                message: 'Select at least one bookend to rotate',
                path: [idsPath],
            })
        } else if (new Set(ids).size !== ids.length) {
            ctx.addIssue({
                code: z.ZodIssueCode.custom,
                message: 'A bookend is listed more than once',
                path: [idsPath],
            })
        }
    }
    if (mode === 'timeOfDay') {
        if (windows.length === 0) {
            ctx.addIssue({
                code: z.ZodIssueCode.custom,
                message: 'Add at least one time window',
                path: [windowsPath],
            })
        }
        windows.forEach((w, i) => {
            if (!w.bookendId) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'Pick a bookend for this window',
                    path: [windowsPath, i, 'bookendId'],
                })
            }
            if (!TIME_HHMM_REGEX.test(w.start)) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'Time must be HH:MM',
                    path: [windowsPath, i, 'start'],
                })
            }
            if (!TIME_HHMM_REGEX.test(w.end)) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'Time must be HH:MM',
                    path: [windowsPath, i, 'end'],
                })
            }
            if (w.start && w.start === w.end) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'Start and end must differ',
                    path: [windowsPath, i, 'end'],
                })
            }
        })
        if (voiceWindowsOverlap(windows)) {
            ctx.addIssue({
                code: z.ZodIssueCode.custom,
                message: 'Time windows must not overlap',
                path: [windowsPath],
            })
        }
    }
}
/**
 * Mirrors the domain's `ScheduleCalendar.Create` admission rules that a UI state can
 * actually violate. The caps match `ScheduleCalendar.MaxPatterns`/`MaxAssignments`
 * so the console refuses at the same boundary the server does.
 */
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/

const ScheduleCalendarSchema = z
    .object({
        patterns: z
            .array(
                z.object({
                    id: z.string().min(1),
                    name: z.string().min(1, 'Every day type needs a name'),
                    cronExpression: z.string().min(1),
                })
            )
            .max(24, 'At most 24 day types per station')
            .readonly(),
        assignments: z
            .array(
                z.object({
                    startDate: z.string().regex(ISO_DATE),
                    endDate: z.string().regex(ISO_DATE),
                    patternId: z.string().nullable(),
                    suppress: z.boolean(),
                    note: z.string().nullable(),
                })
            )
            .max(400, 'At most 400 dated exceptions per station')
            .readonly(),
    })
    .superRefine((calendar, ctx) => {
        const names = new Set<string>()
        for (const pattern of calendar.patterns) {
            const key = pattern.name.trim().toLowerCase()
            if (names.has(key))
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: `Two day types are both called "${pattern.name}"`,
                })
            names.add(key)
        }

        const ids = new Set(
            calendar.patterns.map((p) => p.id.trim().toLowerCase())
        )
        for (const assignment of calendar.assignments) {
            if (assignment.endDate < assignment.startDate)
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: `${assignment.startDate}: the end date is before the start date`,
                })
            // A dangling id resolves back to the weekly clock at airtime — bulletins
            // reappearing on a date someone deliberately changed. Caught here.
            if (
                !assignment.suppress &&
                !ids.has((assignment.patternId ?? '').trim().toLowerCase())
            )
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: `${assignment.startDate} points at a day type that no longer exists`,
                })
        }
    })

/**
 * Validation schema for the Schedule form.
 */
export const ScheduleFormSchema = z
    .object({
        marketId: z.string().min(1, 'Market ID is required'),
        name: z
            .string()
            .min(1, 'Name is required')
            .max(
                NAME_MAX_CHARS,
                `Name must be at most ${NAME_MAX_CHARS} characters`
            ),
        station: z
            .string()
            .min(1, 'Station is required')
            .max(
                NAME_MAX_CHARS,
                `Station must be at most ${NAME_MAX_CHARS} characters`
            ),
        timezone: z.string().min(1, 'Timezone is required'),
        enabled: z.boolean(),
        playoutEnabled: z.boolean(),
        latitude: z.number().min(-90).max(90),
        longitude: z.number().min(-180).max(180),
        radiusKm: z.number().min(RADIUS_KM_MIN),
        targetDurationSeconds: z
            .number()
            .min(TARGET_DURATION_MIN, `Minimum ${TARGET_DURATION_MIN} seconds`)
            .max(TARGET_DURATION_MAX, `Maximum ${TARGET_DURATION_MAX} seconds`),
        notifyBeforePlayoutMinutes: z
            .number()
            .int('Must be a whole number')
            .min(0, 'Must be at least 0 minutes')
            .max(120, 'Must be at most 120 minutes'),
        escalationBeforePlayoutMinutes: z
            .number()
            .int('Must be a whole number')
            .min(0, 'Must be at least 0 minutes')
            .max(120, 'Must be at most 120 minutes'),
        voiceProfileId: z.string().min(1, 'Voice profile is required'),
        deliveryStyleId: z.string(),
        playbookId: z.string().default(''),
        pronunciationDictionaryId: z.string(),
        s3Path: z.string().optional().nullable(),
        daysOfWeek: z.array(z.number()).min(1, 'Select at least one day'),
        scheduleMode: z.enum(['interval', 'specific']),
        startTime: z.string(),
        endTime: z.string(),
        intervalMinutes: z
            .number()
            .int('Must be a whole number')
            .min(
                INTERVAL_MIN_MINUTES,
                `Minimum ${INTERVAL_MIN_MINUTES} minutes`
            )
            .max(
                INTERVAL_MAX_MINUTES,
                'Use 60 or less. For longer gaps use specific times'
            ),
        specificTimes: z.array(
            z
                .string()
                .regex(
                    /^([01]\d|2[0-3]):[0-5]\d$/,
                    'Time must be in HH:MM format (00:00–23:59)'
                )
        ),
        shapeType: z.enum(COVERAGE_SHAPE_TYPES).optional(),
        west: z.number().optional(),
        south: z.number().optional(),
        east: z.number().optional(),
        north: z.number().optional(),
        corridorPoints: z.array(CoveragePointSchema).optional(),
        content: ContentSelectionSchema.superRefine((c, ctx) => {
            // "Always include" implies selected: mirrors the backend's 400
            // on a PUT that flags a source it does not select.
            if (c.weatherRequired && !c.weather) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message:
                        'Always include requires the source to be selected',
                    path: ['weatherRequired'],
                })
            }
            if (c.fuelRequired && !c.fuel) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message:
                        'Always include requires the source to be selected',
                    path: ['fuelRequired'],
                })
            }
            if (c.segmentOrder) {
                const enabledOrder = (
                    [
                        ['Traffic', c.traffic],
                        ['Weather', c.weather],
                        ['Fuel', c.fuel],
                    ] as const
                )
                    .filter(([, enabled]) => enabled)
                    .map(([name]) => name)
                const unique = new Set(c.segmentOrder)
                if (
                    unique.size !== c.segmentOrder.length ||
                    enabledOrder.length !== c.segmentOrder.length ||
                    enabledOrder.some((segment) => !unique.has(segment))
                ) {
                    ctx.addIssue({
                        code: z.ZodIssueCode.custom,
                        message:
                            'Running order must list each enabled source exactly once',
                        path: ['segmentOrder'],
                    })
                }
            }
            if (c.trafficOrder && !c.traffic) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'Traffic order requires Traffic to be selected',
                    path: ['trafficOrder'],
                })
            }
        }),
        fillOrder: z
            .array(z.enum(FILL_SOURCES))
            .refine(
                (order) => new Set(order).size === order.length,
                'Each fill source can appear only once'
            ),
        /**
         * Year calendar. Validated for the things a bad UI state could produce —
         * an inverted date range, a date pointing at a day type that no longer
         * exists — so the studio reports them at the step rather than the operator
         * discovering a 400 from the backend's own `ScheduleCalendar.Create`.
         * Cron VALIDITY per pattern stays a server concern: it owns the parser.
         */
        calendar: ScheduleCalendarSchema.nullable(),
        corridorRadiusMeters: z
            .number()
            .min(MIN_CORRIDOR_RADIUS_M)
            .max(MAX_CORRIDOR_RADIUS_M)
            .optional(),
        polygonPoints: z.array(CoveragePointSchema).optional(),
        weatherCoverage: SourceCoverageSchema.optional(),
        fuelCoverage: SourceCoverageSchema.optional(),
        voiceRotationMode: z.enum(VOICE_SELECTION_MODES),
        voiceRotationIds: z.array(z.string()),
        voiceWindows: z.array(
            z.object({
                start: z.string(),
                end: z.string(),
                voiceProfileId: z.string(),
                /** Wire ids beyond the one the editor binds, carried through so
                 *  saving cannot silently drop them. See `VoiceWindowSchema`. */
                extraVoiceProfileIds: z.array(z.string()).optional(),
            })
        ),
        bookendId: z.string(),
        bookendRotationMode: z.enum(BOOKEND_SELECTION_MODES),
        bookendRotationIds: z.array(z.string()),
        bookendWindows: z.array(
            z.object({
                start: z.string(),
                end: z.string(),
                bookendId: z.string(),
                /** Wire ids beyond the one the editor binds, carried through so
                 *  saving cannot silently drop them. See `BookendWindowSchema`. */
                extraBookendIds: z.array(z.string()).optional(),
            })
        ),
        /** Token overrides as editable rows; blank rows are dropped on submit. */
        bookendTokens: z.array(
            z.object({
                key: z.string(),
                value: z.string(),
            })
        ),
    })
    .refine(
        (data) =>
            data.escalationBeforePlayoutMinutes <
            data.notifyBeforePlayoutMinutes,
        {
            message: 'Escalation time must be less than notification time',
            path: ['escalationBeforePlayoutMinutes'],
        }
    )
    .superRefine((data, ctx) => {
        if (data.scheduleMode === 'interval') {
            if (!data.startTime) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'Start time is required',
                    path: ['startTime'],
                })
            }
            if (!data.endTime) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'End time is required',
                    path: ['endTime'],
                })
            }
        }
        if (
            data.scheduleMode === 'specific' &&
            data.specificTimes.length === 0
        ) {
            ctx.addIssue({
                code: z.ZodIssueCode.custom,
                message:
                    'Add at least one time (use the hour buttons or the custom time input)',
                path: ['specificTimes'],
            })
        }
        // `buildCronFromCalendar` is the single source of truth for whether the
        // calendar values are expressible as a cron, so validate by CALLING it
        // rather than restating its rules here (they would drift). Without this,
        // an off-the-hour interval time such as 06:15 passed validation, the
        // builder returned a null cron, the submit bailed with only a toast, and
        // the studio closed exactly as it does on success — silently discarding
        // every edit across every step.
        const intervalTimesPresent =
            data.scheduleMode !== 'interval' ||
            (!!data.startTime && !!data.endTime)
        const specificTimesPresent =
            data.scheduleMode !== 'specific' || data.specificTimes.length > 0
        if (intervalTimesPresent && specificTimesPresent) {
            const { cronExpression, error } = buildCronFromCalendar({
                scheduleMode: data.scheduleMode,
                daysOfWeek: data.daysOfWeek,
                startTime: data.startTime,
                endTime: data.endTime,
                intervalMinutes: data.intervalMinutes,
                specificTimes: data.specificTimes,
            })
            if (!cronExpression) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message:
                        error ?? 'This schedule cannot be turned into a cron',
                    path: [
                        data.scheduleMode === 'interval'
                            ? 'startTime'
                            : 'specificTimes',
                    ],
                })
            }
        }
        // Coverage shape validation \u2014 shared constraints, default messages
        for (const issue of validateCoverageShape(data)) {
            ctx.addIssue({
                code: z.ZodIssueCode.custom,
                message: issue.message,
                path: [issue.path],
            })
        }
        // Voice rotation validation
        if (data.voiceRotationMode === 'roundRobin') {
            if (data.voiceRotationIds.length === 0) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'Select at least one voice to rotate',
                    path: ['voiceRotationIds'],
                })
            } else if (
                new Set(data.voiceRotationIds).size !==
                data.voiceRotationIds.length
            ) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'A voice is listed more than once',
                    path: ['voiceRotationIds'],
                })
            }
        }
        if (data.voiceRotationMode === 'timeOfDay') {
            if (data.voiceWindows.length === 0) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'Add at least one time window',
                    path: ['voiceWindows'],
                })
            }
            data.voiceWindows.forEach((w, i) => {
                if (!w.voiceProfileId) {
                    ctx.addIssue({
                        code: z.ZodIssueCode.custom,
                        message: 'Pick a voice for this window',
                        path: ['voiceWindows', i, 'voiceProfileId'],
                    })
                }
                if (!TIME_HHMM_REGEX.test(w.start)) {
                    ctx.addIssue({
                        code: z.ZodIssueCode.custom,
                        message: 'Time must be HH:MM',
                        path: ['voiceWindows', i, 'start'],
                    })
                }
                if (!TIME_HHMM_REGEX.test(w.end)) {
                    ctx.addIssue({
                        code: z.ZodIssueCode.custom,
                        message: 'Time must be HH:MM',
                        path: ['voiceWindows', i, 'end'],
                    })
                }
                if (w.start && w.start === w.end) {
                    ctx.addIssue({
                        code: z.ZodIssueCode.custom,
                        message: 'Start and end must differ',
                        path: ['voiceWindows', i, 'end'],
                    })
                }
            })
            if (voiceWindowsOverlap(data.voiceWindows)) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'Time windows must not overlap',
                    path: ['voiceWindows'],
                })
            }
        }
        // Bookend rotation validation — mirrors the voice rules, for the single pair
        validateBookendRotation(
            data.bookendRotationMode,
            data.bookendRotationIds,
            data.bookendWindows,
            ctx
        )
        // Bookend token overrides. Keys must be usable as {token} placeholders
        // (matching the backend's extraction regex), unique case-insensitively,
        // and a non-empty key needs a value. Blank rows are dropped by the
        // payload builder; a value with an empty key would be silently lost,
        // so that direction is flagged instead.
        const seenTokenKeys = new Set<string>()
        data.bookendTokens.forEach((row, i) => {
            const key = row.key.trim()
            if (!key) {
                if (row.value.trim()) {
                    ctx.addIssue({
                        code: z.ZodIssueCode.custom,
                        message: 'A token override needs a key',
                        path: ['bookendTokens', i, 'key'],
                    })
                }
                return
            }
            if (!/^[A-Za-z][A-Za-z0-9_]*$/.test(key)) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message:
                        'Token key must start with a letter and use only letters, numbers and underscores',
                    path: ['bookendTokens', i, 'key'],
                })
            }
            const lowerKey = key.toLowerCase()
            if (seenTokenKeys.has(lowerKey)) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'Each token key can appear only once',
                    path: ['bookendTokens', i, 'key'],
                })
            }
            seenTokenKeys.add(lowerKey)
            if (!row.value.trim()) {
                ctx.addIssue({
                    code: z.ZodIssueCode.custom,
                    message: 'A token override needs a value',
                    path: ['bookendTokens', i, 'value'],
                })
            }
        })
    })
