import { z } from 'zod'
import { createMarketResourceHooks } from './create-market-resource-hooks'
import { marketPriorityRoads, PRIORITY_ROADS_INVENTORY } from '@/lib/api/paths'

// =============================================================================
// Per-market priority roads (WS2-012): arterials that matter to listeners even
// when flowing. Separate resource from the schedule document — own GET/PUT.
// Backend caps: 50 roads, 80 chars each (mirrored in the studio editor).
// =============================================================================

export const PRIORITY_ROADS_MAX = 50
export const PRIORITY_ROAD_MAX_LENGTH = 80

/** Flow slot-competitiveness tier for a priority road (mirrors backend FlowPriorityTier). */
export type FlowTier = 'none' | 'normal' | 'high' | 'critical'

export interface PriorityRoadEntry {
    readonly name: string
    readonly boostIncidents: boolean
    readonly flowTier: FlowTier
    readonly enabled: boolean
}

const PriorityRoadEntrySchema = z
    .object({
        Name: z.string(),
        BoostIncidents: z
            .boolean()
            .nullish()
            .transform((v) => v ?? true),
        FlowTier: z
            .enum(['none', 'normal', 'high', 'critical'])
            .nullish()
            .transform((v) => v ?? 'high'),
        Enabled: z
            .boolean()
            .nullish()
            .transform((v) => v ?? true),
    })
    .transform((r): PriorityRoadEntry => ({
        name: r.Name,
        boostIncidents: r.BoostIncidents,
        flowTier: r.FlowTier,
        enabled: r.Enabled,
    }))

const PriorityRoadsSchema = z
    .object({
        Market: z.string(),
        Roads: z.array(PriorityRoadEntrySchema),
    })
    .transform((api) => ({ market: api.Market, roads: api.Roads }))

export type PriorityRoads = z.infer<typeof PriorityRoadsSchema>

export interface MarketRoads {
    readonly marketId: string
    readonly roads: readonly PriorityRoadEntry[]
}

export interface SetPriorityRoadsRequest {
    readonly marketId: string
    readonly roads: readonly PriorityRoadEntry[]
}

const priorityRoadsHooks = createMarketResourceHooks<
    PriorityRoadEntry,
    PriorityRoads,
    MarketRoads,
    SetPriorityRoadsRequest
>({
    key: 'priorityRoads',
    singlePath: marketPriorityRoads,
    inventoryPath: PRIORITY_ROADS_INVENTORY,
    schema: PriorityRoadsSchema,
    marketId: (resource) => resource.market,
    items: (resource) => resource.roads,
    mutationItems: (request) => request.roads,
    toMarketResource: (marketId, roads) => ({ marketId, roads }),
    itemsField: 'roads',
})

export const usePriorityRoads = priorityRoadsHooks.useResource
export const useSetPriorityRoads = priorityRoadsHooks.useSetResource

/**
 * Aggregate priority roads across every accessible market in ONE request. The backend
 * loops the requested markets server-side and enforces scoping (markets the caller can't
 * access are dropped), replacing the former per-market client fan-out. Returns one entry
 * per requested market, in input order; a market the backend omitted yields empty roads.
 */
export const useAllPriorityRoads = priorityRoadsHooks.useAllResources
