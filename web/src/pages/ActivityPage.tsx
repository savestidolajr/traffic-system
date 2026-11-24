import { useMemo, useState } from 'react'
import { ScrollText } from 'lucide-react'
import { Header, PageContainer } from '@/components/layout'
import { Input } from '@/components/ui/input'
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from '@/components/ui/select'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { ListSkeleton } from '@/components/LoadingSkeleton'
import { DataBoundary } from '@/components/DataBoundary'
import { Placeholder } from '@/components/Placeholder'
import { MetricCard } from '@/components/MetricCard'
import { useDocumentTitle } from '@/hooks/use-document-title'
import { useActivityLogHistory } from '@/features/activity/api/activity-hooks'
import { useLedgerMarket } from '@/features/activity/api/ledger-periphery-hooks'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import { ActivityTable } from '@/features/activity/components/ActivityTable'
import { StickyFilterRail } from '@/components/StickyFilterRail'
import { formatRelativeTime } from '@/lib/formatting'

const ALL_RESOURCES = 'all'

// Mirrors the backend's ActivityPathResolver resource table -- keep in sync
// when a new admin route is added there.
const RESOURCE_OPTIONS = [
    { value: 'schedules', label: 'Stations' },
    { value: 'priority-roads', label: 'Priority roads' },
    { value: 'filler-statements', label: 'Filler statements' },
    { value: 'guidance', label: 'Bulletin rules' },
    { value: 'delivery-styles', label: 'Delivery styles' },
    {
        value: 'pronunciation-dictionaries',
        label: 'Pronunciation dictionaries',
    },
    { value: 'voices/profiles', label: 'Voice profiles' },
    { value: 'operators', label: 'Operators' },
    { value: 'safety-terms', label: 'Safety terms' },
    { value: 'jobs', label: 'Jobs' },
    { value: 'broadcast', label: 'Ad-hoc broadcasts' },
    { value: 'config', label: 'Config transfer' },
    { value: 'config-imports', label: 'Config imports' },
] as const

const FEED_SOURCE_NAMES: Record<string, string> = {
    here: 'HERE',
    'livetraffic-nsw': 'Live Traffic NSW',
    qldtraffic: 'QLDTraffic',
    victraffic: 'VicTraffic',
    'mainroads-wa': 'Main Roads WA',
    trafficsa: 'Traffic SA',
    tasalert: 'TasALERT',
}

function FeedHealthPanel({ market }: { readonly market: string }) {
    const [now] = useState(() => Date.now())
    const query = useLedgerMarket(market)
    const feedHealth = query.data?.feedHealth ?? []

    return (
        <DataBoundary
            query={query}
            errorFallback="Failed to load feed health"
            loading={<ListSkeleton rows={3} />}
            empty={<div />}
        >
            {/* Feed health is a secondary request and can fail independently,
                so expose its retry state in the tab instead of returning null. */}
            {feedHealth.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                    No provenance-stamped bulletins for this market yet. Feed
                    health starts accumulating from the first bulletin after the
                    fusion release.
                </p>
            ) : (
                <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                    {feedHealth.map((feed) => {
                        const lastContributedUtc = feed.lastContributedUtc
                        const stale =
                            feed.jobsWithSource === 0 ||
                            !lastContributedUtc ||
                            Date.parse(lastContributedUtc) <
                                now - 24 * 60 * 60 * 1000
                        const sourceName =
                            FEED_SOURCE_NAMES[feed.sourceId.toLowerCase()] ??
                            feed.sourceId
                        return (
                            <MetricCard
                                key={feed.sourceId}
                                label={sourceName}
                                className={
                                    stale
                                        ? 'border-warning/30 bg-warning/5'
                                        : undefined
                                }
                            >
                                <span
                                    className={
                                        stale ? 'text-warning' : undefined
                                    }
                                >
                                    contributed {feed.jobsWithSource} of{' '}
                                    {feed.jobsInWindow} bulletins
                                </span>
                                <span
                                    className={
                                        stale
                                            ? 'text-warning'
                                            : 'text-muted-foreground'
                                    }
                                >
                                    · last seen{' '}
                                    {feed.lastContributedUtc
                                        ? formatRelativeTime(
                                              feed.lastContributedUtc
                                          )
                                        : 'never'}
                                </span>
                            </MetricCard>
                        )
                    })}
                </div>
            )}
        </DataBoundary>
    )
}

