/**
 * Enumerate future occurrences from a standard 5-field cron expression + timezone.
 *
 * Supports standard 5-field cron (minute hour dom month dow):
 *   - `*` | specific values | lists (`,`)
 *   - ranges (`-`) with optional step (`/`)
 *   - multi-line crons (each line is OR'd)
 *   - day-of-week 0=Sunday, 7 also accepted as Sunday
 *
 * Timezone-aware: uses Intl.DateTimeFormat to resolve date parts
 * in the target timezone before matching cron fields.
 *
 * No npm dependencies beyond what's already in the project.
 */
import {
    CronField,
    TzDateParts,
    firstValue,
    daysInMonth,
    tzDateFrom,
} from './date-math'

interface ParsedLine {
    minute: CronField
    hour: CronField
    dayOfMonth: CronField
    month: CronField
    dayOfWeek: CronField
}

function parseField(raw: string, min: number, max: number): CronField {
    if (raw === '*') return { type: 'all', values: [] }

    const set = new Set<number>()

    for (const piece of raw.split(',')) {
        const parts = piece.split('/')
        const range = parts[0]
        const step = parts.length > 1 ? parseInt(parts[1], 10) : 1

        let rMin: number
        let rMax: number

        if (range === '*') {
            rMin = min
            rMax = max
        } else if (range.includes('-')) {
            const [s, e] = range.split('-').map(Number)
            rMin = s
            rMax = e
        } else {
            rMin = parseInt(range, 10)
            rMax = rMin
        }

        for (let v = rMin; v <= rMax; v += step) {
            set.add(v)
        }
    }

    const values = [...set].sort((a, b) => a - b)

    // If the set covers the entire range it's effectively `all`
    if (values.length === max - min + 1) {
        return { type: 'all', values: [] }
    }

    return { type: 'set', values }
}

function matches(field: CronField, value: number): boolean {
    return field.type === 'all' || field.values.includes(value)
}

/** Longest a month can be, by month number (Feb counts its leap-year 29). */
const MAX_DAYS_IN_MONTH = [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]

function parseLine(line: string): ParsedLine | null {
    let parts = line.trim().split(/\s+/)
    if (parts.length < 5) return null
    // NCRONTAB (6 fields, seconds first): drop the seconds field
    if (parts.length >= 6) parts = parts.slice(1, 6)

    const parsed: ParsedLine = {
        minute: parseField(parts[0], 0, 59),
        hour: parseField(parts[1], 0, 23),
        dayOfMonth: parseField(parts[2], 1, 31),
        month: parseField(parts[3], 1, 12),
        dayOfWeek: parseField(parts[4], 0, 7), // accept 7 for Sunday
    }

    // A day-of-month constraint that exceeds every allowed month's maximum
    // length can NEVER match (e.g. `0 8 31 2 *`, Feb 31). Treat it like any
    // other unparseable line: the alternative is an enumerator that scans
    // years of calendar, one bounded firstInMonth walk per step, to discover
    // "never". (dom/dow OR-semantics: a dow constraint is satisfiable within
    // a week, so only dom-only lines can be infeasible.)
    if (parsed.dayOfMonth.type === 'set' && parsed.dayOfWeek.type === 'all') {
        const months =
            parsed.month.type === 'all'
                ? MAX_DAYS_IN_MONTH
                : parsed.month.values.map((m) => MAX_DAYS_IN_MONTH[m - 1])
        const feasible = parsed.dayOfMonth.values.some((d) =>
            months.some((dim) => d <= dim)
        )
        if (!feasible) return null
    }

    return parsed
}

// One formatter per timezone, cached for the module's lifetime. Constructing
// Intl.DateTimeFormat is 10-50x the cost of formatToParts, and the enumeration
// loop below resolves parts once per candidate minute — an uncached formatter
// made the on-air board's lane build the single biggest main-thread task in
// the app (Lighthouse TBT breach, CI run 2755).
const TZ_FORMATTER_CACHE = new Map<string, Intl.DateTimeFormat>()

function tzFormatter(timezone: string): Intl.DateTimeFormat {
    const key = timezone || 'UTC'
    let fmt = TZ_FORMATTER_CACHE.get(key)
    if (!fmt) {
        // en-US + hour12:false gives clean 24h numeric parts for parseInt.
        fmt = new Intl.DateTimeFormat('en-US', {
            timeZone: key,
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            weekday: 'short',
            hour12: false,
        })
        TZ_FORMATTER_CACHE.set(key, fmt)
    }
    return fmt
}

