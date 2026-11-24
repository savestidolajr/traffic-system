// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ReviseDialog } from './ReviseDialog'
import type { Job } from '../../types'
import type { BulletinReview } from '../../hooks/use-bulletin-review'
import type { RejectDecision } from '../../api/job-hooks'
import { TooltipProvider } from '@/components/ui/tooltip'

Element.prototype.scrollIntoView = vi.fn()
Element.prototype.hasPointerCapture = vi.fn(() => false)
Element.prototype.setPointerCapture = vi.fn()
Element.prototype.releasePointerCapture = vi.fn()

// The audition is the only network call this flow makes; a 37s read against a
// 30s slot is outside the ±10% fit band, so the verdict warns.
vi.mock('../../api/job-hooks', async (importOriginal) => {
    const actual = (await importOriginal()) as Record<string, unknown>
    return {
        ...actual,
        auditionScript: vi.fn().mockResolvedValue({
            url: 'blob:audition',
            durationSec: 37,
            tagCount: 0,
        }),
    }
})

vi.mock('@/features/voices/api/voice-profile-hooks', () => ({
    useVoiceProfiles: () => ({
        data: {
            profiles: [
                {
                    id: 'vp-dolly',
                    name: 'Dolly Male Young',
                    baseVoiceName: 'Dolly',
                    status: 'active',
                    currentVersion: 1,
                    versions: [{ version: 1, model: 'eleven_v3' }],
                },
            ],
        },
        isLoading: false,
    }),
}))

vi.mock('@/lib/api/client', () => ({
    apiClient: { post: vi.fn().mockResolvedValue({ data: {} }) },
    ApiError: class ApiError extends Error {},
}))

afterEach(() => {
    cleanup()
    vi.clearAllMocks()
})

const job = {
    jobId: 'job-1',
    market: 'Cairns',
    status: 'WAITING_APPROVAL',
    targetDurationSec: 30,
    script: { text: 'James Street is at a standstill this morning.' },
    input: {
        voice: { voiceName: 'Cairns Anna', model: 'eleven_multilingual_v2' },
    },
} as unknown as Job

function makeReview(overrides: Partial<BulletinReview> = {}): BulletinReview {
    return {
        changes: [
            {
                id: 'voice',
                kind: 'voice',
                label: 'Voice: Dolly Male Young',
                removable: true,
            },
        ],
        removeChange: vi.fn(),
        voiceOverride: {
            profileId: 'vp-dolly',
            name: 'Dolly Male Young',
            model: 'eleven_v3',
        },
        setVoiceOverride: vi.fn(),
        applyKind: 'revoice',
        regenerate: vi.fn(),
        isRegenerating: false,
        ...overrides,
    } as unknown as BulletinReview
}

function renderDialog(
    review: BulletinReview,
    props: {
        onReject?: (decision: RejectDecision) => Promise<void>
        onResolveIssue?: (route: 'pronunciation' | 'sources' | 'script') => void
    } = {}
) {
    return render(
        <TooltipProvider>
            <ReviseDialog
                open
                onOpenChange={vi.fn()}
                job={job}
                review={review}
                onReject={props.onReject ?? vi.fn()}
                onRejectFallback={vi.fn()}
                onResolveIssue={props.onResolveIssue}
                isRejecting={false}
                isRejectingFallback={false}
            />
        </TooltipProvider>
    )
}

describe('ReviseDialog — canonical revision paths', () => {
    it('sends wording changes to the script editor instead of a blind regeneration instruction', () => {
        renderDialog(
            makeReview({
                changes: [],
                voiceOverride: null,
                applyKind: null,
            })
        )

        expect(
            screen.queryByPlaceholderText(/describe a change to regenerate/i)
        ).not.toBeInTheDocument()
        expect(
            screen.getAllByText(/edit the script or adjust source information/i)
        ).not.toHaveLength(0)
        expect(
            screen.getByRole('button', { name: /no changes to apply/i })
        ).toBeDisabled()
    })

    it('reserves regeneration for queued source changes', () => {
        renderDialog(
            makeReview({
                changes: [
                    {
                        id: 'excl-inc-1',
                        kind: 'incident-removed',
                        label: 'Dropped blocked lane',
                        removable: true,
                    },
                ],
                voiceOverride: null,
                applyKind: 'regenerate',
            })
        )

        expect(
            screen.getByText(
                /source changes require the bulletin to be rewritten/i
            )
        ).toBeInTheDocument()
        expect(
            screen.getByRole('button', { name: /regenerate · ~1 min/i })
        ).toBeEnabled()
    })
})

