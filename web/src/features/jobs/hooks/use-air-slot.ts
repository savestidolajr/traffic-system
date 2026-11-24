import { useMemo } from 'react'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import { findAirSlot } from '@/features/onair/lib/slots'
import type { JobSummary } from '../types'

/**
 * The bulletin's slot on its station's schedule, or null for an ad-hoc bulletin
 * or a station with no enabled schedule.
 *
 * The shared resolver also walks schedule calendar overrides by station-local
 * date, keeping the review deadline on the same slot the on-air board shows.
 *
 * Extracted because two surfaces need the same answer: the vitals header (which
 * shows the slot) and the decision dock (which now counts down to it). Resolving
 * it twice from two components invited them to disagree about the one fact the
 * operator is being asked to beat.
 */
export function useAirSlot(
    job: Pick<JobSummary, 'jobId' | 'station' | 'createdAt' | 'status'>
): Date | null {
    const { data } = useSchedules()
    const schedules = data?.schedules
    return useMemo(
        () => findAirSlot(schedules ?? [], job as JobSummary),
        [schedules, job]
    )
}
