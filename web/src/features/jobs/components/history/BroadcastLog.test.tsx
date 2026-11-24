// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { TooltipProvider } from '@/components/ui/tooltip'
import { BroadcastLog } from './BroadcastLog'
import type { JobSummary } from '../../types'

afterEach(cleanup)

const job: JobSummary = {
    jobId: 'job-1',
    createdAt: '2026-06-11T06:00:00Z',
    station: 'KIIS 101.1',
    market: 'Melbourne',
    targetDurationSec: 30,
    status: 'PUBLISHED',
    lastUpdatedAt: '2026-06-11T06:30:00Z',
    measuredDurationSec: 30,
    deltaSec: 0,
}

function renderLog(hasMore: boolean, onLoadMore = vi.fn()) {
    render(
        <MemoryRouter>
            <TooltipProvider>
                <BroadcastLog
                    allJobs={[job]}
                    pipelineCount={0}
                    hasMore={hasMore}
                    onLoadMore={onLoadMore}
                    isLoadingMore={false}
                />
            </TooltipProvider>
        </MemoryRouter>
    )
}

describe('BroadcastLog · filtered empty state', () => {
    it('keeps Load older reachable when a filter matches nothing in the loaded page', async () => {
        const user = userEvent.setup()
        const onLoadMore = vi.fn()
        renderLog(true, onLoadMore)

        await user.type(screen.getByPlaceholderText(/search/i), 'zzz-no-match')

        // History loads only the newest page, so "no matches" here can simply
        // mean the matching bulletins are older. Removing the paging control
        // told the operator the data did not exist.
        expect(screen.getByText('No matching bulletins')).toBeInTheDocument()
        const loadOlder = screen.getByRole('button', { name: /load older/i })
        await user.click(loadOlder)
        expect(onLoadMore).toHaveBeenCalledTimes(1)
    })

    it('says to adjust the filters when there is genuinely nothing older', async () => {
        const user = userEvent.setup()
        renderLog(false)

        await user.type(screen.getByPlaceholderText(/search/i), 'zzz-no-match')

        expect(screen.getByText('No matching bulletins')).toBeInTheDocument()
        expect(
            screen.getByText('Try adjusting your search or filters.')
        ).toBeInTheDocument()
        expect(
            screen.queryByRole('button', { name: /load older/i })
        ).not.toBeInTheDocument()
    })
})
