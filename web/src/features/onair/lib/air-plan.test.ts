import { describe, expect, it } from 'vitest'
import type { JobSummary } from '@/features/jobs/types'
import type { StationLane } from './slots'
import {
    airSourceLabel,
    airSourceReason,
    buildAirPlan,
    CUTOFF_LEAD_MS,
    GENERATION_LEAD_MS,
    resolveAirPlan,
} from './air-plan'

const NOW = new Date('2026-08-11T04:00:00Z') // 14:00 Sydney
const SLOT = new Date('2026-08-11T04:10:00Z') // 14:10 Sydney, inside the lead

function job(over: Partial<JobSummary> = {}): JobSummary {
    return {
        jobId: 'arn-job-aaaaa',
        createdAt: '2026-08-11T03:50:00Z',
        station: 'KIIS 90s',
        market: 'Sydney',
        targetDurationSec: 30,
        measuredDurationSec: 29,
        status: 'PUBLISHED',
        lastUpdatedAt: '2026-08-11T03:52:00Z',
        ...over,
    } as JobSummary
}

function lane(over: Partial<StationLane> = {}): StationLane {
    return {
        marketId: 'sydney',
        station: 'KIIS 90s',
        market: 'KIIS 90s Sydney',
        timezone: 'Australia/Sydney',
        enabled: true,
        slots: [{ at: SLOT, state: 'scheduled' }],
        adHoc: [],
        ...over,
    }
}

const CART = true
const NO_CART = false

describe('resolveAirPlan — the cutoff', () => {
    it('places the cutoff a fallback-deadline ahead of the slot', () => {
        const row = resolveAirPlan(lane(), [], NOW, CART)
        expect(row.cutoffAt?.getTime()).toBe(SLOT.getTime() - CUTOFF_LEAD_MS)
        expect(row.secondsToCutoff).toBe(
            (SLOT.getTime() - CUTOFF_LEAD_MS - NOW.getTime()) / 1000
        )
    })

    it('goes negative once the cutoff has passed, rather than hiding the row', () => {
        const late = new Date(SLOT.getTime() - 60_000)
        const row = resolveAirPlan(lane(), [], late, CART)
        expect(row.secondsToCutoff).toBeLessThan(0)
    })

    it('reports no slot left rather than inventing one', () => {
        const row = resolveAirPlan(lane({ slots: [] }), [], NOW, CART)
        expect(row.source).toBe('noSlot')
        expect(row.slotAt).toBeNull()
        expect(row.cutoffAt).toBeNull()
    })

    it('ignores slots that have already gone', () => {
        const past = new Date('2026-08-11T03:30:00Z')
        const row = resolveAirPlan(
            lane({
                slots: [
                    { at: past, state: 'missed' },
                    { at: SLOT, state: 'scheduled' },
                ],
            }),
            [],
            NOW,
            CART
        )
        expect(row.slotAt?.getTime()).toBe(SLOT.getTime())
    })
})

