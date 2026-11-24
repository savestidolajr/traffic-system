import { useState, useMemo } from 'react'
import type { Job, LogEntry, StatusHistoryItem } from '../../types'
import { STATUS_CONFIG } from '../../config/status-config'
import { useJobProgress } from '../../api/progress-hooks'
import {
    isJobProgressUnavailable,
    type JobProgress,
    type JobProgressEvent,
} from '../../api/progress-schemas'
import { ICON } from '@/lib/design/icons'
import { cn } from '@/lib/utils'
import { formatTime, parseTimestamp } from '@/lib/formatting'
import { Placeholder } from '@/components/Placeholder'
import {
    isFlaggedLog,
    resolveStageTitle,
    sourceDiagnosticLogs,
} from './system-audit-utils'

const LOG_LEVEL_CLASS: Record<string, string> = {
    ERROR: 'text-destructive font-semibold',
    WARN: 'text-warning font-semibold',
    INFO: 'text-muted-foreground',
}

/** Wire enum names are not operator copy; both maps fall back to the raw name for a new backend member. */
const PROGRESS_SUBSTAGE_LABEL: Record<string, string> = {
    Evidence: 'Evidence data fetch',
    Select: 'Incident & corridor ranking',
    Write: 'Draft writing',
    Compose: 'Script composition',
    FitWords: 'Word-target fitting',
    Edit: 'Editorial review',
    Gates: 'Quality & safety gates',
    Voice: 'Voice synthesis',
    FitAudio: 'Slot audio fitting',
    Ready: 'Ready for broadcast',
    Failed: 'Generation failed',
}

const PROGRESS_KIND_LABEL: Record<string, string> = {
    StageStarted: 'started',
    StageCompleted: 'done',
    SubStep: 'step',
    AttemptStarted: 'attempt started',
    AttemptFailed: 'attempt failed',
    LlmCall: 'model call',
    Note: 'note',
}
type TimelineRowKind = 'progress' | 'status' | 'log'

export interface AuditTimelineRow {
    readonly kind: TimelineRowKind
    readonly at: string
    readonly attempt: number
    readonly sequence: number
    readonly status?: StatusHistoryItem
    readonly log?: LogEntry
    readonly logs?: readonly LogEntry[]
    readonly progressEvent?: JobProgressEvent
}

export interface SubStageBlock {
    readonly key: string
    readonly title: string
    readonly at: string
    readonly durationMs?: number
    readonly model?: string
    readonly hasFlagged: boolean
    readonly rows: readonly AuditTimelineRow[]
}

export interface StageCardGroup {
    readonly stageKey: string
    readonly title: string
    readonly at: string
    readonly completedAt?: string
    readonly durationMs?: number
    readonly isCompleted: boolean
    readonly hasFlagged: boolean
    readonly summaryCounts?: Readonly<Record<string, number>>
    readonly summaryNote?: string
    readonly models: readonly string[]
    readonly subStages: readonly SubStageBlock[]
    readonly rows: readonly AuditTimelineRow[]
}
export interface AuditTimelineGroup {
    readonly attempt: number
    readonly rows: readonly AuditTimelineRow[]
    readonly stageCards?: readonly StageCardGroup[]
}

