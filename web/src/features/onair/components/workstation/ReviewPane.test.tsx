// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import {
    render,
    screen,
    cleanup,
    within,
    waitFor,
    act,
} from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import userEvent from '@testing-library/user-event'

import { afterEach, describe, expect, it, vi } from 'vitest'
import type { Job } from '@/features/jobs/types'

const mocks = vi.hoisted(() => ({
    useJobDetail: vi.fn(),
    useJobs: vi.fn(),
    useJobActions: vi.fn(),
    useBulletinReview: vi.fn(),
    useScriptVerification: vi.fn(),
    useAirSlot: vi.fn(),
    handleApprove: vi.fn(),
    handlePublish: vi.fn(),
}))

vi.mock('@/features/jobs/api/job-hooks', () => ({
    useJobDetail: mocks.useJobDetail,
    useJobs: mocks.useJobs,
    useProceedCuration: () => ({ mutate: vi.fn(), isPending: false }),
}))
vi.mock('@/features/jobs/hooks/use-job-actions', () => ({
    useJobActions: mocks.useJobActions,
}))
vi.mock('@/features/jobs/hooks/use-bulletin-review', () => ({
    useBulletinReview: mocks.useBulletinReview,
}))
vi.mock('@/features/jobs/hooks/use-script-verification', () => ({
    useScriptVerification: mocks.useScriptVerification,
}))
vi.mock('@/features/jobs/hooks/use-air-slot', () => ({
    useAirSlot: mocks.useAirSlot,
}))
// `use-global-keydown` is deliberately NOT mocked: the `A` approve hotkey was
// silently dropped once by a rewrite, and only the real listener proves the
// registration still reaches window.
vi.mock('@/hooks/use-current-user', () => ({
    useIsAdmin: () => false,
}))
vi.mock('@/features/jobs/components/review/JobReviewCockpit', () => ({
    JobReviewCockpit: () => <div>review cockpit</div>,
}))
vi.mock('@/features/jobs/components/review/ReviewDetailsTab', () => ({
    ReviewDetailsTab: () => <div>review details</div>,
}))
vi.mock('@/features/jobs/components/review/DecisionDock', () => ({
    DecisionDock: ({ onApprove }: { onApprove: () => void }) => (
        <button type="button" onClick={onApprove}>
            Approve
        </button>
    ),
}))
vi.mock('@/features/jobs/components/review/ReviseDialog', () => ({
    ReviseDialog: () => null,
}))
vi.mock('@/features/jobs/components', () => ({
    JobActions: ({ onPublish }: { onPublish: () => void }) => (
        <button type="button" onClick={onPublish}>
            Publish
        </button>
    ),
}))
// `ConfirmDialog` is deliberately NOT mocked: the approve gate's whole value is
// the wording it puts in front of an irreversible action, so the title, the
// description and the confirm label are the contract under test.

import { ReviewPane } from './ReviewPane'

const makeJob = (status: Job['status'], overrides: Partial<Job> = {}): Job =>
    ({
        jobId: 'job-1',
        station: 'Test station',
        market: 'sydney',
        status,
        lastUpdatedAt: new Date().toISOString(),
        etag: 'etag-1',
        ...overrides,
    }) as Job

interface SetupOptions {
    readonly airSlotAt?: Date | null
    readonly flaggedSentenceCount?: number
    readonly isApproving?: boolean
    readonly applyInFlight?: string | null
}

function setup(job: Job, options: SetupOptions = {}) {
    mocks.useJobDetail.mockReturnValue({
        data: job,
        isLoading: false,
        isError: false,
        error: null,
        refetch: vi.fn(),
    })
    mocks.useJobs.mockReturnValue({ data: { jobs: [] } })
    mocks.useJobActions.mockReturnValue({
        handleApprove: mocks.handleApprove,
        handlePublish: mocks.handlePublish,
        handleRegenerate: vi.fn(),
        handleReject: vi.fn(),
        handleRejectToFallback: vi.fn(),
        handleDelete: vi.fn(),
        setIsRejectOpen: vi.fn(),
        isRejectOpen: false,
        isApproving: options.isApproving ?? false,
        isPublishing: false,
        isRegenerating: false,
        isRejecting: false,
        isRejectingToFallback: false,
        isDeleting: false,
    })
    mocks.useBulletinReview.mockReturnValue({
        changes: [],
        applyInFlight: options.applyInFlight ?? null,
        lastRevoiceScript: null,
    })
    mocks.useScriptVerification.mockReturnValue({
        flaggedSentenceCount: options.flaggedSentenceCount ?? 0,
    })
    mocks.useAirSlot.mockReturnValue(options.airSlotAt ?? null)
}

