import { z } from 'zod'
import { apiClient } from '@/lib/api/client'
import { createMarketResourceHooks } from './create-market-resource-hooks'
import {
    marketIncidentPriorities,
    INCIDENT_PRIORITIES_INVENTORY,
    marketIncidentLandscape,
    INCIDENT_LANDSCAPE_INVENTORY,
} from '@/lib/api/paths'
import { useQueryFactory } from '@/lib/api/use-query-factory'

// =============================================================================
// Per-market Incident Type Priorities & Landscape:
// Allows operators to configure priority tiers and incident boosts (-100 to +100)
// across canonical classes, and inspect dynamic accumulated real-time occurrences.
// =============================================================================

export interface IncidentPriorityRule {
    readonly incidentClass: string
    readonly displayName: string
    readonly scoreBonus: number
    readonly enabled: boolean
}

export interface IncidentLandscapeEntry {
    readonly provider: string
    readonly canonicalClass: string
    readonly eventType: string
    readonly details: string
    readonly impact: string
    readonly codes: string | null
    readonly count: number
    readonly firstSeenUtc: string
    readonly lastSeenUtc: string
}

export interface IncidentLandscapeResponse {
    readonly market: string
    readonly entries: readonly IncidentLandscapeEntry[]
    readonly totalCount: number
}

export const CANONICAL_INCIDENT_CLASSES: readonly {
    readonly id: string
    readonly name: string
    readonly defaultBonus: number
    readonly description: string
}[] = [
    {
        id: 'Crash',
        name: 'Crashes and Collisions',
        defaultBonus: 20,
        description:
            'Single and multi-vehicle collisions, rollovers, and emergency incidents.',
    },
    {
        id: 'Closure',
        name: 'Road Closures',
        defaultBonus: 20,
        description:
            'Full carriageway and arterial shutdowns with detour routes.',
    },
    {
        id: 'Fire',
        name: 'Vehicle and Bushfires',
        defaultBonus: 15,
        description: 'Active vehicle fires and emergency smoke hazard zones.',
    },
    {
        id: 'Flooding',
        name: 'Flooding and Water Over Road',
        defaultBonus: 15,
        description:
            'Submerged causeways, flash flooding, and weather disruptions.',
    },
    {
        id: 'Breakdown',
        name: 'Breakdowns and Stalls',
        defaultBonus: 10,
        description:
            'Disabled vehicles, heavy vehicle tow aways, and lane stalls.',
    },
    {
        id: 'Hazard',
        name: 'Hazards and Road Damage',
        defaultBonus: 5,
        description:
            'Debris, traffic signal faults, bridge/culvert damage, and cautionary notices.',
    },
    {
        id: 'Congestion',
        name: 'Heavy Congestion and Queues',
        defaultBonus: 5,
        description:
            'Queuing traffic, bottleneck delays, and changed traffic conditions.',
    },
    {
        id: 'Roadworks',
        name: 'Roadworks and Construction',
        defaultBonus: 0,
        description:
            'Maintenance, resurfacing, upgrades, utility works, and lane reductions.',
    },
    {
        id: 'SpecialEvent',
        name: 'Special Events and Operations',
        defaultBonus: 0,
        description:
            'Community events, races, and scheduled police operations.',
    },
    {
        id: 'Ferry',
        name: 'Ferry Closures',
        defaultBonus: 0,
        description: 'Vehicular river ferry service maintenance and closures.',
    },
] as const

const IncidentPriorityRuleSchema = z
    .object({
        IncidentClass: z.string(),
        DisplayName: z.string().optional(),
        ScoreBonus: z
            .number()
            .nullish()
            .transform((v) => v ?? 0),
        Enabled: z
            .boolean()
            .nullish()
            .transform((v) => v ?? true),
    })
    .transform((r): IncidentPriorityRule => ({
        incidentClass: r.IncidentClass,
        displayName: r.DisplayName ?? r.IncidentClass,
        scoreBonus: r.ScoreBonus,
        enabled: r.Enabled,
    }))

const IncidentPrioritiesSchema = z
    .object({
        Market: z.string(),
        Rules: z.array(IncidentPriorityRuleSchema),
    })
    .transform((api) => ({ market: api.Market, rules: api.Rules }))

export type IncidentPriorities = z.infer<typeof IncidentPrioritiesSchema>

export interface MarketIncidentPriorities {
    readonly marketId: string
    readonly rules: readonly IncidentPriorityRule[]
}

export interface SetIncidentPrioritiesRequest {
    readonly marketId: string
    readonly rules: readonly IncidentPriorityRule[]
}

const incidentPrioritiesHooks = createMarketResourceHooks<
    IncidentPriorityRule,
    IncidentPriorities,
    MarketIncidentPriorities,
    SetIncidentPrioritiesRequest
>({
    key: 'incidentPriorities',
    singlePath: marketIncidentPriorities,
    inventoryPath: INCIDENT_PRIORITIES_INVENTORY,
    schema: IncidentPrioritiesSchema,
    marketId: (resource) => resource.market,
    items: (resource) => resource.rules,
    mutationItems: (request) => request.rules,
    toMarketResource: (marketId, rules) => ({ marketId, rules }),
    itemsField: 'rules',
})

export const useIncidentPriorities = incidentPrioritiesHooks.useResource
export const useSetIncidentPriorities = incidentPrioritiesHooks.useSetResource
export const useAllIncidentPriorities = incidentPrioritiesHooks.useAllResources

const IncidentLandscapeEntrySchema = z
    .object({
        Provider: z.string(),
        CanonicalClass: z.string(),
        EventType: z.string(),
        Details: z.string(),
        Impact: z.string(),
        Codes: z.string().nullable().optional(),
        Count: z.number(),
        FirstSeenUtc: z.string(),
        LastSeenUtc: z.string(),
    })
    .transform((e): IncidentLandscapeEntry => ({
        provider: e.Provider,
        canonicalClass: e.CanonicalClass,
        eventType: e.EventType,
        details: e.Details,
        impact: e.Impact,
        codes: e.Codes ?? null,
        count: e.Count,
        firstSeenUtc: e.FirstSeenUtc,
        lastSeenUtc: e.LastSeenUtc,
    }))

const IncidentLandscapeResponseSchema = z
    .object({
        Market: z.string(),
        Entries: z.array(IncidentLandscapeEntrySchema),
        TotalCount: z.number(),
    })
    .transform((r): IncidentLandscapeResponse => ({
        market: r.Market,
        entries: r.Entries,
        totalCount: r.TotalCount,
    }))

export function useIncidentLandscape(marketId: string, enabled = true) {
    return useQueryFactory<IncidentLandscapeResponse>(
        ['incidentLandscape', marketId],
        async () => {
            const { data } = await apiClient.get(
                marketIncidentLandscape(marketId)
            )
            return IncidentLandscapeResponseSchema.parse(data)
        },
        { enabled: enabled && marketId.length > 0 }
    )
}

export function useAllIncidentLandscapes(
    markets: readonly string[],
    enabled = true
) {
    return useQueryFactory<readonly IncidentLandscapeResponse[]>(
        ['incidentLandscape', 'inventory', [...markets].sort().join(',')],
        async () => {
            if (markets.length === 0) return []
            const params = new URLSearchParams({
                markets: [...markets].join(','),
            })
            const { data } = await apiClient.get(
                `${INCIDENT_LANDSCAPE_INVENTORY}?${params.toString()}`
            )
            return z.array(IncidentLandscapeResponseSchema).parse(data)
        },
        { enabled: enabled && markets.length > 0 }
    )
}
