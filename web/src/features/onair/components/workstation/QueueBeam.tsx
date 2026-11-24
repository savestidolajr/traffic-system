import { memo, useEffect, useRef } from 'react'
import { ArrowRight } from 'lucide-react'
import { Link } from 'react-router'
import { ROUTE_HISTORY, ROUTE_STATIONS } from '@/lib/routes'
import { useNow } from '@/hooks/use-second-tick'
import { PIPELINE_STATUSES } from '@/features/jobs/config/status-values'
import {
    getUrgencyBand,
    urgencyBandMeta,
    type UrgencyBand,
    formatOverdue,
    APPROVAL_WINDOW_MINUTES,
    getApprovalDeadline,
} from '@/features/jobs/lib/job-meta/urgency'
import type { JobSummary, Status } from '@/features/jobs/types'
import { bindingSecondsRemaining } from '@/features/jobs/hooks/use-binding-deadlines'
import { isSameLabel } from '@/features/jobs/lib/job-meta/job-label'
import { STATUS_CONFIG } from '@/features/jobs/config/status-config'
import { MovingBorder } from '@/components/ui/aceternity/moving-border'
import { Spinner } from '@/components/ui/spinner'
import { Skeleton } from '@/components/ui/skeleton'
import { ScrollArea } from '@/components/ui/scroll-area'
import { EmptyState } from '@/components/EmptyState'
import {
    formatDuration,
    formatMarketName,
    formatRelativeTime,
    parseTimestamp,
} from '@/lib/formatting'
import { cn } from '@/lib/utils'

import {
    Tooltip,
    TooltipContent,
    TooltipProvider,
    TooltipTrigger,
} from '@/components/ui/tooltip'
interface QueueBeamProps {
    readonly jobs: readonly JobSummary[]
    /**
     * The deadline that actually binds per waiting bulletin — the earlier of the
     * slot cutoff and the approval dwell. Passed in rather than resolved here so
     * the rail, the board and the nav badge cannot disagree about which clock is
     * running out (they used to: same job, "in 7:27" here vs "0:31" there).
     */
    readonly deadlines: Map<string, Date>
    readonly isLoading: boolean
    readonly selectedJobId: string | null
    readonly onSelect: (jobId: string) => void
    /** Epoch ms of the last successful jobs fetch, for the freshness pill. */
    readonly updatedAt?: number
}

/** Keycap for the shortcut legend. */
function Kbd({ children }: { readonly children: React.ReactNode }) {
    return (
        <kbd className="rounded border border-border/60 bg-muted/60 px-1 font-mono text-3xs text-muted-foreground">
            {children}
        </kbd>
    )
}

/**
 * "Is this thing on?" — a deadline tool must show its data is alive. Green
 * pulse while fresh; flips to an amber "stale" warning if updates stop.
 */
function FreshnessPill({ updatedAt }: { readonly updatedAt?: number }) {
    const now = useNow()
    if (updatedAt === undefined) return null
    const ageSecs = Math.max(0, Math.floor((now - updatedAt) / 1000))
    const stale = ageSecs > 60
    return (
        <span
            className={cn(
                'inline-flex items-center gap-1.5 text-2xs',
                stale ? 'font-medium text-warning' : 'text-muted-foreground'
            )}
        >
            <span className="relative flex size-1.5">
                {!stale && (
                    <span className="absolute inline-flex h-full w-full rounded-full bg-success opacity-75 motion-safe:animate-ping" />
                )}
                <span
                    className={cn(
                        'relative inline-flex size-1.5 rounded-full',
                        stale ? 'bg-warning' : 'bg-success'
                    )}
                />
            </span>
            {stale
                ? `Stale · last update ${formatDuration(ageSecs)} ago`
                : ageSecs < 5
                  ? 'Live · just now'
                  : `Live · ${ageSecs}s ago`}
        </span>
    )
}

/** Countdown-ring geometry: radius of the SVG ring around waiting nodes. */
const RING_R = 8
const RING_C = 2 * Math.PI * RING_R

/**
 * The approval window, depleting: a ring that empties as the deadline
 * approaches, banded by urgency. Overdue renders a full pulsing ring.
 * Sits directly on the spine — the job's node IS its deadline.
 */
