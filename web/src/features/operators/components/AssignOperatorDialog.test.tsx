// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { TooltipProvider } from '@/components/ui/tooltip'
import { AssignOperatorDialog } from './AssignOperatorDialog'

vi.mock('@/features/schedules/api/schedule-hooks', () => ({
    useSchedules: () => ({
        data: { schedules: [{ marketId: 'adelaide' }, { marketId: 'perth' }] },
        isLoading: false,
    }),
}))

vi.mock('../api/operator-hooks', () => ({
    useUpsertOperatorAssignment: () => ({ mutateAsync: vi.fn() }),
}))

/**
 * Regression for bug-bash-2026-08-05 finding 23: the market picker accepted any
 * typed string, so a typo saved with a success toast and silently locked the
 * operator out of every station. The add path must reject markets the schedule
 * catalogue does not own, with a clear warning.
 */
function renderDialog() {
    return render(
        <TooltipProvider>
            <AssignOperatorDialog open onOpenChange={vi.fn()} entry={null} />
        </TooltipProvider>
    )
}

describe('AssignOperatorDialog market picker', () => {
    afterEach(() => {
        cleanup()
        vi.clearAllMocks()
    })

    it('adds a market that exists in the schedule catalogue', async () => {
        const user = userEvent.setup()
        renderDialog()

        await user.type(screen.getByLabelText('Add a station'), 'perth')
        await user.keyboard('{Enter}')

        expect(
            screen.getByRole('button', { name: 'Remove station perth' })
        ).toBeInTheDocument()
    })

    it('rejects a market the catalogue does not know, with a warning', async () => {
        const user = userEvent.setup()
        renderDialog()

        await user.type(
            screen.getByLabelText('Add a station'),
            'zz-bb-ui-bogus'
        )
        await user.keyboard('{Enter}')

        expect(screen.getByRole('alert').textContent).toContain(
            'is not a known station'
        )
        expect(
            screen.queryByRole('button', {
                name: 'Remove station zz-bb-ui-bogus',
            })
        ).not.toBeInTheDocument()
    })
})
