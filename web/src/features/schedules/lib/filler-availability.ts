import { format, parseISO } from 'date-fns'
import type { FillerAvailabilityEntry } from '../api/filler-statements-hooks'

// =============================================================================
// Filler availability — pure, React-free helpers shared by the manager rows and
// the all-ineligible warning. The eligibility rules here MUST mirror the backend
// (FillerAvailability.IsActiveAt + TimeWindowOverlap.Contains): inclusive date
// bounds, a day-of-week set, a half-open [start, end) time window that wraps past
// midnight when end <= start, all evaluated in STATION-local time. Kept separate
// from the component so the subtle wrap logic is unit-testable in isolation.
// =============================================================================

export interface StationLocalNow {
    /** yyyy-MM-dd — lexicographic compare is chronological. */
    readonly date: string
    /** HH:mm — lexicographic compare is chronological. */
    readonly time: string
    /** Full .NET day name ("Monday".."Sunday") — the wire representation of Days. */
    readonly dayName: string
}

const DAY_ORDER: readonly string[] = [
    'Monday',
    'Tuesday',
    'Wednesday',
    'Thursday',
    'Friday',
    'Saturday',
    'Sunday',
]
const DAY_ABBREV: Record<string, string> = {
    Monday: 'Mon',
    Tuesday: 'Tue',
    Wednesday: 'Wed',
    Thursday: 'Thu',
    Friday: 'Fri',
    Saturday: 'Sat',
    Sunday: 'Sun',
}

/**
 * The station-local date/time/day at this instant, resolved via Intl for
 * <paramref name="timezone" />. An absent or unresolvable timezone degrades to
 * UTC — the exact fallback the backend uses (TimeZoneHelper.GetLocalDateAndTime)
 * — so the UI's "all statements currently ineligible" warning agrees with what
 * the backend would actually do at airtime.
 */
export function getStationLocalNow(timezone?: string | null): StationLocalNow {
    const formatIn = (tz: string): StationLocalNow | null => {
        try {
            const parts = new Intl.DateTimeFormat('en-US', {
                timeZone: tz,
                year: 'numeric',
                month: '2-digit',
                day: '2-digit',
                hour: '2-digit',
                minute: '2-digit',
                weekday: 'long',
                hourCycle: 'h23',
            }).formatToParts(new Date())
            const byType = new Map(parts.map((p) => [p.type, p.value]))
            const two = (v: string | undefined): string =>
                (v ?? '').padStart(2, '0')
            return {
                date: `${byType.get('year')}-${two(byType.get('month'))}-${two(byType.get('day'))}`,
                time: `${two(byType.get('hour'))}:${two(byType.get('minute'))}`,
                dayName: byType.get('weekday') ?? '',
            }
        } catch {
            return null // unknown IANA id — fall through to UTC
        }
    }

    const now = new Date()
    const utcFallback: StationLocalNow = {
        date: now.toISOString().slice(0, 10),
        time: now.toISOString().slice(11, 16),
        dayName: new Intl.DateTimeFormat('en-US', {
            timeZone: 'UTC',
            weekday: 'long',
        }).format(now),
    }
    return (timezone ? formatIn(timezone) : null) ?? utcFallback
}

/**
 * True when the statement is eligible at <paramref name="now" />. Null
 * availability (no window) is ALWAYS eligible — the default every existing
 * statement carries. Mirrors the backend's FillerAvailability.IsActiveAt.
 */
export function isEligibleNow(
    availability: FillerAvailabilityEntry | null,
    now: StationLocalNow
): boolean {
    if (!availability) return true
    if (availability.startDate && now.date < availability.startDate)
        return false
    if (availability.endDate && now.date > availability.endDate) return false
    if (
        availability.days &&
        availability.days.length > 0 &&
        !availability.days.includes(now.dayName)
    )
        return false
    const { startLocal, endLocal } = availability
    if (startLocal && endLocal) {
        // Half-open [start, end); end <= start wraps past midnight. Matches
        // TimeWindowOverlap.Contains: inactive iff before start (non-wrap) or at/after end.
        const wraps = startLocal > endLocal
        const beforeStart = now.time < startLocal
        const atOrAfterEnd = now.time >= endLocal
        if (wraps ? beforeStart && atOrAfterEnd : beforeStart || atOrAfterEnd)
            return false
    }
    return true
}

/**
 * Client-side validation for an availability draft, mirroring the backend's
 * FillerAvailability.Create rejections. Returns a human message, or null when valid.
 */
export function availabilityWindowError(
    draft: FillerAvailabilityEntry
): string | null {
    if (draft.startDate && draft.endDate && draft.endDate < draft.startDate)
        return 'End date is before the start date.'
    const hasStart = Boolean(draft.startLocal)
    const hasEnd = Boolean(draft.endLocal)
    if (hasStart !== hasEnd) return 'Set both a start and end time, or neither.'
    if (hasStart && draft.startLocal === draft.endLocal)
        return 'Start and end time must differ.'
    return null
}

/** "Every day" / "Weekdays" / "Weekends" / "Mon, Wed, Fri" from the wire day names. */
export function summariseDays(
    days: readonly string[] | null | undefined
): string {
    if (!days || days.length === 0) return 'Every day'
    if (days.length === 7) return 'Every day' // only 7 names exist, so all of them
    const weekdays = DAY_ORDER.slice(0, 5)
    if (weekdays.every((d) => days.includes(d)) && days.length === 5)
        return 'Weekdays'
    if (
        days.includes('Saturday') &&
        days.includes('Sunday') &&
        days.length === 2
    )
        return 'Weekends'
    return DAY_ORDER.filter((d) => days.includes(d))
        .map((d) => DAY_ABBREV[d])
        .join(', ')
}

/**
 * Compact one-line summary for a row with a window set, e.g.
 * "Weekdays 06:00-09:00, until 30 Nov". Empty string when no window is set.
 */
export function summariseAvailability(
    availability: FillerAvailabilityEntry | null
): string {
    if (!availability) return ''
    // Days and the time range read as ONE phrase ("Weekdays 06:00-09:00"), so they are
    // space-joined; only the date clauses after them are comma-separated. Joining every
    // part with ", " produced "Weekdays, 06:00-09:00", which reads as two separate rules.
    const days = summariseDays(availability.days)
    const when = [
        days === 'Every day' ? '' : days,
        availability.startLocal && availability.endLocal
            ? `${availability.startLocal}-${availability.endLocal}`
            : '',
    ]
        .filter(Boolean)
        .join(' ')

    const parts: string[] = []
    if (when) parts.push(when)
    if (availability.startDate)
        parts.push(`from ${renderAvailabilityDate(availability.startDate)}`)
    if (availability.endDate)
        parts.push(`until ${renderAvailabilityDate(availability.endDate)}`)
    return parts.join(', ')
}

/**
 * "30 Nov" from an ISO yyyy-MM-dd string. date-fns format throws on an invalid
 * date, so a malformed value degrades to the raw string instead of crashing the row.
 */
function renderAvailabilityDate(iso: string): string {
    const parsed = parseISO(iso)
    return Number.isNaN(parsed.getTime()) ? iso : format(parsed, 'd MMM')
}
