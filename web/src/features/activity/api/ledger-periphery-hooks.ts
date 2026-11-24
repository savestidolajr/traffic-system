import { z } from 'zod'
import { apiClient } from '@/lib/api/client'
import { createQueryKeys } from '@/lib/api/create-query-keys'
import { useMutationFactory } from '@/lib/api/use-mutation-factory'
import { useQueryFactory } from '@/lib/api/use-query-factory'
import { SPOT_CHECKS, spotCheck } from '@/lib/api/paths'
import { camelizeKeys } from '@/lib/api-utils'
import { useLedger } from '@/features/ledger/api/ledger-hooks'

const LedgerPeripheryKeys = createQueryKeys('ledger-periphery')

const FeedHealthApiSchema = z
    .object({
        SourceId: z.string(),
        LastContributedUtc: z.string().nullable(),
        JobsWithSource: z.number(),
        JobsInWindow: z.number(),
    })
    .transform(camelizeKeys)

export const LedgerMarketResponseSchema = z
    .object({
        Market: z.string(),
        Events: z.array(z.unknown()),
        FeedHealth: z.array(FeedHealthApiSchema),
    })
    .transform((response) => ({
        market: response.Market,
        events: response.Events,
        feedHealth: response.FeedHealth,
    }))

const SpotCheckApiSchema = z
    .object({
        JobId: z.string(),
        By: z.string(),
        AtUtc: z.string(),
    })
    .transform(camelizeKeys)

export const SpotChecksResponseSchema = z
    .object({ Items: z.array(SpotCheckApiSchema) })
    .transform((response) => ({ items: response.Items }))

export type LedgerMarketResponse = z.infer<typeof LedgerMarketResponseSchema>
export type SpotCheck = z.infer<typeof SpotCheckApiSchema>

const fetchSpotChecks = async (signal?: AbortSignal) => {
    const { data } = await apiClient.get(SPOT_CHECKS, { signal })
    return SpotChecksResponseSchema.parse(data)
}

const markSpotCheck = async ({ jobId }: { jobId: string }): Promise<void> => {
    await apiClient.post(spotCheck(jobId))
}

export const useLedgerMarket = (market: string, enabled = true) =>
    useLedger(enabled ? market : null)

export const useSpotChecks = () =>
    useQueryFactory(
        LedgerPeripheryKeys.filtered({ scope: 'spot-checks' }),
        fetchSpotChecks,
        {
            staleTime: 30_000,
        }
    )

export const useMarkSpotCheck = () =>
    useMutationFactory(markSpotCheck, () => [
        LedgerPeripheryKeys.filtered({ scope: 'spot-checks' }),
    ])
