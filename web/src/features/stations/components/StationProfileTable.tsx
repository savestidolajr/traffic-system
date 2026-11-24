import { useMemo } from 'react'
import { Users } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { Button } from '@/components/ui/button'
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuGroup,
    DropdownMenuItem,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Switch } from '@/components/ui/switch'
import {
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
} from '@/components/ui/table'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { useDeleteConfirm } from '../lib/use-delete-confirm'
import { useScopedSchedules } from '../lib/use-scoped-schedules'
import { getStationAttentionIssues } from '../lib/station-attention'
import { useVoiceProfiles } from '@/features/voices/api/voice-profile-hooks'
import { useMapConfig } from '@/features/jobs/api/map-config-hooks'
import { useIsAdmin } from '@/hooks/use-current-user'
import { ScheduleNextRun } from '@/features/schedules/components/ScheduleNextRun'
import { CronScheduleGlyph } from '@/features/schedules/components/CronScheduleGlyph'
import { VoiceModeGlyph } from '@/features/schedules/components/VoiceModeGlyph'
import { formatDateTime, formatRelativeTime } from '@/lib/formatting'
import { cn } from '@/lib/utils'
import type { MarketSchedule } from '@/features/schedules/types'
import { DataTableColumnHeader } from '@/components/DataTableColumnHeader'
import {
    sortRows,
    useSortState,
    type SortColumns,
    type SortState,
} from '@/lib/table-sort'

type StationSortKey =
    | 'station'
    | 'schedule'
    | 'voice'
    | 'operators'
    | 'status'
    | 'playout'
    | 'nextRun'
    | 'updated'

/** A schedule plus every value the table both renders and sorts on, resolved once. */
interface StationRow {
    readonly schedule: MarketSchedule
    readonly operatorNames?: readonly string[]
    readonly voiceName: string
    readonly stationLabel: string
    readonly operatorsLabel: string
}

function toStationRow(
    schedule: MarketSchedule,
    voiceNamesById: ReadonlyMap<string, string>,
    operatorsByMarket?: ReadonlyMap<string, readonly string[]>
): StationRow {
    const operatorNames = operatorsByMarket?.get(
        schedule.marketId.toLowerCase()
    )
    return {
        schedule,
        operatorNames,
        voiceName:
            voiceNamesById.get(schedule.voiceProfileId ?? '') ?? 'Default',
        stationLabel: schedule.station ?? schedule.marketId,
        // No operator map at all (non-admin) reads as unknown, an empty one as unassigned.
        operatorsLabel: operatorNames
            ? operatorNames.length > 0
                ? operatorNames.join(', ')
                : 'Unassigned'
            : '\u2014',
    }
}

const STATION_COLUMNS: SortColumns<StationRow, StationSortKey> = {
    station: { value: (row) => row.stationLabel, first: 'asc' },
    // Cron strings have no natural magnitude, but ordering them groups every station on the
    // same cadence together, which is the question this column actually gets asked.
    schedule: { value: (row) => row.schedule.cronExpression, first: 'asc' },
    voice: { value: (row) => row.voiceName, first: 'asc' },
    operators: {
        value: (row) => (row.operatorNames?.length ? row.operatorsLabel : null),
        first: 'asc',
    },
    status: { value: (row) => row.schedule.enabled, first: 'desc' },
    // Descending first surfaces the armed stations, which is the question an operator asks
    // when deciding whether this environment is the one airing a station.
    playout: { value: (row) => row.schedule.playoutEnabled, first: 'desc' },
    // Ascending first: "what airs next" is the question, and a paused station has no answer,
    // so its null sinks to the bottom either way.
    nextRun: {
        value: (row) =>
            row.schedule.enabled && row.schedule.nextRunAt
                ? Date.parse(row.schedule.nextRunAt)
                : null,
        first: 'asc',
    },
    updated: {
        value: (row) => Date.parse(row.schedule.updatedAt),
        first: 'desc',
    },
}

