import { PIPELINE_STATUSES } from '@/features/jobs/config/status-values'
import { useState, useCallback } from 'react'
import { ROUTE_JOBS, ROUTE_PRONUNCIATION } from '@/lib/routes'
import { useNavigate } from 'react-router'
import { ArrowLeft } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { toast } from 'sonner'
import {
    useJobDetail,
    useJobs,
    useProceedCuration,
    type JobDetailQuery,
} from '@/features/jobs/api/job-hooks'
import { useJobProgress } from '@/features/jobs/api/progress-hooks'
import { useScriptVerification } from '@/features/jobs/hooks/use-script-verification'
import { isJobProgressUnavailable } from '@/features/jobs/api/progress-schemas'
import {
    getSecondsRemaining,
    getUrgencyBand,
    formatOverdue,
    resolveBindingDeadline,
    isAirSlotPassed,
} from '@/features/jobs/lib/job-meta/urgency'
import { useAirSlot } from '@/features/jobs/hooks/use-air-slot'
import {
    formatDuration,
    formatMarketName,
    formatClockTimeFromDate,
} from '@/lib/formatting'
import { useSecondTick } from '@/hooks/use-second-tick'
import type { JobSummary } from '@/features/jobs/types'
import { JobActions } from '@/features/jobs/components'
import { JobReviewCockpit } from '@/features/jobs/components/review/JobReviewCockpit'
import { ReviewDetailsTab } from '@/features/jobs/components/review/ReviewDetailsTab'
import { DecisionDock } from '@/features/jobs/components/review/DecisionDock'
import { ReviseDialog } from '@/features/jobs/components/review/ReviseDialog'
import { useJobActions } from '@/features/jobs/hooks/use-job-actions'
import { useBulletinReview } from '@/features/jobs/hooks/use-bulletin-review'
import { hasTraffic, isFailed, type Job } from '@/features/jobs/types'
import { CurationPane } from '@/features/jobs/components/curation/CurationPane'
import { STATUS_CONFIG } from '@/features/jobs/config/status-config'
import { ErrorAlert } from '@/components/ErrorAlert'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'
import { useGlobalKeydown } from '@/hooks/use-global-keydown'
import { useIsAdmin } from '@/hooks/use-current-user'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs'
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { cn } from '@/lib/utils'
import { GeneratingView } from './PipelineStepper'

/**
 * Whether the market is already conveyed by the station name, so it need not
 * be repeated beside it. `isSameLabel`'s exact-equality misses multi-word
 * station labels — "3AW 693 Melbourne Current" with market "Melbourne"
 * rendered "3AW 693 Melbourne Current Melbourne" — so this checks containment
 * (case-insensitive) as well.
 */
function marketAlreadyShown(station: string, market: string): boolean {
    const s = station.trim().toLowerCase()
    const m = market.trim().toLowerCase()
    return s === m || s.includes(m)
}

interface ReviewPaneProps {
    readonly jobId: string
    /** Called after the publish request is queued for delivery. */
    readonly onPublished?: () => void
    /**
     * On-Air owns the selected-job observer and passes it here so the queue
     * and workspace consume one authoritative detail result.
     */
    readonly detailQuery?: JobDetailQuery
}

/** Fetch shell: loading / error states, then the workspace body. */
export function ReviewPane({
    jobId,
    onPublished,
    detailQuery,
}: ReviewPaneProps) {
    if (detailQuery) {
        return (
            <ReviewPaneResult query={detailQuery} onPublished={onPublished} />
        )
    }
    return <ReviewPaneFetch jobId={jobId} onPublished={onPublished} />
}

function ReviewPaneFetch({
    jobId,
    onPublished,
}: {
    readonly jobId: string
    readonly onPublished?: () => void
}) {
    const query = useJobDetail(jobId)
    return <ReviewPaneResult query={query} onPublished={onPublished} />
}

