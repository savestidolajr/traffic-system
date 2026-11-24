// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { describe, it, expect, vi, afterEach } from 'vitest'
import {
    render,
    screen,
    cleanup,
    waitFor,
    within,
} from '@testing-library/react'
import type { ComponentProps } from 'react'
import userEvent from '@testing-library/user-event'
import { JobActions } from './JobActions'
import { TooltipProvider } from '@/components/ui/tooltip'
import type { Job } from '../types'

afterEach(() => {
    cleanup()
})

/** Minimal fixture — JobActions only reads `job.status`. */
function makeJob(status: Job['status']): Job {
    return { status } as unknown as Job
}

function renderActions(
    job: Job,
    onRegenerate = vi.fn(),
    slotMissed = false,
    pending: Partial<
        Pick<
            ComponentProps<typeof JobActions>,
            'isApproving' | 'isPublishing' | 'isRegenerating'
        >
    > = {}
) {
    return render(
        <TooltipProvider>
            <JobActions
                job={job}
                onApprove={vi.fn()}
                onPublish={vi.fn()}
                onRegenerate={onRegenerate}
                isApproving={pending.isApproving ?? false}
                isPublishing={pending.isPublishing ?? false}
                isRegenerating={pending.isRegenerating ?? false}
                slotMissed={slotMissed}
            />
        </TooltipProvider>
    )
}

describe('JobActions — fallback retry', () => {
    it('offers Retry generation alongside Approve fallback for FALLBACK_USED', () => {
        renderActions(makeJob('FALLBACK_USED'))

        expect(
            screen.getByRole('button', { name: /retry generation/i })
        ).toBeInTheDocument()
        expect(
            screen.getByRole('button', { name: /approve fallback/i })
        ).toBeInTheDocument()
    })

    it('offers nothing once the slot has gone', () => {
        // A healthy covered slot holds FALLBACK_USED for ~0.2s before system (deadline)
        // publishes it, so a job found here is one whose delivery failed — measured across
        // dev and test, every such row was 12.7 to 28.7 days old. Retrying would generate a
        // bulletin for dead air and approving would sign off audio nothing will collect.
        const { container } = renderActions(
            makeJob('FALLBACK_USED'),
            vi.fn(),
            true
        )

        expect(container).toBeEmptyDOMElement()
    })

    it('confirms before retrying a fallback, then regenerates', async () => {
        const user = userEvent.setup()
        const onRegenerate = vi.fn()
        renderActions(makeJob('FALLBACK_USED'), onRegenerate)

        await user.click(
            screen.getByRole('button', { name: /retry generation/i })
        )
        const dialog = screen.getByRole('alertdialog')
        expect(
            within(dialog).getByText(/replaces the standby bulletin/i)
        ).toBeInTheDocument()

        await user.click(
            within(dialog).getByRole('button', { name: /^retry generation$/i })
        )
        await waitFor(() => expect(onRegenerate).toHaveBeenCalledTimes(1))
    })

    it('does not regenerate when the retry confirm is cancelled', async () => {
        const user = userEvent.setup()
        const onRegenerate = vi.fn()
        renderActions(makeJob('FALLBACK_USED'), onRegenerate)

        await user.click(
            screen.getByRole('button', { name: /retry generation/i })
        )
        await user.click(screen.getByRole('button', { name: /^cancel$/i }))

        expect(onRegenerate).not.toHaveBeenCalled()
    })

    it('retries a FAILED bulletin directly, without a confirm step', async () => {
        const user = userEvent.setup()
        const onRegenerate = vi.fn()
        renderActions(makeJob('FAILED'), onRegenerate)

        await user.click(screen.getByRole('button', { name: /^retry$/i }))

        expect(onRegenerate).toHaveBeenCalledTimes(1)
    })

    it('labels an in-flight retry instead of leaving the operator guessing', () => {
        renderActions(makeJob('FAILED'), vi.fn(), false, {
            isRegenerating: true,
        })

        expect(
            screen.getByRole('button', { name: /retrying/i })
        ).toBeInTheDocument()
    })
})
