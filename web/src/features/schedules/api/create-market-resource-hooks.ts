import { z } from 'zod'
import { apiClient } from '@/lib/api/client'
import { createQueryKeys } from '@/lib/api/create-query-keys'
import { useMutationFactory } from '@/lib/api/use-mutation-factory'
import { useQueryFactory } from '@/lib/api/use-query-factory'

export interface MarketResourceHooksConfig<
    TEntry,
    TResource,
    TMarketResource,
    TMutation extends { readonly marketId: string },
> {
    readonly key: string
    readonly singlePath: (marketId: string) => string
    readonly inventoryPath: string
    readonly schema: z.ZodType<TResource>
    readonly marketId: (resource: TResource) => string
    readonly items: (resource: TResource) => readonly TEntry[]
    readonly mutationItems: (mutation: TMutation) => readonly TEntry[]
    readonly toMarketResource: (
        marketId: string,
        items: readonly TEntry[]
    ) => TMarketResource
    readonly itemsField: string
}

export function createMarketResourceHooks<
    TEntry,
    TResource,
    TMarketResource,
    TMutation extends { readonly marketId: string },
>(
    config: MarketResourceHooksConfig<
        TEntry,
        TResource,
        TMarketResource,
        TMutation
    >
) {
    const keys = createQueryKeys(config.key)
    const inventorySchema = z.array(config.schema)

    const fetchResource = async (marketId: string): Promise<TResource> => {
        const { data } = await apiClient.get(config.singlePath(marketId))
        return config.schema.parse(data)
    }

    const setResource = async (mutation: TMutation): Promise<TResource> => {
        const { data } = await apiClient.put(
            config.singlePath(mutation.marketId),
            {
                [config.itemsField]: config.mutationItems(mutation),
            }
        )
        return config.schema.parse(data)
    }

    const fetchInventory = async (
        markets: readonly string[]
    ): Promise<TMarketResource[]> => {
        if (markets.length === 0) return []
        const params = new URLSearchParams({ markets: [...markets].join(',') })
        const { data } = await apiClient.get(
            `${config.inventoryPath}?${params.toString()}`
        )
        const byMarket = new Map(
            inventorySchema
                .parse(data)
                .map((resource) => [
                    config.marketId(resource),
                    config.items(resource),
                ])
        )
        return markets.map((marketId) =>
            config.toMarketResource(marketId, byMarket.get(marketId) ?? [])
        )
    }

    return {
        useResource: (marketId: string, enabled = true) =>
            useQueryFactory(
                keys.detail(marketId),
                () => fetchResource(marketId),
                { enabled: enabled && marketId.length > 0 }
            ),
        useSetResource: () => useMutationFactory(setResource, () => [keys.all]),
        useAllResources: (markets: readonly string[]) =>
            useQueryFactory<TMarketResource[]>(
                [...keys.all, 'inventory', [...markets].sort().join(',')],
                () => fetchInventory(markets),
                { enabled: markets.length > 0 }
            ),
    }
}