function ReviewPaneResult({
    query,
    onPublished,
}: {
    readonly query: JobDetailQuery
    readonly onPublished?: () => void
}) {
    const { data: job, isLoading, isError, error, refetch } = query

    if (isLoading) {
        return (
            <div className="flex flex-col gap-6 p-6">
                <div className="flex items-start justify-between gap-4">
                    <div className="flex flex-col gap-2">
                        <Skeleton className="h-4 w-32" />
                        <Skeleton className="h-8 w-40" />
                    </div>
                    <Skeleton className="h-9 w-48" />
                </div>
                <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_360px]">
                    <Skeleton className="h-96 w-full rounded-xl" />
                    <Skeleton className="h-96 w-full rounded-xl" />
                </div>
            </div>
        )
    }

    if (isError || !job) {
        return (
            <div className="p-6">
                <h1 className="sr-only">Bulletin</h1>
                <ErrorAlert
                    error={error}
                    fallbackMessage="Bulletin not found. It may have been deleted."
                    onRetry={() => refetch()}
                    className="mx-auto mt-12 max-w-md"
                />
            </div>
        )
    }

    return <ReviewPaneBody job={job} onPublished={onPublished} />
}

/** Declared order is the rendered order of both the triggers and the panels. */
const PANE_TABS = ['review', 'adjust', 'details'] as const
type PaneTab = (typeof PANE_TABS)[number]