/**
 * Resolve a UTC Date's components in a given IANA timezone string.
 */
function getTzParts(date: Date, timezone: string): TzDateParts {
    const fmt = tzFormatter(timezone)
    const raw: Record<string, string> = {}
    for (const p of fmt.formatToParts(date)) {
        if (p.type !== 'literal') raw[p.type] = p.value
    }

    const weekdayMap: Record<string, number> = {
        sun: 0,
        mon: 1,
        tue: 2,
        wed: 3,
        thu: 4,
        fri: 5,
        sat: 6,
    }

    return {
        year: parseInt(raw.year!, 10),
        month: parseInt(raw.month!, 10),
        day: parseInt(raw.day!, 10),
        hour: parseInt(raw.hour!, 10),
        minute: parseInt(raw.minute!, 10),
        weekday: weekdayMap[raw.weekday?.toLowerCase() ?? ''] ?? -1,
    }
}

function matchesLine(line: ParsedLine, parts: TzDateParts): boolean {
    const { minute, hour, dayOfMonth, month, dayOfWeek } = line

    if (!matches(minute, parts.minute)) return false
    if (!matches(hour, parts.hour)) return false
    if (!matches(month, parts.month)) return false

    // Day-of-week: normalize cron's 7 to 0 (Sunday)
    const dowValue = parts.weekday
    const dowSet =
        dayOfWeek.type === 'set'
            ? dayOfWeek.values.map((d) => (d === 7 ? 0 : d))
            : null
    const dowOk =
        dayOfWeek.type === 'all' ||
        (dowSet !== null && dowSet.includes(dowValue))
    const domOk = matches(dayOfMonth, parts.day)

    // Standard cron: when both dayOfMonth and dayOfWeek are restricted,
    // the day matches if EITHER matches.
    const domRestricted = dayOfMonth.type !== 'all'
    const dowRestricted = dayOfWeek.type !== 'all'

    if (domRestricted && dowRestricted) {
        if (!domOk && !dowOk) return false
    } else if (domRestricted) {
        if (!domOk) return false
    } else {
        if (!dowOk) return false
    }

    return true
}
/**
 * Return up to `count` future Date objects matching the cron expression
 * in the given IANA timezone string.
 *
 * Starts scanning from `from ?? new Date()` + 1 minute.
 * Stops early when `until` is given and the candidate walks past it — a
 * caller enumerating a bounded window (the on-air board's lookback) must not
 * pay for occurrences it will discard.
 * Uses skip-ahead to avoid minute-by-minute iteration: when a field doesn't
 * match, it jumps directly to the next value that any parsed cron line
 * could match, reducing worst-case from ~525k to a few thousand steps.
 */
export function getNextCronOccurrences(
    cronExpression: string,
    timezone: string,
    count: number,
    from?: Date,
    until?: Date
): Date[] {
    if (!cronExpression) return []

    const lines = cronExpression.split('\n').filter(Boolean)
    const parsed: ParsedLine[] = []

    for (const lineStr of lines) {
        const pl = parseLine(lineStr)
        if (pl) parsed.push(pl)
    }

    if (parsed.length === 0) return []

    const MINUTE_MS = 60_000

    const now = from ?? new Date()
    // ABSOLUTE-time arithmetic, never `setMinutes`/`setSeconds`. Those mutate the
    // WALL CLOCK of the process's own zone, which is not this function's
    // `timezone` argument and is an Australian zone in CI, in tests
    // (`TZ=Australia/Sydney`) and in an ARN operator's browser. On the April
    // fallback the repeated 02:00-02:59 hour made `+1 minute` land back inside
    // the fold, so the candidate stopped advancing in real time and enumeration
    // spun on 02:00 until the step cap — an hourly Sydney clock reported 1,500
    // occurrences on that one day instead of 25. Milliseconds have no zone.
    const candidate = new Date(
        Math.floor(now.getTime() / MINUTE_MS) * MINUTE_MS + MINUTE_MS
    )

    const results: Date[] = []
    const MAX_STEPS = 5000 // generous safety cap — normal crons use <<100

    for (let steps = 0; steps < MAX_STEPS && results.length < count; steps++) {
        if (until && candidate.getTime() > until.getTime()) break
        const parts = getTzParts(candidate, timezone)

        let matched = false
        for (const line of parsed) {
            if (matchesLine(line, parts)) {
                results.push(new Date(candidate.getTime()))
                matched = true
                break
            }
        }

        if (matched) {
            candidate.setTime(candidate.getTime() + MINUTE_MS)
        } else {
            const next = findEarliestNext(candidate, parsed, timezone)
            if (next !== null && next.getTime() > candidate.getTime()) {
                candidate.setTime(next.getTime())
            } else {
                candidate.setTime(candidate.getTime() + MINUTE_MS)
            }
        }
    }

    return results
}

