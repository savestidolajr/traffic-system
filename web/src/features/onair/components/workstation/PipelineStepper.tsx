import {
    useJobProgress,
    type JobProgressQuery,
} from '@/features/jobs/api/progress-hooks'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { Spinner } from '@/components/ui/spinner'
import { useNow } from '@/hooks/use-second-tick'
import {
    isJobProgressUnavailable,
    type JobProgress,
    type JobProgressEvent,
} from '@/features/jobs/api/progress-schemas'
import { useAirSlot } from '@/features/jobs/hooks/use-air-slot'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import { STATUS_CONFIG } from '@/features/jobs/config/status-config'
import {
    hasScript,
    hasTraffic,
    type Job,
    type Status,
} from '@/features/jobs/types'
import { ICON } from '@/lib/design/icons'
import { formatSlotClock } from '@/features/onair/lib/air-plan'
import {
    formatMarketName,
    formatRelativeTime,
    formatTime,
    formatTookSec,
    parseTimestamp,
} from '@/lib/formatting'
import { cn } from '@/lib/utils'
import type { LucideIcon } from 'lucide-react'

const PIPELINE_GRID_COLS = 'grid-cols-[88px_36px_minmax(0,1fr)]'
const BULLETIN_GRID_COLS =
    'grid grid-cols-[90px_minmax(0,1fr)] items-baseline gap-2'

const PROGRESS_RAIL_STAGES = [
    'Evidence',
    'Select',
    'Write',
    'Compose',
    'FitWords',
    'Edit',
    'Gates',
    'Voice',
    'FitAudio',
] as const
type ProgressRailStage = (typeof PROGRESS_RAIL_STAGES)[number]

const PROGRESS_STAGE_LABELS: Record<ProgressRailStage, string> = {
    Evidence: 'Evidence',
    Select: 'Select and rank',
    Write: 'Write',
    Compose: 'Compose',
    FitWords: 'Fit words',
    Edit: 'Edit',
    Gates: 'Gates',
    Voice: 'Voice',
    FitAudio: 'Fit audio',
}

const PROGRESS_STAGE_ICONS: Record<ProgressRailStage, LucideIcon> = {
    Evidence: ICON.traffic,
    Select: ICON.coverage,
    Write: ICON.ai,
    Compose: ICON.ai,
    FitWords: ICON.duration,
    Edit: ICON.edit,
    Gates: ICON.safety,
    Voice: ICON.voice,
    FitAudio: ICON.audio,
}

const FALLBACK_RAIL_STAGES = [
    'REQUESTED',
    'HERE_FETCHED',
    'INCIDENTS_SELECTED',
    'SCRIPT_READY',
] as const
const FALLBACK_STAGES = [...FALLBACK_RAIL_STAGES, 'WAITING_APPROVAL'] as const

/** Check if the latest action or state on the job is a verbatim re-voice. */
export function isRevoiceJob(job: Job): boolean {
    const lastHistory = job.statusHistory[job.statusHistory.length - 1]
    if (lastHistory?.note?.toLowerCase().includes('re-voice')) return true
    const lastLog = job.logs[job.logs.length - 1]
    if (lastLog?.message.toLowerCase().includes('re-voic')) return true
    return false
}

function formatShortDuration(totalSeconds: number): string {
    const seconds = Math.max(0, Math.round(Math.abs(totalSeconds)))
    if (seconds < 60) return `${seconds}s`
    const minutes = Math.floor(seconds / 60)
    const remainder = seconds % 60
    return remainder === 0 ? `${minutes}m` : `${minutes}m ${remainder}s`
}
function formatSlotClockWithSeconds(date: Date, timezone: string): string {
    try {
        return new Intl.DateTimeFormat('en-GB', {
            timeZone: timezone,
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: false,
        }).format(date)
    } catch {
        return new Intl.DateTimeFormat('en-GB', {
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: false,
        }).format(date)
    }
}

function formatMargin(seconds: number): string {
    return `${seconds >= 0 ? '+' : '-'}${formatShortDuration(seconds)}`
}

function latestEvent(
    events: readonly JobProgressEvent[],
    predicate: (event: JobProgressEvent) => boolean
): JobProgressEvent | undefined {
    return [...events]
        .filter(predicate)
        .sort((a, b) => parseTimestamp(b.at) - parseTimestamp(a.at))[0]
}

