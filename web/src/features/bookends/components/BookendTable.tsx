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
import { extractTokensAcross } from '../lib/tokens'
import type { Bookend } from '../types'

interface BookendTableProps {
    readonly bookends: readonly Bookend[]
    readonly assignmentsByBookendId: ReadonlyMap<string, readonly string[]>
    readonly onView: (bookendId: string) => void
    readonly onEdit: (bookendId: string) => void
    readonly onDuplicate: (bookend: Bookend) => void
    readonly onDelete: (bookend: Bookend) => void
    readonly onArchive: (bookend: Bookend) => void
    readonly onUnarchive: (bookend: Bookend) => void
    /** False for an operator: everything else is theirs, but DELETE stays Admin. */
    readonly canDelete?: boolean
}

/**
 * Table view of the bookend library — the data-dense sibling of the
 * `BookendCard` gallery. Rows open View details (mirrored as the first
 * kebab item so keyboard users reach it without a nested-interactive click
 * target), destructive Delete confirms through the page's dialog.
 * Columns are sortable by clicking the header.
 */
export function BookendTable({
    bookends,
    assignmentsByBookendId,
    onView,
    onEdit,
    onDuplicate,
    onDelete,
    onArchive,
    onUnarchive,
    canDelete = true,
}: BookendTableProps) {
    interface BookendRow {
        readonly bookend: Bookend
        readonly tokenCount: number
        readonly stationCount: number
        readonly stationLabel: string
    }

    type BookendSortKey = 'name' | 'tokens' | 'stations' | 'updated'

    const decorated = useMemo<readonly BookendRow[]>(
        () =>
            bookends.map((bookend) => {
                const tokenCount =
                    bookend.tokens.length > 0
                        ? bookend.tokens.length
                        : extractTokensAcross([
                              bookend.introText,
                              bookend.outroText,
                          ]).length
                const stations = assignmentsByBookendId.get(bookend.id) ?? []
                return {
                    bookend,
                    tokenCount,
                    stationCount: stations.length,
                    stationLabel:
                        stations.length === 0
                            ? 'Unassigned'
                            : `${stations.length} station${stations.length === 1 ? '' : 's'}`,
                }
            }),
        [bookends, assignmentsByBookendId]
    )

    const columns = useMemo<SortColumns<BookendRow, BookendSortKey>>(
        () => ({
            name: { value: (r) => r.bookend.name, first: 'asc' },
            tokens: { value: (r) => r.tokenCount, first: 'desc' },
            stations: { value: (r) => r.stationCount, first: 'desc' },
            updated: {
                value: (r) => Date.parse(r.bookend.updatedAt),
                first: 'desc',
            },
        }),
        []
    )

    const { rows, sort, onSort } = useTableSort(decorated, columns, 'bookends')
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
                            label="Tokens"
                            sortKey="tokens"
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
                        const { bookend } = row

                        return (
                            <TableRow
                                key={bookend.id}
                                onClick={() => onView(bookend.id)}
                                className="cursor-pointer"
                            >
                                <TableCell className="font-medium">
                                    <span className="flex items-center gap-2">
                                        <span className="truncate">
                                            {bookend.name}
                                        </span>
                                        {bookend.status === 'archived' && (
                                            <Badge variant="muted">
                                                Archived
                                            </Badge>
                                        )}
                                    </span>
                                </TableCell>
                                <TableCell className="text-right tabular-nums text-muted-foreground">
                                    {row.tokenCount}
                                </TableCell>
                                <TableCell className="text-muted-foreground">
                                    {row.stationLabel}
                                </TableCell>
                                <TableCell
                                    className="text-muted-foreground"
                                    title={formatDateTime(bookend.updatedAt)}
                                >
                                    {formatRelativeTime(bookend.updatedAt)}
                                </TableCell>
                                <TableCell
                                    className="text-right"
                                    onClick={(e) => e.stopPropagation()}
                                >
                                    <EntityActionsMenu
                                        onView={() => onView(bookend.id)}
                                        onEdit={() => onEdit(bookend.id)}
                                        onDuplicate={() => onDuplicate(bookend)}
                                        onDelete={
                                            canDelete
                                                ? () => onDelete(bookend)
                                                : undefined
                                        }
                                        onArchive={
                                            bookend.status === 'archived'
                                                ? undefined
                                                : () => onArchive(bookend)
                                        }
                                        onUnarchive={
                                            bookend.status === 'archived'
                                                ? () => onUnarchive(bookend)
                                                : undefined
                                        }
                                        ariaLabel={`Actions for ${bookend.name}`}
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
