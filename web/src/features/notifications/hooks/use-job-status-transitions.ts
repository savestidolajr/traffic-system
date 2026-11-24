import { useEffect, useRef, useState } from 'react'
import type { JobSummary } from '@/features/jobs/types'
import { NOTIFICATION_TRIGGER_STATUSES } from '../constants'

const EMPTY_TRANSITIONS: readonly JobSummary[] = []

/**
 * Returns jobs that have newly entered a notification-triggering status.
 *
 * The first active-job snapshot primes the seen set without emitting anything,
 * so mounting the app never treats existing work as a new transition. Each
 * subsequent snapshot only emits a job/status pair once.
 */
export function useJobStatusTransitions(
    jobs: readonly JobSummary[] | undefined
): readonly JobSummary[] {
    const seenRef = useRef<Set<string>>(new Set())
    const initializedRef = useRef(false)
    const [transitions, setTransitions] =
        useState<readonly JobSummary[]>(EMPTY_TRANSITIONS)

    useEffect(() => {
        if (!jobs) return

        if (!initializedRef.current) {
            for (const job of jobs) {
                if (NOTIFICATION_TRIGGER_STATUSES.has(job.status)) {
                    seenRef.current.add(`${job.jobId}:${job.status}`)
                }
            }
            initializedRef.current = true
            setTransitions(EMPTY_TRANSITIONS)
            return
        }

        const newlyTriggered: JobSummary[] = []
        for (const job of jobs) {
            if (!NOTIFICATION_TRIGGER_STATUSES.has(job.status)) continue

            const key = `${job.jobId}:${job.status}`
            if (seenRef.current.has(key)) continue
            seenRef.current.add(key)
            newlyTriggered.push(job)
        }

        setTransitions(
            newlyTriggered.length > 0 ? newlyTriggered : EMPTY_TRANSITIONS
        )
    }, [jobs])

    return transitions
}
