import { ArrowUp, ArrowDown, ArrowUpDown } from 'lucide-react'
import { TableHead } from '@/components/ui/table'
import { cn } from '@/lib/utils'
import type { SortState } from '@/lib/table-sort'

interface DataTableColumnHeaderProps<TKey extends string> {
    readonly label: string
    readonly sortKey: TKey
    readonly sort: SortState<TKey> | null
    readonly onSort: (key: TKey) => void
    readonly className?: string
    readonly align?: 'left' | 'right'
}

/**
 * A clickable `<TableHead>` with a sort-direction indicator. Any click switches the whole table
 * to a flat sort by this column — callers decide what "default" (usually time-descending, with
 * a grouped view) looks like and treat any non-default `SortState` as "flatten and re-sort".
 */
export function DataTableColumnHeader<TKey extends string>({
    label,
    sortKey,
    sort,
    onSort,
    className,
    align = 'left',
}: DataTableColumnHeaderProps<TKey>) {
    const active = sort?.key === sortKey
    const direction = active ? sort.direction : undefined
    const Icon =
        direction === 'asc'
            ? ArrowUp
            : direction === 'desc'
              ? ArrowDown
              : ArrowUpDown

    // `aria-sort` belongs on the element with role `columnheader` — i.e. the
    // `<th>` — not on the button inside it. A `<button>` does not allow the
    // attribute at all, so a screen reader ignored it entirely (axe
    // `aria-allowed-attr`, WCAG 4.1.2 A) and the sort state was never announced.
    return (
        <TableHead
            aria-sort={
                direction === 'asc'
                    ? 'ascending'
                    : direction === 'desc'
                      ? 'descending'
                      : 'none'
            }
            className={cn(
                'select-none',
                align === 'right' && 'text-right',
                className
            )}
        >
            <button
                type="button"
                onClick={() => onSort(sortKey)}
                className={cn(
                    // Type size, weight and resting colour are inherited from `TableHead`, so a
                    // sortable header is typographically identical to a static one.
                    'inline-flex items-center gap-1 rounded px-2 py-1 transition-colors hover:bg-muted/60 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
                    active && 'text-foreground',
                    // Compensate the button's own padding on whichever side sits against the
                    // TableHead's inset, so its rendered content starts/ends flush with the
                    // TableCell's `px-4` text below it — text-align (not margin, which has no
                    // effect on an inline-level box like this button) does the actual right-align.
                    align === 'right' ? '-mr-2 flex-row-reverse' : '-ml-2'
                )}
            >
                {label}
                <Icon
                    className={cn('size-3.5', !active && 'opacity-40')}
                    aria-hidden
                />
            </button>
        </TableHead>
    )
}
