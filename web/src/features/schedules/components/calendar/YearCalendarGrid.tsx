import { memo, useCallback, useMemo, useRef } from 'react'
import { cn } from '@/lib/utils'
import {
    daysInMonth,
    isoDayOfWeek,
    toIsoDate,
} from '../../lib/schedule-calendar'
import type { IsoDate, YearDay } from '../../lib/schedule-calendar'
import { patternTintClass } from './day-type-tints'

// ---------------------------------------------------------------------------
// Twelve month grids, one tint per day type. Click a date to apply the selected
// day type; click it again to put it back on the weekly clock.
//
// PERFORMANCE, and it is the whole design of this file. Three things were making
// this tab unusable, all measured on the real Sydney station (60 cron lines, 366
// cells):
//
//   1. Slot counts came from enumerating cron occurrences through Intl:
//      1,166 ms per day, 6,778 ms to project a year. Now analytic integer maths in
//      `schedule-calendar.ts`, 21 ms for a year.
//   2. Every cell was wrapped in a Radix `Tooltip`, a stateful context provider
//      plus popper machinery, 366 of them. Now a native `title`.
//   3. Cells took `onClick`/`onKeyDown`/`ref` callbacks built inline, so every
//      render handed 366 memoised cells three new function identities and the
//      memo never hit: 775 ms to page one year. Now the handlers live on the month
//      grid and read `data-iso` off the event target, so a cell has NO function
//      props at all and re-renders only when its own day changes.
// ---------------------------------------------------------------------------

const MONTH_NAMES = [
    'Jan',
    'Feb',
    'Mar',
    'Apr',
    'May',
    'Jun',
    'Jul',
    'Aug',
    'Sep',
    'Oct',
    'Nov',
    'Dec',
] as const

/** Monday-first, matching the broadcast week the rest of the console shows. */
const WEEKDAY_INITIALS = ['M', 'T', 'W', 'T', 'F', 'S', 'S'] as const

interface YearCalendarGridProps {
    readonly year: number
    readonly days: readonly YearDay[]
    /** Pattern id to its index in the calendar, so tints are stable across months. */
    readonly patternOrder: Readonly<Record<string, number>>
    readonly selected: IsoDate | null
    readonly onSelect: (iso: IsoDate) => void
    /** Station-local today, so "today" is the station's day and not the viewer's. */
    readonly today: IsoDate | null
}