function CountdownRing({
    secs,
    band,
}: {
    readonly secs: number
    readonly band: UrgencyBand
}) {
    const windowSecs = APPROVAL_WINDOW_MINUTES * 60
    const remaining = Math.min(Math.max(secs / windowSecs, 0), 1)
    return (
        <svg
            viewBox="0 0 20 20"
            className={cn(
                'size-5 shrink-0 -rotate-90 rounded-full bg-background',
                band === 'critical' && 'motion-safe:animate-pulse'
            )}
            aria-hidden
        >
            <circle
                cx="10"
                cy="10"
                r={RING_R}
                fill="none"
                strokeWidth="2.5"
                className="stroke-muted"
            />
            <circle
                cx="10"
                cy="10"
                r={RING_R}
                fill="none"
                strokeWidth="2.5"
                strokeLinecap="round"
                strokeDasharray={RING_C}
                strokeDashoffset={
                    band === 'overdue' ? 0 : RING_C * (1 - remaining)
                }
                className={urgencyBandMeta(band).strokeClass}
            />
        </svg>
    )
}

/** Six tiny segments showing how far through the pipeline a job is. Each
 *  dot has a tooltip naming its stage — the only progress cue on mobile. */
function StageSegments({ stage }: { readonly stage: number }) {
    return (
        <TooltipProvider>
            <span className="flex items-center gap-0.5">
                {PIPELINE_STATUSES.map((status, i) => {
                    const label =
                        STATUS_CONFIG[status as Status]?.label ?? status
                    return (
                        <Tooltip key={status}>
                            <TooltipTrigger asChild>
                                <span
                                    className={cn(
                                        'inline-block h-1.5 w-3 rounded-full',
                                        i < stage
                                            ? i === stage - 1
                                                ? 'bg-primary motion-safe:animate-pulse'
                                                : 'bg-primary/50'
                                            : 'bg-muted-foreground/25'
                                    )}
                                />
                            </TooltipTrigger>
                            <TooltipContent side="bottom">
                                <p className="text-xs">{label}</p>
                            </TooltipContent>
                        </Tooltip>
                    )
                })}
            </span>
        </TooltipProvider>
    )
}

/**
 * A node pinned to the spine, vertically centred on its row. Rows sit in
 * an mx-2 wrapper, so 2px here + the 8px margin centres a size-5 node on
 * the spine at x=20.
 */
function SpineNode({ children }: { readonly children: React.ReactNode }) {
    return (
        <span className="absolute left-[2px] top-1/2 z-10 -translate-y-1/2">
            {children}
        </span>
    )
}

/** Map a Done-item status variant to a text-color class for subtle tinting. */
function doneStatusTextColor(variant: string): string {
    switch (variant) {
        case 'approved':
            return 'text-success'
        case 'rejected':
            return 'text-destructive-on-tint'
        case 'fallback':
            return 'text-warning'
        case 'destructive':
            return 'text-destructive-on-tint'
        default:
            return 'text-muted-foreground'
    }
}

/** Scroll the row into view when it becomes the selected job. Shared by
 *  every QueueItem variant. */
function useScrollIntoViewWhenSelected(selected: boolean) {
    const ref = useRef<HTMLButtonElement>(null)
    useEffect(() => {
        if (selected && ref.current) {
            ref.current.scrollIntoView({ block: 'nearest' })
        }
    }, [selected])
    return ref
}

const ITEM_BASE_CLASSES =
    'group/item relative w-full rounded-lg pl-10 pr-3 text-left transition-all duration-150 motion-reduce:transition-none'

/** Selection is neutral; urgency stays in the node/countdown colors. */
function selectedClasses(selected: boolean): string {
    return selected
        ? 'bg-muted ring-1 ring-inset ring-border'
        : 'hover:bg-muted/60 focus-visible:bg-muted/60'
}

/** WAITING_APPROVAL row: ring node + station + live countdown. Owns its own
 *  `useSecondTick()` subscription (NOT inherited from a parent tick) so only
 *  the handful of rows actually showing a countdown re-render every second —
 *  Generating/Done rows (memoized below) never do, and this component skips
 *  re-rendering when its own props are unchanged between polls even though
 *  its internal tick still drives a re-render on its own 1s cadence. */
