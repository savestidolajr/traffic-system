// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { TooltipProvider } from '@/components/ui/tooltip'
import { PriorityRoadsManager } from './PriorityRoadsManager'

// Identity here is the road NAME, and both dials live on the same record — a rename
// that dropped either of them would be indistinguishable from delete-plus-re-add.
const mocks = vi.hoisted(() => ({
    save: vi.fn().mockResolvedValue(undefined),
    inventory: [
        {
            marketId: 'sydney',
            roads: [
                {
                    name: 'M1 Pacfic Motorway',
                    boostIncidents: true,
                    flowTier: 'critical',
                    enabled: true,
                },
                {
                    name: 'Anzac Bridge',
                    boostIncidents: false,
                    flowTier: 'none',
                    enabled: false,
                },
            ],
        },
    ],
}))

vi.mock('../api/priority-roads-hooks', async (importOriginal) => ({
    ...((await importOriginal()) as Record<string, unknown>),
    useAllPriorityRoads: () => ({ data: mocks.inventory, isLoading: false }),
    useSetPriorityRoads: () => ({ mutateAsync: mocks.save, isPending: false }),
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
                <PriorityRoadsManager
                    markets={[{ marketId: 'sydney', label: 'Sydney' }]}
                    station="sydney"
                />
            </TooltipProvider>
        </QueryClientProvider>
    )
}

async function openRename() {
    const user = userEvent.setup()
    renderManager()
    // Superseded by the shared RuleRow kebab convention.
    await user.click(
        screen.getAllByRole('button', { name: 'priority road actions' })[0]!
    )
    await user.click(
        screen.getByRole('menuitem', { name: 'Rename M1 Pacfic Motorway' })
    )
    return user
}

// One control for both jobs. The fixture covers the two meaningful shapes: 'M1 Pacfic
// Motorway' does both jobs at Critical, 'Anzac Bridge' does neither.
describe('PriorityRoadsManager road use', () => {
    it('states both jobs as pressed or not, instead of a bare word and a tint', () => {
        renderManager()

        const incidents = screen.getAllByRole('button', { name: 'Incidents' })
        const goodRuns = screen.getAllByRole('button', { name: 'Good runs' })

        expect(incidents[0]).toHaveAttribute('aria-pressed', 'true')
        expect(goodRuns[0]).toHaveAttribute('aria-pressed', 'true')
        expect(incidents[1]).toHaveAttribute('aria-pressed', 'false')
        expect(goodRuns[1]).toHaveAttribute('aria-pressed', 'false')
    })

    it('turning Good runs off stores tier none and keeps the incident job', async () => {
        const user = userEvent.setup()
        renderManager()

        await user.click(
            screen.getAllByRole('button', { name: 'Good runs' })[0]!
        )

        expect(mocks.save).toHaveBeenCalledWith({
            marketId: 'sydney',
            roads: [
                {
                    name: 'M1 Pacfic Motorway',
                    boostIncidents: true,
                    flowTier: 'none',
                    enabled: true,
                },
                {
                    name: 'Anzac Bridge',
                    boostIncidents: false,
                    flowTier: 'none',
                    enabled: false,
                },
            ],
        })
    })

    it('turning Good runs on restores the default strength a new road gets', async () => {
        const user = userEvent.setup()
        renderManager()

        await user.click(
            screen.getAllByRole('button', { name: 'Good runs' })[1]!
        )

        expect(mocks.save.mock.calls[0]![0].roads[1]).toEqual({
            name: 'Anzac Bridge',
            boostIncidents: false,
            flowTier: 'high',
            enabled: false,
        })
    })

    // The strength grades the good-run job only, so offering it while that job is off was a
    // second, duplicate off switch ("Don't mention") in a control that reads as a strength.
    it('offers the strength only for a road doing the good-run job', () => {
        renderManager()

        expect(
            screen.getByLabelText('Flow boost for M1 Pacfic Motorway')
        ).toBeInTheDocument()
        expect(
            screen.queryByLabelText(
                'How hard Anzac Bridge competes as a good run'
            )
        ).not.toBeInTheDocument()
    })

    // 50 rows can carry the same two visible words, so the group has to name its road —
    // the old bare pill announced only "Boost incidents", identically on every row.
    it('names the road each use group belongs to', () => {
        renderManager()

        expect(
            screen.getByLabelText('What M1 Pacfic Motorway is used for')
        ).toBeInTheDocument()
        expect(
            screen.getByLabelText('What Anzac Bridge is used for')
        ).toBeInTheDocument()
    })
})

describe('PriorityRoadsManager renaming', () => {
    it('renames a road in place, keeping its boost dials and its position', async () => {
        const user = await openRename()

        const input = screen.getByLabelText('Road name')
        expect(input).toHaveValue('M1 Pacfic Motorway')
        expect(
            screen.getByRole('button', { name: 'Save changes' })
        ).toBeDisabled()

        await user.clear(input)
        await user.type(input, 'M1 Pacific Motorway')
        await user.click(screen.getByRole('button', { name: 'Save changes' }))

        expect(mocks.save).toHaveBeenCalledWith({
            marketId: 'sydney',
            roads: [
                {
                    name: 'M1 Pacific Motorway',
                    boostIncidents: true,
                    flowTier: 'critical',
                    enabled: true,
                },
                {
                    name: 'Anzac Bridge',
                    boostIncidents: false,
                    flowTier: 'none',
                    enabled: false,
                },
            ],
        })
    })

    it('refuses a rename that collides with another road in the market', async () => {
        const user = await openRename()

        const input = screen.getByLabelText('Road name')
        await user.clear(input)
        await user.type(input, 'anzac bridge')
        await user.click(screen.getByRole('button', { name: 'Save changes' }))

        expect(mocks.save).not.toHaveBeenCalled()
    })
})
