import { Info } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { Button } from '@/components/ui/button'
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuGroup,
    DropdownMenuItem,
    DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Badge } from '@/components/ui/badge'
import {
    formatDateTime,
    formatMarketName,
    formatRelativeTime,
} from '@/lib/formatting'
import {
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
} from '@/components/ui/table'
import type { OperatorDirectory, OperatorDirectoryEntry } from '../api/schemas'
import {
    MISCONFIGURED_NOTICE,
    RETAINED_ROW_NOTICE,
    ROLE_UNKNOWN_NOTICE,
    SIGN_IN_ROLE_NOTICE,
    formatSource,
} from '../lib/access-copy'
import { useMemo } from 'react'
import { DataTableColumnHeader } from '@/components/DataTableColumnHeader'
import { useTableSort } from '@/lib/table-sort'
import type { SortColumns } from '@/lib/table-sort'

interface OperatorAssignmentTableProps {
    readonly entries: readonly OperatorDirectoryEntry[]
    /** Directory read state used to distinguish unknown roles from defects. */
    readonly directoryStatus: OperatorDirectory['directoryStatus']
    /** marketId (lowercased) → friendly label, e.g. "2GB · Sydney Breakfast". */
    readonly marketLabels?: ReadonlyMap<string, string>
    readonly onEdit: (entry: OperatorDirectoryEntry) => void
}

/**
 * Table view of live Entra directory entries and their station access.
 * Rows open the station-access editor for non-admin entries. Every sortable
 * column keeps the data-dense view usable for a large directory.
 */
