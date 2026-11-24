import { useMemo } from 'react'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'

export interface DictionaryAssignments {
    /** dictionaryId -> station labels that currently use it (derived from schedules). */
    readonly byDictionaryId: ReadonlyMap<string, string[]>
    /** How many stations exist at all, for describing a platform-wide dictionary's reach. */
    readonly stationCount: number
    readonly isLoading: boolean
}

/**
 * Which stations each pronunciation dictionary is assigned to.
 * Mirrors useStyleAssignments for delivery styles.
 *
 * A platform-wide dictionary is referenced by NO schedule, so it correctly has no
 * entry here. Callers must describe its reach from `stationCount` rather than
 * showing it as unassigned, which would be actively wrong.
 */
export function useDictionaryAssignments(): DictionaryAssignments {
    const { data, isLoading } = useSchedules()

    const { byDictionaryId, stationCount } = useMemo(() => {
        const map = new Map<string, string[]>()
        const schedules = data?.schedules ?? []
        for (const schedule of schedules) {
            const dictId = schedule.pronunciationDictionaryId
            if (!dictId) continue
            const label = schedule.station ?? schedule.name ?? schedule.marketId
            const stations = map.get(dictId) ?? []
            if (!stations.includes(label)) stations.push(label)
            map.set(dictId, stations)
        }
        return { byDictionaryId: map, stationCount: schedules.length }
    }, [data])

    return { byDictionaryId, stationCount, isLoading }
}