export function ActivityPage() {
    useDocumentTitle('Activity')

    const [resource, setResource] = useState(ALL_RESOURCES)
    const [entityId, setEntityId] = useState('')
    const [feedMarket, setFeedMarket] = useState('')
    const { data: schedulesResult } = useSchedules()
    const markets = useMemo(
        () =>
            Array.from(
                new Set(
                    (schedulesResult?.schedules ?? []).map(
                        (schedule) => schedule.marketId
                    )
                )
            ).sort(),
        [schedulesResult]
    )
    const selectedFeedMarket = feedMarket || markets[0] || ''

    const filter = useMemo(
        () => ({
            resource: resource === ALL_RESOURCES ? undefined : resource,
            entityId: entityId.trim() || undefined,
        }),
        [resource, entityId]
    )
    const diffable = Boolean(filter.resource) && Boolean(filter.entityId)

    const {
        data,
        isLoading,
        isError,
        error,
        refetch,
        fetchNextPage,
        hasNextPage,
        isFetchingNextPage,
    } = useActivityLogHistory(filter)

    const entries = useMemo(
        () => data?.pages.flatMap((page) => page.items) ?? [],
        [data]
    )

    return (
        <PageContainer>
            <Header
                title="Activity"
                description="Every admin and operator change, across every market"
            />

            <Tabs defaultValue="audit" className="mt-1">
                <TabsList>
                    <TabsTrigger value="audit">Audit log</TabsTrigger>
                    <TabsTrigger value="feed-health">Feed health</TabsTrigger>
                </TabsList>
                <TabsContent value="audit" className="mt-4">
                    <StickyFilterRail className="mb-4 flex flex-col gap-2 sm:flex-row sm:items-center">
                        <Select value={resource} onValueChange={setResource}>
                            <SelectTrigger
                                className="sm:w-52"
                                aria-label="Filter by resource"
                            >
                                <SelectValue />
                            </SelectTrigger>
                            <SelectContent>
                                <SelectItem value={ALL_RESOURCES}>
                                    All resources
                                </SelectItem>
                                {RESOURCE_OPTIONS.map((r) => (
                                    <SelectItem key={r.value} value={r.value}>
                                        {r.label}
                                    </SelectItem>
                                ))}
                            </SelectContent>
                        </Select>
                        <Input
                            value={entityId}
                            onChange={(event) =>
                                setEntityId(event.target.value)
                            }
                            placeholder="Filter by entity id (e.g. adelaide)…"
                            aria-label="Filter by entity id"
                            className="sm:max-w-64"
                        />
                    </StickyFilterRail>

                    <DataBoundary
                        query={{
                            isError,
                            error,
                            isLoading,
                            data: entries,
                            refetch,
                        }}
                        errorFallback="Failed to load activity"
                        loading={<ListSkeleton rows={8} />}
                        empty={
                            <Placeholder
                                icon={ScrollText}
                                message="No activity recorded"
                                detail="Changes to platform config will appear here once made."
                            />
                        }
                    >
                        <ActivityTable
                            entries={entries}
                            diffable={diffable}
                            hasMore={hasNextPage}
                            onLoadMore={() => void fetchNextPage()}
                            isLoadingMore={isFetchingNextPage}
                        />
                    </DataBoundary>
                </TabsContent>
                <TabsContent value="feed-health" className="mt-4">
                    <Select
                        value={selectedFeedMarket}
                        onValueChange={setFeedMarket}
                        disabled={markets.length === 0}
                    >
                        <SelectTrigger
                            className="mb-4 w-full sm:w-64"
                            aria-label="Select market for feed health"
                        >
                            <SelectValue placeholder="Select market" />
                        </SelectTrigger>
                        <SelectContent>
                            {markets.map((market) => (
                                <SelectItem key={market} value={market}>
                                    {market}
                                </SelectItem>
                            ))}
                        </SelectContent>
                    </Select>
                    {selectedFeedMarket && (
                        <FeedHealthPanel market={selectedFeedMarket} />
                    )}
                </TabsContent>
            </Tabs>
        </PageContainer>
    )
}