function stageCompletedEvent(
    events: readonly JobProgressEvent[],
    stage: ProgressRailStage
): JobProgressEvent | undefined {
    return latestEvent(
        events,
        (event) => event.stage === stage && event.kind === 'StageCompleted'
    )
}

function stageDurationSeconds(
    events: readonly JobProgressEvent[],
    stage: ProgressRailStage,
    completed: JobProgressEvent
): number | null {
    if (completed.durationMs !== null && completed.durationMs !== undefined) {
        return completed.durationMs / 1000
    }
    const started = latestEvent(
        events,
        (event) => event.stage === stage && event.kind === 'StageStarted'
    )
    if (!started) return null
    const duration =
        (parseTimestamp(completed.at) - parseTimestamp(started.at)) / 1000
    return Number.isFinite(duration) && duration >= 0 ? duration : null
}

function formatCount(value: number): string {
    return Number.isInteger(value) ? String(value) : value.toFixed(1)
}

function formatSubStep(event: JobProgressEvent): string {
    const counts = event.counts
    if (counts?.candidate !== undefined && counts.words !== undefined) {
        return `candidate ${formatCount(counts.candidate)}: ${formatCount(counts.words)} words`
    }
    if (counts?.round !== undefined && counts.words !== undefined) {
        return `round ${formatCount(counts.round)}: ${formatCount(counts.words)} words`
    }
    if (event.stage === 'Edit' && event.detail) {
        const detail = event.detail
            .replace(/[.]$/, '')
            .replace(/^editor\s+/i, '')
        return `editor: ${detail.toLowerCase()}`
    }
    if (event.detail) return event.detail
    if (counts) {
        return Object.entries(counts)
            .map(([key, value]) => `${key} ${formatCount(value)}`)
            .join(', ')
    }
    return 'Step completed'
}

function stageSubSteps(
    progress: JobProgress,
    stage: ProgressRailStage
): readonly string[] {
    const events = progress.events
        .filter(
            (event) =>
                event.attempt === progress.attempt &&
                event.stage === stage &&
                event.kind === 'SubStep'
        )
        .sort((a, b) => parseTimestamp(a.at) - parseTimestamp(b.at))
    if (stage !== 'Gates') return events.map(formatSubStep)
    // Gates emit one sub-step per deterministic check with Counts.passed. A live
    // run showed eight passed-check sentences as chips, which buries the one
    // outcome the operator needs: did anything fail. Name only the failures and
    // fold every pass into a single count.
    const failed = events.filter((event) => event.counts?.passed === 0)
    const passedCount = events.length - failed.length
    const chips = failed.map((event) => `failed: ${formatSubStep(event)}`)
    if (passedCount > 0)
        chips.push(`${passedCount} check${passedCount === 1 ? '' : 's'} passed`)
    return chips
}

function progressStageDetail(
    progress: JobProgress,
    stage: ProgressRailStage
): string | null {
    const event = latestEvent(
        progress.events,
        (candidate) =>
            candidate.stage === stage &&
            (candidate.kind === 'StageStarted' ||
                candidate.kind === 'SubStep' ||
                candidate.kind === 'Note')
    )
    return event?.detail || null
}

function progressAttemptStartedAt(progress: JobProgress): string {
    const event = latestEvent(
        progress.events,
        (candidate) =>
            candidate.attempt === progress.attempt &&
            candidate.kind === 'AttemptStarted'
    )
    return event?.at ?? progress.stageStartedAt
}

function typicalDuration(
    progress: JobProgress,
    stage: ProgressRailStage
): number | undefined {
    const value = progress.typicalSeconds?.[stage]
    return typeof value === 'number' && Number.isFinite(value) && value >= 0
        ? value
        : undefined
}

function progressCurrentStage(progress: JobProgress): ProgressRailStage | null {
    if (
        PROGRESS_RAIL_STAGES.includes(
            progress.stage as (typeof PROGRESS_RAIL_STAGES)[number]
        )
    ) {
        return progress.stage as ProgressRailStage
    }
    const event = latestEvent(
        progress.events,
        (candidate) =>
            candidate.attempt === progress.attempt &&
            (candidate.kind === 'StageStarted' ||
                candidate.kind === 'AttemptFailed') &&
            PROGRESS_RAIL_STAGES.includes(
                candidate.stage as (typeof PROGRESS_RAIL_STAGES)[number]
            )
    )
    return (event?.stage as ProgressRailStage | undefined) ?? null
}

