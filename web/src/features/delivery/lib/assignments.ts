import { useMemo } from 'react'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'

export interface StyleAssignments {
    /** styleId -> station labels that currently use it (derived from schedules). */
    readonly byStyleId: ReadonlyMap<string, string[]>
    readonly isLoading: boolean
}

/**
 * Which stations each delivery style is assigned to. The backend stores
 * `DeliveryStyle.assignedStations` but never populates it from a schedule's
 * `deliveryStyleId`, so we derive it from the schedules list here. Read-only,
 * off the synthesis hot path; the live-edit warning and list chips rely on it.
 */
export function useStyleAssignments(): StyleAssignments {
    const { data, isLoading } = useSchedules()

    const byStyleId = useMemo(() => {
        const map = new Map<string, string[]>()
        for (const schedule of data?.schedules ?? []) {
            const styleId = schedule.deliveryStyleId
            if (!styleId) continue
            const label = schedule.station ?? schedule.name ?? schedule.marketId
            const stations = map.get(styleId) ?? []
            if (!stations.includes(label)) stations.push(label)
            map.set(styleId, stations)
        }
        return map
    }, [data])

    return { byStyleId, isLoading }
}
