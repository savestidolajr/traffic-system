import type { UseFormReturn } from 'react-hook-form'
import { cn } from '@/lib/utils'
import { useMemo, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { FormInputField } from '@/components/FormInputField'
import { Input } from '@/components/ui/input'
import {
    FormField,
    FormItem,
    FormLabel,
    FormMessage,
} from '@/components/ui/form'
import { Calendar, CalendarDays } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { formatTimeLabel, buildCronFromCalendar } from '../../lib/cron-calendar'
import { nextOccurrence } from '../../lib/schedule-calendar'
import { YearCalendarPanel } from '../calendar/YearCalendarPanel'
import { formatRelativeTime } from '@/lib/formatting'
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs'
import { Badge } from '@/components/ui/badge'
import { toMin, toTimeStr } from '../../lib/day-track'
import type { ScheduleFormData, ScheduleMode } from '../../types'

const DAYS_OF_WEEK = [
    { id: 1, label: 'Mon' },
    { id: 2, label: 'Tue' },
    { id: 3, label: 'Wed' },
    { id: 4, label: 'Thu' },
    { id: 5, label: 'Fri' },
    { id: 10, label: 'Wkdy', isGroup: true, ids: [1, 2, 3, 4, 5] },
    { id: 6, label: 'Sat' },
    { id: 0, label: 'Sun' },
    { id: 11, label: 'Wknd', isGroup: true, ids: [6, 0] },
]

// ---------------------------------------------------------------------------
// Day timeline — the broadcast day as a track. Specific mode: runs are
const BROADCAST_START_HOUR = 5
const BROADCAST_END_HOUR = 23
const TRACK_START_MIN = BROADCAST_START_HOUR * 60
const TRACK_END_MIN = BROADCAST_END_HOUR * 60
const TRACK_SPAN_MIN = TRACK_END_MIN - TRACK_START_MIN
const SNAP_MIN = 15

function trackPct(min: number): number {
    return ((min - TRACK_START_MIN) / TRACK_SPAN_MIN) * 100
}

/**
 * The run minutes an interval window actually produces, mirroring
 * buildIntervalCron's cron semantics exactly: the per-hour minute pattern
 * (0, interval, 2×interval …) across start→end-1 hours, then end:00.
 * Intervals ≥ 60 collapse to hourly, as cron does.
 */
function intervalRunMinutes(
    startMin: number,
    endMin: number,
    intervalMinutes: number
): number[] {
    if (endMin <= startMin || intervalMinutes <= 0) return []
    const startHour = Math.floor(startMin / 60)
    const endHour = Math.floor(endMin / 60)
    const runs: number[] = []
    if (intervalMinutes < 60) {
        for (let h = startHour; h < endHour; h++) {
            for (let m = 0; m < 60; m += intervalMinutes) {
                runs.push(h * 60 + m)
            }
        }
        runs.push(endHour * 60)
    } else {
        for (let h = startHour; h <= endHour; h++) {
            runs.push(h * 60)
        }
    }
    return runs
}

const TRACK_LABEL_HOURS = Array.from(
    { length: 7 },
    (_, i) => BROADCAST_START_HOUR + i * 3
)

interface DayTimelineProps {
    readonly mode: ScheduleMode
    readonly times: readonly string[]
    readonly startTime: string
    readonly endTime: string
    readonly intervalMinutes: number
    readonly onAddTime: (time: string) => void
    readonly onRemoveTime: (time: string) => void
}

export function DayTimeline({
    mode,
    times,
    startTime,
    endTime,
    intervalMinutes,
    onAddTime,
    onRemoveTime,
}: DayTimelineProps) {
    const trackRef = useRef<HTMLDivElement>(null)

    const handleTrackClick = (e: React.MouseEvent) => {
        if (mode !== 'specific') return
        const rect = trackRef.current?.getBoundingClientRect()
        if (!rect || rect.width === 0) return
        const ratio = Math.min(
            Math.max((e.clientX - rect.left) / rect.width, 0),
            1
        )
        const raw = TRACK_START_MIN + ratio * TRACK_SPAN_MIN
        const snapped = Math.min(
            Math.max(Math.round(raw / SNAP_MIN) * SNAP_MIN, TRACK_START_MIN),
            TRACK_END_MIN
        )
        onAddTime(toTimeStr(snapped))
    }

    // Interval mode: the window band and its cron-faithful run ticks
    const startMin = toMin(startTime)
    const endMin = toMin(endTime)
    const validWindow = endMin > startMin
    const intervalTicks =
        mode === 'interval'
            ? intervalRunMinutes(startMin, endMin, intervalMinutes)
            : []
    const handleTrackKeyDown = (e: React.KeyboardEvent) => {
        if (mode !== 'specific') return
        if (e.key !== 'Enter' && e.key !== ' ') return
        e.preventDefault()
        onAddTime(toTimeStr(TRACK_START_MIN))
    }

    return (
        <div className="flex flex-col gap-0.5">
            <div
                ref={trackRef}
                onClick={handleTrackClick}
                onKeyDown={handleTrackKeyDown}
                role={mode === 'specific' ? 'button' : undefined}
                tabIndex={mode === 'specific' ? 0 : undefined}
                aria-label={
                    mode === 'specific'
                        ? 'Broadcast day track: click to add a run time'
                        : 'Broadcast day track'
                }
                className={cn(
                    'relative h-12 overflow-hidden rounded-lg border bg-muted/20',
                    mode === 'specific' && 'cursor-copy'
                )}
            >
                {Array.from({ length: 19 }, (_, i) => i + 5).map((h) => (
                    <div
                        key={h}
                        aria-hidden
                        className={cn(
                            'absolute inset-y-0 w-px',
                            h % 3 === 2 ? 'bg-border/60' : 'bg-border/25'
                        )}
                        style={{ left: `${trackPct(h * 60)}%` }}
                    />
                ))}

                {/* Interval window band + run ticks */}
                {mode === 'interval' && validWindow && (
                    <>
                        <div
                            aria-hidden
                            className="absolute inset-y-0 border-x border-primary/50 bg-primary/10"
                            style={{
                                left: `${trackPct(Math.max(startMin, TRACK_START_MIN))}%`,
                                width: `${Math.max(trackPct(Math.min(endMin, TRACK_END_MIN)) - trackPct(Math.max(startMin, TRACK_START_MIN)), 0)}%`,
                            }}
                        />
                        {intervalTicks
                            .filter(
                                (t) =>
                                    t >= TRACK_START_MIN && t <= TRACK_END_MIN
                            )
                            .map((t) => (
                                <span
                                    key={t}
                                    aria-hidden
                                    title={toTimeStr(t)}
                                    className="absolute top-1/2 size-1.5 -translate-x-1/2 -translate-y-1/2 rounded-full bg-primary/70"
                                    style={{ left: `${trackPct(t)}%` }}
                                />
                            ))}
                    </>
                )}

                {/* Specific-time markers */}
                {mode === 'specific' &&
                    [...times]
                        .filter((t) => {
                            const min = toMin(t)
                            return (
                                min >= TRACK_START_MIN && min <= TRACK_END_MIN
                            )
                        })
                        .map((t) => (
                            <button
                                key={t}
                                type="button"
                                aria-label={`Run at ${t} - click to remove`}
                                title={`${t} - click to remove`}
                                onClick={(e) => {
                                    e.stopPropagation()
                                    onRemoveTime(t)
                                }}
                                className="absolute top-1/2 size-3.5 -translate-x-1/2 -translate-y-1/2 rounded-full bg-primary ring-2 ring-background transition-transform hover:scale-125 hover:bg-destructive"
                                style={{
                                    left: `${trackPct(toMin(t))}%`,
                                }}
                            />
                        ))}
            </div>

            {/* Hour labels under the track */}
            <div className="relative h-4" aria-hidden>
                {TRACK_LABEL_HOURS.map((h) => (
                    <span
                        key={h}
                        className="absolute -translate-x-1/2 font-mono text-3xs tabular-nums text-muted-foreground-subtle"
                        style={{ left: `${trackPct(h * 60)}%` }}
                    >
                        {h.toString().padStart(2, '0')}:00
                    </span>
                ))}
            </div>
        </div>
    )
}

interface SchedulingSectionProps {
    readonly form: UseFormReturn<ScheduleFormData>
}

/**
 * The station's normal week: the clock that governs every date the year calendar
 * does not override. This is the original Scheduling step, unchanged, moved behind
 * a tab when the year layer became its peer.
 */
function WeeklyClockEditor({ form }: SchedulingSectionProps) {
    const [customTimeInput, setCustomTimeInput] = useState('')
    const [customTimeError, setCustomTimeError] = useState<string | null>(null)

    const selectedDays = form.watch('daysOfWeek')
    const scheduleMode = form.watch('scheduleMode')
    const specificTimes = form.watch('specificTimes')
    const startTime = form.watch('startTime')
    const endTime = form.watch('endTime')
    const intervalMinutes = form.watch('intervalMinutes')

    // The stored cron names the same HH:MM on several lines (a weekday line
    // plus Sat and Sun), so a schedule can still load with repeats from an
    // older parse. Render from the set, never the raw field: duplicate React
    // keys orphan chip DOM nodes whose click handlers go dead, which is what
    // made removal stop working part-way down a long list.
    const uniqueTimes = useMemo(
        () => [...new Set(specificTimes)].sort(),
        [specificTimes]
    )

    // `shouldDirty` is REQUIRED on every one of these, not decoration: the
    // studio's Save is gated on `form.formState.isDirty`, and react-hook-form's
    // `setValue` does NOT recompute dirtiness unless asked. Without it, days,
    // times and the mode switch all changed the form value while the footer
    // stayed disabled, so a specific-times schedule could not be edited at all
    // (interval mode only worked because its inputs are registered fields).
    const commit = { shouldDirty: true, shouldValidate: true } as const

    const toggleDay = (dayId: number) => {
        const current = new Set(selectedDays)
        if (current.has(dayId)) {
            current.delete(dayId)
        } else {
            current.add(dayId)
        }
        form.setValue('daysOfWeek', Array.from(current).sort(), commit)
    }

    const toggleGroup = (ids: number[]) => {
        const current = new Set(selectedDays)
        const allPresent = ids.every((id) => current.has(id))

        if (allPresent) {
            ids.forEach((id) => current.delete(id))
        } else {
            ids.forEach((id) => current.add(id))
        }
        form.setValue('daysOfWeek', Array.from(current).sort(), commit)
    }

    const addSpecificTime = (time: string) => {
        const current = new Set(specificTimes)
        current.add(time)
        form.setValue('specificTimes', Array.from(current).sort(), commit)
    }

    const removeSpecificTime = (time: string) => {
        const current = new Set(specificTimes)
        current.delete(time)
        form.setValue('specificTimes', Array.from(current).sort(), commit)
    }

    const handleModeChange = (mode: string) => {
        form.setValue('scheduleMode', mode as ScheduleMode, commit)
    }

    const addCustomTime = () => {
        const input = customTimeInput.trim()
        const match = input.match(/^(\d{1,2}):(\d{2})$/)
        if (!match) {
            setCustomTimeError('Enter time as HH:MM (for example, 9:30)')
            return
        }

        const hour = parseInt(match[1], 10)
        const minute = parseInt(match[2], 10)

        if (hour < 0 || hour > 23 || minute < 0 || minute > 59) {
            setCustomTimeError('Invalid time (hours: 0-23, minutes: 0-59)')
            return
        }

        const normalized = `${hour.toString().padStart(2, '0')}:${minute.toString().padStart(2, '0')}`

        if (uniqueTimes.includes(normalized)) {
            setCustomTimeError('This time is already added')
            return
        }

        addSpecificTime(normalized)
        setCustomTimeInput('')
        setCustomTimeError(null)
    }

    // Interval mode: how many runs the window produces per day (cron-faithful)
    const runsPerDay = intervalRunMinutes(
        toMin(startTime),
        toMin(endTime),
        intervalMinutes
    ).length

    // Live builder verdict for the interval inputs, shown inline
    const intervalCronError =
        scheduleMode === 'interval'
            ? buildCronFromCalendar({
                  scheduleMode: 'interval',
                  daysOfWeek: selectedDays,
                  startTime,
                  endTime,
                  intervalMinutes,
                  specificTimes: uniqueTimes,
              }).error
            : undefined

    return (
        <Tabs
            value={scheduleMode}
            onValueChange={handleModeChange}
            className="space-y-4"
        >
            <TabsList className="h-8 w-fit">
                <TabsTrigger value="specific" className="gap-1.5 px-3 text-xs">
                    <ICON.time className="size-4" />
                    Specific times
                </TabsTrigger>
                <TabsTrigger value="interval" className="gap-1.5 px-3 text-xs">
                    <Calendar className="size-4" />
                    Interval
                </TabsTrigger>
            </TabsList>

            {/* Days of week — visible across all modes, placed between
                    the tab bar and tab panels so the operator always sees
                    which days are active before picking times */}
            <FormField
                control={form.control}
                name="daysOfWeek"
                render={() => (
                    <FormItem>
                        <FormLabel>Days of week</FormLabel>
                        <div className="flex flex-wrap gap-2 mt-2">
                            {DAYS_OF_WEEK.map((day) => (
                                <Button
                                    key={day.id}
                                    type="button"
                                    variant={
                                        day.isGroup
                                            ? day.ids?.every((id) =>
                                                  selectedDays.includes(id)
                                              )
                                                ? 'default'
                                                : 'outline'
                                            : selectedDays.includes(day.id)
                                              ? 'default'
                                              : 'outline'
                                    }
                                    size="sm"
                                    className="px-3"
                                    onClick={() =>
                                        day.isGroup
                                            ? toggleGroup(day.ids!)
                                            : toggleDay(day.id)
                                    }
                                >
                                    {day.label}
                                </Button>
                            ))}
                        </div>
                        <FormMessage />
                    </FormItem>
                )}
            />

            <TabsContent
                value="specific"
                className="mt-0 flex flex-col gap-3 data-[state=inactive]:hidden"
            >
                <p className="text-xs text-muted-foreground">
                    Click the track to place a run · click a marker to remove
                    it.
                </p>
                <FormField
                    control={form.control}
                    name="specificTimes"
                    render={() => (
                        <FormItem>
                            <DayTimeline
                                mode="specific"
                                times={uniqueTimes}
                                startTime={startTime}
                                endTime={endTime}
                                intervalMinutes={intervalMinutes}
                                onAddTime={addSpecificTime}
                                onRemoveTime={removeSpecificTime}
                            />

                            {uniqueTimes.length > 0 && (
                                <div className="flex flex-wrap gap-1.5 pt-1">
                                    {uniqueTimes.map((t) => (
                                        <Badge
                                            key={t}
                                            variant="secondary"
                                            className="gap-1 pr-1"
                                        >
                                            {formatTimeLabel(t)}
                                            <button
                                                type="button"
                                                aria-label={`Remove ${formatTimeLabel(t)}`}
                                                onClick={() =>
                                                    removeSpecificTime(t)
                                                }
                                                className="rounded-full transition-colors hover:bg-destructive/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                                            >
                                                <ICON.close className="size-3.5" />
                                            </button>
                                        </Badge>
                                    ))}
                                </div>
                            )}

                            <div className="flex items-end gap-2 pt-2">
                                <div className="flex-1">
                                    <FormLabel
                                        htmlFor="custom-time"
                                        className="text-xs font-medium text-muted-foreground"
                                    >
                                        Custom time
                                    </FormLabel>
                                    <Input
                                        id="custom-time"
                                        type="text"
                                        placeholder="HH:MM (for example, 9:30)"
                                        value={customTimeInput}
                                        onChange={(e) => {
                                            setCustomTimeInput(e.target.value)
                                            setCustomTimeError(null)
                                        }}
                                        onKeyDown={(e) => {
                                            if (e.key === 'Enter') {
                                                e.preventDefault()
                                                addCustomTime()
                                            }
                                        }}
                                        className={cn(
                                            'mt-1',
                                            customTimeError &&
                                                'border-destructive'
                                        )}
                                    />
                                    {customTimeError && (
                                        <p className="text-xs text-destructive mt-1">
                                            {customTimeError}
                                        </p>
                                    )}
                                </div>
                                <Button
                                    type="button"
                                    variant="outline"
                                    size="sm"
                                    onClick={addCustomTime}
                                >
                                    <ICON.add data-icon="inline-start" />
                                    Add
                                </Button>
                            </div>
                            <FormMessage />
                        </FormItem>
                    )}
                />
            </TabsContent>

            <TabsContent
                value="interval"
                className="mt-0 flex flex-col gap-4 data-[state=inactive]:hidden"
            >
                <DayTimeline
                    mode="interval"
                    times={uniqueTimes}
                    startTime={startTime}
                    endTime={endTime}
                    intervalMinutes={intervalMinutes}
                    onAddTime={addSpecificTime}
                    onRemoveTime={removeSpecificTime}
                />
                {runsPerDay > 0 && (
                    <p className="-mt-2 text-xs text-muted-foreground">
                        ≈ {runsPerDay} run
                        {runsPerDay === 1 ? '' : 's'} per selected day
                    </p>
                )}

                <div className="grid gap-4 sm:grid-cols-3">
                    <FormInputField
                        control={form.control}
                        name="startTime"
                        label="Start time"
                        type="time"
                    />
                    <FormInputField
                        control={form.control}
                        name="endTime"
                        label="End time"
                        type="time"
                    />
                    <FormInputField
                        control={form.control}
                        name="intervalMinutes"
                        label="Run every (minutes)"
                        type="number"
                    />
                </div>
                {intervalCronError && (
                    <p className="text-xs text-warning">{intervalCronError}</p>
                )}
            </TabsContent>
        </Tabs>
    )
}

/**
 * The Schedule step. Two LAYERS of one schedule, as peer tabs:
 *
 *   Weekly clock  — the normal week, governing every date the calendar leaves alone.
 *   Year calendar — named day types, and the dates that run them instead.
 *
 * They are peers rather than a third sibling of "Specific times"/"Interval" because
 * those two are alternative SPELLINGS of one weekly pattern, whereas the calendar
 * sits on top of whatever the weekly clock says. Both write the same form, so the
 * whole schedule leaves in one PUT.
 */
export function SchedulingSection({ form }: SchedulingSectionProps) {
    const [layer, setLayer] = useState('weekly')

    const enabled = form.watch('enabled')
    const timezone = form.watch('timezone')
    const calendar = form.watch('calendar')
    const scheduleMode = form.watch('scheduleMode')
    const daysOfWeek = form.watch('daysOfWeek')
    const specificTimes = form.watch('specificTimes')
    const startTime = form.watch('startTime')
    const endTime = form.watch('endTime')
    const intervalMinutes = form.watch('intervalMinutes')

    const exceptionCount = calendar?.assignments.length ?? 0

    // Contextual, not a permanent fixture: the one fact worth a live preview while
    // you are shaping days/times. Resolved through the CALENDAR, not the bare cron —
    // with a calendar in play the weekly clock alone can name a slot on a suppressed
    // date, and a "Next run" the poller will not honour is worse than none.
    const nextRunLabel = useMemo(() => {
        if (!enabled) return null
        const { cronExpression } = buildCronFromCalendar({
            scheduleMode,
            daysOfWeek,
            startTime,
            endTime,
            intervalMinutes,
            specificTimes: [...new Set(specificTimes)].sort(),
        })
        if (!cronExpression) return null
        try {
            const next = nextOccurrence(
                {
                    cronExpression,
                    timezone: timezone || 'Australia/Sydney',
                    calendar: calendar ?? null,
                },
                new Date()
            )
            return next ? formatRelativeTime(next.toISOString()) : null
        } catch {
            return null
        }
    }, [
        enabled,
        scheduleMode,
        daysOfWeek,
        startTime,
        endTime,
        intervalMinutes,
        specificTimes,
        timezone,
        calendar,
    ])

    return (
        <div className="flex flex-col gap-4">
            {/* Section header — kept separate from Tabs per the
                primitive pattern; no custom wrappers around TabsList */}
            <div className="flex items-center justify-between gap-2 text-sm font-medium text-muted-foreground">
                <span className="flex items-center gap-2">
                    <Calendar className="size-4" />
                    Scheduling
                </span>
                {nextRunLabel && (
                    <span className="text-xs font-normal text-muted-foreground">
                        Next run {nextRunLabel}
                    </span>
                )}
            </div>

            <Tabs value={layer} onValueChange={setLayer}>
                <TabsList>
                    <TabsTrigger value="weekly" className="gap-1.5">
                        <ICON.time className="size-4" />
                        Every week
                    </TabsTrigger>
                    <TabsTrigger value="year" className="gap-1.5">
                        <CalendarDays className="size-4" />
                        Specific dates
                        {exceptionCount > 0 && (
                            <Badge variant="secondary" className="px-1.5">
                                {exceptionCount}
                            </Badge>
                        )}
                    </TabsTrigger>
                </TabsList>

                <TabsContent value="weekly" className="mt-4">
                    <WeeklyClockEditor form={form} />
                </TabsContent>

                <TabsContent value="year" className="mt-4">
                    <YearCalendarPanel form={form} />
                </TabsContent>
            </Tabs>
        </div>
    )
}
