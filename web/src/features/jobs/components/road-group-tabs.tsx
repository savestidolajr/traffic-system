import type { ReactNode } from 'react'
import { ChevronRight } from 'lucide-react'
import { anchorsSummary, eventSummary } from '../lib/incidents/road-groups'
import type { RoadGroupData } from '../lib/incidents/road-groups'
import { formatRoadName } from '@/lib/formatting'
import { cn } from '@/lib/utils'

// ---------------------------------------------------------------------------
// Shared road-group + tab-bar UI. One definition for every surface that
// buckets incidents into a small set of named groups (Cited/Unused/Below cut
// on Review, In bulletin/Available/Flowing well on the Adjust map explorer)
// and lists each bucket's incidents grouped by road — so the two surfaces
// can never visually or behaviourally drift apart.
// ---------------------------------------------------------------------------

/** A road header with a count badge over its collapsible incident rows. */
export function RoadGroup({
    group,
    collapsed,
    onToggle,
    children,
}: {
    readonly group: RoadGroupData
    readonly collapsed: boolean
    readonly onToggle: (road: string) => void
    readonly children: ReactNode
}) {
    const summary = anchorsSummary(group.incidents)
    return (
        <div className="border-t border-border/35 first:border-t-0">
            <button
                type="button"
                onClick={() => onToggle(group.road)}
                aria-expanded={!collapsed}
                className="flex w-full items-start gap-1.5 px-2 py-1.5 text-left text-xs transition-colors hover:bg-muted/40"
            >
                <ChevronRight
                    className={cn(
                        'mt-0.5 size-3 shrink-0 text-muted-foreground transition-transform',
                        !collapsed && 'rotate-90'
                    )}
                    aria-hidden
                />
                <span className="min-w-0 flex-1">
                    <span className="flex min-w-0 items-center gap-1.5">
                        <span className="truncate font-medium text-foreground/85">
                            {formatRoadName(group.road)}
                        </span>
                        {group.roadClass && (
                            <span className="shrink-0 text-2xs text-muted-foreground">
                                {group.roadClass}
                            </span>
                        )}
                    </span>
                    <span className="block truncate text-2xs leading-snug text-muted-foreground">
                        {eventSummary(group.incidents)}
                        {summary ? ` · ${summary}` : ''}
                    </span>
                </span>
            </button>
            {!collapsed && (
                <div className="border-t border-border/30 bg-muted/10 pl-3">
                    {children}
                </div>
            )}
        </div>
    )
}

/** One entry in a tab bar: a label, a live count, and an active state driven
 *  by the caller — no internal state, so several tab bars on one page never
 *  fight over which is "active". */
export function TabTrigger({
    active,
    onClick,
    label,
    count,
    tone = 'default',
}: {
    readonly active: boolean
    readonly onClick: () => void
    readonly label: string
    readonly count: number
    readonly tone?: 'default' | 'warning' | 'muted'
}) {
    const countClass =
        tone === 'warning'
            ? 'text-warning'
            : tone === 'muted'
              ? 'text-muted-foreground'
              : 'text-foreground'
    return (
        <button
            type="button"
            onClick={onClick}
            className={cn(
                'flex-1 rounded-[5px] px-2 py-1 text-center text-2xs font-medium tabular-nums transition-colors',
                active
                    ? 'bg-background text-foreground shadow-sm'
                    : 'text-muted-foreground hover:text-foreground'
            )}
        >
            {label}
            <span
                className={cn(
                    'ml-1',
                    active ? countClass : 'text-muted-foreground'
                )}
            >
                {count}
            </span>
        </button>
    )
}
