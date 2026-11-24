// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { describe, it, expect, vi, afterEach, beforeEach } from 'vitest'
import { render, screen, cleanup, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { TooltipProvider } from '@/components/ui/tooltip'
import { StationProfileCards } from './StationProfileCards'
import type { MarketSchedule } from '@/features/schedules/types'

vi.mock('@/features/voices/api/voice-profile-hooks', () => ({
    useVoiceProfiles: () => ({ data: undefined }),
}))

// Playout is enabled environment-wide unless a test says otherwise (dev is the
// environment where it is not, and the copy must change there).
const envPlayout = { value: true }
vi.mock('@/features/jobs/api/map-config-hooks', () => ({
    useMapConfig: () => ({
        data: { hereApiKey: '', playoutEnabled: envPlayout.value },
    }),
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

// MapPicker pulls in Leaflet, which does not belong in a jsdom unit test.
vi.mock('./CoverageMapPreview', () => ({
    CoverageMapPreview: () => <div data-testid="coverage-preview" />,
}))

afterEach(() => {
    cleanup()
})

beforeEach(() => {
    window.localStorage.clear()
    envPlayout.value = true
    role.admin = true
    scope.markets = null
})

/**
 * Real dev shape: TWO markets share the SAME station display name
 * ("Star 102.7 FM" for cairns-4ca and cairns-star1027). Only marketId and
 * schedule name differ. This is the fixture every test in this file uses.
 */
const starSchedule = (
    marketId: 'cairns-4ca' | 'cairns-star1027',
    name: string,
    enabled = true,
    playoutEnabled = true
): MarketSchedule => ({
    marketId,
    name,
    station: 'Star 102.7 FM',
    timezone: 'Australia/Brisbane',
    cronExpression: '59 7 * * 1-5',
    enabled,
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

function renderCards(
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
                <StationProfileCards
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

describe('StationProfileCards with duplicate "Star 102.7 FM" station names', () => {
    it('renders BOTH stations as separate cards (keys are marketIds, not the shared name)', () => {
        renderCards()

        expect(screen.getAllByText('Star 102.7 FM')).toHaveLength(2)
        expect(screen.getByText('Cairns 4CA Traffic')).toBeInTheDocument()
        expect(
            screen.getByText('Cairns Star 102.7 Traffic')
        ).toBeInTheDocument()
        // Two switches per card now: Active/Paused and Send to playout.
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

    it('toggles the switch of the SECOND same-named card and acts on cairns-star1027 only', async () => {
        const user = userEvent.setup()
        const onToggle = vi.fn()
        const onEdit = vi.fn()
        renderCards(twoStarStations(), { onToggle, onEdit })

        await user.click(
            screen.getAllByRole('switch', { name: 'Station active' })[1]
        )

        expect(onToggle).toHaveBeenCalledTimes(1)
        expect(onToggle).toHaveBeenCalledWith(
            expect.objectContaining({ marketId: 'cairns-star1027' })
        )
        // The switch click must not bubble into the card's open-edit handler.
        expect(onEdit).not.toHaveBeenCalled()
    })

    it('toggles the switch of the FIRST same-named card and acts on cairns-4ca only', async () => {
        const user = userEvent.setup()
        const onToggle = vi.fn()
        renderCards(twoStarStations(), { onToggle })

        await user.click(
            screen.getAllByRole('switch', { name: 'Station active' })[0]
        )

        expect(onToggle).toHaveBeenCalledTimes(1)
        expect(onToggle).toHaveBeenCalledWith(
            expect.objectContaining({ marketId: 'cairns-4ca' })
        )
    })

    /**
     * The per-station playout arm must act on its own row and must NOT be mistaken for the
     * pause switch: holding a station back leaves it generating, and pausing it does not.
     */
    it('the playout switch of the SECOND card arms cairns-star1027 only, without pausing it', async () => {
        const user = userEvent.setup()
        const onTogglePlayout = vi.fn()
        const onToggle = vi.fn()
        renderCards(twoStarStations(), { onTogglePlayout, onToggle })

        await user.click(
            screen.getAllByRole('switch', { name: 'Send to playout' })[1]
        )

        expect(onTogglePlayout).toHaveBeenCalledTimes(1)
        expect(onTogglePlayout).toHaveBeenCalledWith(
            expect.objectContaining({ marketId: 'cairns-star1027' })
        )
        expect(onToggle).not.toHaveBeenCalled()
    })

    it('a held-back station reads as off without reading as paused', () => {
        renderCards([
            starSchedule('cairns-4ca', 'Cairns 4CA Traffic', true, false),
        ])

        expect(screen.getByText('Active')).toBeInTheDocument()
        expect(screen.getByText(/nothing reaches playout/i)).toBeInTheDocument()
    })

    /**
     * Development has playout off environment-wide and has never delivered. The station flag
     * defaults to armed, so without consulting the environment gate every dev card would have
     * claimed "a published bulletin reaches playout" — a false statement about what a publish
     * does. Delivery needs BOTH gates, so the card must describe both.
     */
    it('never claims a bulletin reaches playout when the environment cannot deliver', () => {
        envPlayout.value = false

        renderCards([starSchedule('cairns-4ca', 'Cairns 4CA Traffic')])

        expect(
            screen.getByText('This environment never sends to playout')
        ).toBeInTheDocument()
        expect(
            screen.queryByText(/a published bulletin reaches playout/i)
        ).not.toBeInTheDocument()
    })

    it('kebab menu of the first card edits cairns-4ca, not the other same-named station', async () => {
        const user = userEvent.setup()
        const onEdit = vi.fn()
        renderCards(twoStarStations(), { onEdit })

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
        renderCards(twoStarStations(), { onDelete })

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
 * creates a job), but the card kept withholding "Trigger now" behind `useIsAdmin`. Dev
 * hid it — the dev fallback makes every caller an Admin — so it only ever surfaced on
 * test and prod, where a real Entra Operator saw a station they may edit and publish for
 * but never generate one for. These lock the operator path.
 */
describe('StationProfileCards as an Operator', () => {
    it('offers Trigger now on an assigned station and generates for that market', async () => {
        const user = userEvent.setup()
        const onTrigger = vi.fn()
        role.admin = false
        scope.markets = ['cairns-star1027']

        renderCards(twoStarStations(), { onTrigger })

        const triggers = screen.getAllByRole('button', { name: 'Trigger now' })
        expect(triggers).toHaveLength(2)
        // In-scope card first, out-of-scope card under "Outside your markets".
        expect(triggers[0]).toBeEnabled()
        await user.click(triggers[0])

        expect(onTrigger).toHaveBeenCalledTimes(1)
        expect(onTrigger).toHaveBeenCalledWith(
            expect.objectContaining({ marketId: 'cairns-star1027' })
        )
    })

    /**
     * The market guard is what makes the grant safe, so the card must not offer a trigger
     * the backend would refuse with a 403.
     */
    it('disables Trigger now on a station outside the assigned markets', () => {
        role.admin = false
        scope.markets = ['cairns-star1027']

        renderCards(twoStarStations())

        expect(screen.getByText('Outside your markets')).toBeInTheDocument()
        const triggers = screen.getAllByRole('button', { name: 'Trigger now' })
        expect(triggers[1]).toBeDisabled()
        expect(triggers[1]).toHaveAttribute(
            'title',
            'Outside your assigned markets'
        )
    })

    it('still withholds the Admin-only Duplicate and Delete actions', async () => {
        const user = userEvent.setup()
        role.admin = false

        renderCards([starSchedule('cairns-4ca', 'Cairns 4CA Traffic')])

        await user.click(
            screen.getByRole('button', {
                name: 'Actions for Star 102.7 FM (cairns-4ca)',
            })
        )

        expect(
            screen.getByRole('menuitem', { name: 'Edit' })
        ).toBeInTheDocument()
        expect(
            screen.queryByRole('menuitem', { name: 'Duplicate' })
        ).not.toBeInTheDocument()
        expect(
            screen.queryByRole('menuitem', { name: 'Delete' })
        ).not.toBeInTheDocument()
    })
})
