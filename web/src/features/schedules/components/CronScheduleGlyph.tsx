import { useMemo } from 'react'
import { cn } from '@/lib/utils'
import { parseCronToCalendar } from '../lib/cron-calendar'
import type { CronCalendarInput } from '../lib/cron-calendar'
import { cronToText } from '../lib/cron-format'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'

// ---------------------------------------------------------------------------
// Day display order: Mon → Sun (shifted from cron's Sun=0 ordering)
// ---------------------------------------------------------------------------

const DAY_ORDER: { readonly label: string; readonly cronDay: number }[] = [
    { label: 'M', cronDay: 1 },
    { label: 'T', cronDay: 2 },
    { label: 'W', cronDay: 3 },
    { label: 'T', cronDay: 4 },
    { label: 'F', cronDay: 5 },
    { label: 'S', cronDay: 6 },
    { label: 'S', cronDay: 0 },
]

// ---------------------------------------------------------------------------
// Time formatting helpers
// ---------------------------------------------------------------------------

/** Drop leading zero for cleaner compact display. */
function fmt(time: string): string {
    return time.replace(/^0(\d)/, '$1')
}

function buildTimeText(input: CronCalendarInput): string {
    const range = `${fmt(input.startTime)}-${fmt(input.endTime)}`

    if (input.scheduleMode === 'specific') {
        if (input.specificTimes.length === 0) return range
        const first = input.specificTimes[0]
        const last = input.specificTimes[input.specificTimes.length - 1]
        const count = input.specificTimes.length
        return `${first}-${last} · ${count}×`
    }

    // Interval mode
    if (input.intervalMinutes < 60) {
        return `${range} · every ${input.intervalMinutes}m`
    }
    if (input.intervalMinutes === 60) {
        return `${range} · hourly`
    }
    const hrs = input.intervalMinutes / 60
    return `${range} · every ${hrs}h`
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export interface CronScheduleGlyphProps {
    readonly expression: string | null
    readonly className?: string
}

/**
 * Compact visual schedule for cards: a 7-day week strip (Mon–Sun, filled dot
 * = active day) plus a one-line time summary.
 *
 * Uses `parseCronToCalendar` for structured parsing (exclusive-end aware).
 * Hovering the glyph shows the full human-readable text, plus the meaning of
 * the specific-mode "N×" shorthand.
 *
 * Falls back to raw cron text when the expression is null or unparseable.
 */
export function CronScheduleGlyph({
    expression,
    className,
}: CronScheduleGlyphProps) {
    const parsed = useMemo(() => parseCronToCalendar(expression), [expression])

    const activeDays = useMemo(
        () => new Set(parsed.daysOfWeek),
        [parsed.daysOfWeek]
    )

    // Tooltip: full cronstrue text (handles compound newline expressions),
    // plus the meaning of the specific-mode "N×" shorthand — the schedule
    // fires at each specific time once per scheduled day.
    const tooltip = useMemo(() => {
        const base = expression
            ? (cronToText(expression) ?? undefined)
            : undefined
        if (
            parsed.scheduleMode === 'specific' &&
            parsed.specificTimes.length > 0
        ) {
            const runs = `${parsed.specificTimes.length} runs per day`
            return base ? `${base} · ${runs}` : runs
        }
        return base
    }, [expression, parsed])

    // Fallback: null expression → show text
    if (!expression) {
        return (
            <span className={cn('font-mono text-xs tabular-nums', className)}>
                -
            </span>
        )
    }

    const timeText = buildTimeText(parsed)

    const glyph = (
        <span
            className={cn('inline-flex min-w-0 items-center gap-2', className)}
        >
            {/* Day strip: single grid so labels and dots share column widths */}
            <span className="grid grid-cols-7 gap-0.5 justify-items-center">
                {DAY_ORDER.map(({ label, cronDay }) => (
                    <span
                        key={cronDay}
                        className="flex flex-col items-center gap-px"
                    >
                        <span className="text-3xs font-semibold leading-none text-muted-foreground-subtle">
                            {label}
                        </span>
                        <span
                            className={cn(
                                'size-2 rounded-full',
                                activeDays.has(cronDay)
                                    ? 'bg-primary'
                                    : 'bg-muted-foreground/15'
                            )}
                        />
                    </span>
                ))}
            </span>
            {/* Time summary */}
            <span className="block min-w-0 truncate text-3xs leading-tight text-muted-foreground-subtle">
                {timeText}
            </span>
        </span>
    )

    if (!tooltip) return glyph

    return (
        <Tooltip>
            <TooltipTrigger asChild>{glyph}</TooltipTrigger>
            <TooltipContent>{tooltip}</TooltipContent>
        </Tooltip>
    )
}
