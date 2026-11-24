// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, renderHook, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { InfiniteData } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useRejectJob } from './job-hooks'
import type { JobsListResponse } from '../types'
import { makeJob } from '@/test/factories'

vi.mock('@/lib/api/client', () => ({
    apiClient: { post: vi.fn().mockResolvedValue({ data: {} }), get: vi.fn() },
    ApiError: class ApiError extends Error {},
}))

afterEach(() => {
    cleanup()
    vi.clearAllMocks()
})

/**
 * The optimistic status flip patches EVERY cached query under the
 * ['jobs','list'] prefix — and that prefix holds two shapes: the plain jobs
 * list (JobsListResponse) and the History page's paged query
 * (InfiniteData<JobsListResponse>, `{ pages }` with no `.jobs`). Patching the
 * paged shape as a plain list threw "Cannot read properties of undefined
 * (reading 'map')" out of the whole mutation — and because a Revise apply
 * rejects BEFORE it regenerates, one visit to History made every later apply
 * die client-side before any request fired (dev job 0c1c11e8, 2026-08-07).
 */
describe('useRejectJob — optimistic patch across both list-cache shapes', () => {
    const PLAIN_KEY = ['jobs', 'list', { filters: {} }]
    const PAGED_KEY = ['jobs', 'list', { filters: { paged: true } }]

    function seedClient() {
        const qc = new QueryClient()
        const summary = makeJob({ jobId: 'j1', status: 'WAITING_APPROVAL' })
        qc.setQueryData<JobsListResponse>(PLAIN_KEY, {
            jobs: [summary],
            total: 1,
            hasMore: false,
        })
        qc.setQueryData<InfiniteData<JobsListResponse>>(PAGED_KEY, {
            pages: [{ jobs: [summary], total: 1, hasMore: false }],
            pageParams: [undefined],
        })
        return qc
    }

    it('rejects without throwing and flips the status in BOTH cache shapes', async () => {
        const qc = seedClient()
        const wrapper = ({ children }: { children: ReactNode }) => (
            <QueryClientProvider client={qc}>{children}</QueryClientProvider>
        )
        const { result } = renderHook(() => useRejectJob(), { wrapper })

        await act(async () => {
            await result.current.mutateAsync({
                jobId: 'j1',
                reasonCode: 'RevisionRequested',
                notes: 'Voice swap',
            })
        })

        const plain = qc.getQueryData<JobsListResponse>(PLAIN_KEY)
        expect(plain?.jobs[0]?.status).toBe('REJECTED')

        const paged = qc.getQueryData<InfiniteData<JobsListResponse>>(PAGED_KEY)
        expect(paged?.pages[0]?.jobs[0]?.status).toBe('REJECTED')
        // The paged entry keeps its infinite shape — not coerced to a list.
        expect(paged?.pageParams).toEqual([undefined])
    })
})
