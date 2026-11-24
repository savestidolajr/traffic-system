import { format, parseISO, formatDistanceToNow } from 'date-fns'

// =============================================================================
// Date Formatting - Pure functions, no optionals
// =============================================================================

/** Accepts an ISO string or a Date; strings are parsed via parseISO. */
const toDate = (value: string | Date): Date =>
    typeof value === 'string' ? parseISO(value) : value

/** date-fns `format` throws on Invalid Date; a malformed backend timestamp
 *  must degrade to a placeholder, not crash the page. */
const safeFormat = (value: string | Date, pattern: string): string => {
    try {
        return format(toDate(value), pattern)
    } catch {
        return '-'
    }
}

/** Format ISO string (or Date) to readable date ('-' when malformed). */
export const formatDateTime = (value: string | Date): string =>
    safeFormat(value, 'MMM d, HH:mm:ss')

/**
 * Relative time for "Updated …" captions. Future instants clamp to
 * "just now": an Updated caption must never read "in 2 minutes" just
 * because the server's clock runs ahead of the client's.
 */
export const formatRelativeTime = (value: string | Date): string => {
    try {
        const d = toDate(value)
        if (d.getTime() > Date.now()) return 'just now'
        return formatDistanceToNow(d, { addSuffix: true })
    } catch {
        return ''
    }
}

/**
 * Compact distance to a FUTURE instant: "4m", "2h 05m". The counterpart to
 * {@link formatRelativeTime}, which deliberately clamps the future to "just now" so an
 * "Updated …" caption never reads "in 2 minutes" on server clock skew — that clamp makes it
 * the wrong tool for a countdown, which is exactly how every upcoming run on the on-air
 * board and the schedules table came to read "just now".
 * Floors at 1m so an imminent run never renders "0m".
 */
export const formatTimeUntil = (value: string | Date): string => {
    const at = typeof value === 'string' ? new Date(value) : value
    // An Invalid Date must not reach the arithmetic: NaN would render as "NaNh NaNm".
    if (Number.isNaN(at.getTime())) return ''
    const mins = Math.max(1, Math.round((at.getTime() - Date.now()) / 60_000))
    if (mins < 60) return `${mins}m`
    const h = Math.floor(mins / 60)
    return `${h}h ${String(mins % 60).padStart(2, '0')}m`
}

// =============================================================================
// Display-name normalization — raw feed/config casing must not reach the UI
// =============================================================================

/** Uppercase route designators (M1, A38) and other tokens that must survive
 *  title-casing untouched. */
const ROAD_TOKEN = /^[A-Z]{1,2}\d+$/