function currentStageIndex(progress: JobProgress): number {
    const stage = progressCurrentStage(progress)
    return stage ? PROGRESS_RAIL_STAGES.indexOf(stage) : -1
}

interface PipelineStepperProps {
    readonly job: Job
    readonly progress?: JobProgressQuery
}

/** The structured, append-only progress rail. */
function PipelineStepper({ job, progress }: PipelineStepperProps) {
    const now = useNow()
    const airSlotAt = useAirSlot(job)
    const schedulesQuery = useSchedules()
    // Station-local clock for the ETA. Match the schedule by station name, then
    // by market id; when neither resolves fall back to the browser zone rather
    // than UTC, which is never a zone an Australian operator reads in.
    const schedules = schedulesQuery.data?.schedules
    const timezone: string =
        schedules?.find((schedule) => schedule.station === job.station)
            ?.timezone ??
        schedules?.find((schedule) => schedule.marketId === job.market)
            ?.timezone ??
        Intl.DateTimeFormat().resolvedOptions().timeZone

    const document = progress?.data
    if (
        !document ||
        isJobProgressUnavailable(document) ||
        progressCurrentStage(document) === null
    ) {
        return <FallbackRail job={job} />
    }

    const currentStage = progressCurrentStage(document)
    const currentIndex = currentStageIndex(document)
    const attemptStart = parseTimestamp(progressAttemptStartedAt(document))
    const elapsedSeconds = Number.isFinite(attemptStart)
        ? Math.max(0, (now - attemptStart) / 1000)
        : 0
    const remainingTypical = PROGRESS_RAIL_STAGES.slice(currentIndex).reduce(
        (sum, stage) => sum + (typicalDuration(document, stage) ?? 0),
        0
    )
    const hasTypical = PROGRESS_RAIL_STAGES.slice(currentIndex).some(
        (stage) => typicalDuration(document, stage) !== undefined
    )
    const readyAt = hasTypical ? new Date(now + remainingTypical * 1000) : null
    const cutoffMargin = readyAt
        ? (airSlotAt?.getTime() ?? 0) - readyAt.getTime()
        : airSlotAt
          ? airSlotAt.getTime() - now
          : null

    const attemptStarted = latestEvent(
        document.events,
        (event) =>
            event.attempt === document.attempt &&
            event.kind === 'AttemptStarted'
    )
    const latestAttemptDetail =
        attemptStarted?.detail ?? 'A new generation attempt started.'
    const progressPercent =
        currentIndex >= 0
            ? ((currentIndex + (document.stage === 'Failed' ? 0.35 : 0.5)) /
                  (PROGRESS_RAIL_STAGES.length + 0.5)) *
              100
            : 0

    return (
        <div className="flex flex-col gap-5">
            {document.attempt > 1 && (
                <div
                    role="status"
                    className="rounded-lg border border-warning/30 bg-warning/10 px-3 py-2 text-xs text-warning"
                >
                    <span className="font-semibold">
                        Attempt {document.attempt}
                    </span>
                    <span className="ml-1.5">{latestAttemptDetail}</span>
                </div>
            )}

            <div className="relative">
                <div
                    aria-hidden
                    className="absolute bottom-2 left-[113px] top-2 w-[2px] bg-border/40 [mask-image:linear-gradient(to_bottom,black_80%,transparent)]"
                >
                    <div
                        className="w-full overflow-hidden"
                        style={{ height: `${Math.min(100, progressPercent)}%` }}
                    >
                        <div className="h-full w-full origin-top rounded-full bg-linear-to-b from-primary via-primary/60 to-primary/30 motion-safe:animate-beam-grow" />
                    </div>
                </div>

                <ol className="relative flex flex-col">
                    {PROGRESS_RAIL_STAGES.map((stage) => {
                        const completed = stageCompletedEvent(
                            document.events,
                            stage
                        )
                        const isRunning =
                            currentStage === stage &&
                            document.stage !== 'Failed'
                        const isFailed =
                            document.stage === 'Failed' &&
                            currentStage === stage
                        const isDone =
                            !isRunning && !isFailed && completed !== undefined
                        const elapsed = isRunning
                            ? Math.max(
                                  0,
                                  Math.floor(
                                      (now -
                                          parseTimestamp(
                                              document.stage === stage
                                                  ? document.stageStartedAt
                                                  : (completed?.at ??
                                                        document.stageStartedAt)
                                          )) /
                                          1000
                                  )
                              )
                            : 0
                        const duration = completed
                            ? stageDurationSeconds(
                                  document.events,
                                  stage,
                                  completed
                              )
                            : null
                        const typical = typicalDuration(document, stage)
                        const Icon = PROGRESS_STAGE_ICONS[stage]
                        const subSteps = stageSubSteps(document, stage)
                        const detail = progressStageDetail(document, stage)

                        return (
                            <li
                                key={stage}
                                className={`grid ${PIPELINE_GRID_COLS} gap-x-2 pb-7 last:pb-1`}
                            >
                                <div className="flex flex-col items-end gap-0.5 pt-1 text-right">
                                    {isRunning || isFailed ? (
                                        <span className="inline-flex items-center gap-0.5 font-mono text-3xs tabular-nums text-primary">
                                            <span>
                                                {formatShortDuration(elapsed)}
                                            </span>
                                            <span className="text-muted-foreground">
                                                elapsed
                                            </span>
                                        </span>
                                    ) : duration !== null ? (
                                        <span className="rounded-full bg-muted/60 px-1.5 py-0.5 font-mono text-3xs tabular-nums text-muted-foreground">
                                            {formatShortDuration(duration)}
                                        </span>
                                    ) : (
                                        <span className="font-mono text-xs text-muted-foreground-subtle">
                                            -
                                        </span>
                                    )}
                                </div>

                                <div className="relative flex justify-center pt-0.5">
                                    <div
                                        className={cn(
                                            'relative z-10 flex size-7 shrink-0 items-center justify-center rounded-full border bg-background',
                                            isFailed
                                                ? 'border-warning/60 text-warning shadow-glow'
                                                : isRunning
                                                  ? 'border-primary/50 text-primary shadow-glow'
                                                  : isDone
                                                    ? 'border-primary/40 text-primary'
                                                    : 'border-dashed border-border text-muted-foreground-subtle'
                                        )}
                                    >
                                        {(isRunning || isFailed) && (
                                            <span
                                                aria-hidden
                                                className={cn(
                                                    'absolute inset-0 rounded-full motion-safe:animate-ping',
                                                    isFailed
                                                        ? 'bg-warning/20'
                                                        : 'bg-primary/20'
                                                )}
                                            />
                                        )}
                                        {isDone ? (
                                            <ICON.success
                                                className="relative size-3.5"
                                                aria-hidden
                                            />
                                        ) : isRunning ? (
                                            <Spinner className="relative size-3.5" />
                                        ) : (
                                            <Icon
                                                className="relative size-3.5"
                                                aria-hidden
                                            />
                                        )}
                                    </div>
                                </div>

                                <div className="flex min-w-0 flex-col gap-1 pt-1">
                                    <span
                                        className={cn(
                                            'text-sm font-semibold tracking-tight',
                                            isFailed
                                                ? 'text-warning'
                                                : isRunning
                                                  ? 'text-foreground'
                                                  : isDone
                                                    ? 'text-muted-foreground'
                                                    : 'text-muted-foreground-subtle'
                                        )}
                                    >
                                        {PROGRESS_STAGE_LABELS[stage]}
                                    </span>
                                    {(isRunning || isFailed) && (
                                        <span className="wrap-break-word text-xs text-muted-foreground">
                                            {detail ??
                                                (isFailed
                                                    ? 'Generation stopped at this stage.'
                                                    : `${PROGRESS_STAGE_LABELS[stage]} in progress.`)}
                                            {typical !== undefined && (
                                                <span className="ml-1 text-muted-foreground-subtle">
                                                    usually ~
                                                    {formatShortDuration(
                                                        typical
                                                    )}
                                                </span>
                                            )}
                                        </span>
                                    )}
                                    {isDone && typical !== undefined && (
                                        <span className="text-xs text-muted-foreground-subtle">
                                            usually ~
                                            {formatShortDuration(typical)}
                                        </span>
                                    )}
                                    {subSteps.length > 0 &&
                                        (isDone || isRunning || isFailed) && (
                                            <div className="flex flex-wrap gap-1">
                                                {subSteps.map(
                                                    (subStep, index) => (
                                                        <span
                                                            key={`${stage}-${index}-${subStep}`}
                                                            className="rounded-full border border-border/60 bg-muted/40 px-1.5 py-0.5 text-3xs text-muted-foreground"
                                                        >
                                                            {subStep}
                                                        </span>
                                                    )
                                                )}
                                            </div>
                                        )}
                                </div>
                            </li>
                        )
                    })}
                </ol>
            </div>

            <p className="border-t pt-3 text-2xs tabular-nums text-muted-foreground">
                Attempt {document.attempt} of {document.maxAttempts} ·{' '}
                {formatShortDuration(elapsedSeconds)} elapsed
                {readyAt && (
                    <>
                        {' '}
                        · ready ≈{' '}
                        {formatSlotClockWithSeconds(readyAt, timezone)}
                    </>
                )}
                {airSlotAt && (
                    <>
                        {' · '}cutoff {formatSlotClock(airSlotAt, timezone)}
                        {cutoffMargin !== null &&
                            ` (${formatMargin(cutoffMargin / 1000)})`}
                    </>
                )}
            </p>
        </div>
    )
}

