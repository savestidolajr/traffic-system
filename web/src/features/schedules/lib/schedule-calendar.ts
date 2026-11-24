// =============================================================================
// Calendar-year scheduling — client-side resolution
//
// Mirrors the backend's ScheduleCalendar.ResolveDay precedence EXACTLY so the
// console can tint a year, count a day's slots and preview a change without a
// round trip. The two implementations are a deliberate pair: if one changes,
// the other must.
// =============================================================================

import { cronSlotsByWeekday } from './cron-calendar'
import type {
    MarketSchedule,
    ScheduleAssignment,
    ScheduleCalendar,
} from '../types'

/** `YYYY-MM-DD`, always a STATION-LOCAL date. Never a UTC date, never a Date object. */
export type IsoDate = string

export type DayKind = 'base' | 'pattern' | 'suppressed'

export interface DayResolution {
    readonly kind: DayKind
    /** The expression governing the day; empty when suppressed. */
    readonly cronExpression: string
    readonly patternId: string | null
    readonly patternName: string | null
    /** The assignment that won, for provenance in the UI ("why is this day different?"). */
    readonly assignment: ScheduleAssignment | null
}

// ---------------------------------------------------------------------------
// Date helpers — plain civil dates, no Date objects, no timezone involved
// ---------------------------------------------------------------------------

/** Parses `YYYY-MM-DD` to a comparable integer (`20260425`). Invalid input sorts last. */
export function isoDateOrdinal(iso: IsoDate): number {
    const n = Number(iso.slice(0, 4) + iso.slice(5, 7) + iso.slice(8, 10))
    return Number.isFinite(n) ? n : Number.MAX_SAFE_INTEGER
}

