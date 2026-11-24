import { formatShortTime, formatTimeUntil } from '@/lib/formatting'
import { SCHEDULE_URGENT_MS } from '@/lib/constants/timing'

/**
 * Format a schedule's next run time for display.
 * Returns relative/absolute text and urgency flag.
 */
export function formatNextRun(
    nextRunAt: string | null,
    enabled: boolean
): { relative: string; absolute: string; urgent: boolean } {
    if (!enabled) return { relative: 'Paused', absolute: '', urgent: false }
    if (!nextRunAt) return { relative: '-', absolute: '', urgent: false }

    try {
        const date = new Date(nextRunAt)
        // `new Date('nonsense')` yields an Invalid Date rather than throwing, so the catch
        // below never fired: NaN comparisons are all false, and a malformed instant fell
        // through the past/future branches to render "NaNh NaNm".
        if (Number.isNaN(date.getTime()))
            return { relative: '-', absolute: '', urgent: false }

        const diffMs = date.getTime() - Date.now()

        if (diffMs < 0)
            return { relative: 'Overdue', absolute: '', urgent: true }

        // Every branch below here is in the FUTURE, so formatRelativeTime is the wrong
        // formatter: it clamps future instants to "just now", which made every upcoming run
        // read "just now" instead of counting down.
        const relative = `in ${formatTimeUntil(date)}`

        const absolute = formatShortTime(date, { tzName: true })

        const urgent = diffMs < SCHEDULE_URGENT_MS

        return { relative, absolute, urgent }
    } catch {
        return { relative: '-', absolute: '', urgent: false }
    }
}