/** Small compatibility rail for jobs created before the progress document. */
function FallbackRail({ job }: { readonly job: Job }) {
    const now = useNow()
    const historyMap = new Map(
        job.statusHistory.map((entry) => [entry.status, entry])
    )
    let effectiveStatus: Status = job.status
    if (
        effectiveStatus === 'SYNTHESIZING_AUDIO' ||
        effectiveStatus === 'AUDIO_READY'
    ) {
        effectiveStatus = 'SCRIPT_READY'
    } else if (
        (effectiveStatus === 'REQUESTED' ||
            effectiveStatus === 'HERE_FETCHED') &&
        hasTraffic(job) &&
        job.traffic.incidents
    ) {
        effectiveStatus = 'INCIDENTS_SELECTED'
    }
    const currentIndex = FALLBACK_STAGES.indexOf(
        effectiveStatus as (typeof FALLBACK_STAGES)[number]
    )
    const latestLog =
        job.logs.length > 0
            ? job.logs.reduce((a, b) =>
                  parseTimestamp(b.at) >= parseTimestamp(a.at) ? b : a
              )
            : undefined

    return (
        <div className="relative">
            <div
                aria-hidden
                className="absolute bottom-2 left-[113px] top-2 w-[2px] bg-border/40 [mask-image:linear-gradient(to_bottom,black_80%,transparent)]"
            >
                <div
                    className="w-full overflow-hidden"
                    style={{
                        height:
                            currentIndex >= 0
                                ? `${((currentIndex + 0.5) / FALLBACK_STAGES.length) * 100}%`
                                : '0%',
                    }}
                >
                    <div className="h-full w-full rounded-full bg-linear-to-b from-primary via-primary/60 to-primary/30 motion-safe:animate-beam-grow" />
                </div>
            </div>
            <ol className="relative flex flex-col">
                {FALLBACK_RAIL_STAGES.map((stage, index) => {
                    const historyEntry = historyMap.get(stage)
                    const isCompleted = index < currentIndex
                    const isCurrent = index === currentIndex
                    const Icon = STATUS_CONFIG[stage].icon
                    const previous =
                        index > 0
                            ? historyMap.get(FALLBACK_RAIL_STAGES[index - 1])
                            : undefined
                    const tookSec =
                        historyEntry && previous
                            ? Math.round(
                                  (parseTimestamp(historyEntry.at) -
                                      parseTimestamp(previous.at)) /
                                      1000
                              )
                            : null
                    const elapsed = isCurrent
                        ? Math.max(
                              0,
                              Math.floor(
                                  (now -
                                      parseTimestamp(
                                          historyEntry?.at ?? job.createdAt
                                      )) /
                                      1000
                              )
                          )
                        : 0
                    return (
                        <li
                            key={stage}
                            className={`grid ${PIPELINE_GRID_COLS} gap-x-2 pb-7 last:pb-1`}
                        >
                            <div className="flex flex-col items-end gap-0.5 pt-1 text-right">
                                {historyEntry && !isCurrent ? (
                                    <time
                                        dateTime={historyEntry.at}
                                        title={formatRelativeTime(
                                            historyEntry.at
                                        )}
                                        className="font-mono text-xs tabular-nums text-muted-foreground"
                                    >
                                        {formatTime(historyEntry.at)}
                                    </time>
                                ) : isCurrent ? (
                                    <span className="font-mono text-3xs tabular-nums text-primary">
                                        {formatShortDuration(elapsed)} elapsed
                                    </span>
                                ) : (
                                    <span className="font-mono text-xs text-muted-foreground-subtle">
                                        -
                                    </span>
                                )}
                                {tookSec !== null && !isCurrent && (
                                    <span className="rounded-full bg-muted/60 px-1.5 py-0.5 font-mono text-3xs tabular-nums text-muted-foreground">
                                        {formatTookSec(tookSec)}
                                    </span>
                                )}
                            </div>
                            <div className="relative flex justify-center pt-0.5">
                                <div
                                    className={cn(
                                        'relative z-10 flex size-7 shrink-0 items-center justify-center rounded-full border bg-background',
                                        isCurrent
                                            ? 'border-primary/50 text-primary shadow-glow'
                                            : isCompleted
                                              ? 'border-primary/40 text-primary'
                                              : 'border-dashed border-border text-muted-foreground-subtle'
                                    )}
                                >
                                    {isCurrent && (
                                        <span
                                            aria-hidden
                                            className="absolute inset-0 rounded-full bg-primary/20 motion-safe:animate-ping"
                                        />
                                    )}
                                    {isCompleted ? (
                                        <ICON.success
                                            className="relative size-3.5"
                                            aria-hidden
                                        />
                                    ) : isCurrent ? (
                                        <Spinner className="relative size-3.5" />
                                    ) : (
                                        <Icon
                                            className="relative size-3.5"
                                            aria-hidden
                                        />
                                    )}
                                </div>
                            </div>
                            <div className="flex min-w-0 flex-col gap-1 pt-1">
                                <span
                                    className={cn(
                                        'text-sm font-semibold tracking-tight',
                                        isCurrent
                                            ? 'text-foreground'
                                            : isCompleted
                                              ? 'text-muted-foreground'
                                              : 'text-muted-foreground-subtle'
                                    )}
                                >
                                    {STATUS_CONFIG[stage].label}
                                </span>
                                {isCurrent && (
                                    <span className="truncate text-xs text-muted-foreground">
                                        {latestLog?.message ??
                                            STATUS_CONFIG[stage].activityLabel}
                                    </span>
                                )}
                            </div>
                        </li>
                    )
                })}
            </ol>
        </div>
    )
}