interface StationProfileTableProps {
    readonly schedules: readonly MarketSchedule[]
    /** Admin-only context: operator display names keyed by lowercased market id. */
    readonly operatorsByMarket?: ReadonlyMap<string, readonly string[]>
    readonly onTrigger: (schedule: MarketSchedule) => void
    readonly onView: (schedule: MarketSchedule) => void
    readonly onEdit: (schedule: MarketSchedule) => void
    readonly onDuplicate: (schedule: MarketSchedule) => void
    readonly onToggle: (schedule: MarketSchedule) => void
    readonly onTogglePlayout: (schedule: MarketSchedule) => void
    readonly onDelete: (schedule: MarketSchedule) => void
}

interface StationProfileTableSectionProps {
    readonly rows: readonly StationRow[]
    readonly sort: SortState<StationSortKey> | null
    readonly onSort: (key: StationSortKey) => void
    readonly onTrigger: (schedule: MarketSchedule) => void
    readonly onView: (schedule: MarketSchedule) => void
    readonly onEdit: (schedule: MarketSchedule) => void
    readonly onDuplicate: (schedule: MarketSchedule) => void
    readonly onToggle: (schedule: MarketSchedule) => void
    readonly onTogglePlayout: (schedule: MarketSchedule) => void
    readonly onDelete: (schedule: MarketSchedule) => void
    readonly outOfScope?: boolean
}

interface StationProfileTableRowProps {
    readonly row: StationRow
    readonly onTrigger: (schedule: MarketSchedule) => void
    readonly onView: (schedule: MarketSchedule) => void
    readonly onEdit: (schedule: MarketSchedule) => void
    readonly onDuplicate: (schedule: MarketSchedule) => void
    readonly onToggle: (schedule: MarketSchedule) => void
    readonly onTogglePlayout: (schedule: MarketSchedule) => void
    readonly onDelete: (schedule: MarketSchedule) => void
    readonly outOfScope: boolean
}