// ---------------------------------------------------------------------------
// Skip-ahead helpers
// ---------------------------------------------------------------------------

/**
 * Check whether a single cron line's day-of-month/day-of-week accepts the
 * given tz-resolved date parts (dom/dow OR semantics).
 */
function matchesDay(line: ParsedLine, parts: TzDateParts): boolean {
    const dowSet =
        line.dayOfWeek.type === 'set'
            ? line.dayOfWeek.values.map((d) => (d === 7 ? 0 : d))
            : null
    const dowOk =
        line.dayOfWeek.type === 'all' ||
        (dowSet !== null && dowSet.includes(parts.weekday))
    const domOk = matches(line.dayOfMonth, parts.day)

    const domRestricted = line.dayOfMonth.type !== 'all'
    const dowRestricted = line.dayOfWeek.type !== 'all'

    if (domRestricted && dowRestricted) return domOk || dowOk
    if (domRestricted) return domOk
    return dowOk
}

/**
 * Find the earliest Date (strictly after `candidate`) that any parsed line
 * matches. Returns `null` only if no line can produce a next candidate
 * (should not happen in practice).
 */
function findEarliestNext(
    candidate: Date,
    lines: ParsedLine[],
    timezone: string
): Date | null {
    let earliest: Date | null = null

    for (const line of lines) {
        const next = nextForLine(candidate, line, timezone)
        if (earliest === null || next.getTime() < earliest.getTime()) {
            earliest = next
        }
    }

    return earliest
}

/**
 * Given a candidate UTC Date and its tz parts that do NOT match `line`,
 * advance field-by-field (month → day → hour → minute) to the next
 * datetime that DOES match `line`.
 *
 * The loop recomputes tz parts after each advance, naturally handling
 * DST boundary crossings.
 */