describe('resolveAirPlan — what will actually air', () => {
    it('a published bulletin on the slot is already delivered', () => {
        const published = job({ status: 'PUBLISHED' })
        const row = resolveAirPlan(
            lane({ slots: [{ at: SLOT, state: 'covered', job: published }] }),
            [published],
            NOW,
            CART
        )
        expect(row.source).toBe('fresh')
        expect(row.job?.jobId).toBe(published.jobId)
        expect(airSourceLabel(row)).toBe('In playout')
    })

    // Approval is the human gate; delivery is mechanical and happens on a sweep tick in the
    // window BEFORE the slot's cutoff (JobWatchdog.PublishApprovedBeforeDeadlineAsync), NOT at
    // the moment of approval and NOT at the cutoff itself. So an approved bulletin is not in the
    // drop yet, and the row states a bound it can keep rather than a tick it cannot know.
    it('an approved bulletin bounds the moment it will be sent', () => {
        const approved = job({ status: 'APPROVED' })
        const row = resolveAirPlan(
            lane({ slots: [{ at: SLOT, state: 'covered', job: approved }] }),
            [approved],
            NOW,
            CART
        )
        expect(row.source).toBe('fresh')
        expect(row.pending).toBeUndefined()
        // Slot 14:10 Sydney, cutoff three minutes earlier; now is 14:00, well outside the
        // delivery window, so the sweep has not sent it yet.
        expect(airSourceLabel(row)).toBe('Sends by 14:07')
        expect(airSourceReason(row)).toContain('Nothing is in the drop yet')
    })

    it('an approved bulletin inside the delivery window says it is going now', () => {
        const approved = job({ status: 'APPROVED' })
        // 14:06 Sydney: one minute before the 14:07 cutoff, inside the 90s delivery window.
        const insideWindow = new Date(SLOT.getTime() - 4 * 60_000)
        const row = resolveAirPlan(
            lane({ slots: [{ at: SLOT, state: 'covered', job: approved }] }),
            [approved],
            insideWindow,
            CART
        )
        expect(airSourceLabel(row)).toBe('Sending now')
        expect(airSourceReason(row)).toContain('within a minute')
    })

    // Past the cutoff nothing sends it: ZettaFileDeliveryService refuses an expired slot. The
    // row used to promise delivery "on the next sweep" here, which was a promise nothing kept —
    // the state that produced a silent slot in prod on 2026-08-13.
    it('an approved bulletin past the cutoff admits nothing will send it', () => {
        const approved = job({ status: 'APPROVED' })
        const afterCutoff = new Date(SLOT.getTime() - 60_000)
        const row = resolveAirPlan(
            lane({ slots: [{ at: SLOT, state: 'covered', job: approved }] }),
            [approved],
            afterCutoff,
            CART
        )
        expect(airSourceLabel(row)).toBe('Deadline passed')
        expect(airSourceReason(row)).toContain('playout will not accept it')
    })

    // The compliance rule, mirrored: the backend refuses to air content nobody
    // approved, so an unreviewed bulletin does NOT become the forecast — the
    // fallback chain does — but it is still the thing a human must act on.
    it('a bulletin awaiting review does not air, and is surfaced separately', () => {
        const waiting = job({ status: 'WAITING_APPROVAL' })
        const row = resolveAirPlan(
            lane({ slots: [{ at: SLOT, state: 'needsReview', job: waiting }] }),
            [waiting],
            NOW,
            CART
        )
        expect(row.source).toBe('filler')
        expect(row.pending?.jobId).toBe(waiting.jobId)
    })

    it('a bulletin still being written says so instead of forecasting a repeat', () => {
        const generating = job({ status: 'SCRIPT_READY' })
        const row = resolveAirPlan(
            lane({
                slots: [{ at: SLOT, state: 'generating', job: generating }],
            }),
            [generating],
            NOW,
            CART
        )
        expect(row.source).toBe('generating')
        expect(airSourceLabel(row)).toBe('Writing')
    })

    // A slot further out than the poller's lead has nothing requested because it
    // is not time yet. Calling that a fallback made an empty diary look broken.
    it('a slot beyond the generation lead is not due yet, not falling back', () => {
        const far = new Date(NOW.getTime() + GENERATION_LEAD_MS + 60_000)
        const row = resolveAirPlan(
            lane({ slots: [{ at: far, state: 'scheduled' }] }),
            [],
            NOW,
            CART
        )
        expect(row.source).toBe('notStarted')
    })

    it('repeats the newest eligible bulletin from today when the slot is empty', () => {
        const older = job({
            jobId: 'older',
            status: 'PUBLISHED',
            createdAt: '2026-08-11T01:00:00Z',
        })
        const newer = job({
            jobId: 'newer',
            status: 'FALLBACK_USED',
            createdAt: '2026-08-11T02:00:00Z',
        })
        const row = resolveAirPlan(lane(), [older, newer], NOW, CART)
        expect(row.source).toBe('reair')
        expect(row.job?.jobId).toBe('newer')
    })

    it('keeps an approved repeat candidate separate from the slot target', () => {
        const repeat = job({
            jobId: 'repeat',
            status: 'APPROVED',
            createdAt: '2026-08-11T01:00:00Z',
        })
        const row = resolveAirPlan(lane(), [repeat], NOW, CART)

        expect(row.source).toBe('reair')
        expect(row.job?.jobId).toBe('repeat')
        expect(row.slotJob).toBeUndefined()
    })

    it('never repeats a bulletin nobody approved', () => {
        const unapproved = job({
            jobId: 'unapproved',
            status: 'WAITING_APPROVAL',
        })
        const row = resolveAirPlan(lane(), [unapproved], NOW, CART)
        expect(row.source).toBe('filler')
    })

    it('never repeats another station or another day', () => {
        const otherStation = job({ jobId: 'other', station: 'GOLD 104.3' })
        const yesterday = job({
            jobId: 'yesterday',
            createdAt: '2026-08-10T04:00:00Z',
        })
        const row = resolveAirPlan(lane(), [otherStation, yesterday], NOW, CART)
        expect(row.source).toBe('filler')
    })

    it('is dead air when there is nothing to repeat and no cart', () => {
        const row = resolveAirPlan(lane(), [], NOW, NO_CART)
        expect(row.source).toBe('none')
        expect(row.candidates).toHaveLength(0)
    })
})

