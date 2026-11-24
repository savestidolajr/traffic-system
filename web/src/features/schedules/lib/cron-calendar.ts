import type { ScheduleMode } from '../types'

export interface CronCalendarInput {
    scheduleMode: ScheduleMode
    daysOfWeek: number[]
    startTime: string
    endTime: string
    intervalMinutes: number
    specificTimes: string[]
}

interface CronResult {
    cronExpression: string | null
    error?: string
}

export const DEFAULT_CALENDAR_CONFIG = {
    startTime: '06:00',
    endTime: '18:00',
    intervalMinutes: 15,
    daysOfWeek: [1, 2, 3, 4, 5],
}

/** All selectable hour slots from 5am to 11pm. */
export const TIME_SLOTS = Array.from({ length: 19 }, (_, i) => {
    const hour = i + 5
    return {
        value: `${hour.toString().padStart(2, '0')}:00`,
        label: formatHourLabel(hour),
    }
})

function formatHourLabel(hour: number): string {
    return `${hour.toString().padStart(2, '0')}:00`
}

/** Format an HH:MM string as a human-readable 24-hour label. */
export function formatTimeLabel(time: string): string {
    const [h, m] = time.split(':').map(Number)
    if (m === 0) return `${h.toString().padStart(2, '0')}:00`
    return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`
}

export function buildCronFromCalendar(input: CronCalendarInput): CronResult {
    const { scheduleMode, daysOfWeek, specificTimes } = input

    // A full week is written as `*`, so parse -> build is idempotent and a weekly-layer
    // save on a `0 6 * * *` station rewrites the identical expression rather than the
    // equivalent-but-noisy `0,1,2,3,4,5,6`.
    const dayStr =
        daysOfWeek.length === 0 || daysOfWeek.length === 7
            ? '*'
            : daysOfWeek.join(',')

    if (scheduleMode === 'specific') {
        return buildSpecificTimesCron(specificTimes, dayStr)
    }

    return buildIntervalCron(input, dayStr)
}

function buildSpecificTimesCron(
    specificTimes: string[],
    dayStr: string
): CronResult {
    if (specificTimes.length === 0) {
        return { cronExpression: null, error: 'Select at least one time' }
    }

    const parsed = specificTimes
        .map((t) => {
            const [h, m] = t.split(':').map(Number)
            return { hour: h, minute: m }
        })
        .sort((a, b) => a.hour * 60 + a.minute - (b.hour * 60 + b.minute))

    // Group by minute to produce compact cron expressions
    const byMinute = new Map<number, number[]>()
    for (const { hour, minute } of parsed) {
        const existing = byMinute.get(minute) ?? []
        existing.push(hour)
        byMinute.set(minute, existing)
    }

    // If all times are on the same minute (typical: all on :00), one expression
    if (byMinute.size === 1) {
        const [minute, hours] = [...byMinute.entries()][0]
        const cronExpression = `${minute} ${hours.join(',')} * * ${dayStr}`
        return { cronExpression }
    }

    // Multiple different minutes — use the most common minute for a single cron.
    // For simplicity, pick :00 times or the first minute group.
    const allOnHour = parsed.filter((p) => p.minute === 0)
    if (allOnHour.length === parsed.length) {
        const cronExpression = `0 ${allOnHour.map((p) => p.hour).join(',')} * * ${dayStr}`
        return { cronExpression }
    }

    // Multiple different minutes — generate one cron per minute group, joined by newlines.
    const cronParts: string[] = []
    for (const [minute, hours] of byMinute.entries()) {
        cronParts.push(
            `${minute} ${hours.sort((a, b) => a - b).join(',')} * * ${dayStr}`
        )
    }
    const cronExpression = cronParts.join('\n')
    return { cronExpression }
}

function buildIntervalCron(
    input: CronCalendarInput,
    dayStr: string
): CronResult {
    const { startTime, endTime, intervalMinutes } = input

    const startParts = startTime.split(':').map(Number)
    const endParts = endTime.split(':').map(Number)

    if (
        startParts.length < 2 ||
        endParts.length < 2 ||
        startParts.some(isNaN) ||
        endParts.some(isNaN)
    ) {
        return { cronExpression: null, error: 'Invalid time format' }
    }

    const [startHour, startMinute] = startParts
    const [endHour, endMinute] = endParts

    if (startMinute !== 0 || endMinute !== 0) {
        return { cronExpression: null, error: 'Times must be on the hour' }
    }

    if (startHour >= endHour) {
        return {
            cronExpression: null,
            error: 'Start time must be before end time',
        }
    }

    const minutes: number[] = []
    for (let m = 0; m < 60; m += intervalMinutes) {
        minutes.push(m)
    }

    const minuteStr = minutes.join(',')

    // For sub-hourly intervals, the end hour should only run at :00
    // e.g. "6am–6pm every 15min" → "0,15,30,45 6-17 * * days" + "0 18 * * days"
    if (intervalMinutes < 60 && startHour < endHour) {
        const mainRange = `${minuteStr} ${startHour}-${endHour - 1} * * ${dayStr}`
        const finalHour = `0 ${endHour} * * ${dayStr}`
        return { cronExpression: `${mainRange}\n${finalHour}` }
    }

    const hourStr = `${startHour}-${endHour}`
    const cronExpression = `${minuteStr} ${hourStr} * * ${dayStr}`

    return { cronExpression }
}

/**
 * Expand a single cron field (`*`, `a`, `a-b`, `a,b,c`, `a-b/s`, star/s)
 * into the concrete values it matches within [min, max]. Returns null when
 * the field contains anything unparseable.
 */
function expandCronField(
    raw: string,
    min: number,
    max: number
): number[] | null {
    const out = new Set<number>()
    for (const piece of raw.split(',')) {
        const [range, stepStr] = piece.split('/')
        const step = stepStr !== undefined ? Number(stepStr) : 1
        if (!Number.isInteger(step) || step < 1) return null
        let lo: number
        let hi: number
        if (range === '*') {
            lo = min
            hi = max
        } else if (range.includes('-')) {
            const [s, e] = range.split('-').map(Number)
            lo = s
            hi = e
        } else {
            lo = Number(range)
            hi = lo
        }
        if (Number.isNaN(lo) || Number.isNaN(hi) || lo < min || hi > max) {
            return null
        }
        for (let v = lo; v <= hi; v += step) out.add(v)
    }
    return [...out].sort((a, b) => a - b)
}

/**
 * Days-of-week from a cron field, expanding ranges and normalizing
 * NCRONTAB's `7` (Sunday) to `0`. Falls back to Mon–Fri when malformed.
 */
function expandDays(dayPart: string): number[] {
    // `*` means EVERY day, and the year calendar renders whatever this returns, so
    // reading it as Mon-Fri made a 7-day station show ~104 empty weekend days and a
    // weekday-only slot total (measured on a live `0 6 * * *` station, 2026-08-18:
    // "261 slots" for a clock that actually airs 365). The Mon-Fri default belongs to
    // DEFAULT_CALENDAR_CONFIG, for a schedule with NO cron yet - not to an expression
    // that explicitly says every day. Keeping the two apart also makes the day toggles
    // round-trip: all seven selected rebuilds as `*` instead of silently narrowing a
    // stored 7-day clock to `1,2,3,4,5` on the next weekly-layer save.
    if (dayPart === '*') return [0, 1, 2, 3, 4, 5, 6]
    const days = expandCronField(dayPart, 0, 7)
    if (!days || days.length === 0) return [1, 2, 3, 4, 5]
    return [...new Set(days.map((d) => (d === 7 ? 0 : d)))].sort(
        (a, b) => a - b
    )
}

/**
 * Normalize one cron line to the 5-field form the calendar model speaks.
 * The backend stores NCRONTAB (6 fields, seconds first) — drop the seconds.
 */
function normalizeCronTokens(line: string): string[] | null {
    const trimmed = line.trim()
    if (!trimmed) return null
    const tokens = trimmed.split(/\s+/)
    if (tokens.length < 5 || tokens.length > 6) return null
    return tokens.length === 6 ? tokens.slice(1) : tokens
}

export function parseCronToCalendar(cron: string | null): CronCalendarInput {
    const defaultInput: CronCalendarInput = {
        scheduleMode: 'interval',
        ...DEFAULT_CALENDAR_CONFIG,
        specificTimes: [],
    }

    if (!cron) return defaultInput

    // Handle multi-line cron
    if (cron.includes('\n')) {
        const lines = cron.split('\n').filter(Boolean)

        // Detect compound interval cron: "minuteStr startHour-(endHour-1) * * days\n0 endHour * * days"
        if (lines.length === 2) {
            const p1 = normalizeCronTokens(lines[0])
            const p2 = normalizeCronTokens(lines[1])
            if (
                p1 !== null &&
                p2 !== null &&
                p1[1].includes('-') &&
                /^\d+$/.test(p2[0]) &&
                Number(p2[0]) === 0 &&
                /^\d+$/.test(p2[1]) &&
                p1[0].includes(',')
            ) {
                const mins = p1[0].split(',').map(Number)
                const intervalMinutes = mins.length > 1 ? mins[1] - mins[0] : 60
                const [s] = p1[1].split('-').map(Number)
                const endHour = Number(p2[1])
                return {
                    scheduleMode: 'interval' as ScheduleMode,
                    daysOfWeek: expandDays(p1[4]),
                    startTime: `${s.toString().padStart(2, '0')}:00`,
                    endTime: `${endHour.toString().padStart(2, '0')}:00`,
                    intervalMinutes,
                    specificTimes: [],
                }
            }
        }

        // Mixed minute groups → specific times. The same HH:MM legitimately
        // appears on several lines (a weekday line plus Sat and Sun lines),
        // and the calendar model is a SET of times — collect it as one so the
        // field never carries repeats downstream.
        const allSpecificTimes = new Set<string>()
        const daysOfWeekSet = new Set<number>()

        for (const line of lines) {
            const lineParts = normalizeCronTokens(line)
            if (lineParts === null) continue
            const [minPart, hourPart, , , dayPart] = lineParts
            for (const d of expandDays(dayPart)) daysOfWeekSet.add(d)
            const minute = Number(minPart)
            const hours = expandCronField(hourPart, 0, 23) ?? []
            if (Number.isNaN(minute)) continue
            for (const h of hours) {
                allSpecificTimes.add(
                    `${h.toString().padStart(2, '0')}:${minute.toString().padStart(2, '0')}`
                )
            }
        }

        const daysOfWeek = [...daysOfWeekSet].sort((a, b) => a - b)

        return {
            scheduleMode: 'specific',
            daysOfWeek: daysOfWeek.length > 0 ? daysOfWeek : [1, 2, 3, 4, 5],
            startTime: '06:00',
            endTime: '18:00',
            intervalMinutes: 60,
            specificTimes: [...allSpecificTimes].sort(),
        }
    }

    const parts = normalizeCronTokens(cron)
    if (parts === null) return defaultInput

    const [minPart, hourPart, , , dayPart] = parts

    const daysOfWeek = expandDays(dayPart)

    // Specific-times pattern: a single fixed minute. Hour ranges count too
    // (NCRONTAB like "30 6-9" means 6:30, 7:30, 8:30, 9:30) — EXCEPT
    // minute 0 with an hour range, which is the builder's hourly-interval
    // form ("0 6-18" = every 60 min from 6:00 to 18:00) and must round-trip
    // back to interval mode.
    const isSpecificPattern =
        /^\d+$/.test(minPart) &&
        !(Number(minPart) === 0 && hourPart.includes('-'))

    if (isSpecificPattern) {
        const minute = Number(minPart)
        const hours = expandCronField(hourPart, 0, 23) ?? []
        const specificTimes = hours.map(
            (h) =>
                `${h.toString().padStart(2, '0')}:${minute.toString().padStart(2, '0')}`
        )
        return {
            scheduleMode: 'specific',
            daysOfWeek,
            startTime: '06:00',
            endTime: '18:00',
            intervalMinutes: 60,
            specificTimes,
        }
    }

    // Interval mode parsing
    let intervalMinutes = 15
    if (minPart.includes(',')) {
        const mins = minPart.split(',').map(Number)
        if (mins.length > 1) {
            intervalMinutes = mins[1] - mins[0]
        }
    } else if (minPart.includes('/')) {
        intervalMinutes = Number(minPart.split('/')[1])
    } else if (/^\d+$/.test(minPart)) {
        intervalMinutes = 60
    }

    let startTime = '06:00'
    let endTime = '18:00'
    if (hourPart.includes('-') || hourPart.includes(',')) {
        // Handle comma-separated ranges like "6-9,15-18": find min start, max end.
        const segments = hourPart.split(',')
        let minHour = 24
        let maxHour = -1
        for (const seg of segments) {
            if (seg.includes('-')) {
                const [a, b] = seg.split('-').map(Number)
                if (!Number.isNaN(a)) minHour = Math.min(minHour, a)
                if (!Number.isNaN(b)) maxHour = Math.max(maxHour, b)
            } else {
                const h = Number(seg)
                if (!Number.isNaN(h)) {
                    minHour = Math.min(minHour, h)
                    maxHour = Math.max(maxHour, h)
                }
            }
        }
        if (minHour < 24 && maxHour >= 0) {
            startTime = `${minHour.toString().padStart(2, '0')}:00`
            endTime = `${maxHour.toString().padStart(2, '0')}:00`
        }
    }

    return {
        scheduleMode: 'interval',
        daysOfWeek,
        startTime,
        endTime,
        intervalMinutes,
        specificTimes: [],
    }
}

/**
 * The concrete (day, minute-of-day) run slots a cron expression schedules,
 * expanded to first-class values. Day is 0-6 (Sun-Sat); the minute-of-day is
 * hours*60+minutes. Returns null when any line is unparseable.
 */
function cronRunSlots(cron: string): Set<string> | null {
    const slots = new Set<string>()
    for (const line of cron.split('\n')) {
        const tokens = normalizeCronTokens(line)
        if (tokens === null) return null
        const [minPart, hourPart, , , dayPart] = tokens
        const minutes = expandCronField(minPart, 0, 59)
        const hours = expandCronField(hourPart, 0, 23)
        const days = expandDays(dayPart)
        if (minutes === null || hours === null) return null
        for (const m of minutes) {
            for (const h of hours) {
                for (const d of days) {
                    slots.add(`${d}:${h * 60 + m}`)
                }
            }
        }
    }
    return slots
}

/**
 * Run times a cron schedules, grouped by day of week (0 = Sunday) and sorted.
 * Times are minutes-past-midnight in the station's own wall clock.
 *
 * Pure integer arithmetic over the parsed fields. The alternative, stepping a
 * candidate minute at a time and asking `Intl.DateTimeFormat` what day it landed
 * on, cost 1,166 ms for ONE day of a real 60-line Sydney cron and 6.8 s to project
 * a year. This is the same answer in microseconds.
 *
 * It does NOT reuse `expandDays`, which reads a `*` day field as Mon-Fri. That is
 * the right default for the editor's day toggles, where `*` means "the operator has
 * not chosen", but it is simply wrong for counting: `0 6 * * *` fires every day of
 * the week, weekends included.
 *
 * Day-of-month and month restrictions are ignored: every clock this drives is a
 * day-of-week pattern, and a date-scoped clock is a calendar assignment instead.
 * Returns null when any line is unparseable.
 */
export function cronSlotsByWeekday(
    cron: string
): readonly (readonly number[])[] | null {
    const perDay: Set<number>[] = [
        new Set(),
        new Set(),
        new Set(),
        new Set(),
        new Set(),
        new Set(),
        new Set(),
    ]

    for (const line of cron.split('\n')) {
        const tokens = normalizeCronTokens(line)
        if (tokens === null) return null
        const [minPart, hourPart, , , dayPart] = tokens
        const minutes = expandCronField(minPart!, 0, 59)
        const hours = expandCronField(hourPart!, 0, 23)
        if (minutes === null || hours === null) return null

        const rawDays =
            dayPart === '*'
                ? [0, 1, 2, 3, 4, 5, 6]
                : expandCronField(dayPart!, 0, 7)
        if (rawDays === null) return null
        const days = [...new Set(rawDays.map((d) => (d === 7 ? 0 : d)))]

        for (const day of days)
            for (const hour of hours)
                for (const minute of minutes)
                    perDay[day]!.add(hour * 60 + minute)
    }

    return perDay.map((minutes) => [...minutes].sort((a, b) => a - b))
}

const LOSS_DAY_NAMES = [
    'Sundays',
    'Mondays',
    'Tuesdays',
    'Wednesdays',
    'Thursdays',
    'Fridays',
    'Saturdays',
]

function lossDayGroupLabel(days: number[]): string {
    const sorted = [...days].sort((a, b) => a - b)
    const key = sorted.join(',')
    if (key === '0,1,2,3,4,5,6') return 'every day'
    if (key === '1,2,3,4,5') return 'weekdays'
    if (key === '0,6') return 'weekends'
    if (sorted.length === 1) return LOSS_DAY_NAMES[sorted[0]]
    return sorted.map((d) => LOSS_DAY_NAMES[d]).join(' and ')
}

function lossTimeLabel(minuteOfDay: number): string {
    const h = Math.floor(minuteOfDay / 60)
    const m = minuteOfDay % 60
    return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`
}