function nextForLine(
    candidate: Date,
    line: ParsedLine,
    timezone: string
): Date {
    const date = new Date(candidate.getTime())

    for (let i = 0; i < 30; i++) {
        const p = getTzParts(date, timezone)

        // --- Month ---
        if (line.month.type !== 'all' && !line.month.values.includes(p.month)) {
            const nextM = line.month.values.find((v) => v > p.month)
            if (nextM !== undefined) {
                return firstInMonth(date, p, line, p.year, nextM, timezone)
            }
            // No matching month left this year — jump to next year
            return firstInMonth(
                date,
                p,
                line,
                p.year + 1,
                line.month.values[0],
                timezone
            )
        }

        // --- Day (dom / dow OR-semantics) ---
        if (!matchesDay(line, p)) {
            const dim = daysInMonth(p.year, p.month)
            // Scan remaining days in this month
            for (let d = p.day + 1; d <= dim; d++) {
                const test = tzDateFrom(
                    date,
                    p,
                    p.year,
                    p.month,
                    d,
                    firstValue(line.hour),
                    firstValue(line.minute)
                )
                const tp = getTzParts(test, timezone)
                if (tp.year !== p.year || tp.month !== p.month) break
                if (matchesDay(line, tp)) {
                    date.setTime(test.getTime())
                    // Break to outer loop (re-check from month)
                    break
                }
            }
            // If date didn't advance, no matching day this month — advance month
            if (getTzParts(date, timezone).day <= p.day) {
                const nextM =
                    line.month.type === 'all'
                        ? p.month + 1
                        : (line.month.values.find((v) => v > p.month) ??
                          line.month.values[0])
                const targetYear =
                    nextM > 12 ||
                    (line.month.type !== 'all' && nextM <= p.month)
                        ? p.year + 1
                        : p.year
                const targetMonth =
                    nextM > 12
                        ? line.month.type === 'all'
                            ? 1
                            : line.month.values[0]
                        : nextM
                return firstInMonth(
                    date,
                    p,
                    line,
                    targetYear,
                    targetMonth,
                    timezone
                )
            }
            continue
        }

        // --- Hour ---
        if (line.hour.type !== 'all' && !line.hour.values.includes(p.hour)) {
            const nextH = line.hour.values.find((v) => v > p.hour)
            if (nextH !== undefined) {
                date.setTime(
                    tzDateFrom(
                        date,
                        p,
                        p.year,
                        p.month,
                        p.day,
                        nextH,
                        firstValue(line.minute)
                    ).getTime()
                )
            } else {
                // No more matching hours today — advance to next day
                date.setTime(
                    tzDateFrom(
                        date,
                        p,
                        p.year,
                        p.month,
                        p.day + 1,
                        firstValue(line.hour),
                        firstValue(line.minute)
                    ).getTime()
                )
            }
            continue
        }

        // --- Minute ---
        if (
            line.minute.type !== 'all' &&
            !line.minute.values.includes(p.minute)
        ) {
            const nextMin = line.minute.values.find((v) => v > p.minute)
            if (nextMin !== undefined) {
                date.setTime(
                    tzDateFrom(
                        date,
                        p,
                        p.year,
                        p.month,
                        p.day,
                        p.hour,
                        nextMin
                    ).getTime()
                )
            } else {
                // No more matching minutes this hour — advance to next matching hour
                const nextH =
                    line.hour.type === 'all'
                        ? p.hour + 1
                        : (line.hour.values.find((v) => v > p.hour) ??
                          firstValue(line.hour))
                if (nextH < 24 && nextH > p.hour) {
                    date.setTime(
                        tzDateFrom(
                            date,
                            p,
                            p.year,
                            p.month,
                            p.day,
                            nextH,
                            firstValue(line.minute)
                        ).getTime()
                    )
                } else {
                    // Advance to next day
                    date.setTime(
                        tzDateFrom(
                            date,
                            p,
                            p.year,
                            p.month,
                            p.day + 1,
                            firstValue(line.hour),
                            firstValue(line.minute)
                        ).getTime()
                    )
                }
            }
            continue
        }

        // All fields match — return this candidate
        return date
    }

    // Safety fallback
    return new Date(date.getTime() + 60_000)
}

/**
 * Find the first day at or after `targetMonth`/`targetYear` that satisfies the
 * line's day-of-month / day-of-week constraints, scanning forward month by
 * month.
 *
 * The scan is BOUNDED: any satisfiable constraint matches within 8 years (a
 * dow constraint within a week; dom 29/30/31 and Feb-29 within the leap
 * cycle). The bound exists because this used to recurse unboundedly, so an
 * unsatisfiable line (`0 8 31 2 *`) — or corrupted date math — blew the call
 * stack instead of degrading. That is exactly how the e2e frozen-clock shim's
 * one-argument Date constructor (which made daysInMonth report 1 day for
 * every month) took down the whole review page (2026-08-06).
 */
function firstInMonth(
    date: Date,
    parts: TzDateParts,
    line: ParsedLine,
    year: number,
    month: number,
    timezone: string
): Date {
    let y = year
    let m = month
    for (let i = 0; i < 96; i++) {
        const dim = daysInMonth(y, m)
        for (let d = 1; d <= dim; d++) {
            const test = tzDateFrom(
                date,
                parts,
                y,
                m,
                d,
                firstValue(line.hour),
                firstValue(line.minute)
            )
            const tp = getTzParts(test, timezone)
            if (tp.year !== y || tp.month !== m) continue
            if (matchesDay(line, tp)) return test
        }

        // No matching day in this month — advance to the next matching month
        const nextM =
            line.month.type === 'all'
                ? m + 1
                : (line.month.values.find((v) => v > m) ?? line.month.values[0])
        if (nextM > 12 || (line.month.type !== 'all' && nextM <= m)) {
            y += 1
            m = line.month.type === 'all' ? 1 : line.month.values[0]
        } else {
            m = nextM
        }
    }

    // Safety fallback for an unsatisfiable line, mirroring nextForLine's: the
    // enumerator's own step cap then bounds the walk.
    return new Date(date.getTime() + 60_000)
}