function ReviewPaneBody({
    job,
    onPublished,
}: {
    readonly job: Job
    readonly onPublished?: () => void
}) {
    const navigate = useNavigate()
    const actions = useJobActions(job.jobId, job.market, job.etag)
    const review = useBulletinReview(job)
    const proceedCuration = useProceedCuration()
    const isAdmin = useIsAdmin()
    const deleteConfirm = useConfirmDialog()
    const approveConfirm = useConfirmDialog()
    const [tab, setTab] = useState<PaneTab>('review')

    const meta = STATUS_CONFIG[job.status] ?? STATUS_CONFIG.REQUESTED
    const failed = isFailed(job)
    const curationOpen = job.status === 'AWAITING_CURATION'
    const isPipelineStatus = (PIPELINE_STATUSES as readonly string[]).includes(
        job.status
    )
    const progressQuery = useJobProgress(
        job.jobId,
        isPipelineStatus,
        job.status
    )
    const progressReady =
        progressQuery.data !== undefined &&
        !isJobProgressUnavailable(progressQuery.data) &&
        progressQuery.data.stage === 'Ready'

    // Adjust tab is available as long as traffic data exists on the job,
    // even while the pipeline is generating script or audio.
    const hasAdjust = hasTraffic(job)
    const effectiveTab = tab === 'adjust' && !hasAdjust ? 'review' : tab
    // An apply in flight owns the workspace: the reject → regenerate handoff
    // passes through REJECTED (terminal) and back out, and every status-driven
    // branch below — record card, badge, dock — would flash through that in
    // half a second while the operator waits for their new recording.
    const applying = review.applyInFlight
    const waiting = (job.status === 'WAITING_APPROVAL' || failed) && !applying
    // RISK-31: a FAILED job that resulted from a failed verbatim re-voice
    // must retry as a re-voice (revoiceOnly + the operator's edited text),
    // not a plain regenerate — plain regenerate lets the AI freely rewrite
    // the script, discarding the edit the operator asked to keep verbatim.
    // Plain failed generations (no pending revoice text) retry unchanged.
    const retryFailedGeneration = useCallback(async () => {
        if (failed && review.lastRevoiceScript) {
            await actions.handleRegenerate(
                undefined,
                review.lastRevoiceScript,
                undefined,
                true
            )
        } else {
            await actions.handleRegenerate()
        }
    }, [actions, failed, review.lastRevoiceScript])

    // Seeding a regeneration with the bulletin a fallback replaced. No new
    // endpoint: POST jobs/{jobId}/regenerate already accepts EditedScript, which
    // is exactly what the retained take is — a starting draft, not a re-voice, so
    // the pipeline stays free to fix whatever made the slot fall back.
    const useSupersededTakeAsDraft = useCallback(
        (text: string) => {
            void actions.handleRegenerate(undefined, text)
        },
        [actions]
    )

    // `R` opens the Revise dialog — the consolidated regenerate/reject
    // surface. `A` opens the approve confirmation (below), never the approve
    // mutation itself.
    const handleRevise = useCallback(() => {
        actions.setIsRejectOpen(true)
    }, [actions])
    useGlobalKeydown('r', handleRevise)
    useGlobalKeydown('R', handleRevise)

    // Approve flips an irreversible state (there is no un-approve edge), so it
    // ALWAYS confirms before the mutation fires — a contradicted claim merely
    // earns a stricter warning on top (bug-bash #24: the clean-bulletin happy
    // path used to approve on the click itself, the only unguarded action in
    // the dock). The dock still disables the button while a mutation is in
    // flight, so the dialog cannot double-fire.
    const { flaggedSentenceCount } = useScriptVerification(job)
    const doApprove = useCallback(async () => {
        // Approval changes this job to Approved but keeps the operator in the
        // same workspace. Only publishing advances the queue.
        await actions.handleApprove()
    }, [actions])

    // JobActions types its action handlers `() => Promise<void>` (the dock
    // shares the handler, where `() => void` is fine); opening the dialog is
    // synchronous, so an immediately-resolved promise satisfies the contract.
    const requestApprove = useCallback(async () => {
        approveConfirm.open()
    }, [approveConfirm])

    // `A` is a shortcut to the *confirmation*, not to going to air: it opens
    // the same dialog the dock button does, so a stray keystroke still needs a
    // deliberate second action. Guarded on WAITING_APPROVAL and on an
    // in-flight mutation, matching exactly when the dock's Approve is live —
    // otherwise the key would fire an approve dialog for a job that has no
    // approve edge, or double-fire behind a running one. `waiting` already
    // excludes an apply in flight, so the reject→regenerate handoff is covered.
    const approveShortcut = useCallback(() => {
        if (job.status !== 'WAITING_APPROVAL' || !waiting) return
        if (actions.isApproving || actions.isRejecting || actions.isDeleting)
            return
        approveConfirm.open()
    }, [job.status, waiting, actions, approveConfirm])
    useGlobalKeydown('a', approveShortcut)
    useGlobalKeydown('A', approveShortcut)

    // Two independent escalations, because they are different mistakes: a
    // contradicted claim means the script may be wrong, a missed slot means the
    // station has already aired nothing and approving now airs it late. Before
    // this, a bulletin whose slot had passed confirmed with the same
    // "Approving cannot be undone." as one airing in ten minutes.
    const airSlotAt = useAirSlot(job)
    const bindingDeadline = resolveBindingDeadline(job, airSlotAt)
    const slotMissed = bindingDeadline?.slotMissed === true
    // `slotMissed` above is WAITING_APPROVAL-only by construction (resolveBindingDeadline
    // returns null for every other status), which makes it useless as a terminal-state gate:
    // it reads false — "slot fine" — on a FALLBACK_USED job whose slot went weeks ago. The
    // gates below therefore use the job's OWN scheduled slot.
    //
    // Measured on deployed dev + test 2026-08-24: a healthy covered slot occupies
    // FALLBACK_USED for ~0.2s before `system (deadline)` approves and publishes it, so every
    // job actually resting there is one whose delivery failed — all 39 were 12.7 to 28.7 days
    // old. Offering "regenerate for this slot" produces a bulletin for dead air.
    const airSlotPassed = isAirSlotPassed(job)
    const canSeedFromTake =
        !airSlotPassed &&
        (job.status === 'FALLBACK_USED' ||
            job.status === 'FAILED' ||
            job.status === 'REJECTED')
    const approveDialogTitle = slotMissed
        ? 'Approve after the slot has passed?'
        : flaggedSentenceCount > 0
          ? 'Approve with contradicted claims?'
          : 'Approve bulletin?'
    const approveDialogConfirmLabel =
        slotMissed || flaggedSentenceCount > 0 ? 'Approve anyway' : 'Approve'
    const approveDialogDescription =
        [
            slotMissed && airSlotAt
                ? `The ${formatClockTimeFromDate(airSlotAt)} slot has passed, so this bulletin airs late.`
                : null,
            flaggedSentenceCount > 0
                ? `${flaggedSentenceCount} sentence${flaggedSentenceCount === 1 ? '' : 's'} in this script contradict${flaggedSentenceCount === 1 ? 's' : ''} the source data (red markers in the script).`
                : null,
        ]
            .filter(Boolean)
            .join(' ') || 'Approving cannot be undone.'

    // Situational awareness: while heads-down in one bulletin, surface any
    // other waiting bulletin with a tighter deadline than this one. The
    // operator can wave it off — choosing this bulletin first is a valid
    // call, and a permanent banner would train them to ignore it.
    //
    // The dismissal is scoped to "finish this bulletin first" — i.e. this
    // whole review session — not to whichever job happened to be hottest
    // when clicked. Tracking a single job id meant dismissing the hottest
    // candidate just promoted the next-hottest one into its place, so the
    // banner appeared to never go away during busy periods with several
    // waiting bulletins. Resets whenever the reviewed job changes.
    const [hotterDismissed, setHotterDismissed] = useState(false)
    const [trackedJobId, setTrackedJobId] = useState(job.jobId)
    if (job.jobId !== trackedJobId) {
        setTrackedJobId(job.jobId)
        setHotterDismissed(false)
    }
    const { data: jobsResult } = useJobs({})
    const currentSecs = getSecondsRemaining(job)
    const hotter = hotterDismissed
        ? undefined
        : (jobsResult?.jobs ?? [])
              .filter(
                  (j) =>
                      j.status === 'WAITING_APPROVAL' && j.jobId !== job.jobId
              )
              .map((j) => ({
                  job: j,
                  secs: getSecondsRemaining(j) ?? Infinity,
              }))
              .sort((a, b) => a.secs - b.secs)
              .find((c) => currentSecs === null || c.secs < currentSecs)

    /** One panel renderer for all three tabs, so the branches cannot drift. */
    const renderPanel = (t: PaneTab) => {
        if (curationOpen)
            return (
                <CurationPane
                    job={job}
                    isProceeding={proceedCuration.isPending}
                    onProceed={async (editedIncidents) => {
                        await proceedCuration.mutateAsync({
                            jobId: job.jobId,
                            expectedEtag: job.etag,
                            editedIncidents,
                        })
                    }}
                />
            )
        if (t === 'details') return <ReviewDetailsTab job={job} />
        if (t === 'adjust' && hasTraffic(job)) {
            return <JobReviewCockpit job={job} review={review} panel="adjust" />
        }
        if (isPipelineStatus && !progressReady) {
            return <GeneratingView job={job} progress={progressQuery} />
        }
        return (
            <JobReviewCockpit
                job={job}
                review={review}
                panel={t === 'adjust' ? 'adjust' : 'review'}
                onShowLeftOut={hasAdjust ? () => setTab('adjust') : undefined}
                onUseSupersededTakeAsDraft={
                    canSeedFromTake ? useSupersededTakeAsDraft : undefined
                }
            />
        )
    }
    return (
        <Tabs
            value={effectiveTab}
            onValueChange={(v) => setTab(v as PaneTab)}
            className="relative min-h-full"
        >
            <div className="relative flex min-h-full flex-col animate-in fade-in-0 duration-150">
                <div aria-live="polite" role="status" className="sr-only">
                    Status:{' '}
                    {applying
                        ? applying === 'revoice'
                            ? 'Re-voicing'
                            : 'Regenerating'
                        : meta.label}
                </div>

                {/* One sticky header row: identity left, decision right. The
                decision is always one click away, however far you scroll. */}
                <header className="sticky top-0 z-20 border-b bg-background">
                    <div className="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 pt-2.5 sm:px-6">
                        {/* Escape hatch: OnAirPage hides the queue rail below
                        the workstation container's 4xl breakpoint, so this
                        back button must stay visible exactly there. It uses
                        the same container ruler, not the viewport md query
                        that used to strand the selection between 768px and
                        971px. */}
                        <button
                            type="button"
                            onClick={() => navigate(ROUTE_JOBS)}
                            className="@4xl/workstation:hidden -ms-1.5 inline-flex size-9 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted/60 hover:text-foreground"
                            aria-label="Back to queue"
                        >
                            <ArrowLeft className="size-4" />
                        </button>

                        <h1 className="text-lg font-bold tracking-tight">
                            {job.station}
                            {!marketAlreadyShown(job.station, job.market) && (
                                <span className="ml-2 font-normal text-muted-foreground">
                                    {formatMarketName(job.market)}
                                </span>
                            )}
                        </h1>
                        {/* For waiting bulletins the countdown IS the status; a
                    badge would just repeat it. */}
                        {applying ? (
                            <Badge variant={STATUS_CONFIG.REQUESTED.variant}>
                                {applying === 'revoice'
                                    ? 'Re-voicing'
                                    : 'Regenerating'}
                            </Badge>
                        ) : (
                            !waiting &&
                            !curationOpen && (
                                <Badge variant={meta.variant}>
                                    {meta.label}
                                </Badge>
                            )
                        )}

                        <div className="ms-auto flex items-center gap-2">
                            {!waiting && !curationOpen && !applying && (
                                <JobActions
                                    job={job}
                                    onApprove={requestApprove}
                                    onPublish={async () => {
                                        const queued =
                                            await actions.handlePublish()
                                        if (queued === true) onPublished?.()
                                    }}

                                    onRegenerate={retryFailedGeneration}
                                    isApproving={actions.isApproving}
                                    isPublishing={actions.isPublishing}
                                    isRegenerating={actions.isRegenerating}
                                    slotMissed={airSlotPassed}
                                />
                            )}

                            <DropdownMenu>
                                <DropdownMenuTrigger asChild>
                                    <Button
                                        variant="ghost"
                                        size="icon"
                                        className="size-9"
                                        aria-label="More actions"
                                    >
                                        <ICON.more />
                                    </Button>
                                </DropdownMenuTrigger>
                                <DropdownMenuContent align="end">
                                    <DropdownMenuItem
                                        onClick={() => {
                                            navigator.clipboard.writeText(
                                                job.jobId
                                            )
                                            toast.success('Job ID copied')
                                        }}
                                    >
                                        <ICON.duplicate />
                                        Copy job ID
                                    </DropdownMenuItem>
                                    {isAdmin && (
                                        <>
                                            <DropdownMenuSeparator />
                                            <DropdownMenuItem
                                                onClick={deleteConfirm.open}
                                                className="text-destructive focus:text-destructive"
                                            >
                                                <ICON.delete />
                                                Delete bulletin
                                            </DropdownMenuItem>
                                        </>
                                    )}
                                </DropdownMenuContent>
                            </DropdownMenu>
                        </div>
                    </div>
                    {/* Tabs travel with the header; curation has its own single-purpose surface. */}
                    {!curationOpen && (
                        <div className="mx-auto w-full max-w-7xl px-4 pb-2 pt-2 sm:px-6 2xl:max-w-352">
                            <TabsList>
                                <TabsTrigger value="review">Review</TabsTrigger>
                                {hasAdjust && (
                                    <TabsTrigger value="adjust">
                                        Adjust
                                        {review.changes.length > 0 && (
                                            <span className="ml-1.5 inline-flex h-4 min-w-4 items-center justify-center rounded-full bg-primary/20 px-1 font-mono text-3xs tabular-nums text-primary-on-tint">
                                                {review.changes.length}
                                            </span>
                                        )}
                                    </TabsTrigger>
                                )}
                                <TabsTrigger value="details">
                                    Config &amp; audit
                                </TabsTrigger>
                            </TabsList>
                        </div>
                    )}
                    {/* A hotter deadline elsewhere interrupts politely: one slim
                    row, one action. Sticky with the header so it never
                    scrolls out of awareness. */}
                    {hotter && (
                        <HotterJobBanner
                            hotterJob={hotter.job}
                            onSwitch={() =>
                                navigate(`${ROUTE_JOBS}/${hotter.job.jobId}`)
                            }
                            onDismiss={() => setHotterDismissed(true)}
                        />
                    )}
                </header>

                {(curationOpen
                    ? (['review'] as readonly PaneTab[])
                    : PANE_TABS.filter((t) => t !== 'adjust' || hasAdjust)
                ).map((t) => (
                    <TabsContent
                        key={t}
                        value={t}
                        className={cn(
                            'mx-auto flex w-full max-w-7xl flex-1 flex-col gap-4 px-4 pt-4 sm:px-6 sm:pt-6 2xl:max-w-352',
                            waiting ? 'pb-28' : 'pb-4 sm:pb-6'
                        )}
                    >
                        {renderPanel(t)}
                    </TabsContent>
                ))}

                {/* The decision floats above the content, always in reach */}
                {waiting && (
                    <DecisionDock
                        job={job}
                        onRevise={() => actions.setIsRejectOpen(true)}
                        reviseLabel={failed ? 'Regenerate' : 'Revise'}
                        onApprove={
                            job.status === 'WAITING_APPROVAL'
                                ? requestApprove
                                : undefined
                        }
                        isApproving={actions.isApproving}
                        disabled={
                            actions.isApproving ||
                            actions.isRejecting ||
                            actions.isDeleting
                        }
                        changesCount={review.changes.length}
                        airSlotAt={airSlotAt}
                    />
                )}

                <ReviseDialog
                    open={actions.isRejectOpen}
                    onOpenChange={actions.setIsRejectOpen}
                    job={job}
                    review={review}
                    onReject={async (decision) => {
                        await actions.handleReject(decision)
                    }}
                    onRejectFallback={async (decision) => {
                        await actions.handleRejectToFallback(decision)
                    }}
                    isRejecting={actions.isRejecting}
                    isRejectingFallback={actions.isRejectingToFallback}
                    secondsToDeadline={
                        bindingDeadline?.secondsRemaining ?? null
                    }
                    onAddToDictionary={(word) =>
                        navigate(
                            `${ROUTE_PRONUNCIATION}?suggest=${encodeURIComponent(word)}`
                        )
                    }
                    onResolveIssue={(route) => {
                        if (route === 'pronunciation') {
                            navigate(ROUTE_PRONUNCIATION)
                        } else {
                            setTab(route === 'sources' ? 'adjust' : 'review')
                        }
                    }}
                />

                <ConfirmDialog
                    open={approveConfirm.isOpen}
                    onOpenChange={approveConfirm.onOpenChange}
                    onConfirm={doApprove}
                    destructive={false}
                    confirmLabel={approveDialogConfirmLabel}
                    title={approveDialogTitle}
                    description={approveDialogDescription}
                />

                <ConfirmDialog
                    open={deleteConfirm.isOpen}
                    onOpenChange={deleteConfirm.onOpenChange}
                    onConfirm={async () => {
                        await actions.handleDelete()
                        navigate(ROUTE_JOBS)
                    }}
                    title="Delete bulletin"
                    description={`Deleting the ${job.station} ${formatMarketName(job.market)} bulletin cannot be undone.`}
                />
            </div>
        </Tabs>
    )
}

