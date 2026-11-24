// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { z } from 'zod'
import { apiClient } from '@/lib/api/client'
import { createMarketResourceHooks } from './create-market-resource-hooks'

vi.mock('@/lib/api/client', () => ({
    apiClient: {
        get: vi.fn(),
        put: vi.fn(),
    },
}))

afterEach(() => {
    cleanup()
    vi.clearAllMocks()
})

const EntrySchema = z.object({ Name: z.string() })
const ResourceSchema = z.object({
    Market: z.string(),
    Items: z.array(EntrySchema),
})
type Entry = { readonly name: string }
type Resource = { readonly market: string; readonly items: readonly Entry[] }
type MarketResource = {
    readonly marketId: string
    readonly items: readonly Entry[]
}

const resourceHooks = createMarketResourceHooks<
    Entry,
    Resource,
    MarketResource,
    { readonly marketId: string; readonly items: readonly Entry[] }
>({
    key: 'testResource',
    singlePath: (marketId) => `/api/resource/${marketId}`,
    inventoryPath: '/api/resource/inventory',
    schema: ResourceSchema.transform((resource): Resource => ({
        market: resource.Market,
        items: resource.Items.map((item) => ({ name: item.Name })),
    })),
    marketId: (resource) => resource.market,
    items: (resource) => resource.items,
    mutationItems: (mutation) => mutation.items,
    toMarketResource: (marketId, items) => ({ marketId, items }),
    itemsField: 'items',
})

const wrapper = ({ children }: { readonly children: ReactNode }) => (
    <QueryClientProvider client={new QueryClient()}>
        {children}
    </QueryClientProvider>
)

describe('aggregate market resource hooks', () => {
    it('uses one inventory request and preserves input order with empty omissions', async () => {
        vi.mocked(apiClient.get).mockResolvedValueOnce({
            data: [
                { Market: 'm-02', Items: [{ Name: 'second' }] },
                { Market: 'm-11', Items: [{ Name: 'eleventh' }] },
            ],
        })
        const markets = [
            'm-13',
            'm-02',
            'm-07',
            'm-11',
            'm-01',
            'm-05',
            'm-09',
            'm-03',
            'm-12',
            'm-04',
            'm-06',
            'm-08',
            'm-10',
        ] as const

        const { result } = renderHook(
            () => resourceHooks.useAllResources(markets),
            { wrapper }
        )
        await waitFor(() => expect(result.current.data).toHaveLength(13))

        expect(apiClient.get).toHaveBeenCalledOnce()
        expect(apiClient.get).toHaveBeenCalledWith(
            '/api/resource/inventory?markets=m-13%2Cm-02%2Cm-07%2Cm-11%2Cm-01%2Cm-05%2Cm-09%2Cm-03%2Cm-12%2Cm-04%2Cm-06%2Cm-08%2Cm-10'
        )
        expect(result.current.data).toEqual([
            { marketId: 'm-13', items: [] },
            { marketId: 'm-02', items: [{ name: 'second' }] },
            { marketId: 'm-07', items: [] },
            { marketId: 'm-11', items: [{ name: 'eleventh' }] },
            { marketId: 'm-01', items: [] },
            { marketId: 'm-05', items: [] },
            { marketId: 'm-09', items: [] },
            { marketId: 'm-03', items: [] },
            { marketId: 'm-12', items: [] },
            { marketId: 'm-04', items: [] },
            { marketId: 'm-06', items: [] },
            { marketId: 'm-08', items: [] },
            { marketId: 'm-10', items: [] },
        ])
    })
})
