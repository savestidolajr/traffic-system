import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { useCoarseNow, useNow } from '@/hooks/use-second-tick'
import { formatShortTime } from '@/lib/formatting'
import { ROUTE_STATIONS } from '@/lib/routes'
import { ArrowRight, ChevronDown, RadioTower } from 'lucide-react'
import type { JobSummary } from '@/features/jobs/types'
import type { MarketSchedule } from '@/features/schedules/types'
import { computeStationLanes } from '@/features/onair/lib/slots'
import { parseTimestamp } from '@/lib/formatting'
import { laneCondition, CONDITION_META } from './lane-condition'
import { Button } from '@/components/ui/button'
import {
    Popover,
    PopoverContent,
    PopoverTrigger,
} from '@/components/ui/popover'
import { SlotAudition } from './SlotAudition'
import { EmptyState } from '@/components/EmptyState'
import { Skeleton } from '@/components/ui/skeleton'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { AuroraHero } from '@/components/ui/aceternity/aurora-hero'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { GlowBorder } from '@/components/ui/aceternity/glow-border'
import { useOperatorScope } from '@/features/operators/api/operator-hooks'
import { cn } from '@/lib/utils'
import {
    airSourceBar,
    airSourceLabel,
    airSourceReason,
    airSourceTone,
    buildAirPlan,
    cutoffPhrase,
    CUTOFF_LEAD_MS,
    GENERATION_LEAD_MS,
    foreignZoneLabel,
    formatSlotClock,
    type AirPlanRow,
} from '@/features/onair/lib/air-plan'

/** Lane ordering: hottest condition first. */
const KIND_ORDER: Record<string, number> = {
    needsReview: 0,
    atRisk: 1,
    late: 2,
    missed: 3,
    generating: 4,
    clear: 5,
    paused: 6,
}
interface CoverageHomeProps {
    readonly schedules: readonly MarketSchedule[]
    readonly jobs: readonly JobSummary[]
    readonly selectedJobId: string | null
    readonly onSelectJob: (jobId: string) => void
    /** The most urgent waiting bulletin, for the masthead CTA. */
    readonly mostUrgentJobId: string | null
    readonly waitingCount: number
    /** Unvetted road-event counts keyed by normalized market id. */
    /**
     * Schedules still in flight. Without it `schedules` is `[]` on first paint,
     * which is indistinguishable from "no stations configured" — the operator
     * saw the empty state flash on every cold load, and the board replacing it
     * was the page's entire CLS (0.146, `layout-shifts`, 2026-07-27).
     */
    readonly isLoading?: boolean
}

/** The lanes span from just behind now to an hour ahead, on one shared clock. */
const TRACK_BACK_MS = 5 * 60 * 1000
const TRACK_AHEAD_MS = 60 * 60 * 1000

/** Shared grid: station, its own track, what airs, cutoff, action. */
const LANE_GRID =
    'grid grid-cols-[minmax(150px,190px)_1fr_128px_52px_120px] items-center gap-x-3'

function trackPct(t: number, now: number): number {
    const start = now - TRACK_BACK_MS
    const span = TRACK_BACK_MS + TRACK_AHEAD_MS
    return Math.min(100, Math.max(0, ((t - start) / span) * 100))
}

/**
 * One station's own next hour, inline. Only the NEXT slot is drawn as the window
 * it owns — generation start to airtime, with a notch at the publish-by cutoff —
 * because that is the only slot anyone can still act on. The ones after it are
 * bare ticks, which is all it takes to see this station's cadence.
 *
 * Everything the earlier boards drew and nobody could read is deliberately gone:
 * closed slots behind, ad-hoc riders, per-pipeline-state colours, the legend.
 * One colour here, and it means what the row's own words mean.
 */
