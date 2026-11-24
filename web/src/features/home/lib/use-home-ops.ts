import { useMemo } from 'react'
import { PIPELINE_STATUSES } from '@/features/jobs/config/status-values'
import { getSecondsRemaining } from '@/features/jobs/lib/job-meta/urgency'
import type { JobSummary } from '@/features/jobs/types'
import type { MarketSchedule } from '@/features/schedules/types'

export interface HomeOps {
    readonly waiting: readonly JobSummary[]
    readonly settled: readonly JobSummary[]
    readonly stationsOnAir: number
    readonly generating: number
    readonly publishedToday: number
}

/** Derive the live operational summary shown beneath the home hero. */
export function useHomeOps(
    jobs: readonly JobSummary[] | undefined,
    schedules: readonly MarketSchedule[]
): HomeOps {
    const waiting = useMemo(
        () =>
            (jobs ?? [])
                .filter((j) => j.status === 'WAITING_APPROVAL')
                .slice()
                .sort(
                    (a, b) =>
                        (getSecondsRemaining(a) ?? Infinity) -
                        (getSecondsRemaining(b) ?? Infinity)
                ),
        [jobs]
    )

    const settled = useMemo(
        () =>
            (jobs ?? [])
                .filter(
                    (j) =>
                        j.status !== 'WAITING_APPROVAL' &&
                        !(PIPELINE_STATUSES as readonly string[]).includes(
                            j.status
                        )
                )
                .slice()
                .sort(
                    (a, b) =>
                        Date.parse(b.lastUpdatedAt) -
                        Date.parse(a.lastUpdatedAt)
                ),
        [jobs]
    )

    const stationsOnAir = schedules.filter((s) => s.enabled).length
    const generating = (jobs ?? []).filter((j) =>
        (PIPELINE_STATUSES as readonly string[]).includes(j.status)
    ).length
    const publishedToday = (jobs ?? []).filter(
        (j) =>
            j.status === 'PUBLISHED' &&
            new Date(j.lastUpdatedAt).toDateString() ===
                new Date().toDateString()
    ).length

    return {
        waiting,
        settled,
        stationsOnAir,
        generating,
        publishedToday,
    }
}
