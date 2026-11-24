// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { TooltipProvider } from '@/components/ui/tooltip'
import { VerifiedPronunciationsManager } from './VerifiedPronunciationsManager'

// Identity is the NORMALISED word, and provenance rides on the record — a note-only
// correction must not read as a duplicate of itself, nor lose the enabled state.
const mocks = vi.hoisted(() => ({
    save: vi.fn().mockResolvedValue(undefined),
    inventory: [
        {
            marketId: 'melbourne',
            words: [
                {
                    word: 'Keilor',
                    note: 'said KEE-ler',
                    enabled: true,
                    verifiedBy: 'Sam Operator',
                    verifiedAt: '2026-08-01T00:00:00Z',
                },
                {
                    word: 'Prahran',
                    note: null,
                    enabled: true,
                    verifiedBy: null,
                    verifiedAt: null,
                },
            ],
        },
    ],
}))

vi.mock('../api/verified-pronunciation-hooks', async (importOriginal) => ({
    ...((await importOriginal()) as Record<string, unknown>),
    useAllVerifiedPronunciations: () => ({
        data: mocks.inventory,
        isLoading: false,
    }),
    useSetVerifiedPronunciations: () => ({
        mutateAsync: mocks.save,
        isPending: false,
    }),
}))

afterEach(() => {
    cleanup()
    vi.clearAllMocks()
})

function renderManager() {
    return render(
        <QueryClientProvider client={new QueryClient()}>
            <TooltipProvider>
                <VerifiedPronunciationsManager
                    markets={[{ marketId: 'melbourne', label: 'Melbourne' }]}
                    station="melbourne"
                />
            </TooltipProvider>
        </QueryClientProvider>
    )
}

async function openEdit() {
    const user = userEvent.setup()
    renderManager()
    // Superseded by the shared RuleRow kebab convention.
    await user.click(
        screen.getAllByRole('button', {
            name: 'verified pronunciation actions',
        })[0]!
    )
    await user.click(screen.getByRole('menuitem', { name: 'Edit Keilor' }))
    return user
}

describe('VerifiedPronunciationsManager editing', () => {
    it('saves a note-only correction without treating the word as a duplicate', async () => {
        const user = await openEdit()

        expect(screen.getByLabelText('Word')).toHaveValue('Keilor')
        expect(screen.getByLabelText('Note (optional)')).toHaveValue(
            'said KEE-ler'
        )
        expect(
            screen.getByRole('button', { name: 'Save changes' })
        ).toBeDisabled()

        await user.clear(screen.getByLabelText('Note (optional)'))
        await user.type(
            screen.getByLabelText('Note (optional)'),
            'said KEE-lor, transcript writes Keeler'
        )
        await user.click(screen.getByRole('button', { name: 'Save changes' }))

        expect(mocks.save).toHaveBeenCalledWith({
            marketId: 'melbourne',
            words: [
                {
                    word: 'Keilor',
                    note: 'said KEE-lor, transcript writes Keeler',
                    enabled: true,
                    verifiedBy: 'Sam Operator',
                    verifiedAt: '2026-08-01T00:00:00Z',
                },
                mocks.inventory[0].words[1],
            ],
        })
    })

    it('clears the note when it is emptied', async () => {
        const user = await openEdit()

        await user.clear(screen.getByLabelText('Note (optional)'))
        await user.click(screen.getByRole('button', { name: 'Save changes' }))

        expect(mocks.save.mock.calls[0][0].words[0].note).toBeNull()
    })

    it('refuses a word edit that collides with another verdict', async () => {
        const user = await openEdit()

        // The key is letters/digits only, lower-cased: "prahran." keys the same as "Prahran".
        await user.clear(screen.getByLabelText('Word'))
        await user.type(screen.getByLabelText('Word'), 'prahran.')
        await user.click(screen.getByRole('button', { name: 'Save changes' }))

        expect(mocks.save).not.toHaveBeenCalled()
    })

    it('shows the market history disclosure to an operator (no admin gate)', async () => {
        renderManager()

        // Activity log reads are Operator too, so the per-market history rail
        // must render for whoever can edit the verdicts.
        expect(
            screen.getByRole('button', { name: 'History for Melbourne' })
        ).toBeInTheDocument()
    })
})
