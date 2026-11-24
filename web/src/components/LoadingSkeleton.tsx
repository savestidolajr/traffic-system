import { Skeleton } from '@/components/ui/skeleton'
import { CARD_GRID } from '@/lib/layout'
import { cn } from '@/lib/utils'

/**
 * Shared loading placeholders so every data-bound surface animates identically
 * (one shimmer, one rhythm) instead of each page hand-rolling its own. Compose
 * from the `Skeleton` primitive; pick the shape that matches the loaded layout
 * to avoid layout shift when real content arrives.
 */

/**
 * Card-grid placeholder for entity-card pages (stations, voices, operators).
 * Mirrors `CARD_GRID` and the entity-card body so the skeleton occupies the
 * same footprint the loaded cards will.
 */
export function CardGridSkeleton({ count = 3 }: { readonly count?: number }) {
    return (
        <div className={CARD_GRID} role="status" aria-label="Loading">
            {Array.from({ length: count }, (_, i) => (
                <div
                    key={i}
                    className="flex flex-col gap-3 rounded-xl border bg-card p-4"
                >
                    <Skeleton className="h-5 w-32" />
                    <Skeleton className="h-4 w-48" />
                    <Skeleton className="h-4 w-24" />
                    <Skeleton className="h-9 w-full" />
                </div>
            ))}
        </div>
    )
}

// Staggered bar widths so the row list reads as content, not a flat block.
const ROW_WIDTHS = ['w-2/3', 'w-1/2', 'w-3/4', 'w-2/5', 'w-3/5', 'w-1/2']

/**
 * Bordered row-list placeholder for the Bulletin-rules hub inventories and the
 * broadcast log. Matches the `ScopeGroupedList` container, sticky header and
 * row rhythm so loading reads as "a list is coming".
 */
export function ListSkeleton({
    rows = 5,
    className,
}: {
    readonly rows?: number
    readonly className?: string
}) {
    return (
        <div
            role="status"
            aria-label="Loading"
            className={cn(
                'overflow-hidden rounded-lg border border-border bg-card/30',
                className
            )}
        >
            <div className="bg-card px-4 py-2.5">
                <Skeleton className="h-4 w-40" />
            </div>
            <div className="divide-y divide-border">
                {Array.from({ length: rows }, (_, i) => (
                    <div
                        key={i}
                        className="flex items-center gap-3 px-4 py-2.5"
                    >
                        <Skeleton
                            className={cn(
                                'h-4',
                                ROW_WIDTHS[i % ROW_WIDTHS.length]
                            )}
                        />
                        <div className="flex-1" />
                        <Skeleton className="size-7 shrink-0 rounded-md" />
                    </div>
                ))}
            </div>
        </div>
    )
}
