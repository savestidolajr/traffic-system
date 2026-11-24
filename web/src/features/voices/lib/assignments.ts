import { useMemo } from 'react'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import type { MarketSchedule } from '@/features/schedules/types'

export interface VoiceAssignments {
    /** voiceProfileId -> station labels that currently use it (derived from schedules). */
    readonly byProfileId: ReadonlyMap<string, string[]>
    readonly isLoading: boolean
}

/** Every voice id a schedule can air with: the single voice plus any rotation members. */
export function scheduleVoiceIds(schedule: MarketSchedule): string[] {
    const ids = new Set<string>()
    if (schedule.voiceProfileId) ids.add(schedule.voiceProfileId)
    const rotation = schedule.voiceRotation
    if (rotation) {
        if (rotation.mode === 'roundRobin') {
            for (const id of rotation.voiceProfileIds) {
                if (id) ids.add(id)
            }
        } else if (rotation.mode === 'timeOfDay') {
            for (const window of rotation.windows) {
                if (window.voiceProfileId) ids.add(window.voiceProfileId)
            }
        }
    }
    return [...ids]
}

/**
 * voiceProfileId -> station labels, from the schedules list. Pure so the
 * derivation is unit-testable without a React Query provider.
 */
export function collectVoiceAssignments(
    schedules: readonly MarketSchedule[]
): Map<string, string[]> {
    const map = new Map<string, string[]>()
    for (const schedule of schedules) {
        const label = schedule.station ?? schedule.name ?? schedule.marketId
        for (const profileId of scheduleVoiceIds(schedule)) {
            const stations = map.get(profileId) ?? []
            if (!stations.includes(label)) stations.push(label)
            map.set(profileId, stations)
        }
    }
    return map
}

/**
 * Which stations each voice profile is assigned to, derived from the schedules
 * list. The backend stores `VoiceProfile.assignedStations` but never populates
 * it (nothing writes `AssignedStationsJson` after Create), so — like the
 * delivery/bookends/pronunciation sibling galleries — we derive it here. The
 * schedule's `voiceProfileId` (and voice-rotation members) are the real
 * reference; the DTO field stays a dead `[]`.
 */
export function useVoiceAssignments(): VoiceAssignments {
    const { data, isLoading } = useSchedules()

    const byProfileId = useMemo(
        () => collectVoiceAssignments(data?.schedules ?? []),
        [data]
    )

    return { byProfileId, isLoading }
}