function PartialArtifacts({ job }: { readonly job: Job }) {
    let scriptText: string | null = null
    if (hasScript(job) && job.script) scriptText = job.script.text

    return (
        <div className="flex flex-col gap-3 border-t pt-4">
            {hasTraffic(job) && (
                <p className="text-sm text-muted-foreground">
                    {job.traffic.incidents.length} incident
                    {job.traffic.incidents.length === 1 ? '' : 's'} found
                    {job.traffic.corridors.length > 0 && (
                        <>
                            {' · '}
                            {job.traffic.corridors.length} road
                            {job.traffic.corridors.length === 1 ? '' : 's'}
                        </>
                    )}
                </p>
            )}
            {scriptText && (
                <div className="rounded-lg bg-muted/50 p-3">
                    <p className="mb-1 text-xs font-medium text-muted-foreground">
                        Script preview
                    </p>
                    <p
                        key={scriptText.slice(0, 64)}
                        className="line-clamp-3 text-sm leading-relaxed"
                    >
                        {scriptText
                            .split(/(\s+)/)
                            .slice(0, 120)
                            .map((word, index) =>
                                word.trim() ? (
                                    <span
                                        key={index}
                                        className="motion-safe:animate-in motion-safe:fade-in-0 motion-safe:fill-mode-backwards"
                                        style={{
                                            animationDelay: `${Math.min(index * 18, 1400)}ms`,
                                            animationDuration: '350ms',
                                        }}
                                    >
                                        {word}
                                    </span>
                                ) : (
                                    word
                                )
                            )}
                        {scriptText.split(/(\s+)/).length > 120 && '…'}
                    </p>
                </div>
            )}
        </div>
    )
}

