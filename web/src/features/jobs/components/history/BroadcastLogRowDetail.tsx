import { useJobDetail } from '../../api/job-hooks'
import { hasAudio, hasScript } from '../../types'
import { STATUS_CONFIG } from '../../config/status-config'
import { getActorName } from '../../lib/job-meta/get-actor-name'
import { AudioWaveformPlayer } from '../AudioWaveformPlayer'
import { Skeleton } from '@/components/ui/skeleton'
import { Button } from '@/components/ui/button'
import { Link } from 'react-router'
import { FileText } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { ROUTE_JOBS, ROUTE_STATIONS } from '@/lib/routes'
import { formatDateTime } from '@/lib/formatting'

interface BroadcastLogRowDetailProps {
    readonly jobId: string
}

export function BroadcastLogRowDetail({ jobId }: BroadcastLogRowDetailProps) {
    const {
        data: job,
        isLoading,
        isError,
        error,
        refetch,
    } = useJobDetail(jobId)

    // Loading
    if (isLoading) {
        return (
            <div className="flex flex-col gap-3">
                <Skeleton className="h-8 w-full" />
                <Skeleton className="h-4 w-3/4" />
                <Skeleton className="h-4 w-1/2" />
            </div>
        )
    }

    // Error
    if (isError || !job) {
        return (
            <div className="flex items-center gap-2 text-sm text-muted-foreground">
                <ICON.warning className="size-4 shrink-0 text-destructive" />
                <span className="flex-1">
                    {error instanceof Error
                        ? error.message
                        : 'Failed to load job details'}
                </span>
                <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => refetch()}
                    className="gap-1.5 text-xs"
                >
                    <ICON.reset className="size-3" />
                    Retry
                </Button>
            </div>
        )
    }

    const meta = STATUS_CONFIG[job.status] ?? STATUS_CONFIG.REQUESTED
    const StatusIcon = meta.icon

    // Accountability: prefer the explicit decision records on the job detail,
    // falling back to the statusHistory entry for the settled status.
    const settledEntry = job.statusHistory.find((h) => h.status === job.status)
    const approval =
        job.status === 'APPROVED' || job.status === 'PUBLISHED'
            ? job.approval
            : undefined
    const publish = job.status === 'PUBLISHED' ? job.publish : undefined
    const rejection = job.status === 'REJECTED' ? job.rejection : undefined

    let decisionLine: string | null = null
    let decisionNote: string | null = null
    if (approval) {
        decisionLine = `Approved by ${getActorName({ by: approval.decidedBy })} · ${formatDateTime(approval.decidedAt)}`
    } else if (rejection) {
        decisionLine = `Rejected by ${getActorName({ by: rejection.decidedBy })} · ${formatDateTime(rejection.decidedAt)}`
    } else if (settledEntry) {
        decisionLine = `${meta.label} by ${getActorName(settledEntry)} · ${formatDateTime(settledEntry.at)}`
        decisionNote = settledEntry.note || null
    }

    // Revision surfacing: version > 1 means the script was revised
    const scriptVersion = hasScript(job) && job.script ? job.script.version : 0

    // Prior rejections, excluding the current one to avoid duplication
    const currentRejectionAt = rejection?.decidedAt
    const priorRejections =
        job.status === 'REJECTED'
            ? job.rejectionHistory.filter(
                  (entry) => entry.at !== currentRejectionAt
              )
            : []

    return (
        <div className="flex flex-col gap-4 rounded-lg border border-border/60 bg-muted/20 p-4 text-sm">
            {/* Audio */}
            {hasAudio(job) && job.audio?.data?.url && (
                <div>
                    <AudioWaveformPlayer
                        url={job.audio.data.url}
                        duration={job.audio.data.durationSec}
                    />
                </div>
            )}

            {/* Script */}
            <div>
                <span className="mb-1 block text-xs font-medium text-muted-foreground">
                    Script
                </span>
                {hasScript(job) && job.script?.text ? (
                    <div className="max-h-48 overflow-y-auto rounded border border-border/40 bg-background p-3 text-sm leading-relaxed">
                        {job.script.text}
                    </div>
                ) : (
                    <span className="text-muted-foreground">No script</span>
                )}
            </div>

            {/* Sources */}
            {job.content && (job.content.weather || job.content.fuel) && (
                <div className="flex flex-col gap-2">
                    <span className="text-xs font-medium text-muted-foreground">
                        Sources
                    </span>
                    <div className="flex flex-wrap items-center gap-2">
                        {job.content.weather && (
                            <span className="inline-flex items-center gap-1 rounded-md bg-info/10 px-2 py-0.5 text-xs font-medium text-info">
                                <ICON.weather
                                    className="size-3.5"
                                    aria-hidden
                                />
                                Weather
                            </span>
                        )}
                        {job.content.fuel && (
                            <span className="inline-flex items-center gap-1 rounded-md bg-warning/10 px-2 py-0.5 text-xs font-medium text-warning">
                                <ICON.fuel className="size-3.5" aria-hidden />
                            </span>
                        )}
                        {job.weather?.alerts &&
                            job.weather.alerts.length > 0 && (
                                <span className="inline-flex items-center gap-1 rounded-md bg-warning/10 px-2 py-0.5 text-xs font-medium text-warning">
                                    <ICON.warning
                                        className="size-3.5"
                                        aria-hidden
                                    />
                                    Alert led bulletin
                                </span>
                            )}
                    </div>
                    <p className="text-xs text-muted-foreground">
                        {[
                            job.weather &&
                                `${job.weather.temperatureC}° ${job.weather.conditions.toLowerCase()}`,
                            ...(job.fuel?.summaries ?? []).map(
                                (s) =>
                                    `${s.fuelType} from $${s.minPrice.toFixed(2)}`
                            ),
                        ]
                            .filter(Boolean)
                            .join(' · ')}
                    </p>
                </div>
            )}

            {/* Decision */}
            {decisionLine && (
                <div className="flex flex-col gap-1">
                    <span className="text-xs font-medium text-muted-foreground">
                        Decision
                    </span>
                    <div className="flex flex-wrap items-center gap-2">
                        <span className="inline-flex items-center gap-1.5 text-xs text-foreground">
                            <StatusIcon
                                className="size-3.5 shrink-0 text-muted-foreground"
                                aria-hidden
                            />
                            {decisionLine}
                        </span>
                        {scriptVersion > 1 && (
                            <span className="inline-flex items-center gap-1 rounded-md bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
                                <FileText className="size-3" aria-hidden />
                                Script v{scriptVersion} · revised{' '}
                                {scriptVersion - 1}&times;
                            </span>
                        )}
                    </div>
                    {publish && (
                        <p className="text-xs text-muted-foreground">
                            Published to {publish.destination} ·{' '}
                            {formatDateTime(publish.publishedAt)}
                        </p>
                    )}
                    {decisionNote && (
                        <p className="text-xs text-muted-foreground">
                            {decisionNote}
                        </p>
                    )}
                </div>
            )}

            {/* Rejection feedback */}
            {rejection && (
                <div className="border-l-2 border-warning/50 pl-3">
                    <p className="text-xs italic text-muted-foreground">
                        &ldquo;{rejection.reason}&rdquo;
                    </p>
                </div>
            )}

            {/* Prior rejections from earlier revisions */}
            {priorRejections.length > 0 && (
                <div className="flex flex-col gap-2">
                    <span className="text-xs font-medium text-muted-foreground">
                        Revision history
                    </span>
                    {priorRejections.map((entry, i) => (
                        <div
                            key={`${entry.at}-${i}`}
                            className="border-l-2 border-border/60 pl-3"
                        >
                            <p className="text-xs italic text-muted-foreground">
                                &ldquo;{entry.reason}&rdquo;
                            </p>
                            {entry.notes && (
                                <p className="mt-0.5 text-xs text-muted-foreground">
                                    {entry.notes}
                                </p>
                            )}
                            <p className="mt-0.5 text-xs text-muted-foreground">
                                by {getActorName(entry)} ·{' '}
                                {formatDateTime(entry.at)}
                            </p>
                        </div>
                    ))}
                </div>
            )}

            {/* Failure details */}
            {job.status === 'FAILED' && job.failure && (
                <div className="border-l-2 border-destructive/50 pl-3">
                    <p className="text-xs text-destructive">
                        {job.failure.message}
                    </p>
                    {job.failure.stage && (
                        <p className="mt-0.5 text-xs text-muted-foreground">
                            Stage: {job.failure.stage}
                        </p>
                    )}
                </div>
            )}

            {/* What actually aired, when it was not this slot's own bulletin. Reached on
                PUBLISHED as well as FALLBACK_USED: coverage delivers now, so a covered
                slot ends published and would otherwise be indistinguishable from a slot
                that aired its own read. */}
            {(job.status === 'FALLBACK_USED' || job.status === 'PUBLISHED') &&
                job.fallback && (
                    <div className="border-l-2 border-warning/50 pl-3">
                        <p className="text-xs text-muted-foreground">
                            {job.fallback.sourceJobId ? (
                                <>
                                    Aired a repeat of{' '}
                                    <code className="text-xs">
                                        {job.fallback.sourceJobId}
                                    </code>
                                </>
                            ) : (
                                'Aired the standby cart'
                            )}
                        </p>
                        {job.fallback.reason && (
                            <p className="mt-0.5 text-xs text-muted-foreground">
                                {job.fallback.reason}
                            </p>
                        )}
                        {job.fallback.appliedAt && (
                            <p className="mt-0.5 text-xs text-muted-foreground">
                                Applied ·{' '}
                                {formatDateTime(job.fallback.appliedAt)}
                            </p>
                        )}
                        {job.fallback.streak > 1 && (
                            <p className="mt-0.5 text-xs font-medium text-destructive">
                                Chronic: {job.fallback.streak} in a row
                            </p>
                        )}
                    </div>
                )}

            {/* Links */}
            <div className="flex items-center gap-4 pt-1">
                <Link
                    to={`${ROUTE_JOBS}/${job.jobId}`}
                    className="text-xs font-medium text-primary hover:underline"
                >
                    Open job &rarr;
                </Link>
                <Link
                    to={ROUTE_STATIONS}
                    className="text-xs font-medium text-primary hover:underline"
                >
                    View station config &rarr;
                </Link>
            </div>
        </div>
    )
}