/**
 * The interrupt banner's countdown, isolated in its own leaf so the 1Hz
 * refresh it needs stays scoped to one slim row instead of forcing the
 * WHOLE review surface (header, tabs, JobReviewCockpit) to re-render every
 * second — that was the prior bug (`ReviewPaneBody` held `useSecondTick()`
 * at its top level). Recomputes `secs`/`band` fresh each tick from the job
 * itself, matching DecisionDock's pattern.
 */
function HotterJobBanner({
    hotterJob,
    onSwitch,
    onDismiss,
}: {
    readonly hotterJob: JobSummary
    readonly onSwitch: () => void
    readonly onDismiss: () => void
}) {
    useSecondTick()
    const secs = getSecondsRemaining(hotterJob) ?? 0
    const band = getUrgencyBand(secs)
    const hot = band === 'critical' || band === 'overdue'
    return (
        <div
            className={cn(
                'flex w-full flex-wrap items-center justify-center gap-x-2 gap-y-1 border-t px-4 py-1 text-xs font-medium',
                hot
                    ? 'border-destructive/30 bg-destructive/10 text-destructive-on-tint'
                    : 'border-warning/30 bg-warning/10 text-warning'
            )}
        >
            <ICON.warning className="size-3.5 shrink-0" aria-hidden />
            <span className="min-w-0">
                {hotterJob.station}
                {!marketAlreadyShown(hotterJob.station, hotterJob.market) &&
                    ` ${formatMarketName(hotterJob.market)}`}{' '}
                is more urgent ·{' '}
                <span className="font-mono font-semibold tabular-nums">
                    {secs < 0
                        ? `${formatOverdue(Math.abs(secs))} over`
                        : `${formatDuration(secs)} left`}
                </span>
            </span>
            <button
                type="button"
                onClick={onSwitch}
                className="inline-flex items-center gap-1 rounded px-2 py-1 underline underline-offset-2 transition-colors hover:bg-foreground/10"
            >
                Switch
                <ICON.forward className="size-3" aria-hidden />
            </button>
            <button
                type="button"
                onClick={onDismiss}
                aria-label="Dismiss: finish this bulletin first"
                title="Dismiss: finish this bulletin first"
                className="-my-1 inline-flex size-7 items-center justify-center rounded opacity-70 transition-opacity hover:opacity-100"
            >
                <ICON.close className="size-3.5" aria-hidden />
            </button>
        </div>
    )
}
