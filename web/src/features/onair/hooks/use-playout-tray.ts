import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import { marketPlayoutTray } from '@/lib/api/paths'
import { formatShortTime } from '@/lib/formatting'

export interface PlayoutTray {
    readonly fileName: string
    /** False when playout delivery is switched off for the environment. */
    readonly enabled: boolean
    /** True while our WAV is still sitting in the drop folder, uncollected. */
    readonly awaitingPickup: boolean
    readonly writtenAtUtc: string | null
    /** Local clock label for `writtenAtUtc`; empty when nothing is waiting. */
    readonly writtenAtLabel: string
}

/**
 * Whether this market's WAV is still in the playout drop folder.
 *
 * Presence is the only thing that distinguishes "our audio is still swappable"
 * from "Zetta already took it, and writing again queues a duplicate". Polled
 * rather than fetched once, because pickup happens on Zetta's clock, not ours,
 * and an operator can sit on this panel across a collection.
 */
export function usePlayoutTray(marketId: string) {
    return useQuery({
        queryKey: ['playout-tray', marketId],
        queryFn: async ({ signal }): Promise<PlayoutTray> => {
            const { data } = await apiClient.get(marketPlayoutTray(marketId), {
                signal,
            })
            const writtenAtUtc: string | null = data.writtenAtUtc ?? null
            return {
                fileName: data.fileName ?? '',
                enabled: data.enabled ?? false,
                awaitingPickup: data.awaitingPickup ?? false,
                writtenAtUtc,
                writtenAtLabel: writtenAtUtc
                    ? formatShortTime(new Date(writtenAtUtc))
                    : '',
            }
        },
        refetchInterval: 30_000,
        staleTime: 15_000,
    })
}
