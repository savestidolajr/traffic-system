import { useEffect, useMemo, useRef } from 'react'
import {
    useQuery,
    useQueryClient,
    type UseQueryResult,
} from '@tanstack/react-query'
import type { AxiosResponseHeaders } from 'axios'
import { apiClient } from '@/lib/api/client'
import { jobProgress } from '@/lib/api/paths'
import { PROGRESS_POLL_MS, STALE_MS } from '@/lib/constants/timing'
import {
    isJobProgressUnavailable,
    JobProgressSchema,
    type JobProgress,
    type JobProgressResult,
} from './progress-schemas'
import { JOB_PROGRESS_QUERY_KEYS, JOB_QUERY_KEYS } from './query-keys'

const TERMINAL_PROGRESS_STAGES = new Set(['Ready', 'Failed'])
const PIPELINE_STATUS_NAMES = new Set([
    'requested',
    'herefetched',
    'incidentsselected',
    'awaitingcuration',
    'scriptready',
    'synthesizingaudio',
    'audioready',
    'deliverypending',
])

/** Accept both the current uppercase wire statuses and contract-style names. */
export function isProgressPipelineStatus(status: string | undefined): boolean {
    if (!status) return false
    return PIPELINE_STATUS_NAMES.has(
        status.replace(/[^a-z]/gi, '').toLowerCase()
    )
}

function responseHeader(
    headers: AxiosResponseHeaders | Record<string, unknown> | undefined,
    name: string
): string | undefined {
    if (!headers) return undefined
    const headerMap = headers as AxiosResponseHeaders & {
        [key: string]: unknown
    }
    const fromGetter = headerMap.get?.(name)
    const fromMap =
        fromGetter ?? headerMap[name] ?? headerMap[name.toLowerCase()]
    if (Array.isArray(fromMap)) return fromMap[0]
    return typeof fromMap === 'string' ? fromMap : undefined
}

/** Query result returned by useJobProgress, including the supported 404 path. */
export type JobProgressQuery = UseQueryResult<JobProgressResult, Error>

/**
 * Read the append-only progress document cheaply while generation is active.
 * A missing document is data, not an error: older and script-only jobs render
 * the status-history fallback and keep the existing detail polling cadence.
 *
 * `jobStatus` is an optional observer hint used to retry a 404 after the detail
 * query reports a new status. The public two-argument form remains sufficient
 * for consumers that only need the document.
 */
export function useJobProgress(
    jobId: string,
    enabled: boolean,
    jobStatus?: string
): JobProgressQuery {
    const queryClient = useQueryClient()
    const queryKey = useMemo(
        () => JOB_PROGRESS_QUERY_KEYS.detail(jobId),
        [jobId]
    )
    const etagRef = useRef<string | null>(null)
    const previousDataRef = useRef<JobProgressResult | undefined>(undefined)
    const lastObservedProgressRef = useRef<JobProgress | undefined>(undefined)
    const previousStatusRef = useRef(jobStatus)

    const query = useQuery<JobProgressResult, Error>({
        queryKey,
        enabled: enabled && jobId.length > 0,
        staleTime: STALE_MS,
        retry: false,
        refetchOnWindowFocus: false,
        queryFn: async ({ signal }) => {
            const response = await apiClient.get(jobProgress(jobId), {
                signal,
                headers: etagRef.current
                    ? { 'If-None-Match': etagRef.current }
                    : undefined,
                validateStatus: (status) =>
                    status === 200 || status === 304 || status === 404,
            })

            if (response.status === 404) {
                etagRef.current = null
                return { unavailable: true as const }
            }

            if (response.status === 304) {
                const previous =
                    previousDataRef.current ??
                    queryClient.getQueryData<JobProgressResult>(queryKey)
                if (!previous || isJobProgressUnavailable(previous)) {
                    throw new Error(
                        'Progress returned 304 without a previous document'
                    )
                }
                return previous
            }

            const parsed = JobProgressSchema.parse(response.data)
            etagRef.current = responseHeader(response.headers, 'etag') ?? null
            return parsed
        },
        refetchInterval: (currentQuery) => {
            const data = currentQuery.state.data
            if (!enabled || isJobProgressUnavailable(data)) return false
            if (!data) return PROGRESS_POLL_MS
            if (TERMINAL_PROGRESS_STAGES.has(data.stage)) return false
            return isProgressPipelineStatus(data.status)
                ? PROGRESS_POLL_MS
                : false
        },
    })

    useEffect(() => {
        const progress = query.data
        const prior = lastObservedProgressRef.current

        if (progress && !isJobProgressUnavailable(progress)) {
            const stageChanged =
                prior !== undefined &&
                (prior.status !== progress.status ||
                    prior.stage !== progress.stage)
            const terminalEntered =
                (progress.stage === 'Ready' || progress.stage === 'Failed') &&
                (prior === undefined ||
                    (prior.stage !== 'Ready' && prior.stage !== 'Failed'))

            if (stageChanged || terminalEntered) {
                void queryClient.invalidateQueries({
                    queryKey: JOB_QUERY_KEYS.detail(jobId),
                    exact: true,
                })
            }
            lastObservedProgressRef.current = progress
        }

        previousDataRef.current = progress
    }, [jobId, query.data, queryClient])
    useEffect(() => {
        const statusChanged =
            previousStatusRef.current !== undefined &&
            jobStatus !== undefined &&
            previousStatusRef.current !== jobStatus
        previousStatusRef.current = jobStatus

        if (statusChanged && isJobProgressUnavailable(query.data)) {
            etagRef.current = null
            void queryClient.refetchQueries({
                queryKey,
                type: 'active',
            })
        }
    }, [jobStatus, query.data, queryClient, queryKey])

    return query
}
