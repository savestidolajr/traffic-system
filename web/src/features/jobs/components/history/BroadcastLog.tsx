import { useState, useMemo, useCallback, Fragment } from 'react'
import { Link } from 'react-router'
import { Filter } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { ROUTE_JOBS } from '@/lib/routes'
import { Input } from '@/components/ui/input'
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from '@/components/ui/select'
import { Button } from '@/components/ui/button'
import {
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
} from '@/components/ui/table'
import { DataTableColumnHeader } from '@/components/DataTableColumnHeader'
import { LoadOlderButton } from '@/components/LoadOlderButton'
import { ExportButton } from '@/components/TransferButtons'
import { StickyFilterRail } from '@/components/StickyFilterRail'
import { datedFilename, downloadCsvFile, toCsv } from '@/lib/transfer'
import { Placeholder } from '@/components/Placeholder'
import { cn } from '@/lib/utils'
import { toggleSort, type SortState } from '@/lib/table-sort'
import type { JobSummary } from '../../types'
import {
    LOG_OUTCOMES,
    OUTCOME_LABEL,
    applyFilters,
    groupByDay,
    sortJobs,
    isDefaultHistorySort,
    DEFAULT_HISTORY_SORT,
    type HistorySortKey,
} from './history-utils'
import { BroadcastLogRow } from './BroadcastLogRow'

interface BroadcastLogProps {
    readonly allJobs: readonly JobSummary[]
    /** In-flight (not yet settled) job count, from a separate lightweight query —
     *  the paginated `allJobs` here only ever contains settled outcomes. */
    readonly pipelineCount: number
    readonly hasMore: boolean
    readonly onLoadMore: () => void
    readonly isLoadingMore: boolean
}

// ---------------------------------------------------------------------------
// Filter options derived from data
// ---------------------------------------------------------------------------

function useDistinctValues(
    jobs: readonly JobSummary[],
    key: (j: JobSummary) => string
): string[] {
    return useMemo(() => {
        const seen = new Set<string>()
        for (const j of jobs) {
            if (LOG_OUTCOMES.has(j.status)) seen.add(key(j))
        }
        return Array.from(seen).sort()
    }, [jobs, key])
}

const OUTCOME_OPTIONS = [
    { value: 'all', label: 'All outcomes' },
    ...[...LOG_OUTCOMES].map((s) => ({
        value: s,
        label: OUTCOME_LABEL[s],
    })),
]

const PERIOD_OPTIONS = [
    { value: 'all', label: 'All time' },
    { value: 'today', label: 'Today' },
    { value: '7d', label: 'Last 7 days' },
    { value: '30d', label: 'Last 30 days' },
]

const COLUMN_COUNT = 7

const CSV_HEADER = [
    'Job ID',
    'Station',
    'Market',
    'Settled at',
    'Outcome',
    'Measured duration',
    'Target duration',
    'Delta',
] as const

// ---------------------------------------------------------------------------
// Main Component
// ---------------------------------------------------------------------------

