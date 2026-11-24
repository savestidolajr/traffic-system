// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { apiClient } from '@/lib/api/client'
import { useLedger, useSetVerdict, ledgerQueryKey } from './ledger-hooks'
import { useLedgerMarket } from '@/features/activity/api/ledger-periphery-hooks'

vi.mock('@/lib/api/client', () => ({
    apiClient: {
        get: vi.fn(),
        post: vi.fn(),
    },
}))

afterEach(() => {
    cleanup()
    vi.clearAllMocks()
})

const LEDGER = {
    Market: 'sydney',
    Events: [
        {
            Key: 'here:1',
            RoadName: 'M7',
            Headline: 'Traffic is slow',
            Detail: null,
            Sources: ['here'],
            FirstSeenUtc: '2026-09-02T00:00:00Z',
            LastSeenUtc: '2026-09-02T00:05:00Z',
            StartTimeUtc: null,
            EndTimeUtc: null,
            RoadClosed: false,
            Lane: 'Active',
            SightingCount: 1,
            Observations: [],
            Airings: [],
            Verdict: null,
            VerdictNotes: null,
            VerdictBy: null,
            VerdictAtUtc: null,
            Record: {
                Key: 'here:1',
                Priority: 1,
                Headline: 'Traffic is slow',
                Detail: null,
                Latitude: null,
                Longitude: null,
            },
            OfficialEvidences: [],
        },
    ],
    FeedHealth: [
        {
            SourceId: 'here',
            LastContributedUtc: null,
            JobsWithSource: 0,
            JobsInWindow: 2,
        },
    ],
}

function wrapperFor(queryClient: QueryClient) {
    return ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={queryClient}>
            {children}
        </QueryClientProvider>
    )
}

describe('shared ledger observers', () => {
    it('deduplicates Desk and Activity market reads while preserving adapters', async () => {
        vi.mocked(apiClient.get).mockResolvedValue({ data: LEDGER })
        const queryClient = new QueryClient()
        const wrapper = wrapperFor(queryClient)

        const desk = renderHook(() => useLedger('sydney'), { wrapper })
        const activity = renderHook(() => useLedgerMarket('sydney'), {
            wrapper,
        })

        await waitFor(() => {
            expect(desk.result.current.data).toBeDefined()
            expect(activity.result.current.data).toBeDefined()
        })

        expect(apiClient.get).toHaveBeenCalledOnce()
        expect(desk.result.current.data?.market).toBe(
            activity.result.current.data?.market
        )
        expect(desk.result.current.data?.events).toHaveLength(
            activity.result.current.data?.events.length
        )
        expect(desk.result.current.data?.feedHealth).toEqual(
            activity.result.current.data?.feedHealth
        )
    })

    it('refreshes both observers through the mutation invalidation path', async () => {
        vi.mocked(apiClient.get)
            .mockResolvedValueOnce({ data: LEDGER })
            .mockResolvedValueOnce({
                data: {
                    ...LEDGER,
                    FeedHealth: [{ ...LEDGER.FeedHealth[0], JobsInWindow: 3 }],
                },
            })
        vi.mocked(apiClient.post).mockResolvedValue({ data: undefined })
        const queryClient = new QueryClient()
        const wrapper = wrapperFor(queryClient)

        const desk = renderHook(() => useLedger('sydney'), { wrapper })
        const activity = renderHook(() => useLedgerMarket('sydney'), {
            wrapper,
        })
        const mutation = renderHook(() => useSetVerdict(), { wrapper })
        await waitFor(() => expect(apiClient.get).toHaveBeenCalledOnce())

        mutation.result.current.mutate({
            market: 'sydney',
            eventKey: 'here:1',
            verdict: 'Omitted',
        })
        await waitFor(() => expect(apiClient.post).toHaveBeenCalledOnce())
        await waitFor(() => expect(apiClient.get).toHaveBeenCalledTimes(2))
        await waitFor(() =>
            expect(
                activity.result.current.data?.feedHealth[0].jobsInWindow
            ).toBe(3)
        )
        expect(desk.result.current.data?.feedHealth[0].jobsInWindow).toBe(3)
        expect(ledgerQueryKey('sydney')).toEqual(['ledger', 'detail', 'sydney'])
    })

    it('keeps empty-market observers disabled on both surfaces', () => {
        const queryClient = new QueryClient()
        const wrapper = wrapperFor(queryClient)

        renderHook(() => useLedger(null), { wrapper })
        renderHook(() => useLedgerMarket('', true), { wrapper })

        expect(apiClient.get).not.toHaveBeenCalled()
    })

    it('uses one GET per shared 60-second interval', async () => {
        vi.useFakeTimers()
        try {
            vi.mocked(apiClient.get).mockResolvedValue({ data: LEDGER })
            const queryClient = new QueryClient()
            const wrapper = wrapperFor(queryClient)

            renderHook(() => useLedger('sydney'), { wrapper })
            renderHook(() => useLedgerMarket('sydney'), { wrapper })
            expect(apiClient.get).toHaveBeenCalledOnce()

            await act(async () => {
                await vi.advanceTimersByTimeAsync(60_000)
            })
            expect(apiClient.get).toHaveBeenCalledTimes(2)
        } finally {
            vi.useRealTimers()
        }
    })
})
