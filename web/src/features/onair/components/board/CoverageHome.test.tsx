// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { TooltipProvider } from '@/components/ui/tooltip'
import { CoverageHome } from './CoverageHome'
import { computeStationLanes } from '@/features/onair/lib/slots'
import { formatSlotClock } from '@/features/onair/lib/air-plan'
import { formatTime } from '@/lib/formatting'
import type { MarketSchedule } from '@/features/schedules/types'

// Unscoped operator: every lane renders (scope partitioning is presentation
// only and would otherwise need a QueryClientProvider).
vi.mock('@/features/operators/api/operator-hooks', () => ({
    useOperatorScope: vi.fn(() => ({
        data: undefined,
        isLoading: false,
        isError: false,
    })),
}))

/**
 * A Perth metro schedule. The mock dev feed's enabled stations (sydney-2gb,
 * melbourne-3aw) both share the operator's +10 timezone, so a Perth row
 * rendered on a Sydney clock is invisible there — the suite's TZ pin has the
 * same masking effect. This fixture exercises the real cross-timezone case.
 */
const PERTH: MarketSchedule = {
    marketId: 'perth',
    name: 'Perth Traffic',
    station: 'Gold 96FM',
    timezone: 'Australia/Perth',
    cronExpression: '0,30 * * * *',
    enabled: true,
    targetDurationSeconds: 32,
    notifyBeforePlayoutMinutes: 5,
    escalationBeforePlayoutMinutes: 5,
    voiceProfileId: null,
    deliveryStyleId: null,
    pronunciationDictionaryId: null,
    bookendId: null,
    nextRunAt: null,
    currentJobId: null,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    coverage: { latitude: -31.95, longitude: 115.86, radiusKm: 30 },
    content: null,
    voiceRotation: null,
    bookendRotation: null,
    bookendTokens: null,
    fillOrder: null,
}

/** The wall-clock time an instant shows on a Perth clock (24h). */
function perthClock(instant: Date): string {
    return new Intl.DateTimeFormat('en-AU', {
        timeZone: 'Australia/Perth',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
        hour12: false,
    }).format(instant)
}

beforeEach(() => {
    class ResizeObserverStub {
        observe() {}
        unobserve() {}
        disconnect() {}
    }
    vi.stubGlobal('ResizeObserver', ResizeObserverStub)
})

afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
})

describe('CoverageHome · market timezone rendering', () => {
    // The board used to render every slot as a dot on one shared axis labelled in
    // the VIEWER's clock, which meant a Perth row silently claimed a Sydney time.
    // Rows now carry the station's own clock and tag the zone when it differs, so
    // an operator in Sydney reads Perth's airtime as Perth reads it.
    it('shows a Perth slot on the Perth clock, tagged with its zone', () => {
        render(
            <MemoryRouter>
                <TooltipProvider>
                    <CoverageHome
                        schedules={[PERTH]}
                        jobs={[]}
                        selectedJobId={null}
                        onSelectJob={() => {}}
                        mostUrgentJobId={null}
                        waitingCount={0}
                    />
                </TooltipProvider>
            </MemoryRouter>
        )

        const lane = computeStationLanes([PERTH], [], new Date()).find(
            (l) => l.marketId === 'perth'
        )!
        const next = lane.slots.find((s) => s.at.getTime() >= Date.now())!
        const stationClock = perthClock(next.at).slice(0, 5)

        // Under the suite's TZ=Australia/Sydney pin these differ by two hours,
        // so this asserts the station's clock specifically, not just a time.
        expect(stationClock).not.toBe(formatTime(next.at))
        expect(screen.getByText(stationClock)).toBeInTheDocument()
        expect(screen.getByText('AWST')).toBeInTheDocument()
    })

    // The track is labelled in the station's own timezone, just like the row
    // clock, so an operator can compare the two without a browser-zone shift.
    it('describes the slot track on the station clock', () => {
        render(
            <MemoryRouter>
                <TooltipProvider>
                    <CoverageHome
                        schedules={[PERTH]}
                        jobs={[]}
                        selectedJobId={null}
                        onSelectJob={() => {}}
                        mostUrgentJobId={null}
                        waitingCount={0}
                    />
                </TooltipProvider>
            </MemoryRouter>
        )

        const lane = computeStationLanes([PERTH], [], new Date()).find(
            (l) => l.marketId === 'perth'
        )!
        const next = lane.slots.find((s) => s.at.getTime() >= Date.now())!
        const track = screen.getByRole('img', { name: /Generates .* airs / })
        expect(track.getAttribute('aria-label')).toContain(
            formatSlotClock(next.at, 'Australia/Perth')
        )
    })

    it('resolves slot instants in the market timezone; positioning remains absolute', () => {
        const lane = computeStationLanes([PERTH], [], new Date()).find(
            (l) => l.marketId === 'perth'
        )!
        expect(lane.timezone).toBe('Australia/Perth')
        expect(lane.slots.length).toBeGreaterThan(0)

        for (const slot of lane.slots) {
            // The instant IS on the Perth clock (minute :00 or :30) — slot
            // positions are computed in the market's timezone.
            expect(perthClock(slot.at)).toMatch(/:(00|30):00$/)
            // The board formats that instant with formatTime (browser-local),
            // which never matches the Perth clock outside Perth.
            expect(formatTime(slot.at)).not.toBe(perthClock(slot.at))
        }
    })
})