const WaitingQueueItem = memo(function WaitingQueueItem({
    job,
    deadline,
    selected,
    onSelect,
}: {
    readonly job: JobSummary
    readonly deadline?: Date
    readonly selected: boolean
    readonly onSelect: (jobId: string) => void
}) {
    const now = useNow()
    const ref = useScrollIntoViewWhenSelected(selected)
    const at = deadline ?? getApprovalDeadline(job)
    const secs = at ? Math.floor((at.getTime() - now) / 1000) : 0
    const band = getUrgencyBand(secs)
    const overdueLabel =
        band === 'overdue' ? `+${formatOverdue(Math.abs(secs))}` : null
    return (
        <button
            ref={ref}
            type="button"
            onClick={() => onSelect(job.jobId)}
            aria-current={selected ? 'true' : undefined}
            className={cn(
                ITEM_BASE_CLASSES,
                selectedClasses(selected),
                'grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-2.5 py-2.5',
                band === 'overdue' || band === 'critical'
                    ? 'bg-destructive/[0.07]'
                    : band === 'warning'
                      ? 'bg-status-waiting/[0.06]'
                      : ''
            )}
        >
            <SpineNode>
                <CountdownRing secs={secs} band={band} />
            </SpineNode>
            <span className="flex min-w-0 flex-col overflow-hidden">
                <span className="min-w-0 truncate text-sm font-medium">
                    {job.station}
                </span>
                {!isSameLabel(job.station, job.market) && (
                    <span className="min-w-0 truncate text-xs text-muted-foreground">
                        {formatMarketName(job.market)}
                    </span>
                )}
            </span>
            <span
                className={cn(
                    'justify-self-end whitespace-nowrap font-mono text-sm font-semibold tabular-nums',
                    urgencyBandMeta(band).textClass
                )}
            >
                {overdueLabel ?? `in ${formatDuration(secs)}`}
            </span>
        </button>
    )
})

/** In-pipeline row: spinner node + station + stage segments. No live
 *  countdown — memoized so it never re-renders on the 1Hz tick that only
 *  WaitingQueueItem subscribes to; only real data changes move it. */
const GeneratingQueueItem = memo(function GeneratingQueueItem({
    job,
    selected,
    onSelect,
}: {
    readonly job: JobSummary
    readonly selected: boolean
    readonly onSelect: (jobId: string) => void
}) {
    const ref = useScrollIntoViewWhenSelected(selected)
    const meta = STATUS_CONFIG[job.status] ?? STATUS_CONFIG.REQUESTED
    const stage =
        (PIPELINE_STATUSES as readonly string[]).indexOf(job.status) + 1
    return (
        <button
            ref={ref}
            type="button"
            onClick={() => onSelect(job.jobId)}
            aria-current={selected ? 'true' : undefined}
            className={cn(
                ITEM_BASE_CLASSES,
                selectedClasses(selected),
                'grid grid-cols-[minmax(0,1fr)_auto] grid-rows-2 items-center gap-x-2 gap-y-0.5 py-2'
            )}
        >
            <SpineNode>
                <span className="flex size-5 items-center justify-center rounded-full border border-primary/40 bg-background text-primary">
                    <Spinner className="size-3" />
                </span>
            </SpineNode>
            <span className="min-w-0 truncate text-sm font-medium">
                {job.station}
            </span>
            <span className="justify-self-end whitespace-nowrap text-2xs text-muted-foreground">
                {meta.activityLabel}
            </span>
            <span className="min-w-0 truncate text-xs text-muted-foreground">
                {!isSameLabel(job.station, job.market) &&
                    formatMarketName(job.market)}
            </span>
            <span className="justify-self-end">
                <StageSegments stage={stage} />
                {/* The segments are decorative; the stage is the only part of
                    this row an assistive-tech user cannot otherwise read. Named
                    from content rather than aria-label so the accessible name
                    contains the visible text verbatim (WCAG 2.5.3). */}
                <span className="sr-only">
                    , stage {stage} of {PIPELINE_STATUSES.length}
                </span>
            </span>
        </button>
    )
})

/** Terminal row: outcome node + one quiet line. No live countdown —
 *  memoized, same rationale as GeneratingQueueItem. */
const DoneQueueItem = memo(function DoneQueueItem({
    job,
    selected,
    onSelect,
}: {
    readonly job: JobSummary
    readonly selected: boolean
    readonly onSelect: (jobId: string) => void
}) {
    const ref = useScrollIntoViewWhenSelected(selected)
    const meta = STATUS_CONFIG[job.status] ?? STATUS_CONFIG.REQUESTED
    const Icon = meta.icon
    return (
        <button
            ref={ref}
            type="button"
            onClick={() => onSelect(job.jobId)}
            aria-current={selected ? 'true' : undefined}
            className={cn(
                ITEM_BASE_CLASSES,
                selectedClasses(selected),
                'grid grid-cols-[minmax(0,1fr)_auto_auto] items-center gap-x-1.5 py-1.5'
            )}
        >
            <SpineNode>
                <span className="flex size-5 items-center justify-center rounded-full bg-background">
                    <Icon
                        className={cn(
                            'size-3.5',
                            doneStatusTextColor(meta.variant)
                        )}
                        aria-hidden
                    />
                </span>
            </SpineNode>
            <span className="min-w-0 truncate text-sm">{job.station}</span>
            <span
                className={cn(
                    'whitespace-nowrap text-2xs',
                    doneStatusTextColor(meta.variant)
                )}
            >
                {meta.label}
            </span>
            <span className="whitespace-nowrap text-2xs text-muted-foreground">
                {formatRelativeTime(job.lastUpdatedAt)}
            </span>
        </button>
    )
})

