// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor, act } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useJobProgress } from './progress-hooks'

const mocks = vi.hoisted(() => ({
    get: vi.fn(),
}))

vi.mock('@/lib/api/client', () => ({
    apiClient: { get: mocks.get },
}))

const payload = {
    JobId: 'job-1',
    Status: 'INCIDENTS_SELECTED',
    Stage: 'Evidence',
    StageStartedAt: '2026-09-02T00:00:00Z',
    Attempt: 1,
    MaxAttempts: 3,
    UpdatedAt: '2026-09-02T00:00:00Z',
    Version: 1,
    Events: [],
    TypicalSeconds: { Evidence: 4 },
}

function wrapper({ children }: { children: React.ReactNode }) {
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    })
    return (
        <QueryClientProvider client={queryClient}>
            {children}
        </QueryClientProvider>
    )
}

afterEach(() => {
    mocks.get.mockReset()
})

describe('useJobProgress', () => {
    it('sends the last ETag and keeps the document on 304', async () => {
        mocks.get
            .mockResolvedValueOnce({
                status: 200,
                data: payload,
                headers: { etag: '"v1"' },
            })
            .mockResolvedValueOnce({ status: 304, data: {}, headers: {} })

        const result = renderHook(
            () => useJobProgress('job-1', true, 'INCIDENTS_SELECTED'),
            { wrapper }
        )
        await waitFor(() =>
            expect(result.result.current.data).toMatchObject({
                stage: 'Evidence',
            })
        )
        const first = result.result.current.data
        await act(async () => {
            await result.result.current.refetch()
        })

        expect(mocks.get).toHaveBeenCalledTimes(2)
        expect(mocks.get.mock.calls[1]?.[1]).toMatchObject({
            headers: { 'If-None-Match': '"v1"' },
            validateStatus: expect.any(Function),
        })
        expect(result.result.current.data).toEqual(first)
    })

    it('turns 404 into unavailable data without an error', async () => {
        mocks.get.mockResolvedValue({ status: 404, data: {}, headers: {} })
        const result = renderHook(
            () => useJobProgress('old-job', true, 'SCRIPT_READY'),
            { wrapper }
        )

        await waitFor(() =>
            expect(result.result.current.data).toEqual({ unavailable: true })
        )
        expect(result.result.current.isError).toBe(false)
        expect(mocks.get).toHaveBeenCalledTimes(1)
    })
})