function SlotTrack({
    row,
    now,
}: {
    readonly row: AirPlanRow
    readonly now: number
}) {
    const [next, ...later] = row.upcoming
    if (!next) {
        return (
            <div className="relative h-6">
                <span className="absolute inset-y-0 left-0 flex items-center font-mono text-3xs italic text-muted-foreground-subtle">
                    nothing scheduled
                </span>
            </div>
        )
    }

    const airsAt = next.getTime()
    const windowStart = airsAt - GENERATION_LEAD_MS
    const left = trackPct(windowStart, now)
    const right = trackPct(airsAt, now)
    const cutoffOffset =
        ((airsAt - CUTOFF_LEAD_MS - windowStart) / (airsAt - windowStart)) * 100

    return (
        <div className="relative h-6">
            <span
                aria-hidden
                className="absolute inset-x-0 top-1/2 h-px -translate-y-1/2 bg-border/40"
            />

            {/* The window still in play, split at the cutoff: solid while the
                operator can still change what airs, faded for the last stretch
                where the deadline sweep decides instead. The earlier version
                marked the cutoff with a bare white hairline, which read as an
                unexplained artefact rather than a deadline. */}
            <Tooltip>
                <TooltipTrigger asChild>
                    <span
                        className={cn(
                            'absolute top-1/2 flex h-3 -translate-y-1/2 overflow-hidden rounded-sm border',
                            airSourceBar(row.source)
                        )}
                        tabIndex={0}
                        role="img"
                        aria-label={`Generates ${formatSlotClock(new Date(windowStart), row.timezone)}, publish by ${formatSlotClock(new Date(airsAt - CUTOFF_LEAD_MS), row.timezone)}, airs ${formatSlotClock(new Date(airsAt), row.timezone)}`}
                        style={{
                            left: `${left}%`,
                            width: `${Math.max(right - left, 1)}%`,
                        }}
                    >
                        <span
                            className="h-full"
                            style={{ width: `${cutoffOffset}%` }}
                        />
                        <span
                            className="h-full flex-1 bg-background/45"
                            title="after the cutoff"
                        />
                    </span>
                </TooltipTrigger>
                <TooltipContent side="top">
                    <span className="block">
                        Generates{' '}
                        {formatSlotClock(new Date(windowStart), row.timezone)}
                        {' · '}publish by{' '}
                        {formatSlotClock(
                            new Date(airsAt - CUTOFF_LEAD_MS),
                            row.timezone
                        )}
                        {' · '}airs{' '}
                        {formatSlotClock(new Date(airsAt), row.timezone)}
                    </span>
                    <span className="mt-0.5 block text-muted-foreground">
                        Faded: past the cutoff, decided for you.
                    </span>
                </TooltipContent>
            </Tooltip>

            {/* This station's later airtimes: position only, so the cadence
                reads without adding anything to decode. */}
            {later.map((at) => (
                <span
                    key={at.getTime()}
                    aria-hidden
                    className="absolute top-1/2 h-2 w-px -translate-y-1/2 bg-muted-foreground/40"
                    style={{ left: `${trackPct(at.getTime(), now)}%` }}
                />
            ))}

            <span
                aria-hidden
                className="absolute inset-y-0 w-px bg-primary"
                style={{ left: `${trackPct(now, now)}%` }}
            />
        </div>
    )
}

/**
 * How long a bulletin has sat unapproved. Without it, one waiting three hours and
 * one waiting three minutes read identically — the exact shape of the 5am
 * discovery that a slot had been uncovered since 2am.
 */
function waitingFor(job: JobSummary, now: number): string {
    const mins = Math.max(
        0,
        Math.floor((now - parseTimestamp(job.createdAt)) / 60_000)
    )
    if (mins < 60) return `${mins}m`
    return `${Math.floor(mins / 60)}h ${mins % 60}m`
}

/**
 * One station, one line: its own schedule on the shared clock, what will air in
 * its next slot, and how long there is to change it. The whole row opens the
 * bulletin; the kebab chooses what airs instead.
 */
