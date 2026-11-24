import { useMemo } from 'react'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import { findAirSlot } from '@/features/onair/lib/slots'
import { CUTOFF_LEAD_MS } from '@/features/onair/lib/air-plan'
import { getApprovalDeadline } from '@/features/jobs/lib/job-meta/urgency'
import type { JobSummary } from '@/features/jobs/types'

/**
 * One deadline per waiting bulletin, shared by every surface that counts down.
 *
 * The console used to run two unrelated clocks. The queue rail counted the
 * 8-minute approval dwell (`lastUpdatedAt + 8m`) while the on-air board counted
 * the slot cutoff (`airtime - 3m`), so one bulletin appeared as "in 7:27",
 * green, third-least-urgent in the rail and "0:31", red, top row on the board at
 * the same moment. An operator asked which to believe; the answer is whichever
 * expires first, and only one of the two surfaces knew about it.
 *
 * The cutoff — not the airtime — is the air-side deadline, because after it the
 * deadline sweep decides what airs and an approval no longer changes anything.
 *
 * Keyed by job id and memoised on (jobs, schedules): resolving a slot enumerates
 * cron occurrences, which is the work that blew this route's TBT budget once
 * already, so it must not run per render or per tick. Consumers subtract their
 * own clock from the returned Date.
 */
export function useBindingDeadlines(
    jobs: readonly JobSummary[]
): Map<string, Date> {
    const { data } = useSchedules()
    const schedules = data?.schedules

    return useMemo(() => {
        const deadlines = new Map<string, Date>()
        if (!schedules) return deadlines

        for (const job of jobs) {
            if (job.status !== 'WAITING_APPROVAL') continue
            const approvalAt = getApprovalDeadline(job)
            if (!approvalAt) continue

            const slotAt = findAirSlot(schedules, job)
            const cutoffAt = slotAt
                ? new Date(slotAt.getTime() - CUTOFF_LEAD_MS)
                : null

            deadlines.set(
                job.jobId,
                cutoffAt && cutoffAt.getTime() < approvalAt.getTime()
                    ? cutoffAt
                    : approvalAt
            )
        }
        return deadlines
    }, [jobs, schedules])
}

/**
 * Seconds until the deadline that actually binds, negative once it has passed.
 * Null for a bulletin that is not awaiting a decision, matching the contract the
 * queue's row variants already branch on.
 */
export function bindingSecondsRemaining(
    deadlines: Map<string, Date>,
    job: Pick<JobSummary, 'jobId' | 'status' | 'lastUpdatedAt'>,
    now: number = Date.now()
): number | null {
    if (job.status !== 'WAITING_APPROVAL') return null
    const at = deadlines.get(job.jobId) ?? getApprovalDeadline(job)
    if (!at) return null
    return Math.floor((at.getTime() - now) / 1000)
}