export function BroadcastLog({
    allJobs,
    pipelineCount,
    hasMore,
    onLoadMore,
    isLoadingMore,
}: BroadcastLogProps) {
    const [searchQuery, setSearchQuery] = useState('')
    const [outcomeFilter, setOutcomeFilter] = useState('all')
    const [stationFilter, setStationFilter] = useState('all')
    const [marketFilter, setMarketFilter] = useState('all')
    const [periodFilter, setPeriodFilter] = useState('all')
    const [sort, setSort] =
        useState<SortState<HistorySortKey>>(DEFAULT_HISTORY_SORT)

    const distinctStations = useDistinctValues(allJobs, (j) => j.station)
    const distinctMarkets = useDistinctValues(allJobs, (j) => j.market)

    const filters = useMemo(
        () => ({
            search: searchQuery,
            outcome: outcomeFilter,
            station: stationFilter,
            market: marketFilter,
            period: periodFilter,
        }),
        [searchQuery, outcomeFilter, stationFilter, marketFilter, periodFilter]
    )

    const filteredJobs = useMemo(
        () => applyFilters(allJobs, filters),
        [allJobs, filters]
    )

    const isDefaultSort = isDefaultHistorySort(sort)
    const dayGroups = useMemo(
        () => (isDefaultSort ? groupByDay(filteredJobs) : []),
        [filteredJobs, isDefaultSort]
    )
    const flatRows = useMemo(
        () => (isDefaultSort ? [] : sortJobs(filteredJobs, sort)),
        [filteredJobs, sort, isDefaultSort]
    )

    const hasActiveFilters =
        searchQuery !== '' ||
        outcomeFilter !== 'all' ||
        stationFilter !== 'all' ||
        marketFilter !== 'all' ||
        periodFilter !== 'all'

    const clearFilters = useCallback(() => {
        setSearchQuery('')
        setOutcomeFilter('all')
        setStationFilter('all')
        setMarketFilter('all')
        setPeriodFilter('all')
    }, [])

    const handleSort = useCallback(
        (key: HistorySortKey) => setSort((current) => toggleSort(current, key)),
        []
    )

    const exportCsv = useCallback(() => {
        const rows = filteredJobs.map((job) => [
            job.jobId,
            job.station,
            job.market,
            job.lastUpdatedAt,
            OUTCOME_LABEL[job.status] ?? job.status,
            job.measuredDurationSec != null
                ? String(job.measuredDurationSec)
                : '',
            String(job.targetDurationSec),
            job.deltaSec != null ? String(job.deltaSec) : '',
        ])
        downloadCsvFile(
            toCsv([[...CSV_HEADER], ...rows]),
            datedFilename('broadcast-log', 'csv')
        )
    }, [filteredJobs])

    // Filters active but no matches. The loaded window is only the newest page,
    // so "no matches" can simply mean the matching bulletins are older —
    // dropping Load older here told the operator the data did not exist and
    // removed the only control that could fetch it.
    if (hasActiveFilters && filteredJobs.length === 0) {
        return (
            <div className="flex flex-col gap-4">
                <FilterRail
                    searchQuery={searchQuery}
                    onSearchChange={setSearchQuery}
                    outcome={outcomeFilter}
                    onOutcomeChange={setOutcomeFilter}
                    station={stationFilter}
                    onStationChange={setStationFilter}
                    market={marketFilter}
                    onMarketChange={setMarketFilter}
                    period={periodFilter}
                    onPeriodChange={setPeriodFilter}
                    stations={distinctStations}
                    markets={distinctMarkets}
                    hasActiveFilters={hasActiveFilters}
                    onClear={clearFilters}
                    onExport={exportCsv}
                    exportDisabled={filteredJobs.length === 0}
                />
                <Placeholder
                    icon={ICON.noResults}
                    message="No matching bulletins"
                    detail={
                        hasMore
                            ? 'Nothing in the loaded range. Load older bulletins, or adjust your search and filters.'
                            : 'Try adjusting your search or filters.'
                    }
                    action={
                        <Button
                            variant="outline"
                            size="sm"
                            onClick={clearFilters}
                        >
                            <ICON.reset className="mr-1.5 size-3.5" />
                            Clear filters
                        </Button>
                    }
                />
                {hasMore && (
                    <LoadOlderButton
                        onClick={onLoadMore}
                        loading={isLoadingMore}
                    />
                )}
            </div>
        )
    }

    // No settled jobs loaded at all, and nothing older to fetch either
    if (!hasActiveFilters && filteredJobs.length === 0 && !hasMore) {
        return (
            <Placeholder
                icon={ICON.broadcast}
                message="No completed bulletins yet"
                detail="Outcomes land here once bulletins settle."
            />
        )
    }

    return (
        <div className="flex flex-col gap-4">
            {/* Pipeline count banner */}
            {pipelineCount > 0 && (
                <div className="flex items-center gap-2 rounded-lg border border-border/40 bg-muted/30 px-4 py-2 text-sm text-muted-foreground">
                    <ICON.broadcast className="size-4 text-primary" />
                    <span>
                        {pipelineCount}{' '}
                        {pipelineCount === 1 ? 'bulletin' : 'bulletins'} in the
                        pipeline right now
                    </span>
                    <Link
                        to={ROUTE_JOBS}
                        className="ml-auto text-xs font-medium text-primary hover:underline"
                    >
                        &rarr; On air
                    </Link>
                </div>
            )}

            <FilterRail
                searchQuery={searchQuery}
                onSearchChange={setSearchQuery}
                outcome={outcomeFilter}
                onOutcomeChange={setOutcomeFilter}
                station={stationFilter}
                onStationChange={setStationFilter}
                market={marketFilter}
                onMarketChange={setMarketFilter}
                period={periodFilter}
                onPeriodChange={setPeriodFilter}
                stations={distinctStations}
                markets={distinctMarkets}
                hasActiveFilters={hasActiveFilters}
                onClear={clearFilters}
                onExport={exportCsv}
                exportDisabled={filteredJobs.length === 0}
            />

            {filteredJobs.length === 0 ? (
                <Placeholder
                    icon={ICON.broadcast}
                    message="No completed bulletins in range yet"
                    detail="Load older bulletins, or check back once more settle."
                />
            ) : (
                <>
                    {/* Desktop table — visible md+ */}
                    <div className="hidden rounded-lg border md:block">
                        <Table>
                            <TableHeader>
                                <TableRow>
                                    <TableHead className="w-8">
                                        <span className="sr-only">Actions</span>
                                    </TableHead>
                                    <DataTableColumnHeader
                                        label="Time"
                                        sortKey="time"
                                        sort={sort}
                                        onSort={handleSort}
                                    />
                                    <DataTableColumnHeader
                                        label="Station"
                                        sortKey="station"
                                        sort={sort}
                                        onSort={handleSort}
                                    />
                                    <DataTableColumnHeader
                                        label="Market"
                                        sortKey="market"
                                        sort={sort}
                                        onSort={handleSort}
                                    />
                                    <DataTableColumnHeader
                                        label="Outcome"
                                        sortKey="outcome"
                                        sort={sort}
                                        onSort={handleSort}
                                    />
                                    <DataTableColumnHeader
                                        label="Duration"
                                        sortKey="duration"
                                        sort={sort}
                                        onSort={handleSort}
                                        align="right"
                                    />
                                    <DataTableColumnHeader
                                        label={'\u0394'}
                                        sortKey="delta"
                                        sort={sort}
                                        onSort={handleSort}
                                        align="right"
                                    />
                                </TableRow>
                            </TableHeader>
                            <TableBody>
                                {isDefaultSort
                                    ? dayGroups.map((group) => (
                                          <Fragment key={group.isoDate}>
                                              <TableRow className="hover:bg-transparent">
                                                  <TableCell
                                                      colSpan={COLUMN_COUNT}
                                                      className="bg-muted/30 py-2 text-sm font-semibold text-foreground"
                                                  >
                                                      {group.label}
                                                  </TableCell>
                                              </TableRow>
                                              {group.jobs.map((job) => (
                                                  <BroadcastLogRow
                                                      key={job.jobId}
                                                      job={job}
                                                  />
                                              ))}
                                          </Fragment>
                                      ))
                                    : flatRows.map((job) => (
                                          <BroadcastLogRow
                                              key={job.jobId}
                                              job={job}
                                          />
                                      ))}
                            </TableBody>
                        </Table>
                    </div>
                    {/* Mobile card list — visible below md */}
                    <div className="divide-y rounded-lg border md:hidden">
                        {isDefaultSort
                            ? dayGroups.map((group) => (
                                  <Fragment key={group.isoDate}>
                                      <div className="bg-muted/30 px-3 py-2 text-sm font-semibold text-foreground">
                                          {group.label}
                                      </div>
                                      {group.jobs.map((job) => (
                                          <BroadcastLogRow
                                              key={job.jobId}
                                              job={job}
                                              mobile
                                          />
                                      ))}
                                  </Fragment>
                              ))
                            : flatRows.map((job) => (
                                  <BroadcastLogRow
                                      key={job.jobId}
                                      job={job}
                                      mobile
                                  />
                              ))}
                    </div>
                </>
            )}

            {hasMore && (
                <LoadOlderButton onClick={onLoadMore} loading={isLoadingMore} />
            )}
        </div>
    )
}

