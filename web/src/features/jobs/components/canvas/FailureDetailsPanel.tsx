import type { FailureData } from '../../types'
import {
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
} from '@/components/ui/table'
import { Target } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { formatDateTime, formatTime } from '@/lib/formatting'
import { MetricCard } from '@/components/MetricCard'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { FAILURE_STAGE_LABELS } from './constants'

interface FailureDetailsPanelProps {
    readonly failure: FailureData
    /** Re-runs the pipeline. Omitted → no recovery affordance (read-only contexts). */
    readonly onRetry?: () => void
    readonly isRetrying?: boolean
}

/**
 * The alarm surface for a dead bulletin. The banner leads with what broke
 * and — crucially — how to recover: a failed bulletin near airtime needs
 * the retry in arm's reach, not buried in an overflow menu.
 */

// Raw failure messages mapped to honest, actionable headlines. Order matters
// (first match wins): the domain-specific causes (safety block, content
// underrun, traffic fetch, quality gate) come before the generic HTTP/transport
// patterns so a wrapped provider error still headlines what actually broke.
// Covers every real disposition the backend produces (OperationalFailure.cs):
// SafetyRejected → "Content safety violation:…"; ContentLimited →
// "Content-limited:…"; Retryable → traffic-fetch/stale/no-content/synthesis/
// queue/saving messages; Permanent → "Final grounding check failed:…".
const FAILURE_PATTERNS: readonly [RegExp, string][] = [
    [
        /content safety violation|content safety blocked|banned word|safety review blocked/,
        'Blocked by the content-safety filter: the script contains a banned word or failed the AI safety review',
    ],
    [/content-limited/, 'Not enough live traffic to fill the slot'],
    [
        /failed to fetch traffic|traffic data is stale|trafficdatastale/,
        'Traffic data unavailable right now',
    ],
    [/no content available/, 'No traffic, weather or fuel content available'],
    [/final grounding check failed/, 'Script failed the final quality gate'],
    [/queue publishing failed/, 'Failed to queue the bulletin for playout'],
    [/saving failed/, 'Failed to save the bulletin'],
    [/503|service unavailable/, 'Audio service temporarily unavailable'],
    [/502|bad gateway/, 'Audio service temporarily unavailable'],
    [/504|gateway timeout/, 'Audio service timed out'],
    [/429|too many requests/, 'Service receiving too many requests'],
    [/401|unauthorized/, 'Service authentication rejected'],
    [/403|forbidden/, 'Access denied by service'],
    [/timeout|timed out/, 'Service request timed out'],
    [/connection refused|econnrefused/, 'Service connection refused'],
    [/rate limit/, 'Service rate limit reached'],
    [/not found|404/, 'Requested resource not found'],
    [/synthesis failed/, 'Audio synthesis failed'],
]

/** Map raw failure messages to operator-friendly summaries. */
function friendlyFailureMessage(raw: string): string {
    const lower = raw.toLowerCase()
    return (
        FAILURE_PATTERNS.find(([pattern]) => pattern.test(lower))?.[1] ??
        'An unexpected error occurred'
    )
}