/**
 * Group days by the run times they carry, largest group first, so a summary
 * like "weekdays 5 times (…); Saturdays 3 times (…)" reads in a stable order.
 */
function lossDayRunGroups(
    slots: Set<string>
): Array<{ days: number[]; minutes: number[] }> {
    const byDay = new Map<number, number[]>()
    for (const slot of slots) {
        const [dayStr, minuteStr] = slot.split(':')
        const day = Number(dayStr)
        const minute = Number(minuteStr)
        const existing = byDay.get(day) ?? []
        existing.push(minute)
        byDay.set(day, existing)
    }
    const groups = new Map<string, { days: number[]; minutes: number[] }>()
    for (const [day, minutes] of byDay) {
        const key = [...minutes].sort((a, b) => a - b).join(',')
        const group =
            groups.get(key) ??
            ({ days: [], minutes: [...minutes].sort((a, b) => a - b) } as {
                days: number[]
                minutes: number[]
            })
        group.days.push(day)
        groups.set(key, group)
    }
    return [...groups.values()].sort(
        (a, b) =>
            b.days.length - a.days.length || a.minutes.length - b.minutes.length
    )
}

function lossFormatGroup(group: { days: number[]; minutes: number[] }): string {
    const times = group.minutes.map(lossTimeLabel)
    const timeList =
        times.length <= 5
            ? times.join(', ')
            : `${times.slice(0, 5).join(', ')}, and ${times.length - 5} more`
    const runWord = group.minutes.length === 1 ? 'time' : 'times'
    return `${lossDayGroupLabel(group.days)} ${group.minutes.length} ${runWord} (${timeList})`
}