describe('resolveAirPlan — the audio an operator can audition', () => {
    it('offers this slot’s bulletin, earlier bulletins and the cart, marking what airs', () => {
        const approved = job({ jobId: 'approved', status: 'APPROVED' })
        const earlier = job({
            jobId: 'earlier',
            status: 'PUBLISHED',
            createdAt: '2026-08-11T01:00:00Z',
        })
        const row = resolveAirPlan(
            lane({ slots: [{ at: SLOT, state: 'covered', job: approved }] }),
            [approved, earlier],
            NOW,
            CART
        )
        expect(row.candidates.map((c) => c.id)).toEqual([
            'approved',
            'earlier',
            'filler',
        ])
        expect(
            row.candidates.filter((c) => c.current).map((c) => c.id)
        ).toEqual(['approved'])
    })

    // The panel is where an operator LISTENS, so an unapproved bulletin has to be
    // in the list — hearing it is how they decide to approve it. It just isn't the
    // one marked as airing, because it will not air unless someone signs it off.
    it('includes a bulletin awaiting review without claiming it airs', () => {
        const waiting = job({ jobId: 'waiting', status: 'WAITING_APPROVAL' })
        const row = resolveAirPlan(
            lane({ slots: [{ at: SLOT, state: 'needsReview', job: waiting }] }),
            [waiting],
            NOW,
            CART
        )
        expect(row.candidates.map((c) => c.id)).toEqual(['waiting', 'filler'])
        expect(row.candidates[0].current).toBe(false)
        expect(
            row.candidates.filter((c) => c.current).map((c) => c.id)
        ).toEqual(['filler'])
    })

    it('names earlier bulletins by the clock they aired on, not by their job id', () => {
        const earlier = job({
            jobId: 'arn-job-zzzzz',
            status: 'PUBLISHED',
            createdAt: '2026-08-11T02:22:00Z', // 12:22 Sydney
        })
        const row = resolveAirPlan(lane(), [earlier], NOW, CART)
        const repeat = row.candidates.find((c) => c.kind === 'reair')
        expect(repeat?.label).toBe('Repeat 12:22')
    })

    it('marks the re-air that would actually be used, not every re-air', () => {
        const a = job({
            jobId: 'a',
            status: 'PUBLISHED',
            createdAt: '2026-08-11T01:00:00Z',
        })
        const b = job({
            jobId: 'b',
            status: 'PUBLISHED',
            createdAt: '2026-08-11T02:00:00Z',
        })
        const row = resolveAirPlan(lane(), [a, b], NOW, CART)
        expect(
            row.candidates.filter((c) => c.current).map((c) => c.id)
        ).toEqual(['b'])
    })
})

describe('buildAirPlan', () => {
    it('drops paused stations and puts the rows a human must act on first', () => {
        const paused = lane({ marketId: 'paused', enabled: false })
        const covered = lane({
            marketId: 'covered',
            station: 'Covered FM',
            slots: [
                {
                    at: SLOT,
                    state: 'covered',
                    job: job({ jobId: 'pub', station: 'Covered FM' }),
                },
            ],
        })
        const onCart = lane({ marketId: 'cart', station: 'Cart FM' })

        const rows = buildAirPlan(
            [paused, covered, onCart],
            [job({ jobId: 'pub', station: 'Covered FM' })],
            NOW,
            true
        )

        expect(rows.map((r) => r.marketId)).toEqual(['cart', 'covered'])
        expect(rows[0].source).toBe('filler')
    })
})
