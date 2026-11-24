// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { render, screen, cleanup } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi, afterEach } from 'vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CurationPane } from './CurationPane'
import type { Job } from '../../types'
import { TooltipProvider } from '@/components/ui/tooltip'

const mockJob: Job = {
    jobId: 'job-curation-test',
    market: 'sydney',
    station: '2UE',
    status: 'AWAITING_CURATION',
    targetDurationSeconds: 30,
    createdAt: '2026-08-19T10:00:00Z',
    lastUpdatedAt: '2026-08-19T10:00:00Z',
    curationDeadlineAt: '2026-08-19T10:05:00Z',
    etag: 'etag-123',
    statusHistory: [],
    traffic: {
        timestampUtc: '2026-08-19T10:00:00Z',
        incidents: [
            {
                key: 'inc-1',
                headline: 'Crash on M4',
                detail: 'Lane 1 blocked westbound at Parramatta',
                severity: 3,
                priority: 1,
                sources: ['here', 'livetraffic-nsw'],
            },
            {
                key: 'inc-2',
                headline: 'Breakdown on M2',
                detail: 'Car stopped on shoulder eastbound at Epping',
                severity: 1,
                priority: 2,
                sources: ['here'],
            },
        ],
        corridors: [
            {
                roadName: 'M4 Western Motorway',
                rank: 1,
                isSelected: true,
                impactScore: 85,
                direction: 'westbound',
                primaryType: 'accident',
                severity: 3,
                description: 'Crash blocking lane westbound at Parramatta',
                incidentKeys: ['inc-1'],
                sources: ['here', 'livetraffic-nsw'],
            },
            {
                roadName: 'M2 Hills Motorway',
                rank: 2,
                isSelected: false,
                impactScore: 30,
                direction: 'eastbound',
                primaryType: 'breakdown',
                severity: 1,
                description: 'Breakdown on shoulder eastbound at Epping',
                incidentKeys: ['inc-2'],
                sources: ['here'],
            },
        ],
        flowRoads: [
            {
                roadName: 'M5 South-West Motorway',
                score: 10,
                jamFactor: 0.5,
                speedKmh: 95,
                freeFlowKmh: 100,
                lengthKm: 15,
                hasIncident: false,
            },
        ],
    },
}

function renderCurationPane(
    job: Job,
    onProceed: (
        editedIncidents: readonly {
            incidentId: string
            included: boolean
            priority: number
        }[]
    ) => Promise<void> = vi.fn()
) {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false },
        },
    })
    return render(
        <QueryClientProvider client={queryClient}>
            <TooltipProvider>
                <CurationPane job={job} onProceed={onProceed} />
            </TooltipProvider>
        </QueryClientProvider>
    )
}

describe('CurationPane with RundownTable layout', () => {
    afterEach(() => {
        cleanup()
    })

    it('renders the compact curation status and table rundown', () => {
        renderCurationPane(mockJob)

        expect(screen.getByText('Curation window open')).toBeInTheDocument()
        expect(screen.getByText('Traffic rundown')).toBeInTheDocument()
        expect(screen.getByText('M4 Western Motorway')).toBeInTheDocument()
        expect(screen.getByText('M2 Hills Motorway')).toBeInTheDocument()
        expect(
            screen.getByRole('button', { name: 'Proceed to draft' })
        ).toBeInTheDocument()
    })

    it('submits edited incidents payload with on-air priorities when proceeding', async () => {
        const user = userEvent.setup()
        const onProceed = vi.fn().mockResolvedValue(undefined)

        renderCurationPane(mockJob, onProceed)
        await user.click(screen.getByTestId('proceed-draft-button'))

        expect(onProceed).toHaveBeenCalledTimes(1)
        const payload = onProceed.mock.calls[0][0]
        expect(payload).toEqual([
            { incidentId: 'inc-1', included: true, priority: 1 },
            { incidentId: 'inc-2', included: false, priority: 3 },
        ])
    })
})
