import { useMemo } from 'react'
import { Badge } from '@/components/ui/badge'
import { EntityActionsMenu } from '@/components/EntityActionsMenu'
import {
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
} from '@/components/ui/table'
import { DataTableColumnHeader } from '@/components/DataTableColumnHeader'
import { formatDateTime, formatRelativeTime } from '@/lib/formatting'
import { useTableSort } from '@/lib/table-sort'
import type { SortColumns } from '@/lib/table-sort'
import { sanitizeTags } from '../cues'
import { strengthLabel, toStrength } from '../strength'
import type { DeliveryStyle } from '../types'

interface DeliveryStyleTableProps {
    readonly styles: readonly DeliveryStyle[]
    readonly assignmentsByStyleId: ReadonlyMap<string, readonly string[]>
    readonly onView: (styleId: string) => void
    readonly onEdit: (styleId: string) => void
    readonly onDuplicate: (style: DeliveryStyle) => void
    readonly onDelete: (style: DeliveryStyle) => void
    readonly onArchive: (style: DeliveryStyle) => void
    readonly onUnarchive: (style: DeliveryStyle) => void
    /** False for an operator: everything else is theirs, but DELETE stays Admin. */
    readonly canDelete?: boolean
}

/**
 * Table view of the delivery library — the data-dense sibling of the
 * `DeliveryStyleCard` gallery. Rows open View details (mirrored as the first
 * kebab item so keyboard users reach it without a nested-interactive click
 * target), destructive Delete confirms through the page's dialog.
 * Columns are sortable by clicking the header.
 */
export function DeliveryStyleTable({
    styles,
    assignmentsByStyleId,
    onView,
    onEdit,
    onDuplicate,
    onDelete,
    onArchive,
    onUnarchive,
    canDelete = true,
}: DeliveryStyleTableProps) {
    interface DeliveryRow {
        readonly style: DeliveryStyle
        readonly cueCount: number
        readonly intensity: number
        readonly strength: string
        readonly stationCount: number
        readonly stationLabel: string
    }

    type DeliverySortKey = 'name' | 'strength' | 'cues' | 'stations' | 'updated'

    const decorated = useMemo<readonly DeliveryRow[]>(
        () =>
            styles.map((style) => {
                const policy = style.versions.at(-1)?.policy
                const intensity = policy?.intensity ?? 2
                const cueCount = policy
                    ? sanitizeTags(policy.allowedTags).length
                    : 0
                const stations = assignmentsByStyleId.get(style.id) ?? []
                return {
                    style,
                    cueCount,
                    intensity,
                    strength: strengthLabel(toStrength(intensity)),
                    stationCount: stations.length,
                    stationLabel:
                        stations.length === 0
                            ? 'Unassigned'
                            : `${stations.length} station${stations.length === 1 ? '' : 's'}`,
                }
            }),
        [styles, assignmentsByStyleId]
    )

    const columns = useMemo<SortColumns<DeliveryRow, DeliverySortKey>>(
        () => ({
            name: { value: (r) => r.style.name, first: 'asc' },
            strength: { value: (r) => r.intensity, first: 'desc' },
            cues: { value: (r) => r.cueCount, first: 'desc' },
            stations: { value: (r) => r.stationCount, first: 'desc' },
            updated: {
                value: (r) => Date.parse(r.style.updatedAt),
                first: 'desc',
            },
        }),
        []
    )

    const { rows, sort, onSort } = useTableSort(decorated, columns, 'delivery')
    return (
        <div className="overflow-hidden rounded-lg border">
            <Table>
                <TableHeader>
                    <TableRow>
                        <DataTableColumnHeader
                            label="Name"
                            sortKey="name"
                            sort={sort}
                            onSort={onSort}
                        />
                        <DataTableColumnHeader
                            label="Strength"
                            sortKey="strength"
                            sort={sort}
                            onSort={onSort}
                        />
                        <DataTableColumnHeader
                            label="Cues"
                            sortKey="cues"
                            sort={sort}
                            onSort={onSort}
                            align="right"
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
                        const { style } = row

                        return (
                            <TableRow
                                key={style.id}
                                onClick={() => onView(style.id)}
                                className="cursor-pointer"
                            >
                                <TableCell className="font-medium">
                                    <span className="flex items-center gap-2">
                                        <span className="truncate">
                                            {style.name}
                                        </span>
                                        {style.status === 'archived' && (
                                            <Badge variant="muted">
                                                Archived
                                            </Badge>
                                        )}
                                    </span>
                                </TableCell>
                                <TableCell className="text-muted-foreground">
                                    {row.strength}
                                </TableCell>
                                <TableCell className="text-right tabular-nums text-muted-foreground">
                                    {row.cueCount}
                                </TableCell>
                                <TableCell className="text-muted-foreground">
                                    {row.stationLabel}
                                </TableCell>
                                <TableCell
                                    className="text-muted-foreground"
                                    title={formatDateTime(style.updatedAt)}
                                >
                                    {formatRelativeTime(style.updatedAt)}
                                </TableCell>
                                <TableCell
                                    className="text-right"
                                    onClick={(e) => e.stopPropagation()}
                                >
                                    <EntityActionsMenu
                                        onView={() => onView(style.id)}
                                        onEdit={() => onEdit(style.id)}
                                        onDuplicate={() => onDuplicate(style)}
                                        onDelete={
                                            canDelete
                                                ? () => onDelete(style)
                                                : undefined
                                        }
                                        onArchive={
                                            style.status === 'archived'
                                                ? undefined
                                                : () => onArchive(style)
                                        }
                                        onUnarchive={
                                            style.status === 'archived'
                                                ? () => onUnarchive(style)
                                                : undefined
                                        }
                                        ariaLabel={`Actions for ${style.name}`}
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
