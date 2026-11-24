// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { apiClient } from '@/lib/api/client'
import { useJobs } from './job-hooks'

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

describe('useJobs UI lookback', () => {
    it('requests only the rolling 24-hour window through the bounded API path', async () => {
        vi.mocked(apiClient.get).mockResolvedValueOnce({
            data: { Items: [], Total: 0, HasMore: false },
        })
        const queryClient = new QueryClient()
        const wrapper = ({ children }: { children: ReactNode }) => (
            <QueryClientProvider client={queryClient}>
                {children}
            </QueryClientProvider>
        )
        const before = Date.now()

        renderHook(() => useJobs({ status: 'WAITING_APPROVAL' }), { wrapper })
        await waitFor(() => expect(apiClient.get).toHaveBeenCalledOnce())

        const [requestUrl] = vi.mocked(apiClient.get).mock.calls[0]
        const url = new URL(`http://localhost${requestUrl as string}`)
        const from = Date.parse(url.searchParams.get('from') ?? '')

        expect(url.searchParams.get('status')).toBe('WAITING_APPROVAL')
        expect(url.searchParams.get('limit')).toBe('500')
        expect(from).toBeGreaterThanOrEqual(before - 24 * 60 * 60 * 1000 - 1000)
        expect(from).toBeLessThanOrEqual(
            Date.now() - 24 * 60 * 60 * 1000 + 1000
        )
    })
})