const titleCaseWord = (word: string): string =>
    word.replace(
        /[a-zA-Z']+/g,
        (part) => part.charAt(0).toUpperCase() + part.slice(1).toLowerCase()
    )

/**
 * Market names arrive from config as raw ids ("adelaide", "gold coast").
 * All-lowercase input is title-cased; anything already mixed-case is
 * trusted as deliberate ("Sydney News/Talk").
 */
export const formatMarketName = (name: string): string => {
    if (!name || name !== name.toLowerCase()) return name
    return name.split(/\s+/).map(titleCaseWord).join(' ')
}

/**
 * HERE corridor names arrive SHOUTED ("BURWOOD HIGHWAY"). All-caps input is
 * title-cased word-by-word, preserving route designators ("M1 PACIFIC
 * MOTORWAY" → "M1 Pacific Motorway"); mixed-case input passes through.
 */
export const formatRoadName = (name: string): string => {
    if (!name || name !== name.toUpperCase() || !/[A-Z]{2}/.test(name))
        return name
    return name
        .split(/\s+/)
        .map((word) => (ROAD_TOKEN.test(word) ? word : titleCaseWord(word)))
        .join(' ')
}

/** Format ISO string (or Date) to time only (HH:mm:ss; '-' when malformed). */
export const formatTime = (value: string | Date): string =>
    safeFormat(value, 'HH:mm:ss')

/** Broadcast clock time (e.g. "9:00 am") — air slots, not log timestamps. */
export const formatClockTime = (value: string | Date): string =>
    safeFormat(value, 'h:mm a').toLowerCase()

/** Broadcast clock time from a Date (e.g. "9:00 am") — air slots, not log timestamps.
 *  Like every sibling formatter, an Invalid Date must degrade to '-' rather than
 *  throw: DecisionDock derives its deadline as `new Date(parseTimestamp(job.lastUpdatedAt) + …)`,
 *  and a malformed backend timestamp would otherwise crash the whole review cockpit. */
export const formatClockTimeFromDate = (date: Date): string =>
    safeFormat(date, 'h:mm a').toLowerCase()

/** Format a Date to 24-hour HH:mm, optionally with the short timezone name (e.g. "06:00 AEST"). */
export function formatShortTime(
    date: Date,
    opts?: { tzName?: boolean }
): string {
    return date.toLocaleTimeString(undefined, {
        hour: '2-digit',
        minute: '2-digit',
        hour12: false,
        ...(opts?.tzName ? { timeZoneName: 'short' as const } : {}),
    })
}

const stationDayFormatters = new Map<string, Intl.DateTimeFormat>()

function stationDayFormatter(timezone: string): Intl.DateTimeFormat {
    const key = timezone || 'UTC'
    const cached = stationDayFormatters.get(key)
    if (cached) return cached

    let formatter: Intl.DateTimeFormat
    try {
        formatter = new Intl.DateTimeFormat('en-CA', {
            timeZone: key,
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
        })
    } catch {
        // Invalid configuration must not break a board or review card.
        formatter = new Intl.DateTimeFormat('en-CA', {
            timeZone: 'UTC',
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
        })
    }
    stationDayFormatters.set(key, formatter)
    return formatter
}

/**
 * Return the station-local calendar date for an instant as `YYYY-MM-DD`.
 * Invalid timestamps return an empty key so they cannot be grouped as a real day.
 */
export function stationLocalDayKey(
    value: string | number | Date,
    timezone: string
): string {
    const date =
        typeof value === 'number'
            ? new Date(value)
            : typeof value === 'string'
              ? new Date(value)
              : value
    if (!Number.isFinite(date.getTime())) return ''

    const parts = stationDayFormatter(timezone).formatToParts(date)
    const read = (type: string) =>
        parts.find((part) => part.type === type)?.value ?? ''
    return `${read('year')}-${read('month')}-${read('day')}`
}

/** Parse ISO string to epoch milliseconds without allocating a Date object. */
export const parseTimestamp = (iso: string): number => Date.parse(iso)

/** Format seconds as `Xs` when < 60, otherwise delegate to formatDuration. */
export const formatTookSec = (sec: number): string =>
    sec < 60 ? `${sec}s` : formatDuration(sec)

/** Truncates a long ID (e.g. UUID) for UI display. */
export const truncateId = (
    id: string,
    startChars = 6,
    endChars = 4
): string => {
    if (!id || id.length <= startChars + endChars + 3) return id
    return `${id.substring(0, startChars)}...${id.substring(id.length - endChars)}`
}

// =============================================================================
// Duration & safe wrappers — canonical homes (consumers migrate later)
// =============================================================================

/**
 * Format seconds as mm:ss (or h:mm:ss when `showHours` and >=3600).
 * When `opts.sign` and value is negative, prefix with '-'.
 */
export function formatDuration(
    totalSeconds: number,
    opts?: { sign?: boolean; showHours?: boolean }
): string {
    // NaN/Infinity/undefined flow in from Math.round on an unparsed timestamp
    // or a missing duration field; render '-' (the file's malformed-value
    // placeholder) instead of "NaN:NaN" / "Infinity:NaN" on air.
    if (!Number.isFinite(totalSeconds)) return '-'
    const showSign = opts?.sign ?? false
    const showHours = opts?.showHours ?? false
    const negative = showSign && totalSeconds < 0
    const abs = Math.abs(totalSeconds)

    if (showHours && abs >= 3600) {
        const hours = Math.floor(abs / 3600)
        const minutes = Math.floor((abs % 3600) / 60)
        const secs = Math.floor(abs % 60)
        return `${negative ? '-' : ''}${hours}:${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`
    }

    const minutes = Math.floor(abs / 60)
    const secs = Math.floor(abs % 60)
    return `${negative ? '-' : ''}${minutes}:${secs.toString().padStart(2, '0')}`
}

/** Format a Date as a short absolute time string (e.g. "Jun 2, 06:00 AEST"). */
export function formatRunTime(date: Date, timezone?: string | null): string {
    try {
        return date.toLocaleString(undefined, {
            timeZone: timezone ?? undefined,
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
            hour12: false,
            ...(timezone ? { timeZoneName: 'short' } : {}),
        })
    } catch {
        return '-'
    }
}
