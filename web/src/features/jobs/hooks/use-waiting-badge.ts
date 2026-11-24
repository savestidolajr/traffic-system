import { useLiveJobs } from '../api/job-hooks'
import { useNow } from '@/hooks/use-second-tick'
import {
    formatOverdue,
    getUrgencyBand,
    type UrgencyBand,
} from '../lib/job-meta/urgency'
import {
    bindingSecondsRemaining,
    useBindingDeadlines,
} from './use-binding-deadlines'
import { formatDuration } from '@/lib/formatting'

/**
 * Derived nav-badge state shared by the desktop sidebar and the mobile
 * top bar: how many bulletins wait, and how hot the hottest one is.
 */
export interface WaitingBadgeState {
    readonly waitingCount: number
    readonly urgencyBand: UrgencyBand
    readonly badgeAriaLabel: string | undefined
    readonly countdownLabel: string | undefined
}

export function useWaitingBadge(): WaitingBadgeState {
    const { data: jobsData } = useLiveJobs()
    const now = useNow()
    const waitingJobs =
        jobsData?.jobs.filter((j) => j.status === 'WAITING_APPROVAL') ?? []
    const deadlines = useBindingDeadlines(waitingJobs)
    const waitingCount = waitingJobs.length
    // The same clock the rail and the board use: the earlier of the slot cutoff
    // and the approval dwell. The badge used to count the dwell alone, so it
    // could read "7m" while a slot was 30 seconds from being decided for you.
    const remainingSeconds = waitingJobs
        .map((job) => bindingSecondsRemaining(deadlines, job, now))
        .filter((seconds): seconds is number => seconds !== null)
    const mostUrgentSeconds =
        remainingSeconds.length > 0 ? Math.min(...remainingSeconds) : null
    const urgencyBand = getUrgencyBand(mostUrgentSeconds)
    // showHours is REQUIRED: formatDuration defaults it false, so a deadline more than an hour out
    // rendered as unbounded minutes ("120:00 left" for two hours). The overdue half of the same
    // sentence was always correct because formatOverdue handles hours, which made the inconsistency
    // invisible until both branches were compared.
    const countdownLabel =
        mostUrgentSeconds === null
            ? undefined
            : mostUrgentSeconds < 0
              ? `${formatOverdue(mostUrgentSeconds)} overdue`
              : `${formatDuration(mostUrgentSeconds, { showHours: true })} left`
    const deadlineAriaLabel =
        mostUrgentSeconds === null
            ? ''
            : mostUrgentSeconds < 0
              ? `, most urgent ${formatOverdue(mostUrgentSeconds)} overdue`
              : `, next due in ${formatDuration(mostUrgentSeconds, { showHours: true })}`
    const badgeAriaLabel =
        waitingCount === 0
            ? undefined
            : `${waitingCount} bulletin${waitingCount === 1 ? '' : 's'} awaiting review${deadlineAriaLabel}`

    return {
        waitingCount,
        urgencyBand,
        badgeAriaLabel,
        countdownLabel,
    }
}