/** Dispatches to the right row variant by job state. Deliberately NOT
 *  memoized itself (it's a cheap branch); the expensive leaves above are. */
function QueueItem({
    job,
    deadline,
    isWaiting,
    selected,
    onSelect,
}: {
    readonly job: JobSummary
    readonly deadline?: Date
    readonly isWaiting: boolean
    readonly selected: boolean
    readonly onSelect: (jobId: string) => void
}) {
    if (isWaiting) {
        return (
            <WaitingQueueItem
                job={job}
                deadline={deadline}
                selected={selected}
                onSelect={onSelect}
            />
        )
    }
    const isPipeline = (PIPELINE_STATUSES as readonly string[]).includes(
        job.status
    )
    if (isPipeline) {
        return (
            <GeneratingQueueItem
                job={job}
                selected={selected}
                onSelect={onSelect}
            />
        )
    }
    return <DoneQueueItem job={job} selected={selected} onSelect={onSelect} />
}

/**
 * The hottest waiting bulletin gets the traveling-light border — "this
 * one, now" — tinted by how close the deadline is.
 */
const UrgentWrapped = memo(function UrgentWrapped({
    urgent,
    band,
    children,
}: {
    readonly urgent: boolean
    readonly band: UrgencyBand
    readonly children: React.ReactNode
}) {
    if (!urgent) return <div className="mx-2">{children}</div>
    return (
        <MovingBorder
            className="mx-2"
            tone={
                band === 'overdue' || band === 'critical'
                    ? 'destructive'
                    : band === 'warning'
                      ? 'warning'
                      : 'primary'
            }
        >
            {children}
        </MovingBorder>
    )
})
const Group = memo(function Group({
    label,
    jobs,
    deadlines,
    selectedJobId,
    onSelect,
    urgentFirst,
    isWaiting,
    dim,
    linkTo,
}: {
    readonly label: string
    readonly jobs: readonly JobSummary[]
    readonly deadlines: Map<string, Date>
    readonly selectedJobId: string | null
    readonly onSelect: (jobId: string) => void
    readonly urgentFirst?: boolean
    readonly isWaiting?: boolean
    readonly dim?: boolean
    readonly linkTo?: { readonly to: string; readonly label: string }
}) {
    const now = useNow()
    if (jobs.length === 0) return null
    return (
        <div className="flex flex-col gap-1">
            {/* Solid band, not a fade: rows scrolling underneath must never
                show through and overlap the label. The group's dimming lives
                on the rows so this stays fully opaque even in the archive. */}
            <div className="sticky top-0 z-20 flex items-center gap-2 bg-background py-1 pl-12 pr-4 pt-3">
                <span className="text-xs font-semibold text-muted-foreground">
                    {label}
                </span>
                <span className="inline-flex h-4 min-w-4 items-center justify-center rounded-full bg-muted px-1 font-mono text-3xs tabular-nums text-muted-foreground">
                    {jobs.length}
                </span>
            </div>
            <div className={cn('flex flex-col gap-1', dim && 'opacity-70')}>
                {jobs.map((job, i) => {
                    const deadline = deadlines.get(job.jobId)
                    const at = deadline ?? getApprovalDeadline(job)
                    const secs =
                        isWaiting && at
                            ? Math.floor((at.getTime() - now) / 1000)
                            : null
                    return (
                        <UrgentWrapped
                            key={job.jobId}
                            urgent={
                                urgentFirst === true &&
                                i === 0 &&
                                job.jobId !== selectedJobId
                            }
                            band={getUrgencyBand(secs)}
                        >
                            <QueueItem
                                job={job}
                                deadline={deadline}
                                isWaiting={isWaiting ?? false}
                                selected={job.jobId === selectedJobId}
                                onSelect={onSelect}
                            />
                        </UrgentWrapped>
                    )
                })}
                {linkTo && (
                    <Link
                        to={linkTo.to}
                        className="mt-1 inline-flex items-center gap-1 pl-10 text-xs font-medium text-muted-foreground transition-colors hover:text-foreground"
                    >
                        {linkTo.label}
                        <ArrowRight className="size-3" />
                    </Link>
                )}
            </div>
        </div>
    )
})

