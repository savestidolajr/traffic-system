import { useState } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { Skeleton } from '@/components/ui/skeleton'
import { useActivityLog } from '../api/activity-hooks'
import { ActivityFeed } from './ActivityFeed'

interface ScopedActivityHistoryProps {
    readonly resource: string
    readonly entityId: string
    readonly label?: string
}

/**
 * Collapsed-by-default change/access-audit trail for one entity -- mirrors the shared-library
 * VersionHistory disclosure (same chevron/button shape) but reads the generic activity log, not
 * a domain Versions array, so there's no restore action. The query only fires once expanded
 * (`enabled: open`) so a page rendering several of these (one per row/section) doesn't fan out
 * N activity queries on mount.
 */
export function ScopedActivityHistory({
    resource,
    entityId,
    label = 'History',
}: ScopedActivityHistoryProps) {
    const [open, setOpen] = useState(false)
    const { data, isLoading } = useActivityLog({ resource, entityId }, open)

    return (
        <section className="flex flex-col gap-2">
            <button
                type="button"
                onClick={() => setOpen((o) => !o)}
                aria-expanded={open}
                className="flex items-center gap-1.5 self-start rounded-md text-sm font-medium text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            >
                {open ? (
                    <ChevronDown className="size-4" />
                ) : (
                    <ChevronRight className="size-4" />
                )}
                {label}
            </button>
            {open &&
                (isLoading ? (
                    <div className="flex flex-col gap-2 pl-5">
                        <Skeleton className="h-10 w-full" />
                        <Skeleton className="h-10 w-full" />
                    </div>
                ) : (
                    <ActivityFeed entries={data ?? []} diffable />
                ))}
        </section>
    )
}
