import type { QueryClient } from '@tanstack/react-query'
import { apiClient } from '../../../lib/api/client'
import { createQueryKeys } from '../../../lib/api/create-query-keys'
import { pascalizeKeys } from '../../../lib/api-utils'
import { useQueryFactory } from '../../../lib/api/use-query-factory'
import { useMutationFactory } from '../../../lib/api/use-mutation-factory'
import { MarketScheduleSchema, SchedulesListResponseSchema } from './schemas'
import {
    MarketSchedule,
    SchedulesListResponse,
    UpsertScheduleRequest,
} from '../types'
import { SCHEDULES, scheduleDetail } from '@/lib/api/paths'
import { SCHEDULE_POLL_MS } from '@/lib/constants/timing'

const SCHEDULE_QUERY_KEYS = createQueryKeys('schedules')

// API CALLS
const fetchSchedules = async (
    signal?: AbortSignal
): Promise<SchedulesListResponse> => {
    const { data } = await apiClient.get(SCHEDULES, { signal })
    return SchedulesListResponseSchema.parse(data)
}
export const prefetchSchedules = (queryClient: QueryClient) =>
    queryClient.prefetchQuery({
        queryKey: SCHEDULE_QUERY_KEYS.lists(),
        queryFn: ({ signal }) => fetchSchedules(signal),
        staleTime: SCHEDULE_POLL_MS,
    })

const updateSchedule = async ({
    marketId,
    schedule,
}: {
    marketId: string
    schedule: UpsertScheduleRequest
}): Promise<MarketSchedule> => {
    const pascalCaseBody = pascalizeKeys(schedule)
    const { data } = await apiClient.put(
        scheduleDetail(marketId),
        pascalCaseBody
    )
    return MarketScheduleSchema.parse(data)
}

const deleteSchedule = async (marketId: string): Promise<void> => {
    await apiClient.delete(scheduleDetail(marketId))
}

export const useSchedules = (
    options:
        | number
        | {
              readonly enabled?: boolean
              readonly refreshInterval?: number
          } = {}
) => {
    const refreshInterval =
        typeof options === 'number'
            ? options
            : (options.refreshInterval ?? SCHEDULE_POLL_MS)
    const enabled =
        typeof options === 'number' ? true : (options.enabled ?? true)
    return useQueryFactory(SCHEDULE_QUERY_KEYS.lists(), fetchSchedules, {
        refetchInterval: refreshInterval,
        enabled,
    })
}

export const useUpdateSchedule = () =>
    useMutationFactory(updateSchedule, (vars) => {
        const keys: Array<readonly unknown[]> = [SCHEDULE_QUERY_KEYS.lists()]
        if (vars.marketId) keys.push(SCHEDULE_QUERY_KEYS.detail(vars.marketId))
        return keys
    })

export const useDeleteSchedule = () =>
    useMutationFactory(deleteSchedule, (vars) => {
        const keys: Array<readonly unknown[]> = [SCHEDULE_QUERY_KEYS.lists()]
        if (vars) keys.push(SCHEDULE_QUERY_KEYS.detail(vars))
        return keys
    })
