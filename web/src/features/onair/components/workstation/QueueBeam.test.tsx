// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { QueueBeam } from './QueueBeam'
import type { JobSummary } from '@/features/jobs/types'

describe('QueueBeam', () => {
    it('renders without runtime errors when jobs are waiting for review', () => {
        const waitingJob: JobSummary = {
            jobId: 'job-123',
            station: '2GB',
            market: 'Sydney',
            status: 'WAITING_APPROVAL',
            targetDurationSec: 30,
            createdAt: new Date().toISOString(),
            lastUpdatedAt: new Date().toISOString(),
        }

        const deadlines = new Map<string, Date>()
        deadlines.set('job-123', new Date(Date.now() + 60000))

        render(
            <MemoryRouter>
                <QueueBeam
                    jobs={[waitingJob]}
                    deadlines={deadlines}
                    isLoading={false}
                    selectedJobId={null}
                    onSelect={vi.fn()}
                />
            </MemoryRouter>
        )

        expect(screen.getByText('2GB')).toBeInTheDocument()
        expect(screen.getByText('Needs review')).toBeInTheDocument()
    })
})
