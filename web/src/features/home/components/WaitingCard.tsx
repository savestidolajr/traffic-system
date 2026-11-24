import { Link } from 'react-router'
import { ArrowRight, CircleCheck, Inbox } from 'lucide-react'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import type { JobSummary } from '@/features/jobs/types'
import {
    formatOverdue,
    getSecondsRemaining,
    getUrgencyBand,
    urgencyBandMeta,
} from '@/features/jobs/lib/job-meta/urgency'
import { isSameLabel } from '@/features/jobs/lib/job-meta/job-label'
import { formatDuration, formatMarketName } from '@/lib/formatting'
import { ROUTE_JOBS } from '@/lib/routes'
import { cn } from '@/lib/utils'
import { useNow } from '@/hooks/use-second-tick'
import { OpsCardHeader } from './OpsCardHeader'
/** The bulletins waiting on a human, hottest first — each a live countdown. */
export function WaitingCard({
    waiting,
}: {
    readonly waiting: readonly JobSummary[]
}) {
    const now = useNow()
    const top = waiting.slice(0, 3)
    return (
        <SpotlightCard
            className={cn(
                'h-full bg-card/70 p-4 text-left backdrop-blur',
                waiting.length > 0 && 'border-warning/40'
            )}
        >
            <div className="flex h-full flex-col gap-2.5">
                <OpsCardHeader
                    icon={Inbox}
                    title="Waiting on you"
                    count={waiting.length}
                    countTone="warning"
                />
                {top.length === 0 ? (
                    <span className="inline-flex items-center gap-1.5 py-1 text-xs text-muted-foreground">
                        <CircleCheck
                            className="size-3.5 text-success"
                            aria-hidden
                        />
                        Queue clear.
                    </span>
                ) : (
                    top.map((job) => {
                        const secs = getSecondsRemaining(job, now)
                        const band = getUrgencyBand(secs)
                        return (
                            <Link
                                key={job.jobId}
                                to={`${ROUTE_JOBS}/${job.jobId}`}
                                className="-mx-2 flex items-center gap-2 rounded-md px-2 py-1.5 transition-colors hover:bg-muted/50"
                            >
                                <span className="flex min-w-0 flex-1 flex-col leading-tight">
                                    <span className="truncate text-sm font-medium">
                                        {job.station}
                                    </span>
                                    {!isSameLabel(job.station, job.market) && (
                                        <span className="truncate text-xs text-muted-foreground">
                                            {formatMarketName(job.market)}
                                        </span>
                                    )}
                                </span>
                                <span
                                    className={cn(
                                        'shrink-0 font-mono text-sm font-semibold tabular-nums',
                                        urgencyBandMeta(band).textClass
                                    )}
                                >
                                    {secs !== null && secs < 0
                                        ? `+${formatOverdue(Math.abs(secs))}`
                                        : formatDuration(secs ?? 0)}
                                </span>
                            </Link>
                        )
                    })
                )}
                {waiting.length > 0 && (
                    <Link
                        to={ROUTE_JOBS}
                        className="mt-auto inline-flex items-center gap-1.5 rounded-md border border-warning/40 bg-warning/10 px-3 py-2 text-sm font-medium text-warning backdrop-blur-sm transition-colors hover:bg-warning/20"
                    >
                        Review now
                        <ArrowRight className="size-4" aria-hidden />
                    </Link>
                )}
            </div>
        </SpotlightCard>
    )
}
