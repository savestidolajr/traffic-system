import { useMemo } from 'react'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import type { BookendRotation } from '@/lib/api/schemas/shared'

export interface BookendAssignments {
    /** bookendId -> station labels that currently use it (derived from schedules). */
    readonly byBookendId: ReadonlyMap<string, string[]>
    readonly isLoading: boolean
}

/**
 * Which stations each bookend pair is assigned to. The backend stores
 * `Bookend.assignedStations` but never populates it from a schedule's
 * `bookendId` or the rotation policy's referenced ids, so we derive it from
 * the schedules list here. Read-only, off the synthesis hot path; the
 * live-edit warning and list chips rely on it.
 */
export function useBookendAssignments(): BookendAssignments {
    const { data, isLoading } = useSchedules()

    const byBookendId = useMemo(() => {
        const map = new Map<string, string[]>()
        for (const schedule of data?.schedules ?? []) {
            const label = schedule.station ?? schedule.name ?? schedule.marketId
            for (const id of referencedBookendIds(schedule)) {
                const stations = map.get(id) ?? []
                if (!stations.includes(label)) stations.push(label)
                map.set(id, stations)
            }
        }
        return map
    }, [data])

    return { byBookendId, isLoading }
}

/**
 * Every bookend id a schedule can air: its fixed pair plus every id referenced
 * by the rotation policy (the flat set and each window's set).
 */
function referencedBookendIds(schedule: {
    readonly bookendId: string | null
    readonly bookendRotation: BookendRotation | null
}): string[] {
    const ids = new Set<string>()
    if (schedule.bookendId) ids.add(schedule.bookendId)
    const rotation = schedule.bookendRotation
    if (rotation) {
        for (const id of rotation.bookendIds) {
            if (id) ids.add(id)
        }
        for (const window of rotation.windows) {
            if (window.bookendId) ids.add(window.bookendId)
            for (const id of window.extraBookendIds ?? []) {
                if (id) ids.add(id)
            }
        }
    }
    return [...ids]
}
