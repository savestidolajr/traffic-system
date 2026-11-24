import { useMemo, useState } from 'react'
import { X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { DayTimeline } from '../sections/SchedulingSection'
import {
    buildCronFromCalendar,
    cronSlotsByWeekday,
} from '../../lib/cron-calendar'

// ---------------------------------------------------------------------------
// One day type: a name, the days it covers, and the times it runs.
//
// A day type is a LIST OF TIMES, not a two-mode editor. The weekly clock keeps its
// Specific times / Interval split because a station's normal week is genuinely one
// or the other; a holiday clock is a handful of reads. The "every N minutes" row
// below fills the list in one go, then it is just times again.
// ---------------------------------------------------------------------------

const ALL_DAYS = [0, 1, 2, 3, 4, 5, 6]

const EMPTY_SLOTS: readonly (readonly number[])[] = [[], [], [], [], [], [], []]

export interface PatternDraft {
    readonly id: string
    readonly name: string
    readonly cronExpression: string
}

interface PatternEditorProps {
    readonly draft: PatternDraft
    readonly onChange: (next: PatternDraft) => void
    readonly takenNames: readonly string[]
}

export function PatternEditor({
    draft,
    onChange,
    takenNames,
}: PatternEditorProps) {
    // Derived from the EXPRESSION, not from `parseCronToCalendar`. That parse reports
    // an hour-range clock as interval mode with an empty `specificTimes`, so reading
    // it here rendered an empty track for a pattern that had nine reads.
    const slots = useMemo(
        () => cronSlotsByWeekday(draft.cronExpression) ?? EMPTY_SLOTS,
        [draft.cronExpression]
    )
    const times = useMemo(() => {
        const all = new Set<number>()
        for (const minutes of slots) for (const m of minutes) all.add(m)
        return [...all].sort((a, b) => a - b).map(toClock)
    }, [slots])

    const [fillFrom, setFillFrom] = useState('09:00')
    const [fillTo, setFillTo] = useState('16:00')
    const [fillEvery, setFillEvery] = useState(60)

    /**
     * A day type is a list of times, and it ALWAYS covers all seven days. Which
     * DATES it applies to is the calendar's job, so a day-of-week gate here would be
     * a second, silent one: assigning "Public holiday" to a date the pattern did not
     * cover aired nothing, with no way to see why. "Airs nothing" is the palette's
     * own Off air entry instead.
     */
    const write = (nextTimes: string[]) => {
        const built = buildCronFromCalendar({
            scheduleMode: 'specific',
            daysOfWeek: ALL_DAYS,
            startTime: fillFrom,
            endTime: fillTo,
            intervalMinutes: fillEvery,
            specificTimes: nextTimes,
        })
        if (!built.cronExpression) return
        onChange({ ...draft, cronExpression: built.cronExpression })
    }

    const nameTaken =
        draft.name.trim().length > 0 &&
        takenNames.some(
            (n) => n.toLowerCase() === draft.name.trim().toLowerCase()
        )

    const fill = () => {
        const start = toMinutes(fillFrom)
        const end = toMinutes(fillTo)
        if (end < start || fillEvery < 1) return
        const next = new Set(times)
        for (let m = start; m <= end; m += fillEvery) next.add(toClock(m))
        write([...next].sort())
    }

    return (
        <div className="flex flex-col gap-4">
            <div className="flex flex-wrap items-end gap-3">
                <div className="flex min-w-48 flex-1 flex-col gap-1.5">
                    <Label htmlFor="pattern-name">Name</Label>
                    <Input
                        id="pattern-name"
                        value={draft.name}
                        onChange={(e) =>
                            onChange({ ...draft, name: e.target.value })
                        }
                        placeholder="Public holiday"
                        aria-invalid={nameTaken}
                    />
                </div>
                {nameTaken && (
                    <p className="text-xs text-destructive">
                        That name is already used.
                    </p>
                )}
            </div>

            <p className="text-xs text-muted-foreground">
                These times air on every date you give this day type.
            </p>

            <DayTimeline
                mode="specific"
                times={times}
                startTime={fillFrom}
                endTime={fillTo}
                intervalMinutes={fillEvery}
                onAddTime={(time) =>
                    write([...new Set([...times, time])].sort())
                }
                onRemoveTime={(time) => write(times.filter((t) => t !== time))}
            />

            <div className="flex flex-wrap gap-1">
                {times.length === 0 && (
                    <span className="text-xs text-muted-foreground">
                        Click the track to add a time.
                    </span>
                )}
                {times.map((time) => (
                    <Badge
                        key={time}
                        variant="secondary"
                        className="gap-1 pr-1 font-mono tabular-nums"
                    >
                        {time}
                        <button
                            type="button"
                            aria-label={`Remove ${time}`}
                            onClick={() =>
                                write(times.filter((t) => t !== time))
                            }
                            className="rounded-sm p-0.5 hover:bg-destructive/20"
                        >
                            <X className="size-3" />
                        </button>
                    </Badge>
                ))}
            </div>

            <div className="flex flex-wrap items-center gap-2 border-t pt-3 text-xs text-muted-foreground">
                Fill every
                <Input
                    type="number"
                    min={1}
                    max={240}
                    value={fillEvery}
                    aria-label="Fill interval in minutes"
                    onChange={(e) => setFillEvery(Number(e.target.value))}
                    className="h-8 w-16 font-mono"
                />
                min from
                <Input
                    type="time"
                    value={fillFrom}
                    aria-label="Fill from"
                    onChange={(e) => setFillFrom(e.target.value)}
                    className="h-8 w-28 font-mono"
                />
                to
                <Input
                    type="time"
                    value={fillTo}
                    aria-label="Fill to"
                    onChange={(e) => setFillTo(e.target.value)}
                    className="h-8 w-28 font-mono"
                />
                <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    onClick={fill}
                >
                    Fill
                </Button>
            </div>
        </div>
    )
}

function toMinutes(clock: string): number {
    return Number(clock.slice(0, 2)) * 60 + Number(clock.slice(3, 5))
}

function toClock(minutes: number): string {
    return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`
}
