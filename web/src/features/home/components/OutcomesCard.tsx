import { Link } from 'react-router'
import { ArrowRight, History as HistoryIcon } from 'lucide-react'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { STATUS_CONFIG } from '@/features/jobs/config/status-config'
import type { JobSummary } from '@/features/jobs/types'
import { formatRelativeTime } from '@/lib/formatting'
import { ROUTE_HISTORY, ROUTE_JOBS } from '@/lib/routes'
import { OpsCardHeader } from './OpsCardHeader'

/** What just aired (or didn't) — outcome chips, newest first. */
export function OutcomesCard({
    settled,
}: {
    readonly settled: readonly JobSummary[]
}) {
    const recent = settled.slice(0, 3)
    return (
        <SpotlightCard className="h-full bg-card/70 p-4 text-left backdrop-blur">
            <div className="flex h-full flex-col gap-2.5">
                <OpsCardHeader icon={HistoryIcon} title="Recent outcomes" />
                {recent.length === 0 ? (
                    <span className="py-1 text-xs text-muted-foreground">
                        Nothing settled yet today.
                    </span>
                ) : (
                    recent.map((job) => {
                        const meta =
                            STATUS_CONFIG[job.status] ?? STATUS_CONFIG.REQUESTED
                        const Icon = meta.icon
                        return (
                            <Link
                                key={job.jobId}
                                to={`${ROUTE_JOBS}/${job.jobId}`}
                                className="-mx-2 flex items-center gap-2 rounded-md px-2 py-1.5 transition-colors hover:bg-muted/50"
                            >
                                <Icon
                                    className="size-3.5 shrink-0 text-muted-foreground"
                                    aria-hidden
                                />
                                <span className="min-w-0 flex-1 truncate text-sm font-medium">
                                    {job.station}
                                </span>
                                <span className="shrink-0 text-xs text-muted-foreground">
                                    {meta.label} ·{' '}
                                    {formatRelativeTime(job.lastUpdatedAt)}
                                </span>
                            </Link>
                        )
                    })
                )}
                <Link
                    to={ROUTE_HISTORY}
                    className="mt-auto inline-flex items-center gap-1 text-xs font-medium text-muted-foreground transition-colors hover:text-foreground"
                >
                    View all in History
                    <ArrowRight className="size-3" aria-hidden />
                </Link>
            </div>
        </SpotlightCard>
    )
}
