import { Badge } from '@/components/ui/badge'
import { useMemo } from 'react'
import { DataTableColumnHeader } from '@/components/DataTableColumnHeader'
import { useTableSort } from '@/lib/table-sort'
import type { SortColumns } from '@/lib/table-sort'
import { EntityActionsMenu } from '@/components/EntityActionsMenu'
import {
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
} from '@/components/ui/table'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { formatDateTime, formatRelativeTime } from '@/lib/formatting'
import {
    currentVersion,
    wordTargetForDuration,
} from '../lib/voice-profile-utils'
import {
    getModelDisplayName,
    getModelShortLabel,
} from '../lib/model-capabilities'
import type {
    VoiceCalibration,
    VoiceProfile,
    VoiceProfileVersion,
} from '../profile-types'

/** Airtime the calibration WPM is reported against on cards. */
const SLOT_SECONDS = 30

interface VoiceProfileTableProps {
    readonly profiles: readonly VoiceProfile[]
    /** voiceProfileId -> station labels, derived from schedules (the wire
     *  DTO's `assignedStations` is never populated). */
    readonly assignmentsByProfileId: ReadonlyMap<string, readonly string[]>
    readonly onView: (id: string) => void
    readonly onEdit: (id: string) => void
    readonly onDuplicate: (profile: VoiceProfile) => void
    readonly onDelete: (profile: VoiceProfile) => void
    readonly onArchive: (profile: VoiceProfile) => void
    readonly onUnarchive: (profile: VoiceProfile) => void
    /** False for an operator: everything else is theirs, but DELETE stays Admin. */
    readonly canDelete?: boolean
}

/**
 * Sortable table view of the voice library — the data-dense sibling of the
 * `VoiceVariantCard` gallery. Rows open View details, mirrored as the first
 * kebab item for keyboard users; destructive Delete confirms through the
 * page's dialog. All columns are sortable via the shared sort layer.
 */
type VoiceSortKey = 'name' | 'pace' | 'proven' | 'stations' | 'updated'

interface VoiceRow {
    readonly profile: VoiceProfile
    readonly version: VoiceProfileVersion
    readonly calibration: VoiceCalibration | null
    readonly stationCount: number
    readonly wpm: number | null
    readonly runCount: number
    readonly hasAired: boolean
}