export interface CronLossReport {
    /** True when the editor's rebuild of the stored cron changes its run slots. */
    readonly lossy: boolean
    /**
     * Plain-language description of what saving would change, empty when not
     * lossy. Used as the confirmation dialog body.
     */
    readonly summary: string
}

/**
 * Detect whether a stored cron carries information the calendar editor cannot
 * represent. The model is a SET of times x a UNION of days, so a day-specific
 * compound (weekday times on `1-5` plus different weekend times on `6,0`)
 * parses to one merged time list and re-serializes as every time on every
 * day. Rather than enumerate cron shapes, compare the run slots the stored
 * expression schedules against the run slots the editor's rebuild would
 * produce. Any difference is loss, which automatically covers future shapes.
 */
export function detectCronLoss(cron: string | null): CronLossReport {
    if (!cron) return { lossy: false, summary: '' }
    const storedSlots = cronRunSlots(cron)
    if (storedSlots === null) return { lossy: false, summary: '' }
    const rebuilt = buildCronFromCalendar(parseCronToCalendar(cron))
    if (!rebuilt.cronExpression) return { lossy: false, summary: '' }
    const flattenedSlots = cronRunSlots(rebuilt.cronExpression)
    if (flattenedSlots === null) return { lossy: false, summary: '' }

    if (
        storedSlots.size === flattenedSlots.size &&
        [...storedSlots].every((s) => flattenedSlots.has(s))
    ) {
        return { lossy: false, summary: '' }
    }

    const storedDesc = lossDayRunGroups(storedSlots)
        .map(lossFormatGroup)
        .join('; ')
    // The flattened rebuild runs every time on every selected day, so its run
    // groups always collapse to a single all-days group.
    const flatGroup = lossDayRunGroups(flattenedSlots)[0]
    const flatTimes = flatGroup.minutes.map(lossTimeLabel)
    const flatList =
        flatTimes.length <= 5
            ? flatTimes.join(', ')
            : `${flatTimes.slice(0, 5).join(', ')}, and ${
                  flatTimes.length - 5
              } more`
    const flatRunWord = flatGroup.minutes.length === 1 ? 'time' : 'times'
    const flattenedDesc = `every selected day ${flatGroup.minutes.length} ${flatRunWord} (${flatList})`

    const summary = [
        `This station runs day-specific times: ${storedDesc}.`,
        'The editor shows one set of times for all days, so saving replaces the day-specific pattern with one that runs every selected time on every selected day:',
        `${flattenedDesc}.`,
        'The day-specific pattern is replaced permanently and cannot be undone.',
    ].join(' ')
    return { lossy: true, summary }
}