/** The generating workspace keeps the existing bulletin facts side rail. */
export function GeneratingView({
    job,
    progress,
}: {
    readonly job: Job
    readonly progress?: JobProgressQuery
}) {
    const ownProgress = useJobProgress(
        job.jobId,
        progress === undefined,
        job.status
    )
    const progressQuery = progress ?? ownProgress
    const document = progressQuery.data

    if (
        document &&
        !isJobProgressUnavailable(document) &&
        document.stage === 'Ready'
    ) {
        return null
    }
    if (isRevoiceJob(job)) return <ApplyingView kind="revoice" job={job} />

    return (
        <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(240px,300px)] lg:items-start">
            <SpotlightCard className="p-5">
                <div className="flex flex-col gap-5">
                    <PipelineStepper job={job} progress={progressQuery} />
                    <PartialArtifacts job={job} />
                </div>
            </SpotlightCard>
            <SpotlightCard className="p-4">
                <div className="flex flex-col gap-3">
                    <span className="text-xs font-semibold">
                        Bulletin
                        <span className="ml-1.5 font-normal text-muted-foreground">
                            · being generated
                        </span>
                    </span>
                    <dl className="flex flex-col gap-2 text-sm">
                        <div className={BULLETIN_GRID_COLS}>
                            <dt className="text-xs text-muted-foreground">
                                Market
                            </dt>
                            <dd>{formatMarketName(job.market)}</dd>
                        </div>
                        <div className={BULLETIN_GRID_COLS}>
                            <dt className="text-xs text-muted-foreground">
                                Target
                            </dt>
                            <dd className="tabular-nums">
                                {job.targetDurationSec}s
                            </dd>
                        </div>
                        <div className={BULLETIN_GRID_COLS}>
                            <dt className="text-xs text-muted-foreground">
                                Voice
                            </dt>
                            <dd>{job.input.voice.voiceName}</dd>
                        </div>
                        {hasTraffic(job) && (
                            <div className={BULLETIN_GRID_COLS}>
                                <dt className="text-xs text-muted-foreground">
                                    Incidents
                                </dt>
                                <dd className="tabular-nums">
                                    {job.traffic.incidents.length} found
                                </dd>
                            </div>
                        )}
                    </dl>
                    <p className="border-t border-border/40 pt-2 text-2xs leading-relaxed text-muted-foreground">
                        Appears in the queue and chimes when it needs you.
                    </p>
                </div>
            </SpotlightCard>
        </div>
    )
}

