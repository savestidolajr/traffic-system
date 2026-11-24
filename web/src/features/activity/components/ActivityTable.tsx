import { Fragment, useMemo, useState } from 'react'
import { ChevronRight, ScrollText } from 'lucide-react'
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
import { Placeholder } from '@/components/Placeholder'
import { EntityDiffView } from '@/components/EntityDiffView'
import { diffEntities } from '@/lib/entity-diff'
import { tryParseJson } from '@/lib/json'
import { formatDateTime } from '@/lib/formatting'
import { cn } from '@/lib/utils'
import { toggleSort, type SortState } from '@/lib/table-sort'
import type { ActivityLogEntry } from '../api/activity-schemas'
import { formatActivityAction } from '../activity-format'

type SortKey = 'atUtc' | 'actorName' | 'resource'

const DEFAULT_SORT: SortState<SortKey> = { key: 'atUtc', direction: 'desc' }

function sortEntries(
    entries: readonly ActivityLogEntry[],
    sort: SortState<SortKey>
): ActivityLogEntry[] {
    const dir = sort.direction === 'asc' ? 1 : -1
    return [...entries].sort((a, b) => {
        switch (sort.key) {
            case 'atUtc':
                return dir * (Date.parse(a.atUtc) - Date.parse(b.atUtc))
            case 'actorName':
                return dir * a.actorName.localeCompare(b.actorName)
            case 'resource':
                return (
                    dir *
                    `${a.resource}/${a.entityId ?? ''}`.localeCompare(
                        `${b.resource}/${b.entityId ?? ''}`
                    )
                )
        }
    })
}

interface ActivityTableProps {
    readonly entries: readonly ActivityLogEntry[]
    /** Per-entity view (one resource + entityId): consecutive entries in default time
     *  order share a lineage, so each one's submitted body can be diffed against the
     *  entry right after it (chronologically older). A flat non-default sort breaks that
     *  adjacency, so diffing only applies while the table sits at the default sort. */
    readonly diffable: boolean
    readonly hasMore: boolean
    readonly onLoadMore: () => void
    readonly isLoadingMore: boolean
}

