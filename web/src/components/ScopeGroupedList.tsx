import { type ReactNode } from 'react'
import { ListSkeleton } from '@/components/LoadingSkeleton'
import { cn } from '@/lib/utils'

export interface ScopeGroup<T> {
    /** Stable group key (e.g. GLOBAL sentinel or a marketId). */
    readonly key: string
    /** Group header label (e.g. "Global · all markets" or a station label). */
    readonly label: string
    readonly items: readonly T[]
}

interface ScopeGroupedListProps<T, G extends ScopeGroup<T>> {
    readonly groups: readonly G[]
    readonly isLoading?: boolean
    /** Shown instead of the list when not loading and there are no groups. */
    readonly empty: ReactNode
    readonly rowKey: (item: T) => string
    /** Row contents only — the shared wrapper provides padding, gap and hover. */
    readonly renderRow: (item: T, group: G) => ReactNode
    /** Optional extra content rendered inside the group wrapper, after the sticky header and before the rows. */
    readonly renderHeaderExtra?: (group: G) => ReactNode
}

/**
 * Shared scope-grouped list for the Bulletin-rules hub: one bordered container
 * with sticky per-scope headers ("{label} (n)") and dense, evenly-divided rows.
 * Editorial guidance, banned words and priority roads all render through this so
 * their inventories look and scroll identically; each tab supplies only its row
 * contents via `renderRow`, and the wrapper guarantees the same row rhythm.
 */
export function ScopeGroupedList<T, G extends ScopeGroup<T> = ScopeGroup<T>>({
    groups,
    isLoading,
    empty,
    rowKey,
    renderHeaderExtra,
    renderRow,
}: ScopeGroupedListProps<T, G>) {
    if (isLoading) return <ListSkeleton rows={4} />
    if (groups.length === 0) return <>{empty}</>

    return (
        <div className="overflow-hidden rounded-lg border border-border bg-card/30">
            {groups.map((group, gi) => (
                <div key={group.key}>
                    <div
                        className={cn(
                            'sticky top-0 z-10 flex items-center gap-2 bg-card px-4 py-2.5',
                            gi > 0 && 'border-t border-border'
                        )}
                    >
                        <h2 className="text-sm font-semibold">{group.label}</h2>
                        <span className="text-xs font-normal text-muted-foreground">
                            ({group.items.length})
                        </span>
                    </div>
                    {renderHeaderExtra?.(group)}
                    <div className="divide-y divide-border">
                        {group.items.map((item) => (
                            <div
                                key={rowKey(item)}
                                className="group flex items-center gap-3 px-4 py-2.5"
                            >
                                {renderRow(item, group)}
                            </div>
                        ))}
                    </div>
                </div>
            ))}
        </div>
    )
}
