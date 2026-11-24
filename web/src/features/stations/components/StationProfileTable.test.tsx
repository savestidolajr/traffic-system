// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { describe, it, expect, vi, afterEach, beforeEach } from 'vitest'
import { render, screen, cleanup, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { TooltipProvider } from '@/components/ui/tooltip'
import { StationProfileTable } from './StationProfileTable'
import type { MarketSchedule } from '@/features/schedules/types'

vi.mock('@/features/voices/api/voice-profile-hooks', () => ({
    useVoiceProfiles: () => ({ data: undefined }),
}))

vi.mock('@/features/jobs/api/map-config-hooks', () => ({
    useMapConfig: () => ({ data: { hereApiKey: '', playoutEnabled: true } }),
}))

// Role and scope are per-test: a station's Trigger now must reach an OPERATOR (POST
// /broadcast is market-scoped Operator), while Duplicate and Delete must not. Dev cannot
// show this — Auth__DevFallbackRole=Admin makes every dev caller an Admin.
const role = { admin: true }
vi.mock('@/hooks/use-current-user', () => ({
    useIsAdmin: () => role.admin,
}))

const scope: { markets: string[] | null } = { markets: null }
vi.mock('@/features/operators/api/operator-hooks', () => ({
    useOperatorScope: () => ({
        data: { scoped: scope.markets !== null, markets: scope.markets },
        isLoading: false,
    }),
}))

afterEach(() => {
    cleanup()
})

beforeEach(() => {
    window.localStorage.clear()
    role.admin = true
    scope.markets = null
})

/**
 * Real dev shape: TWO markets share the SAME station display name
 * ("Star 102.7 FM" for cairns-4ca and cairns-star1027). Only marketId and
 * schedule name differ.
 */
const starSchedule = (
    marketId: 'cairns-4ca' | 'cairns-star1027',
    name: string,
    playoutEnabled = true
): MarketSchedule => ({
    marketId,
    name,
    station: 'Star 102.7 FM',
    timezone: 'Australia/Brisbane',
    cronExpression: '59 7 * * 1-5',
    enabled: true,
    playoutEnabled,
    targetDurationSeconds: 30,
    notifyBeforePlayoutMinutes: 5,
    escalationBeforePlayoutMinutes: 5,
    voiceProfileId: 'vp-1',
    deliveryStyleId: null,
    pronunciationDictionaryId: null,
    bookendId: null,
    nextRunAt: '2026-08-11T07:59:00Z',
    currentJobId: null,
    createdAt: '2026-06-19T04:11:48Z',
    updatedAt: '2026-07-13T04:01:45Z',
    coverage: {
        latitude: -16.92,
        longitude: 145.77,
        radiusKm: 20,
        shapeType: 'circle',
        west: null,
        south: null,
        east: null,
        north: null,
        corridorPoints: null,
        corridorRadiusMeters: null,
        polygonPoints: null,
    },
    content: {
        traffic: true,
        weather: true,
        fuel: true,
        weatherRequired: false,
        fuelRequired: false,
    },
    voiceRotation: null,
    bookendRotation: null,
    bookendTokens: null,
    fillOrder: null,
})

const twoStarStations = (): readonly MarketSchedule[] => [
    starSchedule('cairns-4ca', 'Cairns 4CA Traffic'),
    starSchedule('cairns-star1027', 'Cairns Star 102.7 Traffic'),
]

function renderTable(
    schedules: readonly MarketSchedule[] = twoStarStations(),
    handlers: Partial<
        Record<
            | 'onTrigger'
            | 'onView'
            | 'onEdit'
            | 'onDuplicate'
            | 'onToggle'
            | 'onTogglePlayout'
            | 'onDelete',
            (schedule: MarketSchedule) => void
        >
    > = {}
) {
    return render(
        <QueryClientProvider client={new QueryClient()}>
            <TooltipProvider>
                <StationProfileTable
                    schedules={schedules}
                    onTrigger={handlers.onTrigger ?? vi.fn()}
                    onView={handlers.onView ?? vi.fn()}
                    onEdit={handlers.onEdit ?? vi.fn()}
                    onDuplicate={handlers.onDuplicate ?? vi.fn()}
                    onToggle={handlers.onToggle ?? vi.fn()}
                    onTogglePlayout={handlers.onTogglePlayout ?? vi.fn()}
                    onDelete={handlers.onDelete ?? vi.fn()}
                />
            </TooltipProvider>
        </QueryClientProvider>
    )
}

describe('StationProfileTable with duplicate "Star 102.7 FM" station names', () => {
    it('renders BOTH stations as separate rows (keys are marketIds, not the shared name)', () => {
        renderTable()

        expect(screen.getAllByText('Star 102.7 FM')).toHaveLength(2)
        expect(screen.getByText('cairns-4ca')).toBeInTheDocument()
        expect(screen.getByText('cairns-star1027')).toBeInTheDocument()
        // Two switches per row now: Active/Paused and Send to playout.
        expect(screen.getAllByRole('switch')).toHaveLength(4)
        expect(
            screen.getAllByRole('switch', { name: 'Station active' })
        ).toHaveLength(2)
        expect(
            screen.getAllByRole('switch', { name: 'Send to playout' })
        ).toHaveLength(2)
        expect(
            screen.getByRole('button', {
                name: 'Actions for Star 102.7 FM (cairns-4ca)',
            })
        ).toBeInTheDocument()
        expect(
            screen.getByRole('button', {
                name: 'Actions for Star 102.7 FM (cairns-star1027)',
            })
        ).toBeInTheDocument()
    })

    it('flips the status switch of the SECOND same-named row and toggles cairns-star1027 only', async () => {
        const user = userEvent.setup()
        const onToggle = vi.fn()
        const onEdit = vi.fn()
        renderTable(twoStarStations(), { onToggle, onEdit })

        await user.click(
            screen.getAllByRole('switch', { name: 'Station active' })[1]
        )

        expect(onToggle).toHaveBeenCalledTimes(1)
        expect(onToggle).toHaveBeenCalledWith(
            expect.objectContaining({ marketId: 'cairns-star1027' })
        )
        // The switch click must not bubble into the row's open-edit handler.
        expect(onEdit).not.toHaveBeenCalled()
    })

    it('flips the status switch of the FIRST same-named row and toggles cairns-4ca only', async () => {
        const user = userEvent.setup()
        const onToggle = vi.fn()
        renderTable(twoStarStations(), { onToggle })

        await user.click(
            screen.getAllByRole('switch', { name: 'Station active' })[0]
        )

        expect(onToggle).toHaveBeenCalledTimes(1)
        expect(onToggle).toHaveBeenCalledWith(
            expect.objectContaining({ marketId: 'cairns-4ca' })
        )
    })

    it('flips the playout switch of the SECOND same-named row only', async () => {
        const user = userEvent.setup()
        const onTogglePlayout = vi.fn()
        const onToggle = vi.fn()
        const onEdit = vi.fn()
        renderTable(twoStarStations(), { onTogglePlayout, onToggle, onEdit })

        await user.click(
            screen.getAllByRole('switch', { name: 'Send to playout' })[1]
        )

        expect(onTogglePlayout).toHaveBeenCalledTimes(1)
        expect(onTogglePlayout).toHaveBeenCalledWith(
            expect.objectContaining({ marketId: 'cairns-star1027' })
        )
        expect(onToggle).not.toHaveBeenCalled()
        expect(onEdit).not.toHaveBeenCalled()
    })

    it('clicking the SECOND same-named row edits cairns-star1027, not the first', async () => {
        const user = userEvent.setup()
        const onEdit = vi.fn()
        renderTable(twoStarStations(), { onEdit })

        // Row click = open edit; the second "Star 102.7 FM" cell belongs to
        // the cairns-star1027 row.
        await user.click(screen.getAllByText('Star 102.7 FM')[1])

        expect(onEdit).toHaveBeenCalledTimes(1)
        expect(onEdit).toHaveBeenCalledWith(
            expect.objectContaining({ marketId: 'cairns-star1027' })
        )
    })

    it('kebab menu of the FIRST same-named row edits cairns-4ca', async () => {
        const user = userEvent.setup()
        const onEdit = vi.fn()
        renderTable(twoStarStations(), { onEdit })

        await user.click(
            screen.getByRole('button', {
                name: 'Actions for Star 102.7 FM (cairns-4ca)',
            })
        )
        await user.click(screen.getByRole('menuitem', { name: 'Edit' }))

        expect(onEdit).toHaveBeenCalledTimes(1)
        expect(onEdit).toHaveBeenCalledWith(
            expect.objectContaining({ marketId: 'cairns-4ca' })
        )
    })

    it('delete confirmation names the marketId, so the two same-named records are distinguishable', async () => {
        const user = userEvent.setup()
        const onDelete = vi.fn()
        renderTable(twoStarStations(), { onDelete })

        await user.click(
            screen.getByRole('button', {
                name: 'Actions for Star 102.7 FM (cairns-star1027)',
            })
        )
        await user.click(screen.getByRole('menuitem', { name: 'Delete' }))

        const dialog = screen.getByRole('alertdialog')
        expect(
            within(dialog).getByText(
                'Deleting Star 102.7 FM (cairns-star1027) stops automatic bulletins.'
            )
        ).toBeInTheDocument()

        // The action itself still targets the correct record.
        await user.click(within(dialog).getByRole('button', { name: 'Delete' }))
        expect(onDelete).toHaveBeenCalledTimes(1)
        expect(onDelete).toHaveBeenCalledWith(
            expect.objectContaining({ marketId: 'cairns-star1027' })
        )
    })
})

/**
 * `POST /api/broadcast` became market-scoped Operator on 2026-08-13 (RouteRoleMatrix:
 * BroadcastHttpTrigger resolves the market and runs EnsureMarketAccessAsync before it
 * creates a job), but the row menu kept withholding "Trigger now" behind `useIsAdmin`.
 * Dev hid it — the dev fallback makes every caller an Admin — so it only ever surfaced
 * on test and prod, where a real Entra Operator saw a station they may edit and publish
 * for but never generate one for. These lock the operator path.
 */
describe('StationProfileTable as an Operator', () => {
    it('offers Trigger now in the row menu and generates for that market', async () => {
        const user = userEvent.setup()
        const onTrigger = vi.fn()
        role.admin = false
        scope.markets = ['cairns-4ca']

        renderTable(twoStarStations(), { onTrigger })

        await user.click(
            screen.getByRole('button', {
                name: 'Actions for Star 102.7 FM (cairns-4ca)',
            })
        )
        await user.click(screen.getByRole('menuitem', { name: 'Trigger now' }))

        expect(onTrigger).toHaveBeenCalledTimes(1)
        expect(onTrigger).toHaveBeenCalledWith(
            expect.objectContaining({ marketId: 'cairns-4ca' })
        )
    })

    /**
     * The market guard is what makes the grant safe, so the menu must not offer a trigger
     * the backend would refuse with a 403.
     */
    it('disables Trigger now on a station outside the assigned markets', async () => {
        const user = userEvent.setup()
        role.admin = false
        scope.markets = ['cairns-4ca']

        renderTable(twoStarStations())

        await user.click(
            screen.getByRole('button', {
                name: 'Actions for Star 102.7 FM (cairns-star1027)',
            })
        )
        // `DropdownMenuItem` carries `data-[disabled]:pointer-events-none`, so the browser
        // never lands the click. jsdom loads no CSS, so assert the markers the browser acts
        // on rather than a click that only a styleless DOM would deliver.
        const item = screen.getByRole('menuitem', { name: 'Trigger now' })
        expect(item).toHaveAttribute('data-disabled')
        expect(item).toHaveAttribute('aria-disabled', 'true')
        expect(item).toHaveAttribute('title', 'Outside your assigned markets')
    })

    it('still withholds the Admin-only Duplicate and Delete actions', async () => {
        const user = userEvent.setup()
        role.admin = false

        renderTable([starSchedule('cairns-4ca', 'Cairns 4CA Traffic')])

        await user.click(
            screen.getByRole('button', {
                name: 'Actions for Star 102.7 FM (cairns-4ca)',
            })
        )

        expect(
            screen.getByRole('menuitem', { name: 'Trigger now' })
        ).toBeInTheDocument()
        expect(
            screen.queryByRole('menuitem', { name: 'Duplicate' })
        ).not.toBeInTheDocument()
        expect(
            screen.queryByRole('menuitem', { name: 'Delete' })
        ).not.toBeInTheDocument()
    })
})
