import { useMemo, useState } from 'react'
import type { UseFormReturn } from 'react-hook-form'
import { ChevronLeft, ChevronRight, Pencil, Plus } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { cn } from '@/lib/utils'
import { buildCronFromCalendar } from '../../lib/cron-calendar'
import {
    isoDateOrdinal,
    occurrenceTimesForDate,
    projectYear,
    resolveDay,
    summariseYear,
} from '../../lib/schedule-calendar'
import type { IsoDate } from '../../lib/schedule-calendar'
import { YearCalendarGrid } from './YearCalendarGrid'
import { patternTintClass } from './day-type-tints'
import { PatternEditor } from './PatternEditor'
import type {
    ScheduleAssignment,
    ScheduleCalendar,
    ScheduleFormData,
    SchedulePattern,
} from '../../types'

// ---------------------------------------------------------------------------
// The year layer of the Schedule step: pick what a day should run, then click the
// dates that run it. One screen, no wizard, no separate composer form.
//
// The earlier version had three sub-tabs (Year / Day types / Dates), a five-field
// "add dates" form and a table that repeated what the grid already showed. The
// grid IS the record, so applying a day type is a click on the date.
//
// It reads the base clock from the LIVE FORM, so the year reflects weekly edits
// before they are saved, and writes back to the same form, so the whole schedule
// leaves in one PUT.
// ---------------------------------------------------------------------------

const EMPTY: ScheduleCalendar = { patterns: [], assignments: [] }

/** The "off air" brush is not a pattern, so it needs a value the select can carry. */
const OFF_AIR = '\u0000off-air'
const WEEKLY = '\u0000weekly'

function patternIdFrom(name: string, taken: readonly string[]): string {
    const base =
        name
            .toLowerCase()
            .replace(/[^a-z0-9]+/g, '-')
            .replace(/^-+|-+$/g, '')
            .slice(0, 40) || 'day-type'
    if (!taken.includes(base)) return base
    let n = 2
    while (taken.includes(`${base}-${n}`)) n++
    return `${base}-${n}`
}

interface YearCalendarPanelProps {
    readonly form: UseFormReturn<ScheduleFormData>
}

