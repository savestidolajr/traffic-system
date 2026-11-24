import { parseISO, format, isToday, isYesterday } from 'date-fns'
import type { JobSummary, JobSummaryStatus } from '../../types'
import { STATUS_CONFIG } from '../../config/status-config'
import { STATUS_VALUES } from '../../config/status-values'
import type { SortState } from '@/lib/table-sort'
// =============================================================================
// Outcome constants for the broadcast log
// =============================================================================

/** Statuses shown in the log: terminal + approved (settled outcomes). MISSED
 *  must be countable separately from REJECTED: an expiry is not a rejection. */
export const LOG_OUTCOMES = new Set<JobSummaryStatus>([
    'PUBLISHED',
    'APPROVED',
    'REJECTED',
    'FAILED',
    'FALLBACK_USED',
    'MISSED',
])

/**
 * The exact complement of `LOG_OUTCOMES` — every status that has NOT yet settled. Powers the
 * pipeline-count banner's own small query (`useJobs({ status: ... })`), kept separate from the
 * settled-only paginated fetch that feeds the table below.
 */
export const PIPELINE_QUERY_STATUSES: JobSummaryStatus[] = STATUS_VALUES.filter(
    (s) => !LOG_OUTCOMES.has(s)
)

/** Outcome labels come from STATUS_CONFIG so the log's filter vocabulary
 *  can never drift from the chips (which already render STATUS_CONFIG). */
export const OUTCOME_LABEL: Record<string, string> = Object.fromEntries(
    [...LOG_OUTCOMES].map((s) => [s, STATUS_CONFIG[s].label])
)

// =============================================================================
// Filtering
// =============================================================================

/** Count jobs still in the pipeline (not yet settled). */
export function countPipelineJobs(jobs: readonly JobSummary[]): number {
    return jobs.filter((j) => !LOG_OUTCOMES.has(j.status)).length
}

/** Filter to settled outcomes only. */
export function filterSettled(jobs: readonly JobSummary[]): JobSummary[] {
    return jobs.filter((j) => LOG_OUTCOMES.has(j.status))
}

export interface LogFilters {
    readonly search: string
    readonly outcome: string
    readonly station: string
    readonly market: string
    readonly period: string
}

export function applyFilters(
    jobs: readonly JobSummary[],
    filters: LogFilters
): JobSummary[] {
    const q = filters.search.trim().toLowerCase()
    const now = new Date()
    const periodCutoffMs =
        filters.period === 'today'
            ? new Date(
                  now.getFullYear(),
                  now.getMonth(),
                  now.getDate()
              ).getTime()
            : filters.period === '7d'
              ? now.getTime() - 7 * 86400000
              : filters.period === '30d'
                ? now.getTime() - 30 * 86400000
                : 0

    return filterSettled(jobs).filter((job) => {
        if (filters.outcome !== 'all' && job.status !== filters.outcome)
            return false
        if (filters.station !== 'all' && job.station !== filters.station)
            return false
        if (filters.market !== 'all' && job.market !== filters.market)
            return false
        if (
            periodCutoffMs > 0 &&
            parseISO(job.lastUpdatedAt).getTime() < periodCutoffMs
        )
            return false
        if (q) {
            const haystack =
                `${job.jobId} ${job.station} ${job.market}`.toLowerCase()
            if (!haystack.includes(q)) return false
        }
        return true
    })
}

// =============================================================================
// Sorting (flat table mode — see groupByDay below for the default view)
// =============================================================================

export type HistorySortKey =
    'time' | 'station' | 'market' | 'outcome' | 'duration' | 'delta'

/** The table's resting state: newest settled first, grouped by day (see groupByDay). Any other
 *  sort flattens the table — a column sort and day-grouping are mutually exclusive views. */
export const DEFAULT_HISTORY_SORT: SortState<HistorySortKey> = {
    key: 'time',
    direction: 'desc',
}

export function isDefaultHistorySort(sort: SortState<HistorySortKey>): boolean {
    return (
        sort.key === DEFAULT_HISTORY_SORT.key &&
        sort.direction === DEFAULT_HISTORY_SORT.direction
    )
}

export function sortJobs(
    jobs: readonly JobSummary[],
    sort: SortState<HistorySortKey>
): JobSummary[] {
    const dir = sort.direction === 'asc' ? 1 : -1
    return [...jobs].sort((a, b) => {
        switch (sort.key) {
            case 'time':
                return (
                    dir *
                    (parseISO(a.lastUpdatedAt).getTime() -
                        parseISO(b.lastUpdatedAt).getTime())
                )
            case 'station':
                return dir * a.station.localeCompare(b.station)
            case 'market':
                return dir * a.market.localeCompare(b.market)
            case 'outcome':
                return (
                    dir *
                    (OUTCOME_LABEL[a.status] ?? a.status).localeCompare(
                        OUTCOME_LABEL[b.status] ?? b.status
                    )
                )
            case 'duration':
                return (
                    dir *
                    ((a.measuredDurationSec ?? -1) -
                        (b.measuredDurationSec ?? -1))
                )
            case 'delta':
                return dir * ((a.deltaSec ?? 0) - (b.deltaSec ?? 0))
        }
    })
}

// =============================================================================
// Day grouping
// =============================================================================

export interface DayGroup {
    readonly label: string
    readonly date: Date
    readonly isoDate: string
    readonly jobs: JobSummary[]
}

function formatDayLabel(date: Date): string {
    const now = new Date()
    const dayOfWeek = format(date, 'EEEE')
    const dayMonth = format(date, 'd MMMM')
    const year = format(date, 'yyyy')

    if (isToday(date)) return `Today · ${dayOfWeek} ${dayMonth}`
    if (isYesterday(date)) return `Yesterday · ${dayOfWeek} ${dayMonth}`

    const sameYear = date.getFullYear() === now.getFullYear()
    return sameYear
        ? `${dayOfWeek} ${dayMonth}`
        : `${dayOfWeek} ${dayMonth} ${year}`
}

export function groupByDay(jobs: readonly JobSummary[]): DayGroup[] {
    const groups = new Map<string, JobSummary[]>()

    for (const job of jobs) {
        // Use the ISO string directly for UTC-based day grouping (avoids
        // timezone skew where local midnight shifts the calendar day).
        const isoDate = job.lastUpdatedAt.slice(0, 10)
        const existing = groups.get(isoDate)
        if (existing) {
            existing.push(job)
        } else {
            groups.set(isoDate, [job])
        }
    }

    return (
        Array.from(groups.entries())
            .map(([isoDate, dayJobs]) => {
                const date = parseISO(isoDate)
                // Sort newest first within the day
                dayJobs.sort(
                    (a, b) =>
                        parseISO(b.lastUpdatedAt).getTime() -
                        parseISO(a.lastUpdatedAt).getTime()
                )
                return {
                    label: formatDayLabel(date),
                    date,
                    isoDate,
                    jobs: dayJobs,
                }
            })
            // Sort newest day first
            .sort((a, b) => b.date.getTime() - a.date.getTime())
    )
}