export function OperatorAssignmentTable({
    entries,
    directoryStatus,
    marketLabels,
    onEdit,
}: OperatorAssignmentTableProps) {
    interface OperatorRow {
        readonly entry: OperatorDirectoryEntry
        readonly displayName: string
        /** Market id + resolved display label; the id is the record identity, the label is not. */
        readonly markets: readonly {
            readonly marketId: string
            readonly label: string
        }[]
        readonly updated: number
    }

    type OperatorSortKey = 'operator' | 'roles' | 'access' | 'updated'

    const decorated = useMemo<readonly OperatorRow[]>(
        () =>
            entries.map((entry) => ({
                entry,
                displayName:
                    entry.displayName ||
                    entry.userPrincipalName ||
                    'Unknown operator',
                markets: entry.markets.map((market) => ({
                    marketId: market,
                    label:
                        marketLabels?.get(market.toLowerCase()) ??
                        formatMarketName(market),
                })),
                updated: entry.updatedAtUtc
                    ? Date.parse(entry.updatedAtUtc)
                    : Number.NEGATIVE_INFINITY,
            })),
        [entries, marketLabels]
    )

    const columns = useMemo<SortColumns<OperatorRow, OperatorSortKey>>(
        () => ({
            operator: { value: (row) => row.displayName, first: 'asc' },
            roles: {
                value: (row) => row.entry.roles.join(', '),
                first: 'asc',
            },
            access: { value: (row) => row.markets.length, first: 'desc' },
            updated: {
                value: (row) => row.updated,
                first: 'desc',
            },
        }),
        []
    )

    const { rows, sort, onSort } = useTableSort(decorated, columns, 'operators')

    return (
        <div className="overflow-hidden rounded-lg border">
            <Table>
                <TableHeader>
                    <TableRow>
                        <DataTableColumnHeader
                            label="Operator"
                            sortKey="operator"
                            sort={sort}
                            onSort={onSort}
                        />
                        <DataTableColumnHeader
                            label="Role / source"
                            sortKey="roles"
                            sort={sort}
                            onSort={onSort}
                        />
                        <DataTableColumnHeader
                            label="Stations"
                            sortKey="access"
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
                        const { entry, displayName, markets } = row
                        const isEditable = !entry.isAdmin
                        const isDirectorySource =
                            entry.source === 'Direct' ||
                            entry.source.startsWith('Group:')
                        const hasDirectoryRole =
                            isDirectorySource && entry.roles.length > 0
                        const isDirectoryMisconfigured =
                            isDirectorySource && entry.roles.length === 0
                        const isStoredAssignment = entry.source === 'Assignment'
                        // Roles on a stored row are what the person's own token presented at
                        // their last sign-in — worth showing, but never dressed up as a
                        // directory read.
                        const hasSignInRole =
                            isStoredAssignment && entry.roles.length > 0

                        return (
                            <TableRow
                                key={entry.objectId}
                                onClick={() => {
                                    if (isEditable) onEdit(entry)
                                }}
                                className={
                                    isEditable ? 'cursor-pointer' : undefined
                                }
                            >
                                <TableCell>
                                    <div className="min-w-0">
                                        <div className="truncate font-medium">
                                            {displayName}
                                        </div>
                                        <div className="truncate font-mono text-xs text-muted-foreground">
                                            {entry.userPrincipalName ??
                                                'UPN unavailable'}
                                        </div>
                                    </div>
                                </TableCell>
                                <TableCell>
                                    <div className="flex flex-wrap items-center gap-1.5">
                                        {hasDirectoryRole || hasSignInRole ? (
                                            entry.roles.map((role) => (
                                                <Badge
                                                    key={role}
                                                    variant={
                                                        role === 'Admin'
                                                            ? 'default'
                                                            : 'secondary'
                                                    }
                                                    title={
                                                        hasSignInRole
                                                            ? 'Recorded from this person’s own token at their last sign-in'
                                                            : undefined
                                                    }
                                                >
                                                    {role}
                                                </Badge>
                                            ))
                                        ) : isDirectoryMisconfigured ? (
                                            <Badge variant="warning">
                                                Misconfigured
                                            </Badge>
                                        ) : isStoredAssignment ? (
                                            <Badge
                                                variant={
                                                    directoryStatus === 'Ok'
                                                        ? 'warning'
                                                        : 'secondary'
                                                }
                                            >
                                                Role unknown
                                            </Badge>
                                        ) : null}
                                    </div>
                                    <div className="mt-1 truncate text-xs text-muted-foreground">
                                        From {formatSource(entry.source)}
                                    </div>
                                    {isDirectoryMisconfigured && (
                                        <div
                                            role="alert"
                                            className="mt-1 flex items-center gap-1 text-xs text-warning"
                                        >
                                            <ICON.warning
                                                className="size-3 shrink-0"
                                                aria-hidden
                                            />
                                            {MISCONFIGURED_NOTICE}
                                        </div>
                                    )}
                                    {/* Steady-state silence: see the note in OperatorAssignmentCard. */}
                                    {isStoredAssignment &&
                                        !(
                                            hasSignInRole &&
                                            directoryStatus !== 'Ok'
                                        ) && (
                                            <div
                                                role={
                                                    directoryStatus === 'Ok'
                                                        ? 'alert'
                                                        : 'status'
                                                }
                                                className={
                                                    directoryStatus === 'Ok'
                                                        ? 'mt-1 flex items-start gap-1 text-xs text-warning'
                                                        : 'mt-1 flex items-start gap-1 text-xs text-muted-foreground'
                                                }
                                            >
                                                {directoryStatus === 'Ok' ? (
                                                    <ICON.warning
                                                        className="mt-0.5 size-3 shrink-0"
                                                        aria-hidden
                                                    />
                                                ) : (
                                                    <Info
                                                        className="mt-0.5 size-3 shrink-0"
                                                        aria-hidden
                                                    />
                                                )}
                                                <span>
                                                    {directoryStatus === 'Ok'
                                                        ? RETAINED_ROW_NOTICE
                                                        : hasSignInRole
                                                          ? SIGN_IN_ROLE_NOTICE
                                                          : ROLE_UNKNOWN_NOTICE}
                                                </span>
                                            </div>
                                        )}
                                </TableCell>
                                <TableCell className="text-muted-foreground">
                                    {entry.isAdmin ? (
                                        <span className="font-medium text-primary-on-tint">
                                            All stations (admin)
                                        </span>
                                    ) : markets.length === 0 ? (
                                        <span className="inline-flex items-center rounded-md border border-border bg-muted px-2.5 py-1 text-xs text-muted-foreground">
                                            All stations (unscoped)
                                        </span>
                                    ) : (
                                        <div className="flex flex-wrap gap-1.5">
                                            {markets.map(
                                                ({ marketId, label }) => (
                                                    <span
                                                        key={marketId}
                                                        className="max-w-60 truncate rounded-md border border-border/50 bg-background/40 px-2.5 py-1 text-xs"
                                                    >
                                                        {label}
                                                    </span>
                                                )
                                            )}
                                        </div>
                                    )}
                                </TableCell>
                                <TableCell
                                    className="whitespace-nowrap text-muted-foreground"
                                    title={
                                        entry.updatedAtUtc
                                            ? formatDateTime(entry.updatedAtUtc)
                                            : undefined
                                    }
                                >
                                    {entry.updatedAtUtc
                                        ? formatRelativeTime(entry.updatedAtUtc)
                                        : '-'}
                                </TableCell>
                                <TableCell
                                    className="text-right"
                                    onClick={(event) => event.stopPropagation()}
                                >
                                    {isEditable && (
                                        <DropdownMenu>
                                            <DropdownMenuTrigger asChild>
                                                <Button
                                                    variant="ghost"
                                                    size="icon"
                                                    className="size-8"
                                                    aria-label={`Actions for ${displayName}`}
                                                    onClick={(event) =>
                                                        event.stopPropagation()
                                                    }
                                                    onKeyDown={(event) =>
                                                        event.stopPropagation()
                                                    }
                                                >
                                                    <ICON.more />
                                                </Button>
                                            </DropdownMenuTrigger>
                                            <DropdownMenuContent
                                                align="end"
                                                onClick={(event) =>
                                                    event.stopPropagation()
                                                }
                                            >
                                                <DropdownMenuGroup>
                                                    <DropdownMenuItem
                                                        onClick={() =>
                                                            onEdit(entry)
                                                        }
                                                    >
                                                        <ICON.edit />
                                                        Edit station access
                                                    </DropdownMenuItem>
                                                </DropdownMenuGroup>
                                            </DropdownMenuContent>
                                        </DropdownMenu>
                                    )}
                                </TableCell>
                            </TableRow>
                        )
                    })}
                </TableBody>
            </Table>
        </div>
    )
}