function renderPane(onPublished: () => void = vi.fn()) {
    // The pane now issues a real (fail-soft) ledger-delta query; give it a client.
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    })
    render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>
                <ReviewPane jobId="job-1" onPublished={onPublished} />
            </MemoryRouter>
        </QueryClientProvider>
    )
}

/**
 * The real `useGlobalKeydown` listens on `window`, and its ignore-guard reads
 * `event.target` as an Element - so the event has to come off a real node the
 * way a browser delivers it, not off `window` itself.
 */
async function pressKey(key: string) {
    await act(async () => {
        document.body.dispatchEvent(
            new KeyboardEvent('keydown', { key, bubbles: true })
        )
    })
}

afterEach(() => {
    cleanup()
    vi.clearAllMocks()
})

describe('ReviewPane decision navigation', () => {
    it('keeps the current job after approval', async () => {
        const onPublished = vi.fn()
        setup(makeJob('WAITING_APPROVAL'))
        const user = userEvent.setup()

        renderPane(onPublished)

        await user.click(screen.getByRole('button', { name: 'Approve' }))
        const dialog = await screen.findByRole('alertdialog')
        await user.click(
            within(dialog).getByRole('button', { name: 'Approve' })
        )

        expect(onPublished).not.toHaveBeenCalled()
    })

    it('calls the host navigation callback only after delivery is queued', async () => {
        const onPublished = vi.fn()
        setup(makeJob('APPROVED'))
        mocks.handlePublish.mockResolvedValueOnce(true)
        const user = userEvent.setup()

        renderPane(onPublished)
        await user.click(screen.getByRole('button', { name: 'Publish' }))

        expect(mocks.handlePublish).toHaveBeenCalledTimes(1)
        expect(onPublished).toHaveBeenCalledTimes(1)
    })

    it('does not navigate when publish is not queued', async () => {
        const onPublished = vi.fn()
        setup(makeJob('APPROVED'))
        mocks.handlePublish.mockResolvedValueOnce(false)
        const user = userEvent.setup()

        renderPane(onPublished)
        await user.click(screen.getByRole('button', { name: 'Publish' }))

        expect(mocks.handlePublish).toHaveBeenCalledTimes(1)
        expect(onPublished).not.toHaveBeenCalled()
    })
})

describe('ReviewPane - FAILED job with traffic data', () => {
    it('shows the Adjust tab when a failed job carries traffic data', () => {
        setup({
            ...makeJob('FAILED'),
            failure: {
                stage: 'GENERATING_SCRIPT',
                message: 'LLM call failed',
                occurredAt: '2026-06-11T00:02:00Z',
                attemptCount: 1,
                lastEstimatedDurationSec: null,
                targetDurationSec: 30,
                attemptHistory: [],
            },
            traffic: {
                fetchedAt: '2026-06-11T00:01:00Z',
                bbox: [144.5, -38.2, 145.5, -37.5],
                incidents: [
                    {
                        key: 'inc-1',
                        priority: 1,
                        headline: 'Crash',
                        detail: 'Blocked',
                    },
                ],
                corridors: [],
                flowRoads: [],
            },
        })
        renderPane()

        expect(screen.getByRole('tab', { name: /adjust/i })).toBeInTheDocument()
        expect(screen.getByRole('tab', { name: /review/i })).toBeInTheDocument()
    })
})

