import { useEffect, useRef } from 'react'
import { useNow } from '@/hooks/use-second-tick'
import { useLiveJobs } from '@/features/jobs/api/job-hooks'
import {
    getSecondsRemaining,
    formatOverdue,
} from '@/features/jobs/lib/job-meta/urgency'
import { formatDuration } from '@/lib/formatting'
import type { JobSummary } from '@/features/jobs/types'

/**
 * Manage `document.title` with a countdown for WAITING_APPROVAL jobs.
 *
 * Interplay with `useDocumentTitle`: when waiting jobs exist, this hook
 * owns the title with a countdown prefix. When waiting drops to 0, it
 * restores the previous non-alarm title that `useDocumentTitle` set.
 * On the next tick (if waiting > 0 again), it re-acquires the current
 * title as the restore point, so `useDocumentTitle` changes between
 * alarm bursts (e.g. navigating between pages) are respected.
 */
export function useTitleAlarm() {
    const { data: jobsData } = useLiveJobs()
    const restoreTitleRef = useRef<string | null>(null)
    const wasWaitingRef = useRef(false)

    const now = useNow()

    useEffect(() => {
        const waiting = jobsData?.jobs.filter(
            (j) => j.status === 'WAITING_APPROVAL'
        )
        const count = waiting?.length ?? 0

        if (count === 0) {
            if (wasWaitingRef.current && restoreTitleRef.current !== null) {
                document.title = restoreTitleRef.current
                restoreTitleRef.current = null
            }
            wasWaitingRef.current = false
            return
        }

        // Capture current title as restore point (may have changed via
        // useDocumentTitle between alarm bursts)
        if (restoreTitleRef.current === null) {
            restoreTitleRef.current = document.title
        }

        const mostUrgent = waiting!.reduce<{
            job: JobSummary
            seconds: number | null
        } | null>((best, job) => {
            const secs = getSecondsRemaining(job, now)
            if (best === null) return { job, seconds: secs }
            if (secs === null) return best
            if (best.seconds === null) return { job, seconds: secs }
            return secs < best.seconds ? { job, seconds: secs } : best
        }, null)

        const secs = mostUrgent?.seconds
        const countdown =
            secs === null || secs === undefined
                ? '-'
                : secs < 0
                  ? `+${formatOverdue(Math.abs(secs))} over`
                  : formatDuration(secs)

        document.title = `(${count}) ${countdown} | ARN Bulletin Generator`
    }, [jobsData, now])
}