function StationLane({
    row,
    onSelectJob,
}: {
    readonly row: AirPlanRow
    readonly onSelectJob: (jobId: string) => void
}) {
    // `useNow` rather than `Date.now()` in render: the latter is impure and the
    // react-hooks purity rule rejects it. The board's own clock is coarse, so
    // the countdown would otherwise sit still for a whole minute at a time.
    const now = useNow()
    // The panel closes itself once a choice is committed, so the operator sees
    // the row's forecast change rather than reading it through an open popover.
    const [auditionOpen, setAuditionOpen] = useState(false)
    const [evidenceVisible, setEvidenceVisible] = useState(false)

    const zone = foreignZoneLabel(row.timezone)
    const secs = row.cutoffAt
        ? Math.floor((row.cutoffAt.getTime() - now) / 1000)
        : null
    const past = secs !== null && secs < 0
    const urgent = secs !== null && secs >= 0 && secs < 120
    const target = row.pending ?? row.slotJob ?? row.job
    const openable = Boolean(target)
    return (
        <div
            onClick={openable ? () => onSelectJob(target!.jobId) : undefined}
            onMouseEnter={() => setEvidenceVisible(true)}
            onMouseLeave={() => setEvidenceVisible(false)}
            className={cn(
                LANE_GRID,
                'group/lane border-b border-border/40 py-1.5 last:border-0',
                openable && 'cursor-pointer hover:bg-muted/20'
            )}
        >
            <div className="min-w-0 leading-tight">
                <div className="flex min-w-0 items-center gap-1.5">
                    <span className="min-w-0 truncate text-sm font-semibold">
                        {row.station}
                    </span>
                    <span className="shrink-0 text-2xs font-normal text-muted-foreground">
                        {row.market}
                    </span>
                </div>
                <p className="font-mono text-2xs tabular-nums text-muted-foreground-subtle">
                    {row.slotAt
                        ? formatSlotClock(row.slotAt, row.timezone)
                        : 'no slot'}
                    {zone && <span className="ml-1">{zone}</span>}
                </p>
            </div>
            <SlotTrack row={row} now={now} />

            {/* The forecast IS the control: it says what will air, and clicking it
                opens the panel where that choice is heard and changed. A separate
                "Open" button meant two columns about one fact; a modal for two or
                three options made an exception path feel like a workflow; and a
                menu of links meant leaving the board to hear anything. */}
            <Popover open={auditionOpen} onOpenChange={setAuditionOpen}>
                <Tooltip>
                    <TooltipTrigger asChild>
                        <PopoverTrigger asChild>
                            <button
                                type="button"
                                onClick={(e) => e.stopPropagation()}
                                aria-label={`${row.station}: ${airSourceLabel(row)}. Hear or change it`}
                                className="group/what -mx-1 flex min-w-0 flex-col items-start rounded px-1 py-0.5 text-left hover:bg-muted/50 focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring"
                            >
                                <span className="flex min-w-0 items-center gap-1">
                                    <span
                                        className={cn(
                                            'truncate text-xs font-medium',
                                            airSourceTone(row.source)
                                        )}
                                    >
                                        {airSourceLabel(row)}
                                    </span>
                                    <ChevronDown
                                        className="size-3 shrink-0 text-muted-foreground-subtle group-hover/what:text-muted-foreground"
                                        aria-hidden
                                    />
                                </span>
                                {row.pending && (
                                    <span className="truncate text-2xs text-warning">
                                        waiting {waitingFor(row.pending, now)}
                                    </span>
                                )}
                            </button>
                        </PopoverTrigger>
                    </TooltipTrigger>
                    <TooltipContent side="top" className="max-w-xs">
                        {airSourceReason(row)}
                    </TooltipContent>
                </Tooltip>

                <PopoverContent
                    align="start"
                    className="w-auto p-0"
                    onClick={(e) => e.stopPropagation()}
                >
                    <SlotAudition
                        row={row}
                        onSelectJob={onSelectJob}
                        onCommitted={() => setAuditionOpen(false)}
                    />
                </PopoverContent>
            </Popover>

            <Tooltip>
                <TooltipTrigger asChild>
                    <span
                        tabIndex={0}
                        className={cn(
                            'cursor-help rounded text-right font-mono text-2xs tabular-nums focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
                            past || urgent
                                ? 'text-destructive'
                                : 'text-muted-foreground-subtle'
                        )}
                    >
                        {secs === null
                            ? ''
                            : past
                              ? 'over'
                              : cutoffPhrase(secs).replace('in ', '')}
                    </span>
                </TooltipTrigger>
                <TooltipContent side="top">
                    {row.cutoffAt
                        ? `Publish by ${formatSlotClock(row.cutoffAt, row.timezone)}`
                        : 'No slot left today'}
                </TooltipContent>
            </Tooltip>

            <div className="flex min-w-0 flex-col items-stretch gap-1">
                <Link
                    to={`/desk?market=${encodeURIComponent(row.marketId)}`}
                    onClick={(e) => e.stopPropagation()}
                    className="inline-flex h-6 min-w-0 items-center justify-center rounded-full border border-border bg-muted px-2 text-2xs font-normal text-muted-foreground transition-colors hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring"
                    onFocus={() => setEvidenceVisible(true)}
                    onBlur={() => setEvidenceVisible(false)}
                    style={{
                        opacity: evidenceVisible ? 1 : 0,
                        pointerEvents: evidenceVisible ? 'auto' : 'none',
                    }}
                    title="See the road events feeding this station's next bulletin and omit any that should not air"
                >
                    Review evidence
                </Link>
                {row.pending && (
                    <Button
                        size="sm"
                        className="h-6 w-full justify-center px-2 text-2xs"
                        aria-label={`Review the ${row.station} bulletin`}
                        onClick={(e) => {
                            e.stopPropagation()
                            onSelectJob(row.pending!.jobId)
                        }}
                    >
                        Review
                        <ArrowRight className="ml-0.5 size-3" />
                    </Button>
                )}
            </div>
        </div>
    )
}

