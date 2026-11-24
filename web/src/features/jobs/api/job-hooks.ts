import { apiClient } from '../../../lib/api/client'
import { createQueryKeys } from '../../../lib/api/create-query-keys'
import { useMutationFactory } from '../../../lib/api/use-mutation-factory'
import {
    useInfiniteQueryFactory,
    useQueryFactory,
} from '../../../lib/api/use-query-factory'
import {
    useQueryClient,
    type InfiniteData,
    type Query,
    type QueryClient,
    type QueryKey,
    type UseQueryResult,
} from '@tanstack/react-query'
import {
    JobSchema,
    JobsListResponseSchema,
    AuditionResponseSchema,
    RetoneResponseSchema,
    RewriteResponseSchema,
} from './schemas'
import {
    isJobProgressUnavailable,
    type JobProgressResult,
} from './progress-schemas'
import { JOB_PROGRESS_QUERY_KEYS } from './query-keys'
import { mapToOrchestrateApiRequest } from './mappers'
import {
    Job,
    OrchestrateRequest,
    hasAudio,
    isTerminal,
    JobSummaryStatus,
    JobsListResponse,
    JobFilters,
} from '../types'
import {
    JOBS,
    jobDetail,
    jobApprove,
    jobReject,
    jobRejectToFallback,
    jobPublish,
    jobAudition,
    jobRetone,
    jobRewrite,
    jobRegenerate,
    jobAirSource,
    jobProceedCuration,
    BROADCAST,
} from '@/lib/api/paths'
import { POLL_ACTIVE_MS, POLL_IDLE_MS, STALE_MS } from '@/lib/constants/timing'
import { useNow } from '@/hooks/use-second-tick'
import { z } from 'zod'
import { NOTIFICATION_TRIGGER_STATUSES } from '@/features/jobs/config/status-values'

/** Payload for an operator-edited incident sent with a regenerate request. */
export interface EditedIncidentPayload {
    readonly incidentId: string
    readonly included: boolean
    readonly priority: number
}

export type RejectionReasonCode =
    | 'Pronunciation'
    | 'WrongOrMissingIncident'
    | 'EditorialPriority'
    | 'ContentSelection'
    | 'WordingTone'
    | 'ScriptStructure'
    | 'AudioUnusable'
    | 'TimingLength'
    | 'RevisionRequested'

export interface RejectDecision {
    readonly reasonCode?: RejectionReasonCode
    readonly notes?: string
}

const BASE_KEYS = createQueryKeys('jobs')
const JOB_QUERY_KEYS = {
    ...BASE_KEYS,
    list: (filters?: JobFilters) =>
        [...BASE_KEYS.lists(), { filters }] as const,
}

// API CALLS
/**
 * A hung jobs-feed request must surface as an error, not an eternal skeleton:
 * every console surface (queue beam, home ops band, history table) waits on
 * this endpoint, and a backend that never answers used to leave the operator
 * staring at infinite shimmers with no timeout (bug-bash #25).
 */
const JOBS_FETCH_TIMEOUT_MS = 15000

const fetchJobs = async (
    filters?: JobFilters,
    signal?: AbortSignal
): Promise<JobsListResponse> => {
    const params = new URLSearchParams()
    if (filters?.status) params.append('status', filters.status)
    if (filters?.from) params.append('from', filters.from)
    if (filters?.to) params.append('to', filters.to)
    if (filters?.limit) params.append('limit', filters.limit.toString())

    const { data } = await apiClient.get(`${JOBS}?${params.toString()}`, {
        signal,
        timeout: JOBS_FETCH_TIMEOUT_MS,
    })
    return JobsListResponseSchema.parse(data)
}

const fetchJobDetail = async (
    jobId: string,
    signal?: AbortSignal
): Promise<Job> => {
    const { data } = await apiClient.get(jobDetail(jobId), { signal })
    // The schema's output type is flat (optional artifacts) while Job is a
    // discriminated union; the narrowing is safe because JobSchema's
    // superRefine enforces the union's per-status artifact invariants —
    // a payload that violates them fails the parse instead of reaching here.
    return JobSchema.parse(data) as Job
}