export function YearCalendarGrid({
    year,
    days,
    patternOrder,
    selected,
    onSelect,
    today,
}: YearCalendarGridProps) {
    const rootRef = useRef<HTMLDivElement>(null)

    const byIso = useMemo(() => {
        const index: Record<string, YearDay> = {}
        for (const day of days) index[day.iso] = day
        return index
    }, [days])

    const handleClick = useCallback(
        (event: React.MouseEvent<HTMLDivElement>) => {
            const cell = (event.target as HTMLElement).closest<HTMLElement>(
                '[data-iso]'
            )
            if (cell?.dataset.iso) onSelect(cell.dataset.iso)
        },
        [onSelect]
    )

    const handleKeyDown = useCallback(
        (event: React.KeyboardEvent<HTMLDivElement>) => {
            const step =
                event.key === 'ArrowLeft'
                    ? -1
                    : event.key === 'ArrowRight'
                      ? 1
                      : event.key === 'ArrowUp'
                        ? -7
                        : event.key === 'ArrowDown'
                          ? 7
                          : 0
            if (step === 0) return
            const cell = (event.target as HTMLElement).closest<HTMLElement>(
                '[data-iso]'
            )
            const iso = cell?.dataset.iso
            if (!iso) return

            event.preventDefault()
            const shifted = new Date(`${iso}T00:00:00Z`)
            shifted.setUTCDate(shifted.getUTCDate() + step)
            const next = shifted.toISOString().slice(0, 10)
            if (!byIso[next]) return
            rootRef.current
                ?.querySelector<HTMLElement>(`[data-iso="${next}"]`)
                ?.focus()
        },
        [byIso]
    )

    // Exactly one cell in the whole year is tabbable, so Tab crosses the grid in one
    // hop and the arrow keys navigate: the standard grid pattern.
    const tabbableIso = selected ?? today ?? toIsoDate(year, 1, 1)

    return (
        <div
            ref={rootRef}
            onClick={handleClick}
            onKeyDown={handleKeyDown}
            className="grid gap-2 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4"
        >
            {MONTH_NAMES.map((monthName, monthIndex) => {
                const month = monthIndex + 1
                const length = daysInMonth(year, month)
                const leadingBlanks =
                    (isoDayOfWeek(toIsoDate(year, month, 1)) + 6) % 7

                return (
                    <div key={monthName} className="rounded-md border p-2">
                        <div className="mb-1 text-2xs font-medium text-muted-foreground">
                            {monthName}
                        </div>
                        <div
                            role="grid"
                            aria-label={`${monthName} ${year}`}
                            className="grid grid-cols-7 gap-px"
                        >
                            {WEEKDAY_INITIALS.map((initial, index) => (
                                <div
                                    key={`h${index}`}
                                    aria-hidden
                                    className="text-center text-3xs text-muted-foreground-subtle"
                                >
                                    {initial}
                                </div>
                            ))}
                            {Array.from({ length: leadingBlanks }, (_, i) => (
                                <div key={`p${i}`} aria-hidden />
                            ))}
                            {Array.from({ length }, (_, i) => {
                                const iso = toIsoDate(year, month, i + 1)
                                const day = byIso[iso]
                                if (!day) return null
                                return (
                                    <DayCell
                                        key={iso}
                                        iso={iso}
                                        kind={day.kind}
                                        slots={day.slots}
                                        patternName={day.patternName}
                                        note={day.note}
                                        tintIndex={
                                            day.patternId
                                                ? (patternOrder[
                                                      day.patternId
                                                  ] ?? 0)
                                                : 0
                                        }
                                        isSelected={selected === iso}
                                        isToday={today === iso}
                                        tabbable={tabbableIso === iso}
                                    />
                                )
                            })}
                        </div>
                    </div>
                )
            })}
        </div>
    )
}

interface DayCellProps {
    readonly iso: IsoDate
    readonly kind: YearDay['kind']
    readonly slots: number
    readonly patternName: string | null
    readonly note: string | null
    readonly tintIndex: number
    readonly isSelected: boolean
    readonly isToday: boolean
    readonly tabbable: boolean
}

/**
 * Memoised on PRIMITIVES, and function-prop-free. Both matter: 366 mount at once
 * and the parent re-renders on every year change, selection change and form edit.
 *
 * Taking the whole `YearDay` object would defeat the memo, because re-projecting the
 * year after a single click builds 366 fresh objects and every cell would see a new
 * identity. On primitives, painting one date re-renders one cell. Interaction is
 * delegated to the grid above via `data-iso`.
 */
const DayCell = memo(function DayCell({
    iso,
    kind,
    slots,
    patternName,
    note,
    tintIndex,
    isSelected,
    isToday,
    tabbable,
}: DayCellProps) {
    // This label is the tooltip AND the screen-reader text for all 365 cells, so a
    // station running one read a day announced "1 slots" 365 times over.
    const reads = `${slots} ${slots === 1 ? 'read' : 'reads'}`
    const label =
        kind === 'suppressed'
            ? `${iso}, off air`
            : kind === 'pattern'
              ? `${iso}, ${patternName}, ${reads}`
              : `${iso}, ${reads}`

    return (
        <button
            type="button"
            data-iso={iso}
            tabIndex={tabbable ? 0 : -1}
            title={note ? `${label}. ${note}` : label}
            aria-label={label}
            aria-current={isToday ? 'date' : undefined}
            className={cn(
                'flex aspect-square items-center justify-center rounded-sm text-3xs tabular-nums transition-colors',
                kind === 'pattern'
                    ? patternTintClass(tintIndex)
                    : kind === 'suppressed'
                      ? 'bg-destructive/70 text-destructive-foreground line-through'
                      : slots === 0
                        ? 'text-muted-foreground-subtle hover:bg-muted'
                        : 'bg-muted/40 text-muted-foreground hover:bg-muted',
                isToday && 'ring-1 ring-foreground/60',
                isSelected && 'ring-2 ring-primary'
            )}
        >
            {Number(iso.slice(8, 10))}
        </button>
    )
})