function StationProfileTableRow({
    row,
    onTrigger,
    onView,
    onEdit,
    onDuplicate,
    onToggle,
    onTogglePlayout,
    onDelete,
    outOfScope,
}: StationProfileTableRowProps) {
    const { isOpen, requestDelete, onOpenChange, confirmDelete, pendingItem } =
        useDeleteConfirm(onDelete)
    // Duplicate and Delete are Admin-only server-side. View, Edit/toggle and Trigger now
    // are not: PUT /schedules/{marketId} and POST /broadcast both resolve a market and run
    // EnsureMarketAccessAsync, so an operator reaches exactly their assigned stations and
    // `outOfScope` withholds the rest.
    const isAdmin = useIsAdmin()
    // Delivery needs BOTH gates, so the station flag alone cannot say what a publish will do.
    const { data: runtimeConfig } = useMapConfig()
    const envPlayoutEnabled = runtimeConfig?.playoutEnabled ?? true
    const { schedule, operatorNames, voiceName, stationLabel, operatorsLabel } =
        row
    const attentionIssues = getStationAttentionIssues(schedule)
    const marketLabel = schedule.name ?? schedule.marketId

    return (
        <>
            <TableRow
                key={schedule.marketId}
                onClick={() => onEdit(schedule)}
                className={cn(
                    'cursor-pointer',
                    // De-emphasis by desaturation, NOT opacity — `opacity-50` composited every
                    // string in the row toward the page background and dropped it below AA.
                    // Same fix `StationProfileCard` already carries for this state.
                    outOfScope && 'saturate-0'
                )}
            >
                <TableCell className="max-w-56 font-medium">
                    <div className="min-w-0">
                        <div className="flex min-w-0 items-center gap-1.5">
                            <span className="min-w-0 truncate">
                                {stationLabel}
                            </span>
                            <span
                                className={cn(
                                    'inline-flex size-3.5 shrink-0',
                                    attentionIssues.length === 0 && 'invisible'
                                )}
                                aria-hidden={attentionIssues.length === 0}
                            >
                                {attentionIssues.length > 0 && (
                                    <Tooltip>
                                        <TooltipTrigger asChild>
                                            <button
                                                type="button"
                                                onClick={(event) =>
                                                    event.stopPropagation()
                                                }
                                                className="flex cursor-help text-warning"
                                                aria-label="Attention needed"
                                            >
                                                <ICON.warning className="size-3.5" />
                                            </button>
                                        </TooltipTrigger>
                                        <TooltipContent>
                                            <ul className="list-disc space-y-0.5 pl-4">
                                                {attentionIssues.map(
                                                    (issue) => (
                                                        <li key={issue.key}>
                                                            {issue.label}
                                                        </li>
                                                    )
                                                )}
                                            </ul>
                                        </TooltipContent>
                                    </Tooltip>
                                )}
                            </span>
                        </div>
                        <p className="truncate text-xs font-normal text-muted-foreground">
                            {marketLabel}
                        </p>
                        <p className="truncate text-2xs font-normal text-muted-foreground-subtle">
                            {schedule.marketId}
                        </p>
                    </div>
                </TableCell>
                <TableCell className="min-w-40 text-muted-foreground">
                    <div className="flex min-w-0 flex-col gap-1">
                        <CronScheduleGlyph
                            expression={schedule.cronExpression}
                        />
                        <span className="text-xs tabular-nums">
                            {schedule.targetDurationSeconds}s target
                        </span>
                    </div>
                </TableCell>
                <TableCell className="min-w-36 text-muted-foreground">
                    <VoiceModeGlyph schedule={schedule} voiceName={voiceName} />
                </TableCell>
                <TableCell className="min-w-32 text-muted-foreground">
                    {operatorNames && operatorNames.length > 0 ? (
                        <span className="inline-flex min-w-0 items-center gap-1">
                            <Users
                                className="size-3 shrink-0 opacity-60"
                                aria-hidden
                            />
                            <span className="truncate">{operatorsLabel}</span>
                        </span>
                    ) : (
                        <span>{operatorsLabel}</span>
                    )}
                </TableCell>
                <TableCell>
                    <div className="flex items-center gap-2">
                        <span
                            className={cn(
                                'text-xs font-medium',
                                schedule.enabled
                                    ? 'text-muted-foreground'
                                    : 'text-warning'
                            )}
                        >
                            {schedule.enabled ? 'Active' : 'Paused'}
                        </span>
                        <Switch
                            checked={schedule.enabled}
                            onClick={(event) => event.stopPropagation()}
                            onCheckedChange={
                                outOfScope
                                    ? undefined
                                    : () => onToggle(schedule)
                            }
                            disabled={outOfScope}
                            aria-label={
                                outOfScope
                                    ? 'Station active (outside your assigned markets)'
                                    : 'Station active'
                            }
                        />
                    </div>
                </TableCell>
                <TableCell>
                    <div
                        className="flex items-center gap-2"
                        // The station flag alone does not say what a publish will do: delivery
                        // needs the environment gate too, and development never delivers.
                        title={
                            envPlayoutEnabled
                                ? undefined
                                : 'This environment never sends to playout'
                        }
                    >
                        <span
                            className={cn(
                                'text-xs font-medium',
                                envPlayoutEnabled && schedule.playoutEnabled
                                    ? 'text-muted-foreground'
                                    : 'text-warning'
                            )}
                        >
                            {!envPlayoutEnabled
                                ? 'Env off'
                                : schedule.playoutEnabled
                                  ? 'On'
                                  : 'Off'}
                        </span>
                        <Switch
                            checked={schedule.playoutEnabled}
                            onClick={(event) => event.stopPropagation()}
                            onCheckedChange={
                                outOfScope
                                    ? undefined
                                    : () => onTogglePlayout(schedule)
                            }
                            disabled={outOfScope}
                            aria-label={
                                outOfScope
                                    ? 'Send to playout (outside your assigned markets)'
                                    : 'Send to playout'
                            }
                        />
                    </div>
                </TableCell>
                <TableCell className="min-w-36 text-muted-foreground">
                    <ScheduleNextRun schedule={schedule} />
                </TableCell>
                <TableCell
                    className="text-muted-foreground"
                    title={formatDateTime(schedule.updatedAt)}
                >
                    {formatRelativeTime(schedule.updatedAt)}
                </TableCell>
                <TableCell
                    className="w-12 text-right"
                    onClick={(event) => event.stopPropagation()}
                >
                    <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                            <Button
                                variant="ghost"
                                size="icon"
                                className="size-8"
                                aria-label={`Actions for ${stationLabel} (${schedule.marketId})`}
                                onClick={(event) => event.stopPropagation()}
                                onKeyDown={(event) => event.stopPropagation()}
                            >
                                <ICON.more />
                            </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent
                            align="end"
                            onClick={(event) => event.stopPropagation()}
                        >
                            <DropdownMenuGroup>
                                <DropdownMenuItem
                                    onClick={() => onView(schedule)}
                                >
                                    <ICON.view />
                                    View details
                                </DropdownMenuItem>
                                <DropdownMenuItem
                                    onClick={() => onTrigger(schedule)}
                                    disabled={outOfScope}
                                    title={
                                        outOfScope
                                            ? 'Outside your assigned markets'
                                            : schedule.enabled
                                              ? undefined
                                              : 'Generates immediately, even while paused'
                                    }
                                >
                                    <ICON.urgent />
                                    Trigger now
                                </DropdownMenuItem>
                                <DropdownMenuItem
                                    onClick={() => onEdit(schedule)}
                                >
                                    <ICON.edit />
                                    Edit
                                </DropdownMenuItem>
                                {isAdmin && (
                                    <DropdownMenuItem
                                        onClick={() => onDuplicate(schedule)}
                                    >
                                        <ICON.duplicate />
                                        Duplicate
                                    </DropdownMenuItem>
                                )}
                            </DropdownMenuGroup>
                            {isAdmin && (
                                <>
                                    <DropdownMenuSeparator />
                                    <DropdownMenuItem
                                        onClick={() => requestDelete(schedule)}
                                        className="text-destructive focus:text-destructive"
                                    >
                                        <ICON.delete />
                                        Delete
                                    </DropdownMenuItem>
                                </>
                            )}
                        </DropdownMenuContent>
                    </DropdownMenu>
                </TableCell>
            </TableRow>
            <ConfirmDialog
                open={isOpen}
                onOpenChange={onOpenChange}
                onConfirm={confirmDelete}
                title="Delete station"
                // Station and name are BOTH non-unique in production — dev runs
                // two `Star 102.7 FM` markets whose names also match — so the
                // marketId is the only thing that tells the operator which
                // record this irreversible action will take.
                description={`Deleting ${pendingItem?.station ?? schedule.station ?? ''} (${pendingItem?.marketId ?? schedule.marketId}) stops automatic bulletins.`}
            />
        </>
    )
}