describe('ReviewPane - the approve gate names a missed slot', () => {
    it('escalates the confirmation when the air slot has already passed', async () => {
        // 20 minutes past, comfortably beyond SLOT_MISS_THRESHOLD_MS.
        setup(makeJob('WAITING_APPROVAL'), {
            airSlotAt: new Date(Date.now() - 20 * 60_000),
        })
        const user = userEvent.setup()
        renderPane()

        await user.click(screen.getByRole('button', { name: 'Approve' }))
        const dialog = await screen.findByRole('alertdialog')

        expect(
            within(dialog).getByText('Approve after the slot has passed?')
        ).toBeInTheDocument()
        expect(
            within(dialog).getByText(
                /slot has passed, so this bulletin airs late/i
            )
        ).toBeInTheDocument()
        expect(mocks.handleApprove).not.toHaveBeenCalled()
    })

    it('keeps the plain confirmation while the slot is still ahead', async () => {
        setup(makeJob('WAITING_APPROVAL'), {
            airSlotAt: new Date(Date.now() + 10 * 60_000),
        })
        const user = userEvent.setup()
        renderPane()

        await user.click(screen.getByRole('button', { name: 'Approve' }))
        const dialog = await screen.findByRole('alertdialog')

        expect(
            within(dialog).getByText('Approve bulletin?')
        ).toBeInTheDocument()
    })
})

describe('ReviewPane - approve always confirms (bug-bash #24)', () => {
    it('shows a confirmation before approving a clean bulletin - the click alone never fires the mutation', async () => {
        setup(makeJob('WAITING_APPROVAL'))
        const user = userEvent.setup()
        renderPane()

        await user.click(screen.getByRole('button', { name: 'Approve' }))

        const dialog = await screen.findByRole('alertdialog')
        expect(
            within(dialog).getByText('Approve bulletin?')
        ).toBeInTheDocument()
        expect(
            within(dialog).getByText(/cannot be undone/i)
        ).toBeInTheDocument()
        // The guard is the point: no mutation on the bare click.
        expect(mocks.handleApprove).not.toHaveBeenCalled()

        await user.click(
            within(dialog).getByRole('button', { name: 'Approve' })
        )
        await waitFor(() =>
            expect(mocks.handleApprove).toHaveBeenCalledTimes(1)
        )
    })

    it('does not approve when the confirmation is cancelled', async () => {
        setup(makeJob('WAITING_APPROVAL'))
        const user = userEvent.setup()
        renderPane()

        await user.click(screen.getByRole('button', { name: 'Approve' }))
        const dialog = await screen.findByRole('alertdialog')
        await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

        expect(mocks.handleApprove).not.toHaveBeenCalled()
        expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    })

    it('keeps the stricter contradicted-claims warning when sentences are flagged', async () => {
        setup(makeJob('WAITING_APPROVAL'), { flaggedSentenceCount: 2 })
        const user = userEvent.setup()
        renderPane()

        await user.click(screen.getByRole('button', { name: 'Approve' }))

        const dialog = await screen.findByRole('alertdialog')
        expect(
            within(dialog).getByText('Approve with contradicted claims?')
        ).toBeInTheDocument()
        expect(
            within(dialog).getByText(/2 sentences in this script contradict/)
        ).toBeInTheDocument()
        expect(mocks.handleApprove).not.toHaveBeenCalled()

        await user.click(
            within(dialog).getByRole('button', { name: /^approve anyway$/i })
        )
        await waitFor(() =>
            expect(mocks.handleApprove).toHaveBeenCalledTimes(1)
        )
    })
})

describe('ReviewPane - the A approve hotkey', () => {
    it('opens the approve confirmation and never approves on its own', async () => {
        setup(makeJob('WAITING_APPROVAL'))
        renderPane()

        await pressKey('a')

        expect(await screen.findByRole('alertdialog')).toBeInTheDocument()
        expect(mocks.handleApprove).not.toHaveBeenCalled()
    })

    it('answers the shifted key too, so caps lock is not a dead keyboard', async () => {
        setup(makeJob('WAITING_APPROVAL'))
        renderPane()

        await pressKey('A')

        expect(await screen.findByRole('alertdialog')).toBeInTheDocument()
    })

    it('is inert for a job with no approve edge', async () => {
        setup(makeJob('APPROVED'))
        renderPane()

        await pressKey('a')

        expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
        expect(mocks.handleApprove).not.toHaveBeenCalled()
    })

    it('is inert while an approve is already running', async () => {
        setup(makeJob('WAITING_APPROVAL'), { isApproving: true })
        renderPane()

        await pressKey('a')

        expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    })
})