const responseSchema = z
    .object({ JobId: z.string() })
    .transform((d) => ({ jobId: d.JobId }))

const triggerJob = async (
    payload: OrchestrateRequest
): Promise<{ jobId: string }> => {
    const requestBody = mapToOrchestrateApiRequest(payload)
    const { data } = await apiClient.post(BROADCAST, requestBody)
    const parsed = responseSchema.parse(data)
    return parsed
}

const approveJob = async ({
    jobId,
    expectedEtag,
}: {
    jobId: string
    expectedEtag?: string | null
}): Promise<void> => {
    await apiClient.post(
        jobApprove(jobId),
        expectedEtag ? { expectedEtag } : undefined
    )
}
const proceedCuration = async ({
    jobId,
    expectedEtag,
    editedIncidents,
}: {
    jobId: string
    expectedEtag?: string | null
    editedIncidents: readonly EditedIncidentPayload[]
}): Promise<void> => {
    await apiClient.post(jobProceedCuration(jobId), {
        ExpectedEtag: expectedEtag,
        EditedIncidents: editedIncidents.map((incident) => ({
            IncidentId: incident.incidentId,
            Included: incident.included,
            Priority: incident.priority,
        })),
    })
}

const rejectJob = async ({
    jobId,
    reasonCode,
    notes,
    expectedEtag,
}: {
    jobId: string
    expectedEtag?: string | null
} & RejectDecision): Promise<void> => {
    await apiClient.post(jobReject(jobId), {
        ...(reasonCode ? { reasonCode } : {}),
        ...(notes ? { notes } : {}),
        ...(expectedEtag ? { expectedEtag } : {}),
    })
}

const rejectToFallbackJob = async ({
    jobId,
    reasonCode,
    notes,
    expectedEtag,
}: {
    jobId: string
    expectedEtag?: string | null
} & RejectDecision): Promise<void> => {
    await apiClient.post(jobRejectToFallback(jobId), {
        ...(reasonCode ? { reasonCode } : {}),
        ...(notes ? { notes } : {}),
        ...(expectedEtag ? { expectedEtag } : {}),
    })
}

const publishJob = async ({
    jobId,
    overrideApproval,
    expectedEtag,
}: {
    jobId: string
    overrideApproval?: boolean
    expectedEtag?: string | null
}): Promise<void> => {
    await apiClient.post(jobPublish(jobId), {
        overrideApproval,
        ...(expectedEtag ? { expectedEtag } : {}),
    })
}

const regenerateJob = async ({
    jobId,
    targetDurationSec,
    editedScript,
    editedIncidents,
    revoiceOnly,
    selectedCorridors,
    tonedScript,
    selectedGoodRuns,
    refreshPronunciation,
    voiceProfileId,
}: {
    jobId: string
    targetDurationSec?: number
    editedScript?: string
    editedIncidents?: EditedIncidentPayload[]
    revoiceOnly?: boolean
    /** Producer-curated corridor road names, in on-air order (WS2-010). */
    selectedCorridors?: string[]
    /** Approved words with the operator's per-line audio tags already placed. */
    tonedScript?: string
    /** Operator-curated flowing-road (good-run) names from the Flow tab. */
    selectedGoodRuns?: string[]
    /** Re-resolve the attached dictionary to its CURRENT version — set when the
     *  operator changed a pronunciation rule during this review. Without it the
     *  backend replays the version the job aired with and the fix is a no-op. */
    refreshPronunciation?: boolean
    /** Re-voice onto a DIFFERENT voice profile than the job's configured one.
     *  The backend resolves the profile's CURRENT version server-side, so the
     *  operator never picks a stale snapshot. Applies to both the verbatim
     *  re-voice and a full regenerate. Omit to keep the job's voice. */
    voiceProfileId?: string
}): Promise<void> => {
    await apiClient.post(jobRegenerate(jobId), {
        targetDurationSec,
        editedScript,
        editedIncidents,
        revoiceOnly,
        selectedCorridors,
        tonedScript,
        selectedGoodRuns,
        refreshPronunciation,
        voiceProfileId,
    })
}