function StationProfileTableSection({
    rows,
    sort,
    onSort,
    onTrigger,
    onView,
    onEdit,
    onDuplicate,
    onToggle,
    onTogglePlayout,
    onDelete,
    outOfScope = false,
}: StationProfileTableSectionProps) {
    return (
        <div className="overflow-hidden rounded-lg border">
            <Table>
                <TableHeader>
                    <TableRow>
                        <DataTableColumnHeader
                            label="Station / market"
                            sortKey="station"
                            sort={sort}
                            onSort={onSort}
                        />
                        <DataTableColumnHeader
                            label="Schedule"
                            sortKey="schedule"
                            sort={sort}
                            onSort={onSort}
                        />
                        <DataTableColumnHeader
                            label="Voice"
                            sortKey="voice"
                            sort={sort}
                            onSort={onSort}
                        />
                        <DataTableColumnHeader
                            label="Operators"
                            sortKey="operators"
                            sort={sort}
                            onSort={onSort}
                        />
                        <DataTableColumnHeader
                            label="Status"
                            sortKey="status"
                            sort={sort}
                            onSort={onSort}
                        />
                        <DataTableColumnHeader
                            label="Playout"
                            sortKey="playout"
                            sort={sort}
                            onSort={onSort}
                        />
                        <DataTableColumnHeader
                            label="Next run"
                            sortKey="nextRun"
                            sort={sort}
                            onSort={onSort}
                        />
                        <DataTableColumnHeader
                            label="Updated"
                            sortKey="updated"
                            sort={sort}
                            onSort={onSort}
                        />
                        <TableHead className="w-12 text-right">
                            <span className="sr-only">Actions</span>
                        </TableHead>
                    </TableRow>
                </TableHeader>
                <TableBody>
                    {rows.map((row) => (
                        <StationProfileTableRow
                            key={row.schedule.marketId}
                            row={row}
                            onTrigger={onTrigger}
                            onView={onView}
                            onEdit={onEdit}
                            onDuplicate={onDuplicate}
                            onToggle={onToggle}
                            onTogglePlayout={onTogglePlayout}
                            onDelete={onDelete}
                            outOfScope={outOfScope}
                        />
                    ))}
                </TableBody>
            </Table>
        </div>
    )
}