export function VoiceProfileTable({
    profiles,
    assignmentsByProfileId,
    onView,
    onEdit,
    onDuplicate,
    onDelete,
    onArchive,
    onUnarchive,
    canDelete = true,
}: VoiceProfileTableProps) {
    const decorated = useMemo<readonly VoiceRow[]>(
        () =>
            profiles.map((profile) => {
                const version = currentVersion(profile)
                const calibration = version.calibration
                const stationCount =
                    assignmentsByProfileId.get(profile.id)?.length ?? 0
                const wpm = calibration
                    ? Math.round(calibration.wordsPerMinute)
                    : null
                const runCount = calibration?.runCount ?? 0
                const hasAired = calibration !== null && runCount > 0

                return {
                    profile,
                    version,
                    calibration,
                    stationCount,
                    wpm,
                    runCount,
                    hasAired,
                }
            }),
        [profiles, assignmentsByProfileId]
    )

    const columns = useMemo<SortColumns<VoiceRow, VoiceSortKey>>(
        () => ({
            name: { value: (r) => r.profile.name, first: 'asc' },
            pace: {
                value: (r) => r.wpm,
                first: 'desc',
            },
            proven: {
                value: (r) => r.runCount,
                first: 'desc',
            },
            stations: {
                value: (r) => r.stationCount,
                first: 'desc',
            },
            updated: {
                value: (r) => Date.parse(r.profile.updatedAt),
                first: 'desc',
            },
        }),
        []
    )

    const { rows, sort, onSort } = useTableSort(decorated, columns, 'voices')

    return (
        <div className="overflow-hidden rounded-lg border">
            <Table>
                <TableHeader>
                    <TableRow>
                        <DataTableColumnHeader
                            label="Voice"
                            sortKey="name"
                            sort={sort}
                            onSort={onSort}
                        />
                        <DataTableColumnHeader
                            label="Pace"
                            sortKey="pace"
                            sort={sort}
                            onSort={onSort}
                        />
                        <DataTableColumnHeader
                            label="Proven"
                            sortKey="proven"
                            sort={sort}
                            onSort={onSort}
                        />
                        <DataTableColumnHeader
                            label="Stations"
                            sortKey="stations"
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
                    {rows.map((row) => {
                        return (
                            <TableRow
                                key={row.profile.id}
                                onClick={() => onView(row.profile.id)}
                                className="cursor-pointer"
                            >
                                <TableCell className="font-medium">
                                    <div className="min-w-0">
                                        <span className="flex items-center gap-2">
                                            <span className="truncate">
                                                {row.profile.name}
                                            </span>
                                            {row.profile.status ===
                                                'archived' && (
                                                <Badge variant="muted">
                                                    Archived
                                                </Badge>
                                            )}
                                        </span>
                                        <span
                                            className="mt-1 flex items-center gap-1.5 text-xs text-muted-foreground"
                                            title={row.profile.baseVoiceName}
                                        >
                                            <span
                                                className="shrink-0 rounded bg-primary/10 px-1.5 py-px text-3xs font-semibold uppercase tracking-wide text-primary-on-tint"
                                                title={`ElevenLabs model: ${getModelDisplayName(row.version.model)}`}
                                            >
                                                {getModelShortLabel(
                                                    row.version.model
                                                )}
                                            </span>
                                            <span className="truncate">
                                                {row.profile.baseVoiceName}
                                            </span>
                                        </span>
                                    </div>
                                </TableCell>
                                <TableCell>
                                    {row.wpm === null ? (
                                        <span className="text-muted-foreground">
                                            Not aired yet
                                        </span>
                                    ) : (
                                        <span className="inline-flex items-baseline gap-1.5 tabular-nums">
                                            <span className="font-medium">
                                                {row.wpm}
                                            </span>
                                            <span className="text-xs text-muted-foreground">
                                                wpm
                                            </span>
                                            <span className="text-xs text-muted-foreground">
                                                (~
                                                {wordTargetForDuration(
                                                    row.wpm,
                                                    SLOT_SECONDS
                                                )}
                                                /{SLOT_SECONDS}s)
                                            </span>
                                            {row.version.paceBoostWpm > 0 && (
                                                <Tooltip>
                                                    <TooltipTrigger asChild>
                                                        <span className="cursor-help rounded-full bg-primary/10 px-1.5 py-px text-3xs font-semibold text-primary-on-tint">
                                                            +
                                                            {
                                                                row.version
                                                                    .paceBoostWpm
                                                            }
                                                        </span>
                                                    </TooltipTrigger>
                                                    <TooltipContent>
                                                        Delivery pace boost:
                                                        script is sized{' '}
                                                        {
                                                            row.version
                                                                .paceBoostWpm
                                                        }{' '}
                                                        wpm faster, then
                                                        compressed to fit the
                                                        slot
                                                    </TooltipContent>
                                                </Tooltip>
                                            )}
                                        </span>
                                    )}
                                </TableCell>
                                <TableCell title="Real broadcasts this voice's pace is measured from; the date is when it was last calibrated.">
                                    <span className="tabular-nums font-medium">
                                        {row.runCount}
                                    </span>
                                    <span className="ml-1 text-xs text-muted-foreground">
                                        airing{row.runCount === 1 ? '' : 's'}
                                    </span>
                                    <div className="text-xs text-muted-foreground">
                                        {row.hasAired &&
                                        row.calibration?.calibratedAt
                                            ? formatRelativeTime(
                                                  row.calibration.calibratedAt
                                              )
                                            : 'Not aired yet'}
                                    </div>
                                </TableCell>
                                <TableCell
                                    title={
                                        row.stationCount > 0
                                            ? `On air at: ${(
                                                  assignmentsByProfileId.get(
                                                      row.profile.id
                                                  ) ?? []
                                              ).join(', ')}`
                                            : 'Not assigned to any station'
                                    }
                                >
                                    {row.stationCount > 0 ? (
                                        <>
                                            <span className="tabular-nums font-medium">
                                                {row.stationCount}
                                            </span>
                                            <span className="ml-1 text-xs text-muted-foreground">
                                                station
                                                {row.stationCount === 1
                                                    ? ''
                                                    : 's'}
                                            </span>
                                        </>
                                    ) : (
                                        <span className="text-muted-foreground">
                                            Unassigned
                                        </span>
                                    )}
                                </TableCell>
                                <TableCell
                                    className="text-muted-foreground"
                                    title={formatDateTime(
                                        row.profile.updatedAt
                                    )}
                                >
                                    v{row.profile.currentVersion} · updated{' '}
                                    {formatRelativeTime(row.profile.updatedAt)}
                                </TableCell>
                                <TableCell
                                    className="text-right"
                                    onClick={(e) => e.stopPropagation()}
                                >
                                    <EntityActionsMenu
                                        onView={() => onView(row.profile.id)}
                                        onEdit={() => onEdit(row.profile.id)}
                                        onDuplicate={() =>
                                            onDuplicate(row.profile)
                                        }
                                        onDelete={
                                            canDelete
                                                ? () => onDelete(row.profile)
                                                : undefined
                                        }
                                        onArchive={
                                            row.profile.status === 'archived'
                                                ? undefined
                                                : () => onArchive(row.profile)
                                        }
                                        onUnarchive={
                                            row.profile.status === 'archived'
                                                ? () => onUnarchive(row.profile)
                                                : undefined
                                        }
                                        ariaLabel={`Actions for ${row.profile.name}`}
                                    />
                                </TableCell>
                            </TableRow>
                        )
                    })}
                </TableBody>
            </Table>
        </div>
    )
}