export function FailureDetailsPanel({
    failure,
    onRetry,
    isRetrying = false,
}: FailureDetailsPanelProps) {
    const stageLabel = FAILURE_STAGE_LABELS[failure.stage] ?? failure.stage

    // Hide attempt-table columns that carry no data for this failure type
    const hasEstimates = failure.attemptHistory.some(
        (a) => a.estimatedDurationSec != null
    )
    const hasActuals = failure.attemptHistory.some(
        (a) => a.actualDurationSec != null
    )

    return (
        <div className="flex flex-col gap-5">
            {/* Failure banner: red edge glow makes this read as the alarm
                state it is. Recovery action lives directly below the cause
                so the operator's eye never has to scan the full card width. */}
            <div className="relative overflow-hidden rounded-xl border border-destructive/40 bg-destructive/5 p-4 shadow-[0_0_40px_-16px_hsl(var(--destructive)/0.5)]">
                <div
                    aria-hidden
                    className="pointer-events-none absolute inset-x-0 top-0 h-px bg-linear-to-r from-transparent via-destructive/60 to-transparent"
                />
                <div className="flex items-start gap-3">
                    <div className="flex size-9 shrink-0 items-center justify-center rounded-full bg-destructive/15 text-destructive-on-tint">
                        <ICON.warning className="size-5" aria-hidden />
                    </div>
                    <div className="flex min-w-0 flex-1 flex-col gap-1.5">
                        <div className="flex flex-wrap items-center gap-2">
                            <h2 className="text-sm font-semibold text-destructive">
                                Generation failed
                            </h2>
                            <Badge variant="destructive">{stageLabel}</Badge>
                        </div>
                        <p className="text-sm font-medium text-foreground/90">
                            {friendlyFailureMessage(failure.message)}
                        </p>
                        <details className="group mt-0.5">
                            <summary className="cursor-pointer text-xs text-muted-foreground-subtle hover:text-muted-foreground transition-colors">
                                Technical details
                            </summary>
                            <p className="mt-1 text-xs text-muted-foreground-subtle font-mono leading-relaxed">
                                {failure.message}
                            </p>
                        </details>
                        {onRetry && (
                            <Button
                                onClick={onRetry}
                                disabled={isRetrying}
                                className="mt-1 w-fit"
                            >
                                {isRetrying ? (
                                    <Spinner className="size-4" />
                                ) : (
                                    <ICON.regenerate
                                        className="size-4"
                                        aria-hidden
                                    />
                                )}
                                Retry generation
                            </Button>
                        )}
                    </div>
                </div>
            </div>

            <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                <MetricCard label="Failed at" icon={ICON.time}>
                    {formatDateTime(failure.occurredAt)}
                </MetricCard>
                <MetricCard label="Attempts" icon={ICON.reset}>
                    {failure.attemptCount}
                </MetricCard>
                <MetricCard label="Target duration" icon={Target}>
                    {failure.targetDurationSec}s
                </MetricCard>
                {failure.lastEstimatedDurationSec != null && (
                    <MetricCard label="Last estimate" icon={ICON.time}>
                        {failure.lastEstimatedDurationSec.toFixed(1)}s
                    </MetricCard>
                )}
            </div>

            {failure.attemptHistory.length > 0 && (
                <div className="flex flex-col gap-2">
                    <p className="text-xs font-semibold uppercase tracking-widest text-muted-foreground">
                        Attempt history
                    </p>
                    <Table>
                        <TableHeader>
                            <TableRow>
                                <TableHead className="w-12">#</TableHead>
                                <TableHead>Time</TableHead>
                                {hasEstimates && (
                                    <TableHead>Est. duration</TableHead>
                                )}
                                {hasActuals && <TableHead>Actual</TableHead>}
                                <TableHead>Result</TableHead>
                            </TableRow>
                        </TableHeader>
                        <TableBody>
                            {failure.attemptHistory.map((attempt) => (
                                <TableRow
                                    key={attempt.attemptNumber}
                                    className="hover:bg-muted/50 transition-colors"
                                >
                                    <TableCell className="font-mono">
                                        {attempt.attemptNumber}
                                    </TableCell>
                                    <TableCell className="font-mono text-muted-foreground">
                                        {formatTime(attempt.at)}
                                    </TableCell>
                                    {hasEstimates && (
                                        <TableCell className="font-mono">
                                            {attempt.estimatedDurationSec !=
                                            null
                                                ? `${attempt.estimatedDurationSec.toFixed(1)}s`
                                                : '-'}
                                        </TableCell>
                                    )}
                                    {hasActuals && (
                                        <TableCell className="font-mono">
                                            {attempt.actualDurationSec != null
                                                ? `${attempt.actualDurationSec.toFixed(1)}s`
                                                : '-'}
                                        </TableCell>
                                    )}
                                    <TableCell>
                                        {attempt.succeeded ? (
                                            <Badge variant="success">OK</Badge>
                                        ) : attempt.failureReason ? (
                                            <Tooltip>
                                                <TooltipTrigger asChild>
                                                    <Badge
                                                        variant="destructive"
                                                        className="max-w-56"
                                                    >
                                                        <span className="min-w-0 truncate">
                                                            {
                                                                attempt.failureReason
                                                            }
                                                        </span>
                                                    </Badge>
                                                </TooltipTrigger>
                                                <TooltipContent
                                                    side="top"
                                                    className="max-w-sm"
                                                >
                                                    {attempt.failureReason}
                                                </TooltipContent>
                                            </Tooltip>
                                        ) : (
                                            <Badge variant="destructive">
                                                Failed
                                            </Badge>
                                        )}
                                    </TableCell>
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                </div>
            )}
        </div>
    )
}
