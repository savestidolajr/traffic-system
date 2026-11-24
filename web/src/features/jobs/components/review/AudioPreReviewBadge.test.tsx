// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AudioPreReviewBadge } from './AudioPreReviewBadge'
import { TooltipProvider } from '@/components/ui/tooltip'
import type { PreReviewData } from '../../types'

afterEach(() => {
    cleanup()
})

interface Counts {
    readonly suspectCount?: number
    readonly verifiedCount?: number
    readonly figureCount?: number
    readonly unlocated?: readonly string[]
}

function renderBadge(
    preReview: PreReviewData | undefined,
    counts: Counts = {}
) {
    return render(
        <TooltipProvider>
            <AudioPreReviewBadge
                preReview={preReview}
                suspectCount={counts.suspectCount ?? 0}
                verifiedCount={counts.verifiedCount ?? 0}
                figureCount={counts.figureCount ?? 0}
                unlocated={counts.unlocated ?? []}
            />
        </TooltipProvider>
    )
}

const clean: PreReviewData = {
    matchRatio: 0.97,
    suspectWords: [],
    numberMismatches: [],
}

describe('AudioPreReviewBadge', () => {
    it('renders nothing when preReview is absent', () => {
        const { container } = renderBadge(undefined)
        expect(container).toBeEmptyDOMElement()
    })

    it('shows the match percentage when nothing is outstanding', async () => {
        renderBadge(clean)
        expect(screen.getByText('97%')).toBeInTheDocument()

        await userEvent.hover(screen.getByText('97%'))
        expect(
            await screen.findByText(/Audio verified.*97%/)
        ).toBeInTheDocument()
    })

    it('counts words and figures separately', () => {
        renderBadge(clean, { suspectCount: 2, figureCount: 1 })
        expect(
            screen.getByText(/2 words to verify by ear · 1 figure unconfirmed/)
        ).toBeInTheDocument()
    })

    it('reads as clean once every flag is verified, and still says so', async () => {
        renderBadge(clean, { verifiedCount: 2 })
        expect(screen.getByText(/2 words verified/)).toBeInTheDocument()

        await userEvent.hover(screen.getByText('97%'))
        expect(
            await screen.findByText(/Nothing left to check/)
        ).toBeInTheDocument()
    })

    // The point of the redesign: the badge counts, the script carries the words.
    it('never renders a chip per suspect word', () => {
        renderBadge(
            { matchRatio: 0.8, suspectWords: ['Keilor'], numberMismatches: [] },
            { suspectCount: 1 }
        )
        expect(screen.queryByText('Keilor')).not.toBeInTheDocument()
    })

    it('never renders a chip per unconfirmed figure', () => {
        renderBadge(
            { matchRatio: 0.9, suspectWords: [], numberMismatches: ['8'] },
            { figureCount: 1 }
        )
        expect(screen.getByText(/1 figure unconfirmed/)).toBeInTheDocument()
        expect(screen.queryByText('8')).not.toBeInTheDocument()
    })

    it('points the operator at the script', async () => {
        renderBadge(clean, { suspectCount: 1 })
        await userEvent.hover(screen.getByText(/1 word to verify by ear/))
        expect(
            await screen.findByText(/Highlighted in the script below/)
        ).toBeInTheDocument()
    })

    // A word the script no longer contains cannot be marked in situ, so the
    // badge is the only place left to surface it — dropping it would hide a
    // real warning.
    it('names a flagged word that is no longer in the script', async () => {
        renderBadge(clean, { suspectCount: 1, unlocated: ['Woolloongabba'] })
        expect(screen.getByText('Woolloongabba')).toBeInTheDocument()

        await userEvent.hover(screen.getByText('Woolloongabba'))
        expect(
            await screen.findByText(/no longer in the script/)
        ).toBeInTheDocument()
    })
})