/**
 * Auditions a draft read on the job's own voice WITHOUT touching the job — no safety
 * re-run, no slot refit, no status change. Delivery on v3 is only steerable by trying
 * it, so the operator needs a loop measured in seconds, not a full re-voice.
 */
export const auditionScript = async ({
    jobId,
    text,
    voiceProfileId,
}: {
    jobId: string
    /** The draft VERBATIM, tags included, so the tags can be heard. */
    text: string
    /** Hear the SAME words on a different voice profile — the candidate's current
     *  version is resolved server-side, same as a regenerate override. Omit to
     *  audition on the job's configured voice. */
    voiceProfileId?: string
}): Promise<{ url: string; durationSec: number; tagCount: number }> => {
    const { data } = await apiClient.post(jobAudition(jobId), {
        text,
        ...(voiceProfileId ? { voiceProfileId } : {}),
    })
    return AuditionResponseSchema.parse(data)
}

/**
 * Rewrites the draft into the register the operator described, and returns the text for review.
 * Deliberately does not re-voice: this CHANGES words, so a human reads them first and the safety
 * reviewer sees them when the re-voice is submitted.
 */
export const retoneScript = async ({
    jobId,
    text,
    intent,
}: {
    jobId: string
    text: string
    /** One plain sentence: "talk about this more calmly, it's about a death". */
    intent: string
}): Promise<{
    script: string
    wordsBefore: number
    wordsAfter: number
    tags: string[]
    /** Station-authored lines (bookend, filler) the backend held out and restored verbatim. */
    preservedLines: number
}> => {
    const { data } = await apiClient.post(jobRetone(jobId), { text, intent })
    return RetoneResponseSchema.parse(data)
}

/**
 * Rewrites the draft's CONTENT from a free-form operator instruction and returns a preview.
 * Unlike retone this may drop, reorder, reword and re-emphasise — the operator owns the requested
 * content change. The backend supplies the job's compact reference data to the editor but does not
 * run a grounding verdict; content safety remains the only semantic gate before preview.
 * Nothing is synthesised and the job is not mutated. The operator compares the preview, then
 * applies it through the normal verbatim re-voice path.
 */
export const rewriteScript = async ({
    jobId,
    text,
    instruction,
}: {
    jobId: string
    /** Free-form: "drop the weather and lead with the closure". */
    instruction: string
    text: string
}): Promise<{
    script: string
    wordsBefore: number
    wordsAfter: number
    /** Station-authored lines (bookend, filler) the backend held out and restored verbatim. */
    preservedLines: number
}> => {
    const { data } = await apiClient.post(jobRewrite(jobId), {
        text,
        instruction,
    })
    return RewriteResponseSchema.parse(data)
}

const JOBS_LOOKBACK_MS = 24 * 60 * 60 * 1000
const UI_JOBS_PAGE_SIZE = 500

export const useJobs = (
    filters?: JobFilters,
    refreshInterval = 10000,
    enabled = true
) => {
    // The UI's non-History consumers should never scan the complete jobs table.
    // The request uses the exact current clock; the cache key is minute-bucketed
    // so a console left open overnight does not create a new query every second.
    const now = useNow()
    const from = filters?.from ?? new Date(now - JOBS_LOOKBACK_MS).toISOString()
    const recentFilters: JobFilters = {
        ...filters,
        from,
        limit: filters?.limit ?? UI_JOBS_PAGE_SIZE,
    }
    const queryFilters: JobFilters = {
        ...recentFilters,
        from:
            filters?.from ??
            new Date(
                Math.floor(now / 60_000) * 60_000 - JOBS_LOOKBACK_MS
            ).toISOString(),
    }
    return useQueryFactory(
        JOB_QUERY_KEYS.list(queryFilters),
        (signal) => fetchJobs(recentFilters, signal),
        { staleTime: STALE_MS, refetchInterval: refreshInterval, enabled }
    )
}

