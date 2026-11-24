// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { TooltipProvider } from '@/components/ui/tooltip'
import { DeliveryStyleStudio } from './DeliveryStyleStudio'

vi.mock('@/features/delivery/api/delivery-style-hooks', () => ({
    useDeliveryStyle: () => ({ data: undefined, isLoading: false }),
    useCreateDeliveryStyle: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useUpdateDeliveryStyle: () => ({ mutateAsync: vi.fn(), isPending: false }),
    usePreviewDeliveryStyle: () => ({ mutateAsync: vi.fn(), isPending: false }),
}))

vi.mock('@/features/schedules/api/schedule-hooks', () => ({
    useSchedules: () => ({ data: undefined, isLoading: false }),
}))

class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
}
vi.stubGlobal('ResizeObserver', ResizeObserverStub)

/**
 * Regression for bug-bash-2026-08-05 finding 29: the delivery studio closed
 * Cancel/X/ESC with no confirm while the station studio guarded the identical
 * flow — typed tone/cue work was silently discarded. The studio must confirm
 * before discarding unsaved edits, exactly like ScheduleStudio.
 */
function renderStudio(onOpenChange = vi.fn()) {
    const queryClient = new QueryClient()
    render(
        <QueryClientProvider client={queryClient}>
            <TooltipProvider>
                <DeliveryStyleStudio
                    open
                    onOpenChange={onOpenChange}
                    styleId={null}
                />
            </TooltipProvider>
        </QueryClientProvider>
    )
    return { onOpenChange }
}

describe('DeliveryStyleStudio Cancel — unsaved changes', () => {
    afterEach(() => {
        cleanup()
        vi.clearAllMocks()
    })

    it('closes immediately when nothing has been edited', async () => {
        const user = userEvent.setup()
        const { onOpenChange } = renderStudio()

        await user.click(screen.getByRole('button', { name: 'Cancel' }))

        expect(onOpenChange).toHaveBeenCalledWith(false)
        expect(
            screen.queryByText('Discard unsaved changes?')
        ).not.toBeInTheDocument()
    })

    it('prompts a discard confirmation instead of closing after an edit', async () => {
        const user = userEvent.setup()
        const { onOpenChange } = renderStudio()

        await user.type(screen.getByLabelText(/Style name/), 'Peak Drive')

        await user.click(screen.getByRole('button', { name: 'Cancel' }))

        expect(onOpenChange).not.toHaveBeenCalled()
        expect(screen.getByText('Discard unsaved changes?')).toBeInTheDocument()

        await user.click(screen.getByRole('button', { name: 'Discard' }))
        expect(onOpenChange).toHaveBeenCalledWith(false)
    })

    it('keeps the studio open when the confirmation is cancelled', async () => {
        const user = userEvent.setup()
        const { onOpenChange } = renderStudio()

        await user.type(screen.getByLabelText(/Style name/), 'Peak Drive')
        await user.click(screen.getByRole('button', { name: 'Cancel' }))

        // Dismiss the confirmation (its own Cancel), not the studio's
        const alertDialog = screen.getByRole('alertdialog')
        await user.click(
            within(alertDialog).getByRole('button', { name: 'Cancel' })
        )

        expect(onOpenChange).not.toHaveBeenCalled()
        // The studio is still open with the typed value intact
        expect(screen.getByLabelText(/Style name/)).toHaveValue('Peak Drive')
    })
})
