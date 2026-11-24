import { afterEach, describe, expect, it, vi } from 'vitest'
import { QueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import { prefetchSchedules } from './schedule-hooks'

vi.mock('@/lib/api/client', () => ({
    apiClient: {
        get: vi.fn(),
    },
}))

afterEach(() => {
    vi.clearAllMocks()
})

describe('prefetchSchedules', () => {
    it('warms the canonical scope once and shares the cached snapshot', async () => {
        vi.mocked(apiClient.get).mockResolvedValueOnce({
            data: { Items: [], Total: 0 },
        })
        const queryClient = new QueryClient()

        await prefetchSchedules(queryClient)
        await prefetchSchedules(queryClient)

        expect(apiClient.get).toHaveBeenCalledOnce()
        expect(apiClient.get).toHaveBeenCalledWith('/schedules', {
            signal: expect.any(AbortSignal),
        })
    })
})