/**
 * The workstation's home view: a broadcast rundown board. One shared
 * clock (the operator's local time, LOOKBACK_MS behind now to
 * BOARD_LOOKAHEAD_MS ahead), one lane per station, every slot a dot.
 * The six chip-stuffed cards this replaces made the operator read;
 * the board lets them see.
 */
export function CoverageHome({
    schedules,
    jobs,
    onSelectJob,
    mostUrgentJobId,
    waitingCount,
    isLoading = false,
}: CoverageHomeProps) {
    const now = useCoarseNow()

    const lanes = useMemo(
        () => computeStationLanes(schedules, jobs, new Date()),
        [schedules, jobs]
    )

    // Hottest stations first; within "needs review", most overdue first.
    // Order recomputes when the data changes, not every tick — lanes must
    // not reshuffle under the operator's eyes.
    const ordered = useMemo(() => {
        const at = now
        return lanes
            .slice()
            .map((lane) => ({ lane, cond: laneCondition(lane, at) }))
            .sort((a, b) => {
                const kind =
                    (KIND_ORDER[a.cond.kind] ?? 9) -
                    (KIND_ORDER[b.cond.kind] ?? 9)
                if (kind !== 0) return kind
                if (
                    a.cond.kind === 'needsReview' &&
                    b.cond.kind === 'needsReview'
                ) {
                    return (a.cond.secs ?? Infinity) - (b.cond.secs ?? Infinity)
                }
                return 0
            })
            .map((e) => e.lane)
    }, [lanes, now])

    const {
        data: scope,
        isLoading: scopeLoading,
        isError: scopeError,
    } = useOperatorScope()

    // Scoped operator: partition lanes into in-scope and out-of-scope. An
    // unscoped operator sees every lane. Loading/error/admin => no partition;
    // this is presentation only, the API enforces access on every call.
    const scopeMarkets = useMemo(
        () =>
            !scopeLoading && scope?.scoped && scope.markets
                ? new Set(scope.markets.map((m) => m.toLowerCase()))
                : null,
        [scopeLoading, scope]
    )
    const inScopeLanes = useMemo(() => {
        if (!scopeMarkets) return ordered
        return ordered.filter((l) => scopeMarkets.has(l.marketId.toLowerCase()))
    }, [ordered, scopeMarkets])

    // Paused stations collapse into one line at the bottom: countable, and out
    // of the way of the stations that are actually on air.
    const [pausedOpen, setPausedOpen] = useState(false)

    // Quarter-hour labels for the shared axis above the lanes.
    const axisTicks = useMemo(() => {
        const start = now - TRACK_BACK_MS
        const end = now + TRACK_AHEAD_MS
        const ticks: number[] = []
        for (
            let t = Math.ceil(start / 900_000) * 900_000;
            t <= end;
            t += 900_000
        )
            ticks.push(t)
        return ticks
    }, [now])
    const pausedPhrase = CONDITION_META.paused.phrase
    const activeLanes = useMemo(
        () => inScopeLanes.filter((l) => l.enabled),
        [inScopeLanes]
    )
    const pausedLanes = useMemo(
        () => inScopeLanes.filter((l) => !l.enabled),
        [inScopeLanes]
    )

    // The rows ARE the board: urgent outcomes first, then by airtime.
    const airPlan = useMemo(
        () =>
            // The standby cart is network-wide and configured in every
            // environment, so an uncovered slot is a misconfiguration, not a
            // station-by-station question the console has to guess at.
            buildAirPlan(activeLanes, jobs, new Date(now), true),
        [activeLanes, jobs, now]
    )

    const standbyCount = useMemo(
        () => airPlan.filter((row) => row.source === 'filler').length,
        [airPlan]
    )

    return (
        <div className="relative flex h-full flex-col overflow-y-auto">
            {/* Top-anchored, deliberately. This panel used to carry `my-auto`,
                which vertically re-centred everything already painted each time
                the panel's height changed — so the empty state giving way to the
                loaded board moved the masthead and accounted for the whole
                0.146 CLS on this route. A dashboard that grows downward from a
                fixed top has no such shift to make. */}
            <div className="relative mx-auto flex w-full max-w-5xl flex-col gap-5 p-6">
                {/* Masthead: the triage state in one line, on the aurora */}
                <AuroraHero contentClassName="flex flex-wrap items-center justify-between gap-x-6 gap-y-3 p-5">
                    <div className="flex min-w-0 flex-col gap-1">
                        <h1 className="text-lg font-bold tracking-tight">
                            On air
                        </h1>
                        {/* The masthead counts what the ROWS below say, in the
                            same words. It used to carry five pipeline chips
                            ("2 at risk · 2 late · 1 missed · 7 paused") that
                            counted lanes on a different basis from the board, so
                            it claimed six problem stations while one row showed
                            distress — and an operator had no way to reconcile
                            them. */}
                        <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-muted-foreground">
                            <span>
                                <span className="font-mono tabular-nums text-foreground">
                                    {airPlan.length}
                                </span>{' '}
                                stations
                            </span>
                            {waitingCount > 0 && (
                                <span className="text-warning">
                                    <span className="font-mono tabular-nums">
                                        {waitingCount}
                                    </span>{' '}
                                    need review
                                </span>
                            )}
                            {standbyCount > 0 && (
                                <span>
                                    <span className="font-mono tabular-nums">
                                        {standbyCount}
                                    </span>{' '}
                                    on standby
                                </span>
                            )}
                            {pausedLanes.length > 0 && (
                                <span className="text-muted-foreground-subtle">
                                    <span className="font-mono tabular-nums">
                                        {pausedLanes.length}
                                    </span>{' '}
                                    paused
                                </span>
                            )}
                        </p>
                    </div>
                    {waitingCount > 0 && mostUrgentJobId ? (
                        <GlowBorder className="rounded-md">
                            <button
                                type="button"
                                onClick={() => onSelectJob(mostUrgentJobId)}
                                className="inline-flex items-center gap-1.5 rounded-[calc(0.375rem-1px)] bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
                            >
                                Review next
                                <ArrowRight className="size-4" />
                            </button>
                        </GlowBorder>
                    ) : (
                        // Gated on the SAME number the board shows, not on a
                        // separate waiting count: "Nothing needs you" used to be
                        // able to render beside "2 late · 1 missed".
                        <span className="inline-flex items-center gap-2 text-sm text-muted-foreground">
                            <RadioTower
                                className="size-4 text-primary"
                                aria-hidden
                            />
                            All covered
                        </span>
                    )}
                </AuroraHero>

                {/* Fail honest: scope unknown means we show everything, but
                    say so — the server still rejects out-of-scope decisions. */}
                {scopeError && (
                    <p className="rounded-md border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
                        Couldn&apos;t verify your market scope; showing all
                        stations. Decisions are still checked server-side.
                    </p>
                )}

                {/* One row per station: who, when it airs, what will air, and how
                    long there is to change it. The timeline this replaces drew
                    every slot as a mark on a clock — six marks a lane, of which
                    one was ever actionable — and needed a seven-colour legend to
                    decode. These are the only four facts an operator acts on. */}
                {lanes.length > 0 && (
                    <SpotlightCard className="p-4 sm:p-5">
                        {/* One shared clock across the lanes, so the stations can
                            be compared at a glance; each lane then carries only
                            its OWN schedule. */}
                        <div
                            className={cn(
                                LANE_GRID,
                                'mb-1 text-2xs uppercase tracking-wide text-muted-foreground-subtle'
                            )}
                        >
                            <span>Station · airtime</span>
                            <div className="relative h-4">
                                <span
                                    className="absolute -translate-x-1/2 font-mono text-3xs font-semibold normal-case text-primary"
                                    style={{ left: `${trackPct(now, now)}%` }}
                                >
                                    now
                                </span>
                                {axisTicks.map((t) => {
                                    const at = trackPct(t, now)
                                    if (Math.abs(at - trackPct(now, now)) < 7)
                                        return null
                                    return (
                                        <span
                                            key={t}
                                            className="absolute -translate-x-1/2 font-mono text-3xs tabular-nums normal-case"
                                            style={{ left: `${at}%` }}
                                        >
                                            {formatShortTime(new Date(t))}
                                        </span>
                                    )
                                })}
                            </div>
                            <span>What airs</span>
                            <span className="text-right">Cutoff</span>
                            <span className="sr-only">Action</span>
                        </div>

                        {airPlan.map((row) => (
                            <StationLane
                                key={row.marketId}
                                row={row}
                                onSelectJob={onSelectJob}
                            />
                        ))}

                        {pausedLanes.length > 0 && (
                            <div className="mt-3 border-t border-border/40 pt-2">
                                <button
                                    type="button"
                                    onClick={() => setPausedOpen((v) => !v)}
                                    className="flex items-center gap-1.5 text-2xs text-muted-foreground hover:text-foreground"
                                >
                                    <ChevronDown
                                        className={cn(
                                            'size-3 transition-transform',
                                            !pausedOpen && '-rotate-90'
                                        )}
                                        aria-hidden
                                    />
                                    {pausedLanes.length} stations {pausedPhrase}
                                </button>
                                {pausedOpen && (
                                    <div className="mt-1.5 flex flex-wrap gap-x-4 gap-y-1 pl-4">
                                        {pausedLanes.map((lane) => (
                                            <span
                                                key={lane.marketId}
                                                className="text-2xs text-muted-foreground-subtle"
                                            >
                                                {lane.station}
                                            </span>
                                        ))}
                                    </div>
                                )}
                            </div>
                        )}
                    </SpotlightCard>
                )}

                {isLoading && lanes.length === 0 && (
                    <SpotlightCard className="p-4 sm:p-5">
                        <div className="flex flex-col gap-3">
                            {[0, 1, 2, 3].map((i) => (
                                <Skeleton key={i} className="h-9 w-full" />
                            ))}
                        </div>
                    </SpotlightCard>
                )}

                {!isLoading && lanes.length === 0 && (
                    <EmptyState
                        variant="section"
                        action={
                            <Link
                                to={ROUTE_STATIONS}
                                className="inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline"
                            >
                                Set up a station
                                <ArrowRight className="size-3.5" aria-hidden />
                            </Link>
                        }
                    >
                        No stations configured. The rundown lights up once a
                        station has a schedule.
                    </EmptyState>
                )}
            </div>
        </div>
    )
}
