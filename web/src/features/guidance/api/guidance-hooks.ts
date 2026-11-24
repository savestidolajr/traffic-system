import { apiClient } from '../../../lib/api/client'
import { createQueryKeys } from '../../../lib/api/create-query-keys'
import { useQueryFactory } from '../../../lib/api/use-query-factory'
import { useMutationFactory } from '../../../lib/api/use-mutation-factory'
import {
    GUIDANCE,
    guidanceDetail,
    GUIDANCE_PROMOTE,
    marketGuidance,
    marketGuidanceDetail,
    marketGuidanceComposed,
    marketGuidanceCandidates,
    stationGuidanceCandidate,
    GUIDANCE_INVENTORY,
} from '@/lib/api/paths'
import {
    GuidanceSchema,
    GuidanceListSchema,
    ComposedGuidanceSchema,
    GuidanceCandidateListSchema,
    type Guidance,
    type ComposedGuidance,
    type GuidanceCandidate,
    type GuidanceScopeName,
    type GuidanceTypeName,
} from './schemas'

const GUIDANCE_KEYS = createQueryKeys('guidance')
const INVALIDATE = () => [GUIDANCE_KEYS.all]

// ── Market scope (operator-maintainable) ─────────────────────────────────────

export interface UpsertMarketGuidanceVars {
    market: string
    type: GuidanceTypeName
    text: string
    enabled: boolean
    id?: string
}

const upsertMarketGuidance = async ({
    market,
    ...body
}: UpsertMarketGuidanceVars): Promise<Guidance> => {
    const { data } = await apiClient.put(marketGuidance(market), body)
    return GuidanceSchema.parse(data)
}

export const useUpsertMarketGuidance = () =>
    useMutationFactory(upsertMarketGuidance, INVALIDATE)

const deleteMarketGuidance = async ({
    market,
    id,
}: {
    market: string
    id: string
}): Promise<void> => {
    await apiClient.delete(marketGuidanceDetail(market, id))
}

export const useDeleteMarketGuidance = () =>
    useMutationFactory(deleteMarketGuidance, INVALIDATE)

// ── Global scope (admin) ─────────────────────────────────────────────────────

export interface UpsertGlobalGuidanceVars {
    type: GuidanceTypeName
    text: string
    enabled: boolean
    id?: string
    /** Station-scoped writes reuse the same PUT /api/guidance route; the
     *  backend validates the station against the schedule catalogue. */
    scope?: 'Station'
    scopeId?: string
}

const upsertGlobalGuidance = async (
    body: UpsertGlobalGuidanceVars
): Promise<Guidance> => {
    const { data } = await apiClient.put(GUIDANCE, {
        scope: body.scope ?? 'Global',
        scopeId: body.scopeId ?? '',
        type: body.type,
        text: body.text,
        enabled: body.enabled,
        ...(body.id !== undefined ? { id: body.id } : {}),
    })
    return GuidanceSchema.parse(data)
}

export const useUpsertGlobalGuidance = () =>
    useMutationFactory(upsertGlobalGuidance, INVALIDATE)

export interface DeleteGlobalGuidanceVars {
    id: string
    /** Station-scoped deletes reuse the same DELETE /api/guidance/{id} route. */
    scope?: 'Station'
    scopeId?: string
}

const deleteGlobalGuidance = async ({
    id,
    scope,
    scopeId,
}: DeleteGlobalGuidanceVars): Promise<void> => {
    const params = new URLSearchParams({ scope: scope ?? 'Global' })
    if (scopeId) params.set('scopeId', scopeId)
    await apiClient.delete(`${guidanceDetail(id)}?${params.toString()}`)
}

export const useDeleteGlobalGuidance = () =>
    useMutationFactory(deleteGlobalGuidance, INVALIDATE)

// ── Composed preview ("rules in effect") ─────────────────────────────────────

const fetchComposed = async (
    market: string,
    station?: string | null
): Promise<ComposedGuidance> => {
    const url = station
        ? `${marketGuidanceComposed(market)}?station=${encodeURIComponent(station)}`
        : marketGuidanceComposed(market)
    const { data } = await apiClient.get(url)
    return ComposedGuidanceSchema.parse(data)
}

export const useComposedGuidance = (
    market: string | null,
    station?: string | null
) =>
    useQueryFactory(
        [...GUIDANCE_KEYS.all, 'composed', market, station ?? null],
        () => fetchComposed(market as string, station),
        { enabled: Boolean(market) }
    )

// ── Candidate promotion queue ────────────────────────────────────────────────

const fetchCandidates = async (
    market: string
): Promise<GuidanceCandidate[]> => {
    const { data } = await apiClient.get(marketGuidanceCandidates(market))
    return GuidanceCandidateListSchema.parse(data)
}

export const useGuidanceCandidates = (market: string | null) =>
    useQueryFactory(
        [...GUIDANCE_KEYS.all, 'candidates', market],
        () => fetchCandidates(market as string),
        { enabled: Boolean(market) }
    )

export interface PromoteGuidanceVars {
    scope: GuidanceScopeName
    scopeId: string
    type: GuidanceTypeName
    text: string
    jobId?: string
}

const promoteGuidance = async (
    body: PromoteGuidanceVars
): Promise<Guidance> => {
    const { data } = await apiClient.post(GUIDANCE_PROMOTE, body)
    return GuidanceSchema.parse(data)
}

export const usePromoteGuidance = () =>
    useMutationFactory(promoteGuidance, INVALIDATE)

export interface DismissGuidanceCandidateVars {
    station: string
    id: string
}

const dismissGuidanceCandidate = async ({
    station,
    id,
}: DismissGuidanceCandidateVars): Promise<void> => {
    await apiClient.delete(stationGuidanceCandidate(station, id))
}

export const useDismissGuidanceCandidate = () =>
    useMutationFactory(dismissGuidanceCandidate, INVALIDATE)
// ── Aggregate inventory (global + accessible markets) ─────────────────────────

const fetchGuidanceInventory = async (
    markets: readonly string[]
): Promise<Guidance[]> => {
    const params = new URLSearchParams()
    if (markets.length > 0) params.set('markets', [...markets].join(','))
    const { data } = await apiClient.get(
        `${GUIDANCE_INVENTORY}?${params.toString()}`
    )
    return GuidanceListSchema.parse(data)
}

/**
 * Fetches the merged guidance inventory — global (admin only) plus every accessible
 * market — in ONE request. The backend composes the merge and enforces market scoping
 * (global is included by role; markets the caller can't access are dropped), replacing
 * the former per-market client fan-out. `enabled` lets the page defer the fetch until
 * the market list (derived from schedules) has settled, so it fires once, not twice.
 */
export const useGuidanceInventory = (
    markets: readonly string[],
    includeGlobal: boolean,
    enabled = true
) =>
    useQueryFactory<Guidance[]>(
        [
            ...GUIDANCE_KEYS.all,
            'inventory',
            includeGlobal,
            [...markets].sort().join(','),
        ],
        () => fetchGuidanceInventory(markets),
        { enabled: enabled && (includeGlobal || markets.length > 0) }
    )
