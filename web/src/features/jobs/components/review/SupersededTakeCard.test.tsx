// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { describe, it, expect, afterEach, vi } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TooltipProvider } from '@/components/ui/tooltip'
import { SupersededTakeCard } from './SupersededTakeCard'
import type {
    FallbackJob,
    Job,
    PublishedJob,
    SupersededTake,
} from '../../types'

afterEach(() => {
    cleanup()
})

const take: SupersededTake = {
    capturedAt: '2026-08-24T01:46:19Z',
    reason: 'missed-window',
    text: 'The M4 is slow citybound near Parramatta.',
    taggedText: null,
    audioUrl: 'https://blob.test/broadcast_1.wav',
    measuredDurationSec: 28.4,
    estimatedDurationSec: 29.1,
    sentences: [
        {
            text: 'The M4 is slow citybound near Parramatta.',
            corridorKeys: ['M4'],
            evidenceIds: [],
            routeKeys: [],
        },
    ],
    wordTimings: null,
    preReview: null,
    deliveryStyleId: null,
    deliveryStyleVersion: null,
}

const base = {
    jobId: 'job-1',
    station: '2GB 873',
    market: 'Sydney',
    targetDurationSec: 30,
    createdAt: '2026-08-24T01:40:00Z',
    lastUpdatedAt: '2026-08-24T01:46:19Z',
    input: {
        bulletinType: 'traffic',
        timeWindow: {
            from: '2026-08-24T01:45:00Z',
            to: '2026-08-24T01:50:00Z',
        },
        branding: { stationTag: '2GB', signOff: '' },
        voice: {
            provider: 'elevenlabs',
            voiceId: 'v1',
            voiceName: 'Hudson',
            model: 'eleven_v3',
        },
    },
    logs: [],
    statusHistory: [],
    goodRuns: [],
    traffic: {
        fetchedAt: '2026-08-24T01:41:00Z',
        bbox: [],
        incidents: [],
        corridors: [],
        flowRoads: [],
        monitoredRoutes: [],
    },
    selectedIncidents: [],
    audio: {
        data: {
            format: 'wav' as const,
            sampleRateHz: 44100,
            durationSec: 14,
            url: '/cart.wav',
        },
        measuredDurationSec: 14,
        deltaSec: -16,
    },
}

/** A slot the standby cart covered: aired script is blank, the take is all there is. */
function cartCovered(overrides: Partial<FallbackJob> = {}): Job {
    return {
        ...base,
        status: 'FALLBACK_USED',
        script: { version: 1, text: '', estimatedDurationSec: 0 },
        fallback: {
            sourceJobId: '',
            appliedAt: '2026-08-24T01:46:19Z',
            reason: 'missed-window',
            streak: 1,
        },
        supersededTake: take,
        ...overrides,
    } as FallbackJob
}

function renderCard(job: Job, onUseAsDraft?: (text: string) => void) {
    return render(
        <TooltipProvider>
            <SupersededTakeCard job={job} onUseAsDraft={onUseAsDraft} />
        </TooltipProvider>
    )
}

describe('SupersededTakeCard', () => {
    it('renders nothing when the job never had a bulletin replaced', () => {
        const { container } = renderCard(
            cartCovered({ supersededTake: null } as Partial<FallbackJob>)
        )
        expect(container).toBeEmptyDOMElement()
    })

    it('keeps the retained script readable on a slot the standby cart covered', async () => {
        // This is the case the field exists for: ApplyFillerFallback clears
        // ScriptText, so without the take the operator sees no script at all.
        renderCard(cartCovered())
        expect(
            screen.getByText('Generated Aug 24, 11:46:19 · missed-window')
        ).toBeInTheDocument()

        await userEvent.click(
            screen.getByRole('button', { name: /Bulletin that did not air/ })
        )
        expect(
            screen.getByText('The M4 is slow citybound near Parramatta.')
        ).toBeInTheDocument()
        // Diffing against a blank aired script is noise, so it is suppressed.
        expect(screen.queryByText('Changes vs what aired')).toBeNull()
    })

    it('diffs the take against the script that actually aired', async () => {
        renderCard(
            cartCovered({
                script: {
                    version: 2,
                    text: 'An earlier read that aired instead.',
                    estimatedDurationSec: 12,
                },
                fallback: {
                    sourceJobId: 'job-source',
                    appliedAt: '2026-08-24T01:46:19Z',
                    reason: 'missed-window',
                    streak: 1,
                },
            } as Partial<FallbackJob>)
        )
        await userEvent.click(
            screen.getByRole('button', { name: /Bulletin that did not air/ })
        )
        expect(screen.getByText('Changes vs what aired')).toBeInTheDocument()
        expect(
            screen.getByText(
                'An earlier bulletin from today aired instead of this one.',
                {
                    exact: false,
                }
            )
        ).toBeInTheDocument()
    })

    it('offers the retained script as a regeneration seed when a retry is legitimate', async () => {
        const onUseAsDraft = vi.fn()
        renderCard(cartCovered(), onUseAsDraft)
        await userEvent.click(
            screen.getByRole('button', { name: /Bulletin that did not air/ })
        )
        await userEvent.click(
            screen.getByRole('button', { name: 'Use as working script' })
        )
        expect(onUseAsDraft).toHaveBeenCalledWith(
            'The M4 is slow citybound near Parramatta.'
        )
    })

    it('is purely read-only on a delivered job', async () => {
        // PUBLISHED has no regeneration edge, so ReviewPane withholds the handler
        // and the card must not offer an action that cannot be taken.
        const published = {
            ...base,
            status: 'PUBLISHED',
            script: { version: 1, text: '', estimatedDurationSec: 0 },
            approval: {
                decidedAt: '2026-08-24T01:47:00Z',
                decidedBy: 'op@arn.com.au',
            },
            publish: {
                destination: 'broadcast',
                publishedAt: '2026-08-24T01:48:00Z',
                publishedBy: 'op@arn.com.au',
            },
            fallback: {
                sourceJobId: '',
                appliedAt: '2026-08-24T01:46:19Z',
                reason: 'missed-window',
                streak: 1,
            },
            supersededTake: take,
        } as PublishedJob

        renderCard(published)
        await userEvent.click(
            screen.getByRole('button', { name: /Bulletin that did not air/ })
        )
        expect(
            screen.getByText('The M4 is slow citybound near Parramatta.')
        ).toBeInTheDocument()
        expect(
            screen.queryByRole('button', { name: 'Use as working script' })
        ).toBeNull()
    })
})
