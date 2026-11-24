import { lazy, Suspense, useCallback, useMemo, useRef, useState } from 'react'
import { ROUTE_JOBS } from '@/lib/routes'
import { parseTimestamp } from '@/lib/formatting'
import { useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { useJobs } from '@/features/jobs/api/job-hooks'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import {
    bindingSecondsRemaining,
    useBindingDeadlines,
} from '@/features/jobs/hooks/use-binding-deadlines'
import { formatDuration } from '@/lib/formatting'
import { PIPELINE_STATUSES } from '@/features/jobs/config/status-values'
import { CoverageHome } from '@/features/onair/components/board/CoverageHome'
import { QueueBeam } from '@/features/onair/components/workstation/QueueBeam'
import { useQueueKeyboard } from '@/features/onair/hooks/use-queue-keyboard'
import { useDocumentTitle } from '@/hooks/use-document-title'
import { cn } from '@/lib/utils'
import { DataBoundary } from '@/components/DataBoundary'
import { Skeleton } from '@/components/ui/skeleton'

// Lazy: the review cockpit is the heaviest surface in the app and only renders
// once a job is selected — keeping it off the board's route chunk keeps the
// on-air home paint (and its Lighthouse TBT budget) independent of review code.
const ReviewPane = lazy(() =>
    import('@/features/onair/components/workstation/ReviewPane').then((m) => ({
        default: m.ReviewPane,
    }))
)

/**
 * The operator workstation: live triage queue on the left, workspace on
 * the right. The workspace shows the coverage rundown (home) when nothing
 * is selected, or the review pane for the selected bulletin. Mounted at
 * both `/jobs` and `/jobs/:jobId`; the route param is the selection.
 */
export function OnAirPage() {
    useDocumentTitle('On air')
    const navigate = useNavigate()
    const { jobId } = useParams<{ jobId: string }>()
    const selectedJobId = jobId ?? null
    // Sidebar: user-resizable width. Min keeps fixed metadata visible.
    const [sidebarWidth, setSidebarWidth] = useState(320)
    const handleRef = useRef<HTMLDivElement>(null)
    const handlePointerDown = useCallback(
        (e: React.PointerEvent) => {
            e.preventDefault()
            handleRef.current?.setPointerCapture(e.pointerId)
            const startX = e.clientX
            const startWidth = sidebarWidth
            const onMove = (e: PointerEvent) => {
                const delta = e.clientX - startX
                setSidebarWidth(
                    Math.max(280, Math.min(600, startWidth + delta))
                )
            }
            const onUp = () => {
                document.removeEventListener('pointermove', onMove)
                document.removeEventListener('pointerup', onUp)
                document.body.style.userSelect = ''
            }
            document.addEventListener('pointermove', onMove)
            document.addEventListener('pointerup', onUp)
            document.body.style.userSelect = 'none'
        },
        [sidebarWidth]
    )

    const {
        data: jobsResult,
        isLoading,
        isError,
        error,
        refetch,
        dataUpdatedAt,
    } = useJobs({})
    const { data: schedulesResult, isLoading: schedulesLoading } =
        useSchedules()

    const jobs = useMemo(() => jobsResult?.jobs ?? [], [jobsResult])
    const schedules = useMemo(
        () => schedulesResult?.schedules ?? [],
        [schedulesResult]
    )

    const waitingJobs = useMemo(
        () => jobs.filter((j) => j.status === 'WAITING_APPROVAL'),
        [jobs]
    )

    // One clock across the rail, the board and the nav badge: the earlier of the
    // slot cutoff and the approval dwell. The rail used to sort and count down on
    // the dwell alone, so it ranked a bulletin 31 seconds from its cutoff as the
    // LEAST urgent of three.
    const deadlines = useBindingDeadlines(jobs)
    const secondsFor = useCallback(
        (job: Parameters<typeof bindingSecondsRemaining>[1]) =>
            bindingSecondsRemaining(deadlines, job),
        [deadlines]
    )

    const mostUrgentWaiting = useMemo(() => {
        if (waitingJobs.length === 0) return null
        return waitingJobs
            .slice()
            .sort(
                (a, b) =>
                    (secondsFor(a) ?? Infinity) - (secondsFor(b) ?? Infinity)
            )[0]
    }, [waitingJobs, secondsFor])

    // Flattened queue order for keyboard navigation
    const queueItems = useMemo(() => {
        const pipelineSet = new Set<string>(PIPELINE_STATUSES)
        const waiting = waitingJobs
            .slice()
            .sort(
                (a, b) =>
                    (secondsFor(a) ?? Infinity) - (secondsFor(b) ?? Infinity)
            )
        const inPipeline = jobs
            .filter((j) => pipelineSet.has(j.status))
            .slice()
            .sort(
                (a, b) =>
                    parseTimestamp(b.createdAt) - parseTimestamp(a.createdAt)
            )
        const done = jobs
            .filter(
                (j) =>
                    !pipelineSet.has(j.status) &&
                    j.status !== 'WAITING_APPROVAL'
            )
            .slice()
            .sort(
                (a, b) =>
                    parseTimestamp(b.lastUpdatedAt) -
                    parseTimestamp(a.lastUpdatedAt)
            )
            .slice(0, 8)

        return [...waiting, ...inPipeline, ...done]
    }, [jobs, waitingJobs, secondsFor])

    // After a decision, advance to the next bulletin that needs a human.
    const advanceQueue = () => {
        const next = jobs
            .filter(
                (j) =>
                    j.status === 'WAITING_APPROVAL' && j.jobId !== selectedJobId
            )
            .slice()
            .sort(
                (a, b) =>
                    (secondsFor(a) ?? Infinity) - (secondsFor(b) ?? Infinity)
            )
        if (next.length > 0) {
            const n = next[0]
            const secs = secondsFor(n)
            const time =
                secs !== null ? formatDuration(secs, { sign: true }) : '-'
            toast.info(`Next: ${n.station} · ${n.market} · ${time} remaining`)
            navigate(`${ROUTE_JOBS}/${n.jobId}`, { replace: true })
        } else {
            navigate(ROUTE_JOBS, { replace: true })
        }
    }

    useQueueKeyboard({
        items: queueItems,
        selectedJobId,
        onSelect: (id) => navigate(`${ROUTE_JOBS}/${id}`),
    })

    return (
        <div className="@container/workstation flex h-full min-h-0">
            <aside
                className={cn(
                    'shrink-0 border-r overflow-hidden w-full md:w-[var(--sidebar-width)]',
                    // The queue rejoins the selected workspace only when the
                    // workstation CONTAINER is wide enough for both (≈896px →
                    // workspace keeps ≥570px). Below that, a selected bulletin
                    // takes the full width — viewport `md` was too early and
                    // starved the workspace (incident table forced to scroll).
                    // With nothing selected, the queue is the home view: full
                    // width on narrow, resizable beside the rundown from `md`.
                    selectedJobId ? 'hidden @4xl/workstation:block' : 'block'
                )}
                style={
                    {
                        '--sidebar-width': `${sidebarWidth}px`,
                    } as React.CSSProperties
                }
            >
                <DataBoundary
                    query={{
                        isError,
                        error,
                        isLoading,
                        data: jobsResult,
                        refetch,
                    }}
                    errorFallback="Failed to load the bulletin queue"
                    loading={
                        <div className="flex flex-col gap-2 p-3">
                            <Skeleton className="h-16 w-full" />
                            <Skeleton className="h-16 w-full" />
                            <Skeleton className="h-16 w-3/4" />
                        </div>
                    }
                    empty={null}
                >
                    <QueueBeam
                        jobs={jobs}
                        deadlines={deadlines}
                        isLoading={isLoading}
                        selectedJobId={selectedJobId}
                        onSelect={(id) => navigate(`/jobs/${id}`)}
                        updatedAt={dataUpdatedAt}
                    />
                </DataBoundary>
            </aside>
            {/* Resize handle */}
            <div
                ref={handleRef}
                className="hidden shrink-0 cursor-col-resize self-stretch border-r border-transparent transition-colors hover:border-border active:border-border md:block"
                style={{ width: 4 }}
                onPointerDown={handlePointerDown}
            />
            <section
                className={cn(
                    'min-w-0 flex-1',
                    selectedJobId ? 'block overflow-y-auto' : 'hidden md:block'
                )}
                aria-label="Review workspace"
            >
                {selectedJobId ? (
                    <Suspense
                        fallback={
                            <div className="space-y-4 p-6">
                                <Skeleton className="h-8 w-1/3" />
                                <Skeleton className="h-40 w-full" />
                                <Skeleton className="h-24 w-2/3" />
                            </div>
                        }
                    >
                        <ReviewPane
                            jobId={selectedJobId}
                            onPublished={advanceQueue}
                        />
                    </Suspense>
                ) : (
                    /* Home: the coverage rundown gets the whole canvas */
                    <CoverageHome
                        schedules={schedules}
                        jobs={jobs}
                        selectedJobId={selectedJobId}
                        onSelectJob={(id) => navigate(`/jobs/${id}`)}
                        mostUrgentJobId={mostUrgentWaiting?.jobId ?? null}
                        waitingCount={waitingJobs.length}
                        isLoading={schedulesLoading}
                    />
                )}
            </section>
        </div>
    )
}
