import { apiClient } from '../../../lib/api/client'
import { createQueryKeys } from '../../../lib/api/create-query-keys'
import { useQueryFactory } from '../../../lib/api/use-query-factory'
import { useMutationFactory } from '../../../lib/api/use-mutation-factory'
import { JobDeltaSchema, LedgerSchema, type Verdict } from './schemas'

export const ledgerKeys = createQueryKeys('ledger')
export const ledgerQueryKey = (market: string) => ledgerKeys.detail(market)

const fetchLedger = async (market: string, signal?: AbortSignal) => {
    const res = await apiClient.get(`/ledger/${encodeURIComponent(market)}`, {
        signal,
    })
    return LedgerSchema.parse(res.data)
}

/** The Desk's feed: fold + verdicts + trust + feed health for one market. */
export function useLedger(market: string | null) {
    return useQueryFactory(
        ledgerQueryKey(market ?? 'none'),
        (signal) => fetchLedger(market!, signal),
        { enabled: Boolean(market), staleTime: 30_000, refetchInterval: 60_000 }
    )
}

/** The diff-review strip: how a bulletin's events moved vs the market's previous bulletin. */
export function useJobDelta(jobId: string | null) {
    return useQueryFactory(
        ledgerKeys.detail(`delta-${jobId ?? 'none'}`),
        async (signal) => {
            const res = await apiClient.get(`/jobs/${jobId}/delta`, { signal })
            return JobDeltaSchema.parse(res.data)
        },
        { enabled: Boolean(jobId), staleTime: 30_000 }
    )
}

interface SetVerdictVars {
    readonly market: string
    readonly eventKey: string
    /** null clears the verdict, returning the event to unvetted. */
    readonly verdict: Verdict | null
    readonly notes?: string
}

/** Records (or clears) the operator's standing verdict on a ledger event. */
export function useSetVerdict() {
    return useMutationFactory<SetVerdictVars>(
        async ({ market, eventKey, verdict, notes }) => {
            await apiClient.post(
                `/ledger/${encodeURIComponent(market)}/events/${encodeURIComponent(eventKey)}/verdict`,
                { Verdict: verdict, Notes: notes ?? null }
            )
        },
        () => [ledgerKeys.all]
    )
}
