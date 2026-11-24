// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import type { AirPlanRow } from '../../lib/air-plan'
import { SlotAudition } from './SlotAudition'
import type { JobSummary } from '@/features/jobs/types'
const { detailCalls, useJobDetailMock } = vi.hoisted(() => {
    const calls: unknown[][] = []
    return {
        detailCalls: calls,
        useJobDetailMock: (...args: unknown[]) => {
            calls.push(args)
            return { data: undefined }
        },
    }
})

vi.mock('@/features/jobs/api/job-hooks', () => ({
    useJobDetail: useJobDetailMock,
    useSetAirSource: () => ({
        mutate: vi.fn(),
        isPending: false,
        isError: false,
    }),
}))
vi.mock('../../hooks/use-playout-tray', () => ({
    usePlayoutTray: () => ({ data: { enabled: true, awaitingPickup: false } }),
}))

afterEach(() => {
    cleanup()
    detailCalls.length = 0
})

function slotJob(status: JobSummary['status']): JobSummary {
    return {
        jobId: 'slot-1',
        createdAt: '2026-08-24T03:00:00Z',
        station: '5AA',
        market: 'adelaide',
        targetDurationSec: 30,
        status,
        lastUpdatedAt: '2026-08-24T03:05:00Z',
        etag: 'etag-1',
        measuredDurationSec: 25,
        deltaSec: -5,
    }
}

function row(status: JobSummary['status']): AirPlanRow {
    return {
        marketId: 'adelaide',
        station: '5AA',
        market: 'Adelaide',
        slotJob: slotJob(status),
        candidates: [
            {
                id: 'earlier-1',
                kind: 'reair',
                label: 'Earlier bulletin',
                detail: 'from 06:00',
                current: false,
            },
        ],
    } as unknown as AirPlanRow
}

function rowWithRepeatOnly(): AirPlanRow {
    return {
        ...row('APPROVED'),
        slotJob: undefined,
        job: slotJob('APPROVED'),
    }
}

describe('SlotAudition — air-source changes the backend would refuse', () => {
    // SetAirSource applies a fallback to the slot's OWN job, and both Job.ApplyFallback and
    // Job.ApplyFillerFallback refuse Published and FallbackUsed. Rendering "Air this" for
    // those was an action guaranteed to fail.
    it.each(['PUBLISHED', 'FALLBACK_USED'] as const)(
        'offers no air-source change once the slot job is %s',
        (status) => {
            render(
                <SlotAudition
                    row={row(status)}
                    onSelectJob={vi.fn()}
                    onCommitted={vi.fn()}
                />
            )

            expect(screen.queryByRole('button', { name: /^Air /i })).toBeNull()
            expect(screen.getByText('Slot closed')).toBeInTheDocument()
        }
    )

    it.each(['WAITING_APPROVAL', 'FAILED', 'REJECTED', 'APPROVED'] as const)(
        'still offers the change while the slot job is %s',
        (status) => {
            render(
                <SlotAudition
                    row={row(status)}
                    onSelectJob={vi.fn()}
                    onCommitted={vi.fn()}
                />
            )

            expect(
                screen.getByRole('button', { name: /^Air /i })
            ).toBeInTheDocument()
            expect(screen.queryByText('Slot closed')).toBeNull()
        }
    )
    it('keeps candidate detail disabled until row focus activates it', () => {
        render(
            <SlotAudition
                row={row('APPROVED')}
                onSelectJob={vi.fn()}
                onCommitted={vi.fn()}
            />
        )

        expect(detailCalls.at(-1)).toEqual([''])
        fireEvent.focus(screen.getByRole('listitem'))
        expect(detailCalls.at(-1)).toEqual(['earlier-1'])
    })

    it('disables air-source redirect when only an approved repeat exists', () => {
        render(
            <SlotAudition
                row={rowWithRepeatOnly()}
                onSelectJob={vi.fn()}
                onCommitted={vi.fn()}
            />
        )

        const airButton = screen.getByRole('button', { name: /^Air /i })
        expect(airButton).toBeDisabled()
        expect(
            screen.getByText('No bulletin in this slot to redirect')
        ).toBeInTheDocument()
    })
})
