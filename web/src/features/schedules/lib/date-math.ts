/** Cron field type — either wildcard (`all`) or explicit values. */
export interface CronField {
    readonly type: 'all' | 'set'
    readonly values: readonly number[]
}

/** Resolved date parts in a specific timezone. */
export interface TzDateParts {
    year: number
    month: number
    day: number
    hour: number
    minute: number
    weekday: number // 0=Sun, 6=Sat
}

/** Return the smallest value from a cron field (0 for `all`). */
export function firstValue(field: CronField): number {
    return field.type === 'all' ? 0 : field.values[0]
}

/** Number of days in a given year/month. */
export function daysInMonth(year: number, month: number): number {
    // Date's month is 0-indexed; day 0 = last day of previous month
    return new Date(year, month, 0).getDate()
}

function daysInYear(year: number): number {
    let total = 0
    for (let month = 1; month <= 12; month++) {
        total += daysInMonth(year, month)
    }
    return total
}

function daysBeforeYear(year: number): number {
    const baselineYear = 2000
    let total = 0
    if (year >= baselineYear) {
        for (
            let currentYear = baselineYear;
            currentYear < year;
            currentYear++
        ) {
            total += daysInYear(currentYear)
        }
    } else {
        for (
            let currentYear = baselineYear - 1;
            currentYear >= year;
            currentYear--
        ) {
            total -= daysInYear(currentYear)
        }
    }
    return total
}

function daysBeforeMonth(year: number, month: number): number {
    let total = 0
    for (let currentMonth = 1; currentMonth < month; currentMonth++) {
        total += daysInMonth(year, currentMonth)
    }
    return total
}

/** Convert TzDateParts to total local minutes (for delta computations). */
export function toLocalMinutes(p: TzDateParts): number {
    return (
        (daysBeforeYear(p.year) +
            daysBeforeMonth(p.year, p.month) +
            p.day -
            1) *
            1440 +
        p.hour * 60 +
        p.minute
    )
}

/**
 * Given a candidate UTC date and its tz-resolved parts, compute a new UTC Date
 * whose tz parts are the target (year, month, day, hour, minute).
 *
 * Works by computing the delta in LOCAL minutes and applying it to the
 * candidate's UTC timestamp. Small DST-induced errors (±1h) are corrected by
 * the caller's verification loop on the next iteration.
 */
export function tzDateFrom(
    date: Date,
    parts: TzDateParts,
    year: number,
    month: number,
    day: number,
    hour: number,
    minute: number
): Date {
    const currentLocalMin = toLocalMinutes(parts)
    const targetLocalMin =
        (daysBeforeYear(year) + daysBeforeMonth(year, month) + day - 1) * 1440 +
        hour * 60 +
        minute
    return new Date(
        date.getTime() + (targetLocalMin - currentLocalMin) * 60_000
    )
}
