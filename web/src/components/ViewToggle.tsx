import { LayoutGrid, Table2 } from 'lucide-react'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { cn } from '@/lib/utils'
import type { ViewMode } from '@/hooks/use-view-mode'

interface ViewToggleProps {
    readonly value: ViewMode
    readonly onChange: (mode: ViewMode) => void
    readonly className?: string
}

/**
 * Cards | Table segmented control for entity-list pages. Pairs with
 * `useViewMode` for persistence. A single mode is always selected — Radix emits
 * an empty string when the active item is re-clicked, which we ignore.
 *
 * Left at the default `h-10` size deliberately: on all five gallery pages this
 * sits in a `flex items-center` toolbar beside a search `Input` (`h-10`), and
 * `size="sm"` made it 36px against the field's 40px on every one of them.
 */
export function ViewToggle({ value, onChange, className }: ViewToggleProps) {
    return (
        <ToggleGroup
            type="single"
            value={value}
            onValueChange={(next) => {
                if (next === 'cards' || next === 'table') onChange(next)
            }}
            variant="outline"
            aria-label="View mode"
            className={cn('shrink-0', className)}
        >
            {/* The accessible name must CONTAIN the visible text verbatim
                (WCAG 2.5.3): "Card view" does not contain "Cards". */}
            <ToggleGroupItem value="cards" aria-label="Cards view">
                <LayoutGrid />
                Cards
            </ToggleGroupItem>
            <ToggleGroupItem value="table" aria-label="Table view">
                <Table2 />
                Table
            </ToggleGroupItem>
        </ToggleGroup>
    )
}
