import { useMemo } from 'react'
import { useJobs, useJobsHistory } from '@/features/jobs/api/job-hooks'
import { Header, PageContainer } from '@/components/layout'
import { DataBoundary } from '@/components/DataBoundary'
import { BroadcastLog } from '@/features/jobs/components/history/BroadcastLog'
import {
    LOG_OUTCOMES,
    PIPELINE_QUERY_STATUSES,
} from '@/features/jobs/components/history/history-utils'
import { ListSkeleton } from '@/components/LoadingSkeleton'
import { useDocumentTitle } from '@/hooks/use-document-title'
import {
    formatClockTime,
    formatMarketName,
    formatRelativeTime,
} from '@/lib/formatting'
import { cn } from '@/lib/utils'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Button } from '@/components/ui/button'
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import {
    useMarkSpotCheck,
    useSpotChecks,
} from '@/features/activity/api/ledger-periphery-hooks'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { ICON } from '@/lib/design/icons'
import type { JobSummary } from '@/features/jobs/types'
const SETTLED_STATUS_PARAM = [...LOG_OUTCOMES].join(',')
function SpotCheckPanel({ jobs }: { readonly jobs: readonly JobSummary[] }) {
    const spotChecks = useSpotChecks()
    const markSpotCheck = useMarkSpotCheck()

    // JobSummary is the history list DTO and intentionally carries no
    // status-history/publisher attribution. FALLBACK_USED is therefore the
    // only reliably identifiable no-signoff outcome available client-side;
    // PUBLISHED jobs are not included until that attribution is exposed.
    const candidates = useMemo(
        () => jobs.filter((job) => job.status === 'FALLBACK_USED'),
        [jobs]
    )
    const reviewedByJobId = useMemo(() => {
        const reviewed: Record<string, string> = {}
        for (const check of spotChecks.data?.items ?? []) {
            reviewed[check.jobId] = check.by
        }
        return reviewed
    }, [spotChecks.data])

    const review = async (jobId: string) => {
        try {
            await markSpotCheck.mutateAsync({ jobId })
            await spotChecks.refetch()
        } catch {
            // A failed auxiliary review call must not disturb the history tab.
        }
    }

    return (
        <DataBoundary
            query={spotChecks}
            errorFallback="Failed to load spot-checks"
            loading={<ListSkeleton rows={3} />}
            empty={<div />}
        >
            {/* Spot checks load independently from job history, so failures
                must remain actionable in the tab instead of rendering nothing. */}
            {candidates.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                    No fallback bulletins need a spot-check.
                </p>
            ) : (
                <div className="grid gap-3">
                    {candidates.map((job) => {
                        const reviewer = reviewedByJobId[job.jobId]
                        return (
                            <SpotlightCard
                                key={job.jobId}
                                className="flex items-center gap-3 p-3"
                            >
                                <div className="min-w-0 flex-1">
                                    <p className="truncate text-sm font-semibold">
                                        {formatMarketName(job.market)}
                                    </p>
                                    <p className="text-xs text-muted-foreground">
                                        {formatClockTime(job.lastUpdatedAt)} ·{' '}
                                        {job.status}
                                    </p>
                                </div>
                                {reviewer ? (
                                    <span className="shrink-0 text-xs text-muted-foreground">
                                        ✓ reviewed by {reviewer}
                                    </span>
                                ) : (
                                    <DropdownMenu>
                                        <DropdownMenuTrigger asChild>
                                            <Button
                                                variant="ghost"
                                                size="icon"
                                                className="size-8 shrink-0"
                                                aria-label={`${formatMarketName(job.market)} spot-check actions`}
                                            >
                                                <ICON.more
                                                    className="size-4"
                                                    aria-hidden
                                                />
                                            </Button>
                                        </DropdownMenuTrigger>
                                        <DropdownMenuContent align="end">
                                            <DropdownMenuItem
                                                disabled={
                                                    markSpotCheck.isPending
                                                }
                                                onClick={() =>
                                                    void review(job.jobId)
                                                }
                                            >
                                                Mark reviewed
                                            </DropdownMenuItem>
                                        </DropdownMenuContent>
                                    </DropdownMenu>
                                )}
                            </SpotlightCard>
                        )
                    })}
                </div>
            )}
        </DataBoundary>
    )
}

const PIPELINE_STATUS_PARAM = PIPELINE_QUERY_STATUSES.join(',')

/** The broadcast log: every settled bulletin, filterable, day-grouped by default, sortable. */
export function HistoryPage() {
    useDocumentTitle('History')

    // The pipeline summary has its own live query; the history query also polls
    // so delivery transitions reach this table without a manual refresh.
    const {
        data: pipelineResult,
        isFetching,
        dataUpdatedAt,
    } = useJobs({
        status: PIPELINE_STATUS_PARAM,
    })
    const pipelineCount = pipelineResult?.jobs.length ?? 0

    // The settled-job table itself: bounded, "Load older" pagination — never fetches
    // everything at once, unlike the old useJobs({}) this page used to call.
    const {
        data,
        isLoading,
        isError,
        error,
        refetch,
        fetchNextPage,
        hasNextPage,
        isFetchingNextPage,
    } = useJobsHistory({ status: SETTLED_STATUS_PARAM })

    const jobs = useMemo(
        () => data?.pages.flatMap((page) => page.jobs) ?? [],
        [data]
    )

    return (
        <PageContainer>
            <Header
                title="History"
                description="What went to air, and what didn't"
            >
                <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
                    <span
                        className={cn(
                            'relative flex size-2',
                            isFetching && 'animate-pulse'
                        )}
                    >
                        <span className="absolute inline-flex size-full animate-ping rounded-full bg-success opacity-75" />
                        <span className="relative inline-flex size-2 rounded-full bg-success" />
                    </span>
                    Live
                    {dataUpdatedAt > 0 && (
                        <span className="text-xs text-muted-foreground">
                            {' · updated '}
                            {formatRelativeTime(new Date(dataUpdatedAt))}
                        </span>
                    )}
                </div>
            </Header>

            <Tabs defaultValue="history" className="mt-1">
                <TabsList>
                    <TabsTrigger value="history">History</TabsTrigger>
                    <TabsTrigger value="spot-check">Spot-check</TabsTrigger>
                </TabsList>
                <TabsContent value="history" className="mt-4">
                    <div className="flex flex-col gap-4">
                        <DataBoundary
                            query={{
                                isError,
                                error,
                                isLoading,
                                // The raw (non-array) infinite-query result, not `jobs` -- BroadcastLog
                                // owns every empty state itself (including "load older" when a filtered
                                // page comes back empty), so it must always render once loaded.
                                data,
                                refetch,
                            }}
                            errorFallback="Failed to load jobs"
                            loading={<ListSkeleton rows={6} />}
                            empty={<div />}
                        >
                            <BroadcastLog
                                allJobs={jobs}
                                pipelineCount={pipelineCount}
                                hasMore={hasNextPage}
                                onLoadMore={() => void fetchNextPage()}
                                isLoadingMore={isFetchingNextPage}
                            />
                        </DataBoundary>
                    </div>
                </TabsContent>
                <TabsContent value="spot-check" className="mt-4">
                    <SpotCheckPanel jobs={jobs} />
                </TabsContent>
            </Tabs>
        </PageContainer>
    )
}