/**
 * The narrow "active" job set the app shell watches on every page — the badge,
 * audible alarm, desktop notifications, and title countdown. Polls only jobs in a
 * notification-trigger status and the rolling 24-hour window, so background polling
 * cost no longer scales with total job count. History pages backward instead —
 * see `useJobsHistory` below.
 */
const ACTIVE_JOB_FILTERS: JobFilters = {
    status: [...NOTIFICATION_TRIGGER_STATUSES].join(','),
}

export const useActiveJobs = (refreshInterval = 10000) =>
    useJobs(ACTIVE_JOB_FILTERS, refreshInterval)
export const useLiveJobs = useActiveJobs

export interface JobsHistoryFilter {
    readonly status?: string
}

/** Page size for the History page's "Load older" table. */
const HISTORY_PAGE_SIZE = 100

const fetchJobsHistoryPage = async (
    filter: JobsHistoryFilter,
    cursor: string | undefined
): Promise<JobsListResponse> =>
    fetchJobs({
        status: filter.status,
        to: cursor,
        limit: HISTORY_PAGE_SIZE,
    })

/**
 * The History page's full browsable table: "Load older" pagination, each page's oldest job's
 * `LastUpdatedAt|JobId` becoming the next page's cursor. Unlike `useJobs`, which deliberately
 * bounds ordinary UI calls to the last 24 hours, History can continue loading older settled jobs
 * on demand.
 */
export const useJobsHistory = (
    filter: JobsHistoryFilter = {},
    enabled = true
) =>
    useInfiniteQueryFactory(
        JOB_QUERY_KEYS.filtered({ ...filter, paged: true }),
        (cursor) => fetchJobsHistoryPage(filter, cursor),
        {
            enabled,
            staleTime: STALE_MS,
            getNextPageParam: (lastPage) => {
                if (!lastPage.hasMore) return undefined
                const last = lastPage.jobs.at(-1)
                if (!last) return undefined
                // Composite cursor `LastUpdatedAt|JobId` (finding 11): the bare
                // timestamp let jobs sharing the page-boundary timestamp sort
                // ambiguously and silently drop; the job-id tiebreaker closes it.
                // The server still accepts bare timestamps for backward compat.
                return `${last.lastUpdatedAt}|${last.jobId}`
            },
        }
    )

export function jobDetailRefetchInterval(
    query: Query<Job, Error, Job, QueryKey>,
    queryClient?: QueryClient,
    jobId?: string
): number | false {
    const data = query.state.data
    if (!data) return POLL_ACTIVE_MS
    if (isTerminal(data)) return false

    // The progress observer owns refreshes while a live progress document is
    // available. Avoid polling the heavier full-detail endpoint in parallel.
    if (queryClient && jobId) {
        const progress = queryClient.getQueryData<JobProgressResult>(
            JOB_PROGRESS_QUERY_KEYS.detail(jobId)
        )
        if (
            progress &&
            !isJobProgressUnavailable(progress) &&
            progress.stage !== 'Ready' &&
            progress.stage !== 'Failed'
        ) {
            return false
        }
    }

    // Durable delivery is still an active operation even though the job already
    // has audio. Keep watching the detail until the delivery claim resolves.
    if (data.status === 'DELIVERY_PENDING') return POLL_ACTIVE_MS

    return hasAudio(data) ? POLL_IDLE_MS : POLL_ACTIVE_MS
}

export type JobDetailQuery = UseQueryResult<Job, Error> & {
    readonly isRefetching: boolean
}

export const useJobDetail = (jobId: string): JobDetailQuery => {
    const queryClient = useQueryClient()
    const query = useQueryFactory(
        JOB_QUERY_KEYS.detail(jobId),
        (signal) => fetchJobDetail(jobId, signal),
        {
            staleTime: STALE_MS,
            refetchInterval: (currentQuery) =>
                jobDetailRefetchInterval(currentQuery, queryClient, jobId),
            enabled: jobId.length > 0,
        }
    )

    return {
        ...query,
        isRefetching: query.isFetching && !query.isLoading,
    }
}