export function toIsoDate(year: number, month1: number, day: number): IsoDate {
    return `${year}-${String(month1).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}

/** Days in a civil month (`month1` is 1-12). */
export function daysInMonth(year: number, month1: number): number {
    return new Date(Date.UTC(year, month1, 0)).getUTCDate()
}

/** Day of week for a civil date, 0 = Sunday. Computed in UTC so no zone can shift it. */
export function isoDayOfWeek(iso: IsoDate): number {
    return new Date(`${iso}T00:00:00Z`).getUTCDay()
}

/** Inclusive whole-day span of an assignment, in days. Used for the precedence tie-break. */
function assignmentSpanDays(a: ScheduleAssignment): number {
    const start = Date.parse(`${a.startDate}T00:00:00Z`)
    const end = Date.parse(`${a.endDate}T00:00:00Z`)
    if (!Number.isFinite(start) || !Number.isFinite(end))
        return Number.MAX_SAFE_INTEGER
    return Math.floor((end - start) / 86_400_000) + 1
}

// ---------------------------------------------------------------------------
// Resolution
// ---------------------------------------------------------------------------

/**
 * The assignment governing `iso`, or null when the base pattern applies.
 *
 * Precedence, matching the backend: among assignments whose inclusive range
 * contains the date, the SHORTEST window wins — a single-date Christmas Day
 * override must beat a "December reduced clock" range. An exact tie is broken
 * by list order, last one wins, so a later edit supersedes an earlier one.
 */
export function resolveAssignment(
    calendar: ScheduleCalendar | null,
    iso: IsoDate
): ScheduleAssignment | null {
    if (!calendar || calendar.assignments.length === 0) return null

    const target = isoDateOrdinal(iso)
    let best: ScheduleAssignment | null = null
    let bestSpan = Number.MAX_SAFE_INTEGER

    for (const a of calendar.assignments) {
        if (isoDateOrdinal(a.startDate) > target) continue
        if (isoDateOrdinal(a.endDate) < target) continue
        const span = assignmentSpanDays(a)
        // `<=` on purpose: later-in-list wins an exact tie.
        if (span <= bestSpan) {
            best = a
            bestSpan = span
        }
    }

    return best
}

/** Resolves what governs one station-local date for one station. */
export function resolveDay(
    schedule: Pick<MarketSchedule, 'cronExpression' | 'calendar'>,
    iso: IsoDate
): DayResolution {
    const base = schedule.cronExpression ?? ''
    const assignment = resolveAssignment(schedule.calendar, iso)

    if (!assignment) {
        return {
            kind: 'base',
            cronExpression: base,
            patternId: null,
            patternName: null,
            assignment: null,
        }
    }

    if (assignment.suppress) {
        return {
            kind: 'suppressed',
            cronExpression: '',
            patternId: null,
            patternName: null,
            assignment,
        }
    }

    // Case-INSENSITIVE, matching `ScheduleCalendar.ResolveDay`'s OrdinalIgnoreCase
    // lookup. `ScheduleCalendar.Create` also rejects ids that collide case-insensitively,
    // so there is exactly one candidate. A case-sensitive match here would preview a
    // fallback to the base clock for a day the backend actually runs on the pattern.
    const wanted = assignment.patternId?.trim().toLowerCase()
    const pattern = schedule.calendar?.patterns.find(
        (p) => p.id.trim().toLowerCase() === wanted
    )

    // A dangling pattern id is rejected at the write boundary. If a legacy row
    // still carries one, fall back to the base clock — degrade to "normal day",
    // never to silence.
    if (!pattern) {
        return {
            kind: 'base',
            cronExpression: base,
            patternId: null,
            patternName: null,
            assignment,
        }
    }

    return {
        kind: 'pattern',
        cronExpression: pattern.cronExpression,
        patternId: pattern.id,
        patternName: pattern.name,
        assignment,
    }
}

// ---------------------------------------------------------------------------
// Slot counting
//
// MEASURED, 2026-08-18, real Sydney station (60 compound cron lines):
// stepping candidate minutes and asking Intl.DateTimeFormat what day each landed
// on cost 1,166 ms for ONE day and 6,778 ms to project a year, which is what made
// the Year tab unusable. The cron fields already state the wall-clock times, so
// the answer is integer arithmetic over the parsed expression: no enumeration,
// no Intl, no timezone conversion at all.
// ---------------------------------------------------------------------------

const SLOT_CACHE = new Map<string, readonly (readonly number[])[]>()
const NO_SLOTS: readonly (readonly number[])[] = [[], [], [], [], [], [], []]

/** Run minutes-past-midnight per day of week (index 0 = Sunday) for an expression. */
function slotsByWeekday(
    cronExpression: string
): readonly (readonly number[])[] {
    if (!cronExpression) return NO_SLOTS
    const cached = SLOT_CACHE.get(cronExpression)
    if (cached) return cached
    const computed = cronSlotsByWeekday(cronExpression) ?? NO_SLOTS
    SLOT_CACHE.set(cronExpression, computed)
    return computed
}

/** Slot count per day of week (index 0 = Sunday). */
export function weekdaySlotCounts(cronExpression: string): readonly number[] {
    return slotsByWeekday(cronExpression).map((minutes) => minutes.length)
}

/**
 * Local `HH:MM` times a cron expression fires on one station-local date.
 *
 * Wall-clock times straight from the expression. The one case this does not model
 * is a slot inside the hour a spring-forward skips, which the backend's Cronos
 * evaluation drops; no Australian station currently schedules inside 02:00-03:00.
 */
export function occurrenceTimesForDate(
    cronExpression: string,
    iso: IsoDate
): readonly string[] {
    const minutes = slotsByWeekday(cronExpression)[isoDayOfWeek(iso)] ?? []
    return minutes.map(
        (m) =>
            `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`
    )
}

// ---------------------------------------------------------------------------
// Year projection
// ---------------------------------------------------------------------------

export interface YearDay {
    readonly iso: IsoDate
    readonly dayOfWeek: number
    readonly kind: DayKind
    readonly patternId: string | null
    readonly patternName: string | null
    readonly note: string | null
    readonly slots: number
    /** Slots the BASE clock would have run, so the UI can show the delta. */
    readonly baseSlots: number
}

/**
 * Every date of `year` with its resolved day type and slot count. Pure integer
 * work: one field expansion per distinct expression, then a lookup per date.
 */
export function projectYear(
    schedule: Pick<MarketSchedule, 'cronExpression' | 'calendar'>,
    year: number
): readonly YearDay[] {
    const baseCounts = weekdaySlotCounts(schedule.cronExpression ?? '')

    const patternCounts = new Map<string, readonly number[]>()
    for (const p of schedule.calendar?.patterns ?? []) {
        patternCounts.set(p.id, weekdaySlotCounts(p.cronExpression))
    }

    const days: YearDay[] = []
    for (let month = 1; month <= 12; month++) {
        const length = daysInMonth(year, month)
        for (let day = 1; day <= length; day++) {
            const iso = toIsoDate(year, month, day)
            const dow = isoDayOfWeek(iso)
            const resolved = resolveDay(schedule, iso)
            const baseSlots = baseCounts[dow] ?? 0

            let slots = baseSlots
            if (resolved.kind === 'suppressed') slots = 0
            else if (resolved.kind === 'pattern' && resolved.patternId) {
                slots = patternCounts.get(resolved.patternId)?.[dow] ?? 0
            }

            days.push({
                iso,
                dayOfWeek: dow,
                kind: resolved.kind,
                patternId: resolved.patternId,
                patternName: resolved.patternName,
                note: resolved.assignment?.note ?? null,
                slots,
                baseSlots,
            })
        }
    }

    return days
}

/** Totals for the year header: how many days differ, and the net slot change. */
export interface YearSummary {
    readonly totalSlots: number
    readonly baseTotalSlots: number
    readonly overriddenDays: number
    readonly suppressedDays: number
}

export function summariseYear(days: readonly YearDay[]): YearSummary {
    let totalSlots = 0
    let baseTotalSlots = 0
    let overriddenDays = 0
    let suppressedDays = 0

    for (const d of days) {
        totalSlots += d.slots
        baseTotalSlots += d.baseSlots
        if (d.kind === 'pattern') overriddenDays++
        if (d.kind === 'suppressed') suppressedDays++
    }

    return { totalSlots, baseTotalSlots, overriddenDays, suppressedDays }
}

/**
 * Days the walk will look ahead before giving up. Mirrors
 * `ScheduleOccurrenceResolver.MaxCalendarDays` so an all-suppressed calendar
 * terminates here exactly as it does on the poller.
 */
const MAX_LOOKAHEAD_DAYS = 400

/**
 * The next occurrence at or after `from`, honouring the calendar.
 *
 * The plain cron enumerator cannot answer this once a calendar exists: it would
 * report a slot on a suppressed date, or the base clock's slot on a day the station
 * is running a holiday pattern. Same day-by-day walk the backend does, so the
 * console's "Next run" and the poller agree, but resolved from the analytic slot
 * list with one timezone conversion for the single slot that wins.
 */
export function nextOccurrence(
    schedule: Pick<MarketSchedule, 'cronExpression' | 'timezone' | 'calendar'>,
    from: Date
): Date | null {
    const tz = schedule.timezone ?? 'UTC'
    const cursor = Date.parse(`${dayKey(from, tz)}T00:00:00Z`)

    for (let offset = 0; offset < MAX_LOOKAHEAD_DAYS; offset++) {
        const iso = new Date(cursor + offset * 86_400_000)
            .toISOString()
            .slice(0, 10)
        const resolved = resolveDay(schedule, iso)
        if (resolved.kind === 'suppressed' || !resolved.cronExpression) continue

        for (const time of occurrenceTimesForDate(
            resolved.cronExpression,
            iso
        )) {
            const instant = wallTimeToUtc(iso, time, tz)
            if (instant.getTime() > from.getTime()) return instant
        }
    }

    return null
}

/**
 * The UTC instant of a station-local wall time. Two passes because the offset
 * depends on the instant: the first lands within an hour, the second on the right
 * side of a DST boundary.
 */
function wallTimeToUtc(iso: IsoDate, time: string, timezone: string): Date {
    const naive =
        Date.parse(`${iso}T00:00:00Z`) +
        (Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5))) * 60_000
    let ms = naive - zoneOffsetMs(naive, timezone)
    ms = naive - zoneOffsetMs(ms, timezone)
    return new Date(ms)
}

/** Milliseconds to add to a UTC instant to read its wall clock in `timezone`. */
function zoneOffsetMs(utcMs: number, timezone: string): number {
    const parts = wallFormatter(timezone).formatToParts(new Date(utcMs))
    const read = (type: string) =>
        Number(parts.find((p) => p.type === type)?.value ?? '0')
    const asIfUtc = Date.UTC(
        read('year'),
        read('month') - 1,
        read('day'),
        read('hour') % 24,
        read('minute')
    )
    return asIfUtc - utcMs
}

const WALL_FORMATTER_CACHE = new Map<string, Intl.DateTimeFormat>()

function wallFormatter(timezone: string): Intl.DateTimeFormat {
    let fmt = WALL_FORMATTER_CACHE.get(timezone)
    if (!fmt) {
        fmt = new Intl.DateTimeFormat('en-GB', {
            timeZone: timezone,
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            hour12: false,
        })
        WALL_FORMATTER_CACHE.set(timezone, fmt)
    }
    return fmt
}

const DAY_KEY_CACHE = new Map<string, Intl.DateTimeFormat>()

/** The station-local calendar date of an instant, as `YYYY-MM-DD`. */
function dayKey(instant: Date, timezone: string): IsoDate {
    let fmt = DAY_KEY_CACHE.get(timezone)
    if (!fmt) {
        fmt = new Intl.DateTimeFormat('en-CA', {
            timeZone: timezone,
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
        })
        DAY_KEY_CACHE.set(timezone, fmt)
    }
    return fmt.format(instant)
}