const APPLY_COPY = {
    revoice: {
        title: 'Re-voicing this bulletin',
        detail: 'Your words are kept exactly as written and recorded again. Around 15 seconds.',
    },
    regenerate: {
        title: 'Regenerating this bulletin',
        detail: 'The script is rewritten from the sources you chose, then recorded. Around a minute.',
    },
} as const

/** The gap between an operator action and the asynchronous pipeline handoff. */
export function ApplyingView({
    kind,
    job,
}: {
    readonly kind: 'revoice' | 'regenerate'
    readonly job: Job
}) {
    const copy = APPLY_COPY[kind]
    return (
        <SpotlightCard className="p-5">
            <div className="flex items-start gap-3">
                <div className="flex size-11 shrink-0 items-center justify-center rounded-2xl border border-primary/30 bg-primary/10 text-primary shadow-glow">
                    <Spinner className="size-5" aria-label={copy.title} />
                </div>
                <div className="flex min-w-0 flex-1 flex-col gap-1.5">
                    <h2 className="text-sm font-semibold">{copy.title}</h2>
                    <p className="text-sm text-muted-foreground">
                        {copy.detail}
                    </p>
                    <p className="text-xs text-muted-foreground-subtle">
                        {formatMarketName(job.market)} · {job.targetDurationSec}
                        s · {job.input.voice.voiceName}
                    </p>
                    <p className="mt-1 text-2xs leading-relaxed text-muted-foreground">
                        This page updates itself. Nothing to press, and the new
                        recording appears here when it lands.
                    </p>
                </div>
            </div>
        </SpotlightCard>
    )
}