// ---------------------------------------------------------------------------
// Filter rail sub-component
// ---------------------------------------------------------------------------

interface FilterRailProps {
    readonly searchQuery: string
    readonly onSearchChange: (v: string) => void
    readonly outcome: string
    readonly onOutcomeChange: (v: string) => void
    readonly station: string
    readonly onStationChange: (v: string) => void
    readonly market: string
    readonly onMarketChange: (v: string) => void
    readonly period: string
    readonly onPeriodChange: (v: string) => void
    readonly stations: string[]
    readonly markets: string[]
    readonly hasActiveFilters: boolean
    readonly onClear: () => void
    readonly onExport: () => void
    readonly exportDisabled: boolean
}

function FilterRail({
    searchQuery,
    onSearchChange,
    outcome,
    onOutcomeChange,
    station,
    onStationChange,
    market,
    onMarketChange,
    period,
    onPeriodChange,
    stations,
    markets,
    hasActiveFilters,
    onClear,
    onExport,
    exportDisabled,
}: FilterRailProps) {
    const [showFilters, setShowFilters] = useState(false)
    return (
        <StickyFilterRail className="flex flex-wrap items-start gap-2">
            {/* Search — always visible */}
            <div className="relative min-w-50 flex-1">
                <ICON.search className="absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
                <Input
                    placeholder="Search station, market, ID..."
                    value={searchQuery}
                    onChange={(e) => onSearchChange(e.target.value)}
                    className="pl-9"
                />
            </div>

            {/* Mobile filter toggle */}
            <div className="flex md:hidden">
                <Button
                    variant="outline"
                    size="sm"
                    onClick={() => setShowFilters((v) => !v)}
                    className="gap-1.5 text-xs"
                >
                    <Filter className="size-3.5" />
                    Filters
                    {hasActiveFilters && (
                        <span className="ml-0.5 size-1.5 rounded-full bg-primary" />
                    )}
                </Button>
            </div>

            {/* Filter controls — hidden on mobile unless toggled */}
            <div
                className={cn(
                    'flex flex-wrap items-center gap-2',
                    'max-md:w-full',
                    !showFilters && 'max-md:hidden'
                )}
            >
                {/* Outcome */}
                <Select value={outcome} onValueChange={onOutcomeChange}>
                    <SelectTrigger
                        className="w-[150px]"
                        aria-label="Filter by outcome"
                    >
                        <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                        {OUTCOME_OPTIONS.map((opt) => (
                            <SelectItem key={opt.value} value={opt.value}>
                                {opt.label}
                            </SelectItem>
                        ))}
                    </SelectContent>
                </Select>

                {/* Station */}
                <Select value={station} onValueChange={onStationChange}>
                    <SelectTrigger
                        className="w-[150px]"
                        aria-label="Filter by station"
                    >
                        <SelectValue placeholder="Station" />
                    </SelectTrigger>
                    <SelectContent>
                        <SelectItem value="all">All stations</SelectItem>
                        {stations.map((s) => (
                            <SelectItem key={s} value={s}>
                                {s}
                            </SelectItem>
                        ))}
                    </SelectContent>
                </Select>

                {/* Market */}
                <Select value={market} onValueChange={onMarketChange}>
                    <SelectTrigger
                        className="w-[150px]"
                        aria-label="Filter by market"
                    >
                        <SelectValue placeholder="Market" />
                    </SelectTrigger>
                    <SelectContent>
                        <SelectItem value="all">All markets</SelectItem>
                        {markets.map((m) => (
                            <SelectItem key={m} value={m}>
                                {m}
                            </SelectItem>
                        ))}
                    </SelectContent>
                </Select>

                {/* Period */}
                <Select value={period} onValueChange={onPeriodChange}>
                    <SelectTrigger
                        className="w-[150px]"
                        aria-label="Filter by period"
                    >
                        <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                        {PERIOD_OPTIONS.map((opt) => (
                            <SelectItem key={opt.value} value={opt.value}>
                                {opt.label}
                            </SelectItem>
                        ))}
                    </SelectContent>
                </Select>

                {/* Clear */}
                {hasActiveFilters && (
                    <Button
                        variant="ghost"
                        onClick={onClear}
                        className="gap-1.5 text-xs text-muted-foreground"
                    >
                        <ICON.reset className="size-3.5" />
                        Clear
                    </Button>
                )}

                {/* Default size, not `sm`: every other control in this rail is
                    a 40px SelectTrigger, and `sm` left these two at 36px. */}
                <ExportButton
                    label="Export CSV"
                    onExport={onExport}
                    size="default"
                    disabled={exportDisabled}
                />
            </div>
        </StickyFilterRail>
    )
}
