// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useJobsHistory } from './job-hooks'
import { apiClient } from '@/lib/api/client'

vi.mock('@/lib/api/client', () => ({
    apiClient: {
        get: vi.fn(),
        post: vi.fn(),
        delete: vi.fn(),
    },
    ApiError: class ApiError extends Error {},
}))

afterEach(() => {
    cleanup()
    vi.clearAllMocks()
})

/** The wire shape of a job summary row (PascalCase, as the API serialises). */
interface JobSummaryWire {
    JobId: string
    CreatedAt: string
    Station: string
    Market: string
    TargetDurationSec: number
    Status: string
    LastUpdatedAt: string
}

function page(items: readonly JobSummaryWire[], hasMore: boolean) {
    return { data: { Items: items, Total: items.length, HasMore: hasMore } }
}

function renderHistory() {
    const queryClient = new QueryClient()
    const wrapper = ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={queryClient}>
            {children}
        </QueryClientProvider>
    )
    return renderHook(() => useJobsHistory(), { wrapper })
}

describe('useJobsHistory — composite paging cursor (bug-bash #11)', () => {
    // Two jobs TIED on the page-boundary timestamp (the exact condition that
    // used to drop rows: the bare-timestamp cursor sorted them ambiguously and
    // the strict `lt` filter skipped the ones beyond rank 100). The wire must
    // carry `LastUpdatedAt|JobId` so the server can break the tie.
    it('sends the last item as a composite `LastUpdatedAt|JobId` cursor on the next page', async () => {
        const tiedTimestamp = '2026-08-04T15:43:00.0025918Z'
        vi.mocked(apiClient.get)
            .mockResolvedValueOnce(
                page(
                    [
                        {
                            JobId: 'job-a',
                            CreatedAt: '2026-08-04T03:00:00Z',
                            Station: 'Hot Tomato 102.9',
                            Market: 'gold-coast',
                            TargetDurationSec: 30,
                            Status: 'REJECTED',
                            LastUpdatedAt: tiedTimestamp,
                        },
                        {
                            JobId: 'job-b',
                            CreatedAt: '2026-08-04T02:00:00Z',
                            Station: 'KIIS 101.1',
                            Market: 'melbourne',
                            TargetDurationSec: 30,
                            Status: 'REJECTED',
                            LastUpdatedAt: tiedTimestamp,
                        },
                    ],
                    true
                )
            )
            .mockResolvedValueOnce(page([], false))

        const { result } = renderHistory()
        await waitFor(() => expect(result.current.data?.pages).toHaveLength(1))

        await act(async () => {
            await result.current.fetchNextPage()
        })
        await waitFor(() => expect(result.current.data?.pages).toHaveLength(2))

        const calls = vi.mocked(apiClient.get).mock.calls
        expect(calls).toHaveLength(2)
        const secondUrl = new URL(`http://localhost${calls[1][0] as string}`)
        expect(secondUrl.searchParams.get('to')).toBe(`${tiedTimestamp}|job-b`)
    })

    it('still stops paging once the last page reports hasMore=false', async () => {
        vi.mocked(apiClient.get)
            .mockResolvedValueOnce(
                page(
                    [
                        {
                            JobId: 'job-a',
                            CreatedAt: '2026-08-04T03:00:00Z',
                            Station: 'S',
                            Market: 'M',
                            TargetDurationSec: 30,
                            Status: 'REJECTED',
                            LastUpdatedAt: '2026-08-04T15:43:00.0025918Z',
                        },
                    ],
                    false
                )
            )
            .mockResolvedValueOnce(page([], false))

        const { result } = renderHistory()
        await waitFor(() => expect(result.current.data?.pages).toHaveLength(1))

        await act(async () => {
            await result.current.fetchNextPage()
        })
        // getNextPageParam returned undefined, so no second page was fetched.
        expect(result.current.data?.pages).toHaveLength(1)
        expect(vi.mocked(apiClient.get).mock.calls).toHaveLength(1)
    })
})
