import { useMemo } from 'react'
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
import { formatRelativeTime } from '@/lib/formatting'
import { formatRuleCountSummary } from '../lib/rule-count'
import { useTableSort, type SortColumns } from '@/lib/table-sort'
import type { Dictionary } from '../types'

interface PronunciationDictionaryTableProps {
    readonly dictionaries: readonly Dictionary[]
    readonly assignmentsByDictionaryId: ReadonlyMap<string, readonly string[]>
    /** Total stations, used to describe a platform-wide dictionary's reach. */
    readonly stationCount: number
    readonly onView: (id: string) => void
    readonly onEdit: (id: string) => void
    readonly onDuplicate: (dict: Dictionary) => void
    readonly onDelete: (dict: Dictionary) => void
    readonly onArchive: (dict: Dictionary) => void
    readonly onUnarchive: (dict: Dictionary) => void
    /** False for an operator: everything else is theirs, but DELETE stays Admin. */
    readonly canDelete?: boolean
}

interface PronunciationRow {
    readonly dict: Dictionary
    readonly isArchived: boolean
    readonly ruleCount: number
    readonly aliasCount: number
    readonly phonemeCount: number
    readonly previewWords: readonly string[]
    readonly extraWords: number
    readonly stationCount: number
    readonly stationLabel: string
}

type PronunciationSortKey = 'name' | 'rules' | 'stations' | 'updated'

/**
 * Sortable table view of the pronunciation dictionary library — the data-dense sibling
 * of the `PronunciationDictionaryCard` gallery. Rows open View details, which
 * is mirrored as the first kebab item for keyboard users.
 * Columns Name, Rules, Stations, and Updated are sortable.
 */
export function PronunciationDictionaryTable({
    dictionaries,
    assignmentsByDictionaryId,
    stationCount,
    onView,
    onEdit,
    onDuplicate,
    onDelete,
    onArchive,
    onUnarchive,
    canDelete = true,
}: PronunciationDictionaryTableProps) {
    const decorated = useMemo<readonly PronunciationRow[]>(
        () =>
            dictionaries.map((dict) => {
                const isArchived = dict.status === 'archived'
                const ruleCount = dict.rules.length
                const aliasCount = dict.rules.filter(
                    (rule) => rule.type === 'alias'
                ).length
                const phonemeCount = dict.rules.filter(
                    (rule) => rule.type === 'phoneme'
                ).length
                const previewWords = dict.rules
                    .slice(0, 3)
                    .map((rule) => rule.stringToReplace)
                const extraWords = ruleCount - previewWords.length
                const stations = assignmentsByDictionaryId.get(dict.id) ?? []
                // A platform-wide dictionary is referenced by no schedule; sort it above every
                // per-station one by giving it the true reach it has.
                const isPlatformWide = dict.scope === 'Global'
                const effectiveStationCount = isPlatformWide
                    ? stationCount
                    : stations.length
                const stationLabel = isPlatformWide
                    ? `Every station${stationCount > 0 ? ` (${stationCount})` : ''}`
                    : stations.length === 0
                      ? 'Unassigned'
                      : `${stations.length} station${stations.length === 1 ? '' : 's'}`
                return {
                    dict,
                    isArchived,
                    ruleCount,
                    aliasCount,
                    phonemeCount,
                    previewWords,
                    extraWords,
                    stationCount: effectiveStationCount,
                    stationLabel,
                }
            }),
        [dictionaries, assignmentsByDictionaryId, stationCount]
    )

    const columns = useMemo<
        SortColumns<PronunciationRow, PronunciationSortKey>
    >(
        () => ({
            name: { value: (r) => r.dict.name, first: 'asc' },
            rules: { value: (r) => r.ruleCount, first: 'desc' },
            stations: { value: (r) => r.stationCount, first: 'desc' },
            updated: {
                value: (r) => Date.parse(r.dict.updatedAt),
                first: 'desc',
            },
        }),
        []
    )

    const { rows, sort, onSort } = useTableSort(
        decorated,
        columns,
        'pronunciation'
    )
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
                            label="Rules"
                            sortKey="rules"
                            sort={sort}
                            onSort={onSort}
                            align="right"
                        />
                        <TableHead>Words</TableHead>
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
                        const { dict } = row

                        return (
                            <TableRow
                                key={dict.id}
                                onClick={() => onView(dict.id)}
                                className="cursor-pointer"
                            >
                                <TableCell className="font-medium">
                                    <span className="flex min-w-0 items-center gap-2">
                                        <span className="min-w-0 truncate">
                                            <span className="block truncate">
                                                {dict.name}
                                            </span>
                                            <span className="block text-xs font-normal text-muted-foreground">
                                                v{dict.currentVersion}
                                                {formatRuleCountSummary(
                                                    row.aliasCount,
                                                    row.phonemeCount
                                                )}
                                            </span>
                                        </span>
                                        {row.dict.scope === 'Global' && (
                                            <span className="shrink-0 rounded-full bg-primary/10 px-2 py-px text-3xs font-medium text-primary-on-tint">
                                                Platform-wide
                                            </span>
                                        )}
                                        {row.isArchived && (
                                            <span className="shrink-0 rounded-full bg-muted px-2 py-px text-3xs font-medium text-muted-foreground">
                                                Archived
                                            </span>
                                        )}
                                    </span>
                                </TableCell>
                                <TableCell className="text-right tabular-nums text-muted-foreground">
                                    {row.ruleCount}
                                </TableCell>
                                <TableCell className="text-muted-foreground">
                                    {row.previewWords.length > 0 ? (
                                        <span>
                                            {row.previewWords.join(', ')}
                                            {row.extraWords > 0 &&
                                                ` +${row.extraWords} more`}
                                        </span>
                                    ) : (
                                        'None'
                                    )}
                                </TableCell>
                                <TableCell className="text-muted-foreground">
                                    {row.stationLabel}
                                </TableCell>
                                <TableCell className="text-muted-foreground">
                                    {formatRelativeTime(dict.updatedAt)}
                                </TableCell>
                                <TableCell
                                    className="text-right"
                                    onClick={(event) => event.stopPropagation()}
                                >
                                    <EntityActionsMenu
                                        onView={() => onView(dict.id)}
                                        onEdit={() => onEdit(dict.id)}
                                        onDuplicate={() => onDuplicate(dict)}
                                        onDelete={
                                            canDelete
                                                ? () => onDelete(dict)
                                                : undefined
                                        }
                                        onArchive={
                                            row.isArchived
                                                ? undefined
                                                : () => onArchive(dict)
                                        }
                                        onUnarchive={
                                            row.isArchived
                                                ? () => onUnarchive(dict)
                                                : undefined
                                        }
                                        ariaLabel={`Actions for ${dict.name}`}
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