describe('ReviseDialog — rejection feedback boundary', () => {
    // 15s, not the 5s default: this drives a long interaction (two dialogs, a
    // select and a ~60-char typed sentence) and it times out at 5s when the
    // verify lanes run in parallel — observed twice on 2026-08-10.
    it('sends only the fields the operator supplied', async () => {
        const user = userEvent.setup({ delay: null })
        const onReject = vi.fn().mockResolvedValue(undefined)
        renderDialog(makeReview(), { onReject })

        await user.click(
            screen.getByRole('button', {
                name: /this bulletin must not air/i,
            })
        )
        await user.click(screen.getByRole('button', { name: /yes, reject/i }))
        await user.click(screen.getByRole('combobox', { name: /reason/i }))
        await user.click(
            screen.getByRole('option', {
                name: /important item was not prioritised/i,
            })
        )

        await user.type(
            screen.getByPlaceholderText(/why can this bulletin not air/i),
            'A routine delay appeared before the motorway closure.'
        )
        await user.click(
            screen.getByRole('button', { name: /reject bulletin/i })
        )

        // The reject handler is async — under a loaded CPU the click can resolve
        // before the submit's microtasks flush, so wait rather than assert inline.
        await waitFor(() =>
            expect(onReject).toHaveBeenCalledWith({
                reasonCode: 'EditorialPriority',
                notes: 'A routine delay appeared before the motorway closure.',
            })
        )
    }, 15_000)

    it('rejects with no reason and no notes — both are optional', async () => {
        const user = userEvent.setup({ delay: null })
        const onReject = vi.fn().mockResolvedValue(undefined)
        renderDialog(makeReview(), { onReject })

        await user.click(
            screen.getByRole('button', {
                name: /this bulletin must not air/i,
            })
        )
        await user.click(screen.getByRole('button', { name: /yes, reject/i }))
        await user.click(
            screen.getByRole('button', { name: /reject bulletin/i })
        )

        await waitFor(() => expect(onReject).toHaveBeenCalledWith({}))
    }, 15_000)

    it('routes pronunciation to its correction tool', async () => {
        const user = userEvent.setup()
        const onResolveIssue = vi.fn()
        renderDialog(makeReview(), { onResolveIssue })

        await user.click(
            screen.getByRole('button', {
                name: /this bulletin must not air/i,
            })
        )
        await user.click(screen.getByRole('button', { name: /yes, reject/i }))
        await user.click(screen.getByRole('combobox', { name: /reason/i }))
        await user.click(
            screen.getByRole('option', { name: /^pronunciation$/i })
        )

        await user.click(
            screen.getByRole('button', { name: /fix pronunciation/i })
        )
        expect(onResolveIssue).toHaveBeenCalledWith('pronunciation')
    })
})

/**
 * A voice-only batch applies as a re-voice, but a failing fit verdict means
 * the approved words cannot fill the slot on the staged voice. The warning
 * used to SAY "choose Regenerate" while the dialog offered no way to choose
 * it — the operator's only path was to queue fake content feedback. The
 * warning is now the escalation control itself.
 */
describe('ReviseDialog — fit-verdict escalation to regenerate', () => {
    it('offers Regenerate on a failing verdict and applies the batch as a full regenerate', async () => {
        const user = userEvent.setup()
        const review = makeReview()
        renderDialog(review)

        // Voice-only batch: the primary action is the cheap re-voice.
        expect(
            screen.getByRole('button', { name: /re-voice · ~15 sec/i })
        ).toBeInTheDocument()

        await user.click(
            screen.getByRole('button', { name: /audition with this voice/i })
        )
        await screen.findByText(/37s vs 30s slot/)

        await user.click(
            screen.getByRole('button', { name: /regenerate instead/i })
        )

        // The whole apply is escalated: label, estimate and the wire flag. The
        // escalation is a state-driven re-render — find, don't get, under load.
        const apply = await screen.findByRole('button', {
            name: /regenerate · ~1 min/i,
        })
        await user.click(apply)
        await waitFor(() =>
            expect(review.regenerate).toHaveBeenCalledWith(false)
        )
    })

    it('lets the operator step back down to the re-voice', async () => {
        const user = userEvent.setup()
        const review = makeReview()
        renderDialog(review)

        await user.click(
            screen.getByRole('button', { name: /audition with this voice/i })
        )
        await screen.findByText(/37s vs 30s slot/)
        await user.click(
            screen.getByRole('button', { name: /regenerate instead/i })
        )
        await user.click(screen.getByRole('button', { name: /keep re-voice/i }))

        const apply = await screen.findByRole('button', {
            name: /re-voice · ~15 sec/i,
        })
        await user.click(apply)
        await waitFor(() =>
            expect(review.regenerate).toHaveBeenCalledWith(true)
        )
    })
})