/**
 * The live triage queue as a beam: one continuous spine, top-to-bottom in
 * priority order — bulletins needing a human (countdown rings as nodes,
 * hottest wrapped in a traveling-light border), the machine's in-flight
 * work (spinner nodes + stage segments), recent outcomes (status nodes,
 * one quiet line each). The same track vocabulary as the audit timeline,
 * the pipeline stepper, and the rundown board. Never paginated.
 */
export function QueueBeam({
    jobs,
    deadlines,
    isLoading,
    selectedJobId,
    onSelect,
    updatedAt,
}: QueueBeamProps) {
    // One clock for the rail, the board and the badge: the earlier of the slot
    // cutoff and the approval dwell.
    const secondsFor = (job: JobSummary) =>
        bindingSecondsRemaining(deadlines, job)

    if (isLoading) {
        return (
            <div className="flex flex-col gap-2 p-3">
                {Array.from({ length: 6 }, (_, i) => (
                    <Skeleton key={i} className="h-12 w-full" />
                ))}
            </div>
        )
    }

    const waiting = jobs
        .filter((j) => j.status === 'WAITING_APPROVAL')
        .slice()
        .sort(
            (a, b) => (secondsFor(a) ?? Infinity) - (secondsFor(b) ?? Infinity)
        )
    const inPipeline = jobs
        .filter((j) =>
            (PIPELINE_STATUSES as readonly string[]).includes(j.status)
        )
        .slice()
        .sort(
            (a, b) => parseTimestamp(b.createdAt) - parseTimestamp(a.createdAt)
        )
    const done = jobs
        .filter(
            (j) =>
                !(PIPELINE_STATUSES as readonly string[]).includes(j.status) &&
                j.status !== 'WAITING_APPROVAL'
        )
        .slice()
        .sort(
            (a, b) =>
                parseTimestamp(b.lastUpdatedAt) -
                parseTimestamp(a.lastUpdatedAt)
        )
        .slice(0, 8)

    return (
        <div className="flex h-full flex-col bg-linear-to-b from-muted/30 to-transparent">
            <div className="flex items-center justify-between border-b border-border/40 px-4 py-1.5">
                <FreshnessPill updatedAt={updatedAt} />
            </div>
            <ScrollArea className="min-h-0 flex-1">
                <nav
                    aria-label="Bulletin queue"
                    aria-keyshortcuts="j k Enter"
                    className="relative flex flex-col gap-1 pb-3"
                >
                    {/* The spine: hot at the top where deadlines live,
                        cooling toward the archive at the bottom */}
                    {jobs.length > 0 && (
                        <div
                            aria-hidden
                            className="absolute bottom-2 left-[19px] top-2 w-[2px] bg-linear-to-b from-destructive/60 via-primary/50 to-muted-foreground/20 [mask-image:linear-gradient(to_bottom,transparent,black_24px,black_85%,transparent)]"
                        />
                    )}
                    <Group
                        label="Needs review"
                        deadlines={deadlines}
                        jobs={waiting}
                        selectedJobId={selectedJobId}
                        onSelect={onSelect}
                        urgentFirst
                        isWaiting
                    />
                    <Group
                        label="Generating"
                        deadlines={deadlines}
                        jobs={inPipeline}
                        selectedJobId={selectedJobId}
                        onSelect={onSelect}
                    />
                    <Group
                        label="Done"
                        deadlines={deadlines}
                        jobs={done}
                        selectedJobId={selectedJobId}
                        onSelect={onSelect}
                        dim
                        linkTo={{
                            to: ROUTE_HISTORY,
                            label: 'View all in History',
                        }}
                    />
                    {jobs.length === 0 && (
                        <EmptyState
                            variant="section"
                            action={
                                <Link
                                    to={ROUTE_STATIONS}
                                    className="inline-flex items-center gap-1 text-xs font-medium text-primary hover:underline"
                                >
                                    Trigger one from Stations
                                    <ArrowRight
                                        className="size-3"
                                        aria-hidden
                                    />
                                </Link>
                            }
                        >
                            <p className="text-sm">
                                No bulletins yet; they appear when a station
                                schedule runs.
                            </p>
                        </EmptyState>
                    )}
                </nav>
            </ScrollArea>
            {/* The shortcuts exist — say so. Hidden on touch-first screens. */}
            <div className="hidden items-center gap-2 border-t border-border/40 px-4 py-2 text-2xs text-muted-foreground md:flex">
                <span className="inline-flex items-center gap-1">
                    <Kbd>J</Kbd>
                    <Kbd>K</Kbd> navigate
                </span>
                <span className="inline-flex items-center gap-1">
                    <Kbd>↵</Kbd> open
                </span>
                <span className="inline-flex items-center gap-1">
                    <Kbd>R</Kbd> revise
                </span>
            </div>
        </div>
    )
}