export const useTriggerJob = () =>
    useMutationFactory(triggerJob, () => [JOB_QUERY_KEYS.lists()])

const deleteJob = async ({
    jobId,
    expectedEtag,
}: {
    jobId: string
    expectedEtag?: string | null
}): Promise<void> => {
    // DELETE /jobs/{id} carries a revision precondition (finding 12): no body
    // or a stale etag is rejected with 428/409 and the delete never proceeds.
    // Send the etag read from the job DTO, exactly as approve/publish do.
    await apiClient.delete(jobDetail(jobId), {
        data: expectedEtag ? { expectedEtag } : undefined,
    })
}

/**
 * Overrides what airs in a slot: an earlier approved bulletin from today
 * (`repeat`, with the source's job id) or the network standby cart (`cart`).
 *
 * Not a status transition dressed up as one. The backend attaches the chosen
 * audio to the slot's job AND writes it to the playout drop in the same call,
 * because a choice that changes a status without reaching the station is the
 * exact failure this endpoint exists to remove.
 */
const setAirSource = async ({
    jobId,
    kind,
    sourceJobId,
    expectedEtag,
}: {
    jobId: string
    kind: 'repeat' | 'cart'
    sourceJobId?: string | null
    expectedEtag?: string | null
}): Promise<void> => {
    await apiClient.post(jobAirSource(jobId), {
        kind,
        sourceJobId: sourceJobId ?? null,
        expectedEtag: expectedEtag ?? null,
    })
}

// HOOKS

interface OptimisticJobCandidate {
    readonly jobId: string
    readonly status: JobSummaryStatus
}

/**
 * A status mutation (approve/reject/fallback) only knows the target status —
 * the server alone populates that variant's extra fields (approval,
 * rejection + rejectionHistory, fallback, etc). Rather than fabricate them
 * with a blind `as Job` cast, verify the merged candidate genuinely carries
 * every field its target variant requires and skip the optimistic write
 * otherwise; the settled refetch (`onSettled` in `useMutationFactory`)
 * supplies the real, complete `Job` moments later.
 */
function isCompleteJob(candidate: OptimisticJobCandidate): candidate is Job {
    // Presence must test the VALUE, not the key. `JobSchema`'s transform emits
    // every optional field explicitly (`rejection: api.Rejection ?? undefined`),
    // so the key is always there and a `field in candidate` check passed for
    // EVERY status — the guard was a no-op. That let a Revise on an
    // already-once-rejected job install `status: 'REJECTED'` with no
    // `rejection`, and AsAiredCard (mounted the instant the status turns
    // terminal, reading the reason under the retained REJECTED history entry)
    // died with "Cannot read properties of undefined (reading 'reason')".
    const values = candidate as unknown as Record<string, unknown>
    const has = (field: string) => values[field] != null
    switch (candidate.status) {
        case 'REQUESTED':
        case 'FAILED':
            return true
        case 'HERE_FETCHED':
            return has('traffic')
        case 'INCIDENTS_SELECTED':
        case 'WAITING_APPROVAL':
            return has('traffic') && has('selectedIncidents')
        case 'SCRIPT_READY':
            return has('traffic') && has('selectedIncidents') && has('script')
        case 'SYNTHESIZING_AUDIO':
        case 'AUDIO_READY':
            return (
                has('traffic') &&
                has('selectedIncidents') &&
                has('script') &&
                has('audio')
            )
        case 'APPROVED':
            return (
                has('traffic') &&
                has('selectedIncidents') &&
                has('script') &&
                has('audio') &&
                has('approval')
            )
        case 'REJECTED':
            return (
                has('traffic') &&
                has('selectedIncidents') &&
                has('script') &&
                has('audio') &&
                has('rejection') &&
                has('rejectionHistory')
            )
        case 'PUBLISHED':
            return (
                has('traffic') &&
                has('selectedIncidents') &&
                has('script') &&
                has('audio') &&
                has('approval') &&
                has('publish')
            )
        case 'FALLBACK_USED':
            return (
                has('traffic') &&
                has('selectedIncidents') &&
                has('script') &&
                has('audio') &&
                has('fallback')
            )
        case 'MISSED':
            return (
                has('traffic') &&
                has('selectedIncidents') &&
                has('script') &&
                has('audio') &&
                has('missed')
            )
        default:
            return false
    }
}