export function StationProfileTable({
    schedules,
    operatorsByMarket,
    onTrigger,
    onView,
    onEdit,
    onDuplicate,
    onToggle,
    onTogglePlayout,
    onDelete,
}: StationProfileTableProps) {
    const { inScope, outOfScope } = useScopedSchedules(schedules)
    const { data: voiceProfiles } = useVoiceProfiles()

    const voiceNamesById = useMemo(
        () =>
            new Map(
                (voiceProfiles?.profiles ?? []).map((profile) => [
                    profile.id,
                    profile.name,
                ])
            ),
        [voiceProfiles]
    )
    // One sort drives both scope sections, so the two tables never disagree about ordering.
    const { sort, onSort } = useSortState(STATION_COLUMNS, 'stations')

    const inScopeRows = useMemo(
        () =>
            sortRows(
                inScope.map((schedule) =>
                    toStationRow(schedule, voiceNamesById, operatorsByMarket)
                ),
                sort,
                STATION_COLUMNS
            ),
        [inScope, voiceNamesById, operatorsByMarket, sort]
    )

    const outOfScopeRows = useMemo(
        () =>
            sortRows(
                outOfScope.map((schedule) =>
                    toStationRow(schedule, voiceNamesById, operatorsByMarket)
                ),
                sort,
                STATION_COLUMNS
            ),
        [outOfScope, voiceNamesById, operatorsByMarket, sort]
    )

    return (
        <div className="flex flex-col gap-6">
            <StationProfileTableSection
                rows={inScopeRows}
                sort={sort}
                onSort={onSort}
                onTrigger={onTrigger}
                onView={onView}
                onEdit={onEdit}
                onDuplicate={onDuplicate}
                onToggle={onToggle}
                onTogglePlayout={onTogglePlayout}
                onDelete={onDelete}
            />

            {outOfScopeRows.length > 0 && (
                <div>
                    <p className="mb-3 text-xs font-medium text-muted-foreground-subtle uppercase tracking-wide">
                        Outside your markets
                    </p>
                    <StationProfileTableSection
                        rows={outOfScopeRows}
                        sort={sort}
                        onSort={onSort}
                        onTrigger={onTrigger}
                        onView={onView}
                        onEdit={onEdit}
                        onDuplicate={onDuplicate}
                        onToggle={onToggle}
                        onTogglePlayout={onTogglePlayout}
                        onDelete={onDelete}
                        outOfScope
                    />
                </div>
            )}
        </div>
    )
}