export function YearCalendarPanel({ form }: YearCalendarPanelProps) {
    const calendar = form.watch('calendar') ?? EMPTY
    const timezone = form.watch('timezone') || 'Australia/Sydney'
    const scheduleMode = form.watch('scheduleMode')
    const daysOfWeek = form.watch('daysOfWeek')
    const specificTimes = form.watch('specificTimes')
    const startTime = form.watch('startTime')
    const endTime = form.watch('endTime')
    const intervalMinutes = form.watch('intervalMinutes')

    const setCalendar = (next: ScheduleCalendar) => {
        // `shouldDirty` is what the studio's Save gate reads.
        form.setValue('calendar', next, {
            shouldDirty: true,
            shouldValidate: true,
        })
    }

    const baseCron = useMemo(
        () =>
            buildCronFromCalendar({
                scheduleMode,
                daysOfWeek,
                startTime,
                endTime,
                intervalMinutes,
                specificTimes: [...new Set(specificTimes)].sort(),
            }).cronExpression ?? '',
        [
            scheduleMode,
            daysOfWeek,
            startTime,
            endTime,
            intervalMinutes,
            specificTimes,
        ]
    )

    const stationToday = useMemo(
        () =>
            new Intl.DateTimeFormat('en-CA', {
                timeZone: timezone,
                year: 'numeric',
                month: '2-digit',
                day: '2-digit',
            }).format(new Date()),
        [timezone]
    )

    const [year, setYear] = useState(() => Number(stationToday.slice(0, 4)))
    const [brush, setBrush] = useState<string>(WEEKLY)
    const [selected, setSelected] = useState<IsoDate | null>(null)
    const [editing, setEditing] = useState<string | null>(null)

    const patternOrder = useMemo(() => {
        const order: Record<string, number> = {}
        calendar.patterns.forEach((p, i) => {
            order[p.id] = i
        })
        return order
    }, [calendar.patterns])

    const schedule = useMemo(
        () => ({ cronExpression: baseCron, timezone, calendar }),
        [baseCron, timezone, calendar]
    )
    const days = useMemo(() => projectYear(schedule, year), [schedule, year])
    const summary = useMemo(() => summariseYear(days), [days])

    /** Applies the current brush to one date, or clears it when already applied. */
    const paint = (iso: IsoDate) => {
        setSelected(iso)
        const existing = calendar.assignments.find(
            (a) =>
                isoDateOrdinal(a.startDate) <= isoDateOrdinal(iso) &&
                isoDateOrdinal(a.endDate) >= isoDateOrdinal(iso) &&
                a.startDate === a.endDate
        )

        if (brush === WEEKLY) {
            if (!existing) return
            setCalendar({
                ...calendar,
                assignments: calendar.assignments.filter((a) => a !== existing),
            })
            return
        }

        const wanted: ScheduleAssignment = {
            startDate: iso,
            endDate: iso,
            patternId: brush === OFF_AIR ? null : brush,
            suppress: brush === OFF_AIR,
            note: existing?.note ?? null,
        }

        // Clicking a date that already carries the brush clears it: one control for
        // apply and undo, so there is no separate remove step.
        const alreadyApplied =
            existing &&
            existing.suppress === wanted.suppress &&
            existing.patternId === wanted.patternId
        const rest = calendar.assignments.filter((a) => a !== existing)
        setCalendar({
            ...calendar,
            assignments: alreadyApplied ? rest : [...rest, wanted],
        })
    }

    const addPattern = () => {
        const id = patternIdFrom(
            'Day type',
            calendar.patterns.map((p) => p.id)
        )
        const pattern: SchedulePattern = {
            id,
            name: `Day type ${calendar.patterns.length + 1}`,
            // Seeded from the station's own clock so the operator trims down from the
            // real week instead of building one from nothing.
            cronExpression: baseCron || '0 9-16 * * 0,1,2,3,4,5,6',
        }
        setCalendar({ ...calendar, patterns: [...calendar.patterns, pattern] })
        setBrush(id)
        setEditing(id)
    }

    const removePattern = (id: string) => {
        setCalendar({
            patterns: calendar.patterns.filter((p) => p.id !== id),
            // Dates pointing at a deleted day type would fall back to the weekly clock
            // silently, putting bulletins back on a date someone deliberately changed.
            assignments: calendar.assignments.filter((a) => a.patternId !== id),
        })
        if (brush === id) setBrush(WEEKLY)
        if (editing === id) setEditing(null)
    }

    const setNote = (iso: IsoDate, note: string) => {
        setCalendar({
            ...calendar,
            assignments: calendar.assignments.map((a) =>
                a.startDate === iso && a.endDate === iso
                    ? { ...a, note: note.trim() || null }
                    : a
            ),
        })
    }

    const editingPattern =
        calendar.patterns.find((p) => p.id === editing) ?? null

    if (editingPattern) {
        return (
            <div className="flex flex-col gap-4">
                <div className="flex items-center justify-between gap-2">
                    <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        onClick={() => setEditing(null)}
                        className="gap-1.5"
                    >
                        <ChevronLeft className="size-4" />
                        Back to the year
                    </Button>
                    <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="text-destructive hover:text-destructive"
                        onClick={() => removePattern(editingPattern.id)}
                    >
                        Delete
                    </Button>
                </div>
                <PatternEditor
                    key={editingPattern.id}
                    draft={editingPattern}
                    takenNames={calendar.patterns
                        .filter((p) => p.id !== editingPattern.id)
                        .map((p) => p.name)}
                    onChange={(next) =>
                        setCalendar({
                            ...calendar,
                            patterns: calendar.patterns.map((p) =>
                                p.id === next.id ? next : p
                            ),
                        })
                    }
                />
            </div>
        )
    }

    const selectedDay = selected
        ? days.find((d) => d.iso === selected)
        : undefined
    const selectedTimes = selectedDay
        ? occurrenceTimesForDate(
              resolveDay(schedule, selectedDay.iso).cronExpression,
              selectedDay.iso
          )
        : []

    return (
        <div className="flex flex-col gap-3">
            {/* The model in one line, because the tab pair alone read as a choice
                between weekly and yearly scheduling. It is not a choice: the weekly
                clock runs all year, and these dates replace it. */}
            <p className="text-xs text-muted-foreground">
                The weekly clock runs every day of the year. Dates you set here
                run something else instead.
            </p>
            <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex items-center gap-1">
                    <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        className="size-7"
                        aria-label="Previous year"
                        onClick={() => setYear(year - 1)}
                    >
                        <ChevronLeft className="size-4" />
                    </Button>
                    <span className="min-w-12 text-center text-sm font-semibold tabular-nums">
                        {year}
                    </span>
                    <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        className="size-7"
                        aria-label="Next year"
                        onClick={() => setYear(year + 1)}
                    >
                        <ChevronRight className="size-4" />
                    </Button>
                </div>
                <span className="text-xs text-muted-foreground tabular-nums">
                    {summary.totalSlots.toLocaleString()} slots,{' '}
                    {summary.overriddenDays + summary.suppressedDays} of{' '}
                    {days.length} days changed
                </span>
            </div>

            {/* The brush palette doubles as the legend: one row, no separate key. */}
            <div className="flex flex-wrap items-center gap-1.5">
                <span className="mr-1 text-xs text-muted-foreground">
                    Click dates to set them to
                </span>
                <BrushChip
                    label="Weekly clock"
                    swatch="bg-muted"
                    active={brush === WEEKLY}
                    onClick={() => setBrush(WEEKLY)}
                />
                {calendar.patterns.map((pattern, index) => (
                    <BrushChip
                        key={pattern.id}
                        label={pattern.name}
                        swatch={patternTintClass(index)}
                        active={brush === pattern.id}
                        onClick={() => setBrush(pattern.id)}
                        onEdit={() => setEditing(pattern.id)}
                    />
                ))}
                <BrushChip
                    label="Off air"
                    swatch="bg-destructive/70"
                    active={brush === OFF_AIR}
                    onClick={() => setBrush(OFF_AIR)}
                />
                <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="h-7 gap-1 px-2 text-xs"
                    onClick={addPattern}
                >
                    <Plus className="size-3.5" />
                    New day type
                </Button>
            </div>

            <YearCalendarGrid
                year={year}
                days={days}
                patternOrder={patternOrder}
                selected={selected}
                onSelect={paint}
                today={stationToday}
            />

            {selectedDay && (
                <div className="flex flex-wrap items-center gap-2 rounded-md border bg-card/40 px-3 py-2">
                    <span className="text-sm font-medium tabular-nums">
                        {selectedDay.iso}
                    </span>
                    <span className="text-xs text-muted-foreground">
                        {selectedDay.kind === 'suppressed'
                            ? 'Off air'
                            : selectedDay.kind === 'pattern'
                              ? selectedDay.patternName
                              : 'Weekly clock'}
                    </span>
                    {selectedDay.kind !== 'base' && (
                        <Input
                            value={selectedDay.note ?? ''}
                            onChange={(e) =>
                                setNote(selectedDay.iso, e.target.value)
                            }
                            placeholder="Why (optional)"
                            aria-label={`Note for ${selectedDay.iso}`}
                            className="h-7 w-48 text-xs"
                        />
                    )}
                    <span className="ml-auto flex flex-wrap gap-1">
                        {selectedTimes.length === 0 ? (
                            <span className="text-xs text-muted-foreground">
                                Nothing airs
                            </span>
                        ) : (
                            selectedTimes.map((time) => (
                                <span
                                    key={time}
                                    className="rounded bg-muted px-1 font-mono text-2xs tabular-nums"
                                >
                                    {time}
                                </span>
                            ))
                        )}
                    </span>
                </div>
            )}
        </div>
    )
}

interface BrushChipProps {
    readonly label: string
    readonly swatch: string
    readonly active: boolean
    readonly onClick: () => void
    readonly onEdit?: () => void
}

function BrushChip({ label, swatch, active, onClick, onEdit }: BrushChipProps) {
    return (
        <span
            className={cn(
                'inline-flex items-center gap-1.5 rounded-full border py-1 pl-2 text-xs transition-colors',
                onEdit ? 'pr-1' : 'pr-2.5',
                active ? 'border-primary bg-accent' : 'hover:bg-accent/50'
            )}
        >
            <button
                type="button"
                onClick={onClick}
                aria-pressed={active}
                className="inline-flex items-center gap-1.5"
            >
                <span
                    aria-hidden
                    className={cn('size-2.5 rounded-full', swatch)}
                />
                {label}
            </button>
            {onEdit && (
                <button
                    type="button"
                    onClick={onEdit}
                    aria-label={`Edit ${label}`}
                    className="rounded-full p-1 hover:bg-muted"
                >
                    <Pencil className="size-3" />
                </button>
            )}
        </span>
    )
}
