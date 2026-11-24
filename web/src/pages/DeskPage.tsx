import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from '@/components/ui/select'
import { Header, PageContainer } from '@/components/layout'
import { InfoHint } from '@/components/InfoHint'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { DataBoundary } from '@/components/DataBoundary'
import { ListSkeleton } from '@/components/LoadingSkeleton'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import { useLedger, useSetVerdict } from '@/features/ledger/api/ledger-hooks'
import { DeskHeader } from '@/features/ledger/components/DeskRow'
import { DeskRoadGroup } from '@/features/ledger/components/DeskRoadGroup'
import type { LedgerEvent } from '@/features/ledger/api/schemas'
import { useDocumentTitle } from '@/hooks/use-document-title'
import { formatTimeUntil } from '@/lib/formatting'

function groupByRoad(
    events: readonly LedgerEvent[]
): Array<[string, LedgerEvent[]]> {
    const groups: Array<[string, LedgerEvent[]]> = []
    const index: Record<string, number> = {}
    for (const e of events) {
        if (index[e.roadName] === undefined) {
            index[e.roadName] = groups.length
            groups.push([e.roadName, []])
        }
        groups[index[e.roadName]][1].push(e)
    }
    return groups
}

/**
 * The market's standing evidence view. Everything here feeds bulletins by default; the one
 * action is Omit, which keeps an event out until it is restored or leaves the feed. Operators
 * come here from a station's "Review evidence" link before a bulletin generates, or when a
 * recurring event needs a standing ban. There is nothing to work through.
 */
export default function DeskPage() {
    useDocumentTitle('Desk')
    const [searchParams] = useSearchParams()
    const { data: schedules } = useSchedules()
    const scheduleRows = useMemo(
        () =>
            schedules && 'schedules' in schedules ? schedules.schedules : [],
        [schedules]
    )
    const markets = useMemo(
        () => [...new Set(scheduleRows.map((s) => s.marketId))].sort(),
        [scheduleRows]
    )
    const [market, setMarket] = useState<string | null>(
        searchParams.get('market')
    )
    const effectiveMarket =
        market && markets.includes(market) ? market : (markets[0] ?? null)

    const {
        data: ledger,
        isLoading,
        isError,
        error,
        refetch,
    } = useLedger(effectiveMarket)
    const setVerdict = useSetVerdict()

    // What the next fetch will draft from: everything still in the feed and not omitted.
    const inPlay = useMemo(
        () =>
            (ledger?.events ?? []).filter(
                (e) => e.lane !== 'Cleared' && e.verdict !== 'Omitted'
            ),
        [ledger]
    )
    const omitted = useMemo(
        () => (ledger?.events ?? []).filter((e) => e.verdict === 'Omitted'),
        [ledger]
    )
    // Only a FUTURE run is worth announcing: a stale nextRunAt would read "just now".
    const [now] = useState(() => Date.now())
    const nextRun = useMemo(() => {
        const row = scheduleRows.find(
            (s) => s.marketId === effectiveMarket && s.enabled && s.nextRunAt
        )
        const at = row?.nextRunAt ? new Date(row.nextRunAt) : null
        return at && at.getTime() > now ? at : null
    }, [scheduleRows, effectiveMarket, now])

    const omit = (event: LedgerEvent) => {
        if (!effectiveMarket) return
        setVerdict.mutate({
            market: effectiveMarket,
            eventKey: event.key,
            verdict: 'Omitted',
        })
    }
    const restore = (event: LedgerEvent) => {
        if (!effectiveMarket) return
        setVerdict.mutate({
            market: effectiveMarket,
            eventKey: event.key,
            verdict: null,
        })
    }

    const table = (
        events: readonly LedgerEvent[],
        mode: 'in-play' | 'omitted'
    ) => (
        <SpotlightCard className="p-3">
            {/* The ledger request can fail after this page mounts, so keep the
                retry affordance visible instead of blanking the evidence card. */}
            <DataBoundary
                query={{
                    isLoading,
                    isError,
                    error,
                    data: ledger,
                    refetch,
                }}
                errorFallback="The evidence view could not be loaded. Bulletins are unaffected."
                loading={<ListSkeleton rows={5} />}
                empty={<div />}
            >
                {events.length === 0 ? (
                    <p className="px-2 py-4 text-xs text-muted-foreground">
                        {mode === 'in-play'
                            ? `No road events in play for ${effectiveMarket}.`
                            : 'Nothing is omitted. Everything in the feed reaches bulletins.'}
                    </p>
                ) : (
                    <div className="flex flex-col gap-0.5">
                        <DeskHeader />
                        {groupByRoad(events).map(([road, grouped]) => (
                            <DeskRoadGroup
                                key={road}
                                roadName={road}
                                events={grouped}
                                mode={mode}
                                onOmit={omit}
                                onRestore={restore}
                            />
                        ))}
                    </div>
                )}
            </DataBoundary>
        </SpotlightCard>
    )

    return (
        <PageContainer>
            <Header
                title="Desk"
                description="The road events feeding this market's bulletins. Omit anything that should not air."
                titleAfter={
                    <InfoHint label="How this works">
                        Every event here reaches bulletins automatically; there
                        is nothing to approve. Omit keeps an event out of every
                        bulletin until you restore it or it leaves the feed. Use
                        it before a bulletin generates (open this page from a
                        station's Review evidence link on the On air board) or
                        to keep a recurring event off air for good.
                    </InfoHint>
                }
            >
                <div className="flex items-center gap-3">
                    {nextRun && (
                        <span
                            className="text-xs text-muted-foreground"
                            title={nextRun.toLocaleString()}
                        >
                            Next bulletin in {formatTimeUntil(nextRun)}
                        </span>
                    )}
                    {markets.length > 1 && (
                        <Select
                            value={effectiveMarket ?? undefined}
                            onValueChange={setMarket}
                        >
                            <SelectTrigger className="w-44" aria-label="Market">
                                <SelectValue placeholder="Market" />
                            </SelectTrigger>
                            <SelectContent>
                                {markets.map((m) => (
                                    <SelectItem key={m} value={m}>
                                        {m}
                                    </SelectItem>
                                ))}
                            </SelectContent>
                        </Select>
                    )}
                </div>
            </Header>

            <Tabs defaultValue="in-play" className="mt-1">
                <TabsList>
                    <TabsTrigger value="in-play">
                        In play ({inPlay.length})
                    </TabsTrigger>
                    <TabsTrigger value="omitted">
                        Omitted ({omitted.length})
                    </TabsTrigger>
                </TabsList>
                <TabsContent value="in-play" className="mt-4">
                    {table(inPlay, 'in-play')}
                </TabsContent>
                <TabsContent value="omitted" className="mt-4">
                    {table(omitted, 'omitted')}
                </TabsContent>
            </Tabs>
        </PageContainer>
    )
}
