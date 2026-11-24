// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { TooltipProvider } from '@/components/ui/tooltip'
import { FillerStatementsManager } from './FillerStatementsManager'

// One market, one statement that carries BOTH an availability window and the enabled
// switch — the two things a delete-plus-re-add "edit" would silently drop.
const mocks = vi.hoisted(() => ({
    save: vi.fn().mockResolvedValue(undefined),
    inventory: [
        {
            marketId: 'sydney',
            statements: [
                {
                    id: 'stmt-1',
                    text: 'Take it easy through the work zones.',
                    enabled: true,
                    availability: {
                        startDate: null,
                        endDate: null,
                        days: ['Monday'],
                        startLocal: '06:00',
                        endLocal: '09:00',
                    },
                },
            ],
        },
    ],
}))

vi.mock('../api/filler-statements-hooks', async (importOriginal) => ({
    ...((await importOriginal()) as Record<string, unknown>),
    useAllFillerStatements: () => ({
        data: mocks.inventory,
        isLoading: false,
    }),
    useSetFillerStatements: () => ({
        mutateAsync: mocks.save,
        isPending: false,
    }),
}))

vi.mock('@/hooks/use-current-user', () => ({
    useIsAdmin: () => false,
}))

afterEach(() => {
    cleanup()
    vi.clearAllMocks()
})

function renderManager() {
    return render(
        <QueryClientProvider client={new QueryClient()}>
            <TooltipProvider>
                <FillerStatementsManager
                    markets={[
                        {
                            marketId: 'sydney',
                            label: 'Sydney',
                            timezone: 'Australia/Sydney',
                        },
                    ]}
                    station="sydney"
                />
            </TooltipProvider>
        </QueryClientProvider>
    )
}

describe('FillerStatementsManager text editing', () => {
    it('rewrites an existing statement in place, keeping its id, switch and window', async () => {
        const user = userEvent.setup()
        renderManager()

        await user.click(
            screen.getByRole('button', { name: 'filler statement actions' })
        )
        // Superseded by the shared RuleRow kebab convention.
        await user.click(
            screen.getByRole('menuitem', { name: 'Edit filler statement text' })
        )

        const input = screen.getByLabelText('Statement text')
        expect(input).toHaveValue('Take it easy through the work zones.')

        // Nothing changed yet, so there is nothing to save. Re-query after typing:
        // RuleComposer remounts the button inside GlowBorder once the input is ready.
        expect(
            screen.getByRole('button', { name: 'Save changes' })
        ).toBeDisabled()

        await user.clear(input)
        await user.type(
            input,
            'Give yourself a little extra time this morning.'
        )
        const submit = screen.getByRole('button', { name: 'Save changes' })
        expect(submit).toBeEnabled()
        await user.click(submit)

        expect(mocks.save).toHaveBeenCalledTimes(1)
        expect(mocks.save).toHaveBeenCalledWith({
            marketId: 'sydney',
            statements: [
                {
                    id: 'stmt-1',
                    text: 'Give yourself a little extra time this morning.',
                    enabled: true,
                    availability: {
                        startDate: null,
                        endDate: null,
                        days: ['Monday'],
                        startLocal: '06:00',
                        endLocal: '09:00',
                    },
                },
            ],
        })
    })

    it('rejects bookend token syntax before it can reach the store', async () => {
        const user = userEvent.setup()
        renderManager()

        await user.click(
            screen.getByRole('button', { name: 'filler statement actions' })
        )
        // Superseded by the shared RuleRow kebab convention.
        await user.click(
            screen.getByRole('menuitem', { name: 'Edit filler statement text' })
        )
        const input = screen.getByLabelText('Statement text')
        await user.clear(input)
        // `{{` is userEvent's escape for a literal brace.
        await user.type(input, 'Next update at {{time}.')

        expect(screen.getByRole('alert')).toHaveTextContent(
            'cannot use bookend token syntax'
        )
        expect(
            screen.getByRole('button', { name: 'Save changes' })
        ).toBeDisabled()
        expect(mocks.save).not.toHaveBeenCalled()
    })
})
