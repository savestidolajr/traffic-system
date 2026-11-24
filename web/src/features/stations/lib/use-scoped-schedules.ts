import { useMemo } from 'react'
import { useOperatorScope } from '@/features/operators/api/operator-hooks'
import type { MarketSchedule } from '@/features/schedules/types'

/** Partition station schedules by the current operator's assigned markets. */
export function useScopedSchedules(schedules: readonly MarketSchedule[]): {
    readonly inScope: readonly MarketSchedule[]
    readonly outOfScope: readonly MarketSchedule[]
} {
    const { data: scope, isLoading: scopeLoading } = useOperatorScope()

    return useMemo(() => {
        const scopeMarkets =
            !scopeLoading && scope?.scoped && scope.markets
                ? new Set(scope.markets.map((market) => market.toLowerCase()))
                : null

        if (!scopeMarkets) {
            return { inScope: schedules, outOfScope: [] }
        }

        return {
            inScope: schedules.filter((schedule) =>
                scopeMarkets.has(schedule.marketId.toLowerCase())
            ),
            outOfScope: schedules.filter(
                (schedule) => !scopeMarkets.has(schedule.marketId.toLowerCase())
            ),
        }
    }, [schedules, scopeLoading, scope])
}
