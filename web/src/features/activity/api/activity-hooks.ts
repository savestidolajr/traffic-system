import { apiClient } from '@/lib/api/client'
import { createQueryKeys } from '@/lib/api/create-query-keys'
import {
    useQueryFactory,
    useInfiniteQueryFactory,
} from '@/lib/api/use-query-factory'
import { ACTIVITY } from '@/lib/api/paths'
import { ActivityLogListResponseApiSchema } from './activity-schemas'
import type {
    ActivityLogEntry,
    ActivityLogListResponse,
} from './activity-schemas'

const ACTIVITY_KEYS = createQueryKeys('activity')

export interface ActivityFilter {
    readonly resource?: string
    readonly entityId?: string
    readonly fromUtc?: string
    readonly toUtc?: string
}

// ---------------------------------------------------------------------------
// API calls
// ---------------------------------------------------------------------------

export const fetchActivityLog = async (
    filter: ActivityFilter = {}
): Promise<ActivityLogEntry[]> => {
    const params = new URLSearchParams()
    if (filter.resource) params.append('resource', filter.resource)
    if (filter.entityId) params.append('entityId', filter.entityId)
    if (filter.fromUtc) params.append('from', filter.fromUtc)
    if (filter.toUtc) params.append('to', filter.toUtc)

    const query = params.toString()
    const { data } = await apiClient.get(
        query ? `${ACTIVITY}?${query}` : ACTIVITY
    )
    return ActivityLogListResponseApiSchema.parse(data).items
}

// ---------------------------------------------------------------------------
// Hooks
// ---------------------------------------------------------------------------

/**
 * The small embedded audit rail (a card's "History" disclosure, a Studio's History step): one
 * shot, whatever the backend's default window/limit returns, rendered as `ActivityFeed`'s
 * timeline. Scoped to a single entity, so it never needs sorting or pagination.
 */
export const useActivityLog = (filter: ActivityFilter = {}, enabled = true) =>
    useQueryFactory(
        ACTIVITY_KEYS.filtered(filter as Record<string, unknown>),
        () => fetchActivityLog(filter),
        { staleTime: 1000 * 30, enabled }
    )

/** Page size for the Activity page's "Load older" table — distinct from the embedded rail above. */
const HISTORY_PAGE_SIZE = 100

export interface ActivityHistoryFilter {
    readonly resource?: string
    readonly entityId?: string
}

/** One page, newest-first. `cursor` (an `atUtc` value) narrows to strictly older than it. */
const fetchActivityLogHistoryPage = async (
    filter: ActivityHistoryFilter,
    cursor: string | undefined
): Promise<ActivityLogListResponse> => {
    const params = new URLSearchParams()
    if (filter.resource) params.append('resource', filter.resource)
    if (filter.entityId) params.append('entityId', filter.entityId)
    if (cursor) params.append('to', cursor)
    params.append('limit', String(HISTORY_PAGE_SIZE))

    const { data } = await apiClient.get(`${ACTIVITY}?${params.toString()}`)
    return ActivityLogListResponseApiSchema.parse(data)
}

/**
 * The Activity page's full browsable table: "Load older" pagination, each page's oldest entry's
 * `atUtc` becoming the next page's cursor — bounded, incremental, never fetches everything at once.
 */
export const useActivityLogHistory = (
    filter: ActivityHistoryFilter = {},
    enabled = true
) =>
    useInfiniteQueryFactory(
        ACTIVITY_KEYS.filtered({ ...filter, paged: true } as Record<
            string,
            unknown
        >),
        (cursor) => fetchActivityLogHistoryPage(filter, cursor),
        {
            enabled,
            staleTime: 1000 * 30,
            getNextPageParam: (lastPage) =>
                lastPage.hasMore ? lastPage.items.at(-1)?.atUtc : undefined,
        }
    )
