import { useMemo } from 'react'
import type { Job, StatusHistoryItem } from '../../types'
import { hasAudio } from '../../types'
import { STATUS_CONFIG } from '../../config/status-config'
import { RECORD_STATUSES } from '../../config/status-values'
import { formatDuration, formatDateTime } from '@/lib/formatting'
import { Badge } from '@/components/ui/badge'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'

const RECORD_LABELS: Record<string, string> = {
    WAITING_APPROVAL: 'Ready for review',
    APPROVED: 'Approved',
    PUBLISHED: 'Published',
    REJECTED: 'Rejected',
    FALLBACK_USED: 'Fallback used',
    FAILED: 'Failed',
    MISSED: 'Missed',
}

/** Humanize an actor's email for by-lines: 'reviewer@arn.com.au' →
 *  'Reviewer'. Non-email names ('AI', 'System', plain names) pass through
 *  untouched; the raw value is preserved on the element's `title`. */
function displayActor(by: string): string {
    const at = by.indexOf('@')
    if (at === -1) return by
    return by
        .slice(0, at)
        .split(/[._-]+/)
        .filter(Boolean)
        .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
        .join(' ')
}

/**
 * The record of a finished bulletin: what happened, when, and by whom.
 * Terminal jobs aren't reviews anymore — the right rail answers
 * "what aired?" instead of "should this air?".
 */
export function AsAiredCard({ job }: { readonly job: Job }) {
    const meta = STATUS_CONFIG[job.status] ?? STATUS_CONFIG.REQUESTED
    const audio = hasAudio(job) ? job.audio : undefined

    const recordEntries = useMemo<readonly StatusHistoryItem[]>(() => {
        const byStatus = new Map(job.statusHistory.map((h) => [h.status, h]))
        return RECORD_STATUSES.map((s) => byStatus.get(s)).filter(
            (h): h is StatusHistoryItem => h !== undefined
        )
    }, [job.statusHistory])

    return (
        <SpotlightCard className="p-4">
            <div className="flex flex-col gap-4">
                <div className="flex items-center justify-between gap-2">
                    <span className="text-xs font-semibold">Record</span>
                    <Badge variant={meta.variant}>{meta.label}</Badge>
                </div>
                <div className="flex flex-col gap-2 pt-1 text-xs">
                    {recordEntries.map((entry) => {
                        // The rejection reason travels on job.rejection (and is
                        // mirrored onto the status-history note); show it under
                        // the Rejected entry so the card says why, not just who.
                        //
                        // Read OPTIONALLY even though RejectedJob declares
                        // `rejection` non-optional. Two boundaries already prove
                        // it — JobSchema refuses a REJECTED payload with no
                        // decision record, and the optimistic status patch
                        // (`isCompleteJob`) refuses to install one — so a hard
                        // deref adds no detection here, only blast radius: this
                        // exact line is what turned an invariant violation into
                        // "Cannot read properties of undefined (reading
                        // 'reason')" on the app's error boundary, losing the
                        // whole workspace mid-review. The mirrored note carries
                        // the same words, so degrading costs nothing.
                        const rejectionReason =
                            entry.status === 'REJECTED'
                                ? (job.status === 'REJECTED'
                                      ? job.rejection?.reason
                                      : null) ||
                                  entry.note ||
                                  null
                                : null
                        return (
                            <div
                                key={entry.status}
                                className="rounded-lg border border-border/40 bg-background/30 px-2.5 py-2"
                            >
                                <div className="flex items-center gap-2">
                                    <span
                                        className="size-1.5 shrink-0 rounded-full bg-primary/80"
                                        aria-hidden
                                    />
                                    <span className="font-medium">
                                        {RECORD_LABELS[entry.status] ??
                                            entry.status}
                                    </span>
                                </div>
                                <div className="mt-1 flex flex-wrap items-center gap-x-1.5 gap-y-0.5 pl-3.5 text-2xs text-muted-foreground">
                                    <span className="tabular-nums">
                                        {formatDateTime(entry.at)}
                                    </span>
                                    {entry.by && (
                                        <span title={entry.by}>
                                            · by {displayActor(entry.by)}
                                        </span>
                                    )}
                                </div>
                                {rejectionReason && (
                                    <p className="mt-1 pl-3.5 text-2xs italic text-muted-foreground">
                                        &ldquo;{rejectionReason}&rdquo;
                                    </p>
                                )}
                            </div>
                        )
                    })}
                    {audio && (
                        <div className="flex items-center justify-between gap-3 rounded-lg bg-muted/20 px-2.5 py-2 text-xs">
                            <span className="text-muted-foreground">
                                Audio length
                            </span>
                            <span className="tabular-nums">
                                {formatDuration(
                                    Math.round(audio.measuredDurationSec)
                                )}
                            </span>
                        </div>
                    )}
                </div>
            </div>
        </SpotlightCard>
    )
}