function attemptFromText(text: string): number | null {
    const match = /\battempt(?:\s*#|\s+)(\d+)\b/i.exec(text)
    return match ? Number(match[1]) : null
}

function attemptAt(
    at: string,
    text: string,
    events: readonly JobProgressEvent[]
): number {
    const explicit = attemptFromText(text)
    if (explicit !== null && Number.isFinite(explicit)) return explicit
    const prior = [...events]
        .filter((event) => parseTimestamp(event.at) <= parseTimestamp(at))
        .sort((a, b) => parseTimestamp(a.at) - parseTimestamp(b.at))
        .at(-1)
    // Orchestrator events before the engine loop carry attempt 0; they are the
    // first attempt's setup, so status and log rows inherit 1, never 0.
    return Math.max(1, prior?.attempt ?? 1)
}

function compressContiguousLogs(
    rows: readonly AuditTimelineRow[]
): AuditTimelineRow[] {
    const result: AuditTimelineRow[] = []
    let currentLogBuffer: LogEntry[] = []
    let firstLogRow: AuditTimelineRow | null = null

    const flushLogs = () => {
        if (!firstLogRow || currentLogBuffer.length === 0) return
        if (currentLogBuffer.length === 1) {
            result.push(firstLogRow)
        } else {
            result.push({
                ...firstLogRow,
                logs: [...currentLogBuffer],
                log: currentLogBuffer[0],
            })
        }
        currentLogBuffer = []
        firstLogRow = null
    }

    for (const row of rows) {
        if (row.kind === 'log' && row.log) {
            if (!firstLogRow) firstLogRow = row
            currentLogBuffer.push(row.log)
        } else {
            flushLogs()
            result.push(row)
        }
    }
    flushLogs()
    return result
}

function stageKeyForProgressStage(stage: string): string {
    switch (stage) {
        case 'Evidence':
            return 'Evidence'
        case 'Select':
            return 'Select'
        case 'Write':
        case 'Compose':
        case 'FitWords':
        case 'Edit':
        case 'Gates':
            return 'Composition'
        case 'Voice':
        case 'FitAudio':
            return 'Voice'
        case 'Ready':
            return 'Ready'
        case 'Failed':
            return 'Failed'
        default:
            return stage
    }
}

function stageKeyForRow(row: AuditTimelineRow): string {
    if (row.kind === 'progress' && row.progressEvent) {
        return stageKeyForProgressStage(row.progressEvent.stage)
    }
    if (row.kind === 'status' && row.status) {
        switch (row.status.status) {
            case 'REQUESTED':
            case 'HERE_FETCHED':
                return 'Evidence'
            case 'INCIDENTS_SELECTED':
                return 'Select'
            case 'AWAITING_CURATION':
            case 'SCRIPT_READY':
                return 'Composition'
            case 'SYNTHESIZING_AUDIO':
            case 'AUDIO_READY':
                return 'Voice'
            case 'WAITING_APPROVAL':
            case 'PUBLISHED':
            case 'DELIVERY_PENDING':
            case 'FALLBACK_USED':
                return 'Ready'
            case 'FAILED':
            case 'REJECTED':
                return 'Failed'
            default:
                return 'General'
        }
    }
    if (row.kind === 'log' && (row.log || (row.logs && row.logs.length > 0))) {
        const msg = row.log?.message ?? row.logs?.[0]?.message ?? ''
        if (/Starting orchestration|Fetching traffic|Weather|Fuel/i.test(msg)) {
            return 'Evidence'
        }
        if (/Incident changes|selected|ranked/i.test(msg)) {
            return 'Select'
        }
        if (
            /Generation attempt|Script generated|Review|compose|words|gate/i.test(
                msg
            )
        ) {
            return 'Composition'
        }
        if (/Synthesizing audio|Voice|Audio filled|Audio saved/i.test(msg)) {
            return 'Voice'
        }
        if (/Broadcast ready|Published|Awaiting/i.test(msg)) {
            return 'Ready'
        }
    }
    return 'General'
}

const STAGE_TITLES: Record<string, string> = {
    Evidence: 'Evidence & Ingestion',
    Select: 'Selection & Ranking',
    Composition: 'Script Composition & Review',
    Voice: 'Voice & Audio Synthesis',
    Ready: 'Approval & Delivery',
    Failed: 'Failed',
    General: 'Activity',
}

function rowSubStageKey(row: AuditTimelineRow): { key: string; title: string } {
    if (row.kind === 'progress' && row.progressEvent) {
        const stage = row.progressEvent.stage
        return {
            key: stage,
            title: PROGRESS_SUBSTAGE_LABEL[stage] ?? stage,
        }
    }
    if (row.kind === 'status' && row.status) {
        return {
            key: `status-${row.status.status}`,
            title: resolveStageTitle(
                row.status.status,
                row.status.note,
                STATUS_CONFIG[row.status.status]?.activityLabel ??
                    row.status.status
            ),
        }
    }
    if (row.kind === 'log') {
        const msg = row.log?.message ?? row.logs?.[0]?.message ?? ''
        if (/Starting orchestration|Fetching traffic|Weather|Fuel/i.test(msg)) {
            return { key: 'Evidence', title: 'Evidence data fetch' }
        }
        if (/Incident changes|selected|ranked/i.test(msg)) {
            return { key: 'Select', title: 'Incident & corridor ranking' }
        }
        if (/writeFlow|writeIncidents|Briefing|Draft/i.test(msg)) {
            return { key: 'Write', title: 'Draft writing' }
        }
        if (/compose2|Candidate|Composing/i.test(msg)) {
            return { key: 'Compose', title: 'Script composition' }
        }
        if (/Word-loop|word target/i.test(msg)) {
            return { key: 'FitWords', title: 'Word-target fitting' }
        }
        if (/Review|editing|style/i.test(msg)) {
            return { key: 'Edit', title: 'Editorial review' }
        }
        if (/gate|check|DeskCondition|SegmentRunningOrder/i.test(msg)) {
            return { key: 'Gates', title: 'Quality & safety gates' }
        }
        if (/Synthesizing audio|Voice/i.test(msg)) {
            return { key: 'Voice', title: 'Voice synthesis' }
        }
        if (/Audio filled|Audio saved|Fit audio/i.test(msg)) {
            return { key: 'FitAudio', title: 'Slot audio fitting' }
        }
        return { key: 'logs', title: 'Operational logs' }
    }
    return { key: 'general', title: 'Details' }
}

function buildSubStageBlocks(
    rows: readonly AuditTimelineRow[]
): SubStageBlock[] {
    const blocks: SubStageBlock[] = []
    let currentBlock: {
        key: string
        title: string
        rows: AuditTimelineRow[]
    } | null = null

    const flushBlock = () => {
        if (!currentBlock || currentBlock.rows.length === 0) return
        const bRows = currentBlock.rows
        const at = bRows[0]?.at ?? ''
        let durationMs = 0
        let model: string | undefined
        let hasFlagged = false

        for (const r of bRows) {
            if (r.kind === 'log') {
                if (r.log && isFlaggedLog(r.log)) hasFlagged = true
                if (r.logs?.some(isFlaggedLog)) hasFlagged = true
            }
            if (r.kind === 'progress' && r.progressEvent) {
                const ev = r.progressEvent
                if (ev.durationMs != null) durationMs += ev.durationMs
                if (!model && ev.model) model = ev.model
            }
        }

        blocks.push({
            key: currentBlock.key,
            title: currentBlock.title,
            at,
            durationMs: durationMs > 0 ? durationMs : undefined,
            model,
            hasFlagged,
            rows: bRows,
        })
        currentBlock = null
    }

    for (const row of rows) {
        const sub = rowSubStageKey(row)
        if (!currentBlock || currentBlock.key !== sub.key) {
            flushBlock()
            currentBlock = { key: sub.key, title: sub.title, rows: [row] }
        } else {
            currentBlock.rows.push(row)
        }
    }
    flushBlock()
    return blocks
}

function buildStageCards(rows: readonly AuditTimelineRow[]): StageCardGroup[] {
    const STAGE_ORDER = ['Evidence', 'Select', 'Composition', 'Voice', 'Ready']
    const buckets = new Map<string, AuditTimelineRow[]>()
    for (const key of STAGE_ORDER) {
        buckets.set(key, [])
    }

    for (const row of rows) {
        let key = stageKeyForRow(row)
        if (key === 'General' || !buckets.has(key)) {
            key = 'Evidence'
        }
        buckets.get(key)!.push(row)
    }

    const stages: StageCardGroup[] = []
    for (const stageKey of STAGE_ORDER) {
        const stageRows = buckets.get(stageKey) ?? []
        if (stageRows.length === 0) continue

        const sortedStageRows = [...stageRows].sort(
            (a, b) =>
                parseTimestamp(a.at) - parseTimestamp(b.at) ||
                a.sequence - b.sequence
        )

        const at = sortedStageRows[0]?.at ?? ''
        const lastAt = sortedStageRows[sortedStageRows.length - 1]?.at ?? at

        let isCompleted = false
        let summaryCounts: Record<string, number> | undefined
        let summaryNote: string | undefined
        const modelsSet = new Set<string>()
        let totalDurationMs = 0
        let hasDuration = false
        let hasFlagged = false

        for (const r of sortedStageRows) {
            if (r.kind === 'log') {
                if (r.log && isFlaggedLog(r.log)) hasFlagged = true
                if (r.logs?.some(isFlaggedLog)) hasFlagged = true
            }
            if (r.kind === 'progress' && r.progressEvent) {
                const ev = r.progressEvent
                if (ev.kind === 'StageCompleted') {
                    isCompleted = true
                    if (ev.counts)
                        summaryCounts = { ...summaryCounts, ...ev.counts }
                    if (ev.detail) summaryNote = ev.detail
                }
                if (ev.model) modelsSet.add(ev.model)
                if (ev.durationMs != null) {
                    totalDurationMs += ev.durationMs
                    hasDuration = true
                }
                if (!summaryNote && ev.detail) summaryNote = ev.detail
            }
            if (r.kind === 'status' && r.status?.note) {
                if (!summaryNote) summaryNote = r.status.note
            }
        }

        if (!hasDuration && sortedStageRows.length > 1) {
            const span = parseTimestamp(lastAt) - parseTimestamp(at)
            if (span > 0) totalDurationMs = span
        }

        stages.push({
            stageKey,
            title: STAGE_TITLES[stageKey] ?? stageKey,
            at,
            completedAt: lastAt,
            durationMs: totalDurationMs > 0 ? totalDurationMs : undefined,
            isCompleted,
            hasFlagged,
            summaryCounts,
            summaryNote,
            models: Array.from(modelsSet),
            subStages: buildSubStageBlocks(sortedStageRows),
            rows: sortedStageRows,
        })
    }

    return stages
}

function groupRows(rows: readonly AuditTimelineRow[]): AuditTimelineGroup[] {
    const grouped = new Map<number, AuditTimelineRow[]>()
    for (const row of rows) {
        const group = grouped.get(row.attempt) ?? []
        group.push(row)
        grouped.set(row.attempt, group)
    }
    return [...grouped.entries()]
        .sort(([a], [b]) => a - b)
        .map(([attempt, group]) => {
            const sorted = [...group].sort(
                (a, b) =>
                    parseTimestamp(a.at) - parseTimestamp(b.at) ||
                    a.sequence - b.sequence
            )
            const compressed = compressContiguousLogs(sorted)
            return {
                attempt,
                rows: compressed,
                stageCards: buildStageCards(compressed),
            }
        })
}

/** Build the progress-first timeline, with legacy rows interleaved by timestamp. */
export function buildTimeline(
    job: Job,
    progress?: JobProgress
): AuditTimelineGroup[] {
    const progressEvents = progress?.events ?? []
    const rows: AuditTimelineRow[] = []
    let sequence = 0

    for (const event of progressEvents) {
        rows.push({
            kind: 'progress',
            at: event.at,
            attempt: event.attempt > 0 ? event.attempt : 1,
            sequence: sequence++,
            progressEvent: event,
        })
    }

    const statuses = [...job.statusHistory].sort(
        (a, b) => parseTimestamp(a.at) - parseTimestamp(b.at)
    )
    for (const status of statuses) {
        rows.push({
            kind: 'status',
            at: status.at,
            attempt: attemptAt(status.at, status.note, progressEvents),
            sequence: sequence++,
            status,
        })
    }

    const logs = [...job.logs].sort(
        (a, b) => parseTimestamp(a.at) - parseTimestamp(b.at)
    )
    for (const log of logs) {
        rows.push({
            kind: 'log',
            at: log.at,
            attempt: attemptAt(log.at, log.message, progressEvents),
            sequence: sequence++,
            log,
        })
    }

    return groupRows(rows)
}

/** Legacy jobs have no progress events, so history and logs form one chronology. */
export function buildFallbackTimeline(job: Job): AuditTimelineGroup[] {
    const diagnosticLogs = sourceDiagnosticLogs(
        job.sourceDiagnostics,
        job.lastUpdatedAt
    )
    const rows: AuditTimelineRow[] = []
    const sourceRows = [
        ...job.statusHistory.map((status) => ({
            kind: 'status' as const,
            at: status.at,
            status,
            text: status.note,
        })),
        ...job.logs.map((log) => ({
            kind: 'log' as const,
            at: log.at,
            log,
            text: log.message,
        })),
        ...diagnosticLogs.map((log) => ({
            kind: 'log' as const,
            at: log.at,
            log,
            text: log.message,
        })),
    ].sort((a, b) => parseTimestamp(a.at) - parseTimestamp(b.at))

    let currentAttempt = 1
    sourceRows.forEach((source, sequence) => {
        const explicit = attemptFromText(source.text)
        if (explicit !== null && Number.isFinite(explicit)) {
            currentAttempt = explicit
        }
        rows.push({
            kind: source.kind,
            at: source.at,
            attempt: currentAttempt,
            sequence,
            ...(source.kind === 'status'
                ? { status: source.status }
                : { log: source.log }),
        })
    })
    return groupRows(rows)
}

function formatCounts(
    counts: Readonly<Record<string, number>> | null | undefined
): string | null {
    if (!counts) return null
    const text = Object.entries(counts)
        .map(
            ([key, value]) =>
                `${key} ${Number.isInteger(value) ? value : value.toFixed(1)}`
        )
        .join(', ')
    return text || null
}

function formatDuration(durationMs: number | null | undefined): string | null {
    if (durationMs === null || durationMs === undefined) return null
    const seconds = durationMs / 1000
    return `${seconds < 10 ? seconds.toFixed(1) : Math.round(seconds)}s`
}

function StageIcon({ stageKey }: { readonly stageKey: string }) {
    switch (stageKey) {
        case 'Evidence':
            return <ICON.traffic className="size-3.5" aria-hidden />
        case 'Select':
            return <ICON.coverage className="size-3.5" aria-hidden />
        case 'Composition':
            return <ICON.ai className="size-3.5" aria-hidden />
        case 'Voice':
            return <ICON.voice className="size-3.5" aria-hidden />
        case 'Ready':
            return <ICON.approve className="size-3.5" aria-hidden />
        default:
            return <ICON.time className="size-3.5" aria-hidden />
    }
}

function StageCard({
    stage,
    isLatest,
}: {
    readonly stage: StageCardGroup
    readonly isLatest: boolean
}) {
    const [expanded, setExpanded] = useState(stage.hasFlagged)
    const formattedCounts = formatCounts(stage.summaryCounts)
    const formattedDuration = formatDuration(stage.durationMs)
    return (
        <div
            className={cn(
                'overflow-hidden rounded-lg border bg-card/60 transition-all duration-150',
                stage.hasFlagged
                    ? 'border-warning/50 bg-warning/5'
                    : isLatest
                      ? 'border-primary/40 bg-card/90 shadow-sm'
                      : 'border-border/60 hover:border-border'
            )}
        >
            <button
                type="button"
                onClick={() => setExpanded((prev) => !prev)}
                className="flex w-full items-start justify-between gap-3 px-3.5 py-3 text-left transition-colors hover:bg-muted/30 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                aria-expanded={expanded}
            >
                <div className="flex items-start gap-3 min-w-0">
                    <div
                        className={cn(
                            'mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-md border',
                            stage.hasFlagged
                                ? 'border-warning/40 bg-warning/10 text-warning'
                                : isLatest
                                  ? 'border-primary/40 bg-primary/10 text-primary'
                                  : 'border-border/60 bg-muted/30 text-muted-foreground'
                        )}
                    >
                        <StageIcon stageKey={stage.stageKey} />
                    </div>
                    <div className="flex min-w-0 flex-col gap-1">
                        <div className="flex flex-wrap items-center gap-2">
                            <span className="font-semibold text-sm text-foreground">
                                {stage.title}
                            </span>
                            {stage.hasFlagged && (
                                <span className="rounded bg-warning/20 px-1.5 py-0.5 text-3xs font-semibold text-warning">
                                    Attention Needed
                                </span>
                            )}
                            {formattedDuration && (
                                <span className="rounded-full bg-muted/60 px-2 py-0.5 font-mono text-3xs tabular-nums text-muted-foreground font-medium">
                                    {formattedDuration}
                                </span>
                            )}
                            {stage.models.map((m) => (
                                <span
                                    key={m}
                                    className="rounded-full bg-muted/40 px-2 py-0.5 font-mono text-3xs text-muted-foreground-subtle"
                                >
                                    {m}
                                </span>
                            ))}
                        </div>
                        {stage.summaryNote && (
                            <p className="wrap-break-word text-xs text-muted-foreground line-clamp-2">
                                {stage.summaryNote}
                            </p>
                        )}
                        {formattedCounts && (
                            <span className="text-3xs font-mono text-muted-foreground-subtle tabular-nums">
                                {formattedCounts}
                            </span>
                        )}
                    </div>
                </div>

                <div className="flex shrink-0 items-center gap-2 pt-0.5">
                    <time
                        dateTime={stage.at}
                        className="font-mono text-xs tabular-nums text-muted-foreground-subtle"
                    >
                        {formatTime(stage.at)}
                    </time>
                    <span className="flex items-center gap-1 text-3xs font-medium text-muted-foreground hover:text-foreground">
                        {expanded ? (
                            <ICON.collapse className="size-3.5" aria-hidden />
                        ) : (
                            <ICON.expand className="size-3.5" aria-hidden />
                        )}
                    </span>
                </div>
            </button>

            {expanded && (
                <div className="border-t border-border/40 bg-background/80 font-mono text-3xs">
                    <div className="max-h-72 overflow-y-auto flex flex-col divide-y divide-border/20 px-3 py-2">
                        {stage.rows.map((row, idx) => (
                            <StageRowLogItem
                                key={`${row.at}-${idx}-${row.sequence}`}
                                row={row}
                            />
                        ))}
                    </div>
                </div>
            )}
        </div>
    )
}

function StageRowLogItem({ row }: { readonly row: AuditTimelineRow }) {
    if (row.kind === 'log') {
        const logs =
            row.logs && row.logs.length > 0
                ? row.logs
                : row.log
                  ? [row.log]
                  : []
        return (
            <div className="flex flex-col divide-y divide-border/10">
                {logs.map((l, i) => (
                    <div
                        key={`${l.at}-${i}`}
                        className={cn(
                            'flex items-baseline gap-2.5 py-1 leading-snug',
                            isFlaggedLog(l) &&
                                'bg-warning/10 text-warning px-1.5 rounded'
                        )}
                    >
                        <time
                            dateTime={l.at}
                            className="shrink-0 text-muted-foreground-subtle tabular-nums"
                        >
                            {formatTime(l.at)}
                        </time>
                        <span
                            className={cn(
                                'shrink-0 font-semibold',
                                LOG_LEVEL_CLASS[l.level]
                            )}
                        >
                            {l.level}
                        </span>
                        <span className="min-w-0 flex-1 wrap-break-word font-sans text-xs text-foreground/90">
                            {l.message}
                        </span>
                    </div>
                ))}
            </div>
        )
    }

    if (row.kind === 'status' && row.status) {
        const s = row.status
        const meta = STATUS_CONFIG[s.status]
        const label = resolveStageTitle(
            s.status,
            s.note,
            meta?.activityLabel ?? s.status
        )
        return (
            <div className="flex items-baseline gap-2.5 py-1 leading-snug">
                <time
                    dateTime={s.at}
                    className="shrink-0 text-muted-foreground-subtle tabular-nums"
                >
                    {formatTime(s.at)}
                </time>
                <span className="shrink-0 font-semibold text-muted-foreground uppercase">
                    STATUS
                </span>
                <span className="min-w-0 flex-1 wrap-break-word font-sans text-xs text-muted-foreground">
                    <strong className="text-foreground/90 font-medium">
                        {label}
                    </strong>
                    {s.note && s.note !== label && (
                        <span className="text-muted-foreground-subtle ml-1.5">
                            — {s.note}
                        </span>
                    )}
                </span>
            </div>
        )
    }

    if (row.kind === 'progress' && row.progressEvent) {
        const ev = row.progressEvent
        const duration = formatDuration(ev.durationMs)
        const counts = formatCounts(ev.counts)
        return (
            <div className="flex items-baseline gap-2.5 py-1 leading-snug">
                <time
                    dateTime={ev.at}
                    className="shrink-0 text-muted-foreground-subtle tabular-nums"
                >
                    {formatTime(ev.at)}
                </time>
                <span className="shrink-0 rounded bg-muted/60 px-1 py-0.5 text-3xs font-semibold uppercase text-muted-foreground tracking-wider">
                    {PROGRESS_KIND_LABEL[ev.kind] ?? ev.kind}
                </span>
                <span className="min-w-0 flex-1 wrap-break-word font-sans text-xs text-foreground/90">
                    {ev.detail}
                    {(duration || counts || ev.model || ev.route) && (
                        <span className="ml-2 inline-flex flex-wrap gap-1 font-mono text-3xs text-muted-foreground-subtle align-middle">
                            {duration && (
                                <span className="rounded bg-muted/40 px-1 py-0.5">
                                    {duration}
                                </span>
                            )}
                            {counts && (
                                <span className="rounded bg-muted/40 px-1 py-0.5">
                                    {counts}
                                </span>
                            )}
                            {ev.model && (
                                <span className="rounded bg-muted/40 px-1 py-0.5">
                                    {ev.model}
                                </span>
                            )}
                            {ev.route && (
                                <span className="rounded bg-muted/40 px-1 py-0.5">
                                    {ev.route}
                                </span>
                            )}
                        </span>
                    )}
                </span>
            </div>
        )
    }

    return null
}
interface SystemAuditTabProps {
    readonly job: Job
}

/** Chronological progress, status, and log evidence grouped by engine attempt. */
export function SystemAuditTab({ job }: SystemAuditTabProps) {
    const progressQuery = useJobProgress(job.jobId, true, job.status)
    const progress = progressQuery.data
    const timeline = useMemo(
        () =>
            progress && !isJobProgressUnavailable(progress)
                ? buildTimeline(job, progress)
                : buildFallbackTimeline(job),
        [job, progress]
    )
    const rows = timeline.flatMap((group) => group.rows)

    if (rows.length === 0) {
        return (
            <Placeholder
                icon={ICON.time}
                message="No history yet"
                detail="Stages and their logs will appear here as the bulletin progresses."
            />
        )
    }
    return (
        <section
            aria-label="Bulletin timeline"
            className="relative flex flex-col gap-4"
        >
            {timeline.map((group) => {
                const stageCards = group.stageCards ?? []
                return (
                    <div
                        key={`attempt-${group.attempt}`}
                        className="flex flex-col gap-2.5"
                    >
                        {timeline.length > 1 && (
                            <h2 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                                Generation Attempt {group.attempt}
                            </h2>
                        )}
                        <div className="flex flex-col gap-2.5">
                            {stageCards.map((stage, sIdx) => (
                                <StageCard
                                    key={`${stage.stageKey}-${stage.at}-${sIdx}`}
                                    stage={stage}
                                    isLatest={sIdx === stageCards.length - 1}
                                />
                            ))}
                        </div>
                    </div>
                )
            })}
        </section>
    )
}