export function ActivityTable({
    entries,
    diffable,
    hasMore,
    onLoadMore,
    isLoadingMore,
}: ActivityTableProps) {
    const [sort, setSort] = useState<SortState<SortKey>>(DEFAULT_SORT)
    const [expanded, setExpanded] = useState<string | null>(null)

    const isDefaultOrder =
        sort.key === DEFAULT_SORT.key &&
        sort.direction === DEFAULT_SORT.direction
    // Diffing needs the original newest-first order (each row diffed against the very
    // next, chronologically-older entry) — an explicit re-sort forfeits it, matching how
    // the History table forfeits day-grouping the moment it leaves the default sort.
    const canDiff = diffable && isDefaultOrder

    const rows = useMemo(
        () => (isDefaultOrder ? entries : sortEntries(entries, sort)),
        [entries, sort, isDefaultOrder]
    )

    if (entries.length === 0) {
        return (
            <Placeholder
                icon={ScrollText}
                message="No activity recorded"
                detail="Changes to platform config will appear here once made."
            />
        )
    }

    const handleSort = (key: SortKey) =>
        setSort((current) => toggleSort(current, key))

    return (
        <div className="flex flex-col gap-3">
            {/* Desktop table — hidden below md */}
            <div className="hidden md:block">
                <div className="rounded-lg border">
                    <Table>
                        <TableHeader>
                            <TableRow>
                                <TableHead className="w-8">
                                    <span className="sr-only">Actions</span>
                                </TableHead>
                                <DataTableColumnHeader
                                    label="Time"
                                    sortKey="atUtc"
                                    sort={sort}
                                    onSort={handleSort}
                                />
                                <DataTableColumnHeader
                                    label="Actor"
                                    sortKey="actorName"
                                    sort={sort}
                                    onSort={handleSort}
                                />
                                <DataTableColumnHeader
                                    label="Action"
                                    sortKey="resource"
                                    sort={sort}
                                    onSort={handleSort}
                                />
                            </TableRow>
                        </TableHeader>
                        <TableBody>
                            {rows.map((entry, index) => {
                                const rowKey = `${entry.atUtc}-${entry.actorObjectId}-${index}`
                                const isOpen = expanded === rowKey

                                const older = canDiff
                                    ? entries[index + 1]
                                    : undefined
                                const before = older
                                    ? tryParseJson(older.requestBodyJson)
                                    : null
                                const after = tryParseJson(
                                    entry.requestBodyJson
                                )
                                const showDiff =
                                    canDiff && before !== null && after !== null

                                return (
                                    <Fragment key={rowKey}>
                                        <TableRow
                                            className="cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                                            onClick={() =>
                                                setExpanded(
                                                    isOpen ? null : rowKey
                                                )
                                            }
                                            onKeyDown={(e) => {
                                                if (
                                                    e.key === 'Enter' ||
                                                    e.key === ' '
                                                ) {
                                                    e.preventDefault()
                                                    setExpanded(
                                                        isOpen ? null : rowKey
                                                    )
                                                }
                                            }}
                                            role="button"
                                            tabIndex={0}
                                            aria-expanded={isOpen}
                                        >
                                            <TableCell>
                                                <ChevronRight
                                                    className={cn(
                                                        'size-4 text-muted-foreground transition-transform duration-150 motion-reduce:transition-none',
                                                        isOpen && 'rotate-90'
                                                    )}
                                                    aria-hidden
                                                />
                                            </TableCell>
                                            <TableCell className="whitespace-nowrap tabular-nums text-muted-foreground">
                                                {formatDateTime(entry.atUtc)}
                                            </TableCell>
                                            <TableCell className="font-medium">
                                                {entry.actorName || 'unknown'}
                                            </TableCell>
                                            <TableCell className="text-muted-foreground">
                                                {formatActivityAction(entry)}
                                            </TableCell>
                                        </TableRow>
                                        {isOpen && (
                                            <TableRow className="hover:bg-transparent">
                                                <TableCell
                                                    colSpan={4}
                                                    className="bg-muted/20 py-3"
                                                >
                                                    <div className="flex flex-col gap-2 text-xs">
                                                        <div className="flex flex-wrap gap-x-4 gap-y-1 text-muted-foreground">
                                                            <span>
                                                                Actor object ID:{' '}
                                                                <span className="font-mono text-foreground">
                                                                    {entry.actorObjectId ||
                                                                        '\u2014'}
                                                                </span>
                                                            </span>
                                                            <span>
                                                                Request:{' '}
                                                                <span className="font-mono text-foreground">
                                                                    {
                                                                        entry.method
                                                                    }{' '}
                                                                    {entry.path}
                                                                </span>
                                                            </span>
                                                            <span>
                                                                Status:{' '}
                                                                <span className="font-mono text-foreground">
                                                                    {
                                                                        entry.statusCode
                                                                    }
                                                                </span>
                                                            </span>
                                                        </div>
                                                        {showDiff ? (
                                                            <div className="overflow-hidden rounded-lg border border-border/60 bg-card/30">
                                                                <EntityDiffView
                                                                    entries={diffEntities(
                                                                        before,
                                                                        after
                                                                    )}
                                                                />
                                                            </div>
                                                        ) : (
                                                            <p className="italic text-muted-foreground">
                                                                {diffable &&
                                                                !isDefaultOrder
                                                                    ? 'Diff unavailable while sorted; switch back to Time (newest first) to compare against the previous entry.'
                                                                    : 'No comparable submitted body for this entry.'}
                                                            </p>
                                                        )}
                                                    </div>
                                                </TableCell>
                                            </TableRow>
                                        )}
                                    </Fragment>
                                )
                            })}
                        </TableBody>
                    </Table>
                </div>
            </div>

            {/* Mobile card list — hidden at md+ */}
            <div className="md:hidden flex flex-col gap-2">
                {rows.map((entry, index) => {
                    const rowKey = `${entry.atUtc}-${entry.actorObjectId}-${index}`
                    const isOpen = expanded === rowKey

                    const older = canDiff ? entries[index + 1] : undefined
                    const before = older
                        ? tryParseJson(older.requestBodyJson)
                        : null
                    const after = tryParseJson(entry.requestBodyJson)
                    const showDiff =
                        canDiff && before !== null && after !== null

                    return (
                        <div
                            key={rowKey}
                            className="rounded-lg border bg-card p-3 cursor-pointer select-none"
                            onClick={() => setExpanded(isOpen ? null : rowKey)}
                        >
                            <div className="flex items-center gap-2">
                                <button
                                    type="button"
                                    className="shrink-0 rounded-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                                    aria-expanded={isOpen}
                                    aria-label={
                                        isOpen
                                            ? 'Collapse activity entry'
                                            : 'Expand activity entry'
                                    }
                                    onClick={(event) => {
                                        event.stopPropagation()
                                        setExpanded(isOpen ? null : rowKey)
                                    }}
                                >
                                    <ChevronRight
                                        className={cn(
                                            'size-4 text-muted-foreground transition-transform duration-150 motion-reduce:transition-none',
                                            isOpen && 'rotate-90'
                                        )}
                                        aria-hidden
                                    />
                                </button>
                                <span className="tabular-nums text-xs text-muted-foreground">
                                    {formatDateTime(entry.atUtc)}
                                </span>
                                <span className="ml-auto text-xs font-medium truncate">
                                    {entry.actorName || 'unknown'}
                                </span>
                            </div>
                            <div className="mt-1 pl-6 text-sm wrap-break-word text-muted-foreground">
                                {formatActivityAction(entry)}
                            </div>
                            {isOpen && (
                                <div className="mt-3 pl-6 flex flex-col gap-2 text-xs border-t border-border/40 pt-3">
                                    <div className="flex flex-wrap gap-x-4 gap-y-1 text-muted-foreground">
                                        <span>
                                            Actor object ID:{' '}
                                            <span className="font-mono text-foreground break-all">
                                                {entry.actorObjectId ||
                                                    '\u2014'}
                                            </span>
                                        </span>
                                        <span>
                                            Request:{' '}
                                            <span className="font-mono text-foreground break-all">
                                                {entry.method} {entry.path}
                                            </span>
                                        </span>
                                        <span>
                                            Status:{' '}
                                            <span className="font-mono text-foreground">
                                                {entry.statusCode}
                                            </span>
                                        </span>
                                    </div>
                                    {showDiff ? (
                                        <div className="overflow-hidden rounded-lg border border-border/60 bg-card/30">
                                            <EntityDiffView
                                                entries={diffEntities(
                                                    before,
                                                    after
                                                )}
                                            />
                                        </div>
                                    ) : (
                                        <p className="italic text-muted-foreground">
                                            {diffable && !isDefaultOrder
                                                ? 'Diff unavailable while sorted; switch back to Time (newest first) to compare against the previous entry.'
                                                : 'No comparable submitted body for this entry.'}
                                        </p>
                                    )}
                                </div>
                            )}
                        </div>
                    )
                })}
            </div>
            {hasMore && (
                <LoadOlderButton onClick={onLoadMore} loading={isLoadingMore} />
            )}
        </div>
    )
}