function useStatusMutation<TVars extends { jobId: string }>(
    mutateFn: (vars: TVars) => Promise<unknown>,
    targetStatus: JobSummaryStatus
) {
    return useMutationFactory<TVars, unknown>(
        mutateFn,
        (vars) => [JOB_QUERY_KEYS.detail(vars.jobId), JOB_QUERY_KEYS.lists()],
        {
            cancelKey: JOB_QUERY_KEYS.all,
            apply: (vars, qc) => {
                const prevDetail = qc.getQueryData<Job>(
                    JOB_QUERY_KEYS.detail(vars.jobId)
                )
                if (prevDetail) {
                    const candidate: OptimisticJobCandidate = {
                        ...prevDetail,
                        status: targetStatus,
                    }
                    if (isCompleteJob(candidate)) {
                        qc.setQueryData<Job>(
                            JOB_QUERY_KEYS.detail(vars.jobId),
                            candidate
                        )
                    }
                }
                // Every cached query under the lists() prefix gets the
                // optimistic status flip — and that prefix holds TWO shapes:
                // the plain jobs list (JobsListResponse) and the History
                // page's paged query (InfiniteData<JobsListResponse>, whose
                // data is { pages } with no .jobs). Patching the paged shape
                // as a plain list crashed the whole mutation with "Cannot
                // read properties of undefined (reading 'map')" — and since
                // a Revise apply rejects BEFORE it regenerates, one visit to
                // History made every later apply die client-side (dev job
                // 0c1c11e8, 2026-08-07).
                const patchList = (list: JobsListResponse): JobsListResponse =>
                    list.jobs
                        ? {
                              ...list,
                              jobs: list.jobs.map((j) =>
                                  j.jobId === vars.jobId
                                      ? { ...j, status: targetStatus }
                                      : j
                              ),
                          }
                        : list
                qc.setQueriesData<
                    JobsListResponse | InfiniteData<JobsListResponse>
                >({ queryKey: JOB_QUERY_KEYS.lists() }, (old) => {
                    if (!old) return old
                    if ('pages' in old) {
                        return { ...old, pages: old.pages.map(patchList) }
                    }
                    return patchList(old)
                })
            },
        }
    )
}

export const useApproveJob = () => useStatusMutation(approveJob, 'APPROVED')
export const useProceedCuration = () =>
    useMutationFactory(proceedCuration, (vars) => [
        JOB_QUERY_KEYS.detail(vars.jobId),
        JOB_QUERY_KEYS.lists(),
    ])
export const useRejectJob = () => useStatusMutation(rejectJob, 'REJECTED')
export const useRejectToFallbackJob = () =>
    useStatusMutation(rejectToFallbackJob, 'FALLBACK_USED')
export const usePublishJob = () =>
    useMutationFactory(publishJob, (vars) => [
        JOB_QUERY_KEYS.detail(vars.jobId),
        JOB_QUERY_KEYS.lists(),
    ])
/**
 * The slot's job lands on FALLBACK_USED carrying the chosen audio, so the
 * optimistic status is the same one the deadline sweep would have produced —
 * the only difference is that a human picked the source instead of the chain.
 */
export const useSetAirSource = () =>
    useStatusMutation(setAirSource, 'FALLBACK_USED')
export const useRegenerateJob = () =>
    useMutationFactory(regenerateJob, (vars) => [
        JOB_QUERY_KEYS.detail(vars.jobId),
        JOB_QUERY_KEYS.lists(),
    ])
export const useDeleteJob = () =>
    useMutationFactory(deleteJob, (vars) => [
        JOB_QUERY_KEYS.detail(vars.jobId),
        JOB_QUERY_KEYS.lists(),
    ])
