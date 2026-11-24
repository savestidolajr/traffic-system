import { ScrollText } from 'lucide-react'
import { cn } from '@/lib/utils'
import { formatRelativeTime } from '@/lib/formatting'
import { diffEntities } from '@/lib/entity-diff'
import { tryParseJson } from '@/lib/json'
import { EntityDiffView } from '@/components/EntityDiffView'
import { Placeholder } from '@/components/Placeholder'
import type { ActivityLogEntry } from '../api/activity-schemas'
import { formatActivityAction } from '../activity-format'

interface ActivityFeedProps {
    readonly entries: ActivityLogEntry[]
    /**
     * Per-entity view (one resource + entityId): consecutive entries share a
     * lineage, so each one's submitted body can be diffed against the entry
     * immediately before it. The global feed spans unrelated resources, so
     * this must stay false there -- diffing across unrelated entities would
     * be meaningless noise.
     */
    readonly diffable: boolean
}

/** Read-only change/access-audit timeline. Mirrors VoiceVersionHistory's rail visual, no Restore action. */
export function ActivityFeed({ entries, diffable }: ActivityFeedProps) {
    if (entries.length === 0) {
        return (
            <Placeholder
                icon={ScrollText}
                message="No activity recorded"
                detail="Changes to this configuration will appear here once made."
            />
        )
    }

    return (
        <ol className="flex flex-col">
            {entries.map((entry, index) => {
                const isLast = index === entries.length - 1
                const older = diffable ? entries[index + 1] : undefined
                const before = older
                    ? tryParseJson(older.requestBodyJson)
                    : null
                const after = tryParseJson(entry.requestBodyJson)
                const showDiff = diffable && before !== null && after !== null

                return (
                    <li
                        key={`${entry.atUtc}-${entry.actorObjectId}-${index}`}
                        className="flex gap-3"
                    >
                        {/* Timeline rail */}
                        <div className="flex flex-col items-center pt-1.5">
                            <span className="size-2.5 shrink-0 rounded-full bg-muted-foreground/40 ring-4 ring-background" />
                            {!isLast && (
                                <span className="w-px flex-1 bg-border" />
                            )}
                        </div>

                        {/* Entry */}
                        <div
                            className={cn(
                                'min-w-0 flex-1',
                                isLast ? 'pb-0' : 'pb-5'
                            )}
                        >
                            <div className="flex flex-wrap items-center gap-1.5 text-sm">
                                <span className="font-semibold">
                                    {entry.actorName || 'unknown'}
                                </span>
                                <span
                                    className="text-muted-foreground"
                                    aria-hidden
                                >
                                    &middot;
                                </span>
                                <span className="text-muted-foreground">
                                    {formatActivityAction(entry)}
                                </span>
                                <span
                                    className="text-muted-foreground"
                                    aria-hidden
                                >
                                    &middot;
                                </span>
                                <span className="truncate text-xs text-muted-foreground">
                                    {formatRelativeTime(entry.atUtc)}
                                </span>
                            </div>

                            {showDiff && (
                                <div className="mt-1.5 overflow-hidden rounded-lg border border-border/60 bg-card/30">
                                    <EntityDiffView
                                        entries={diffEntities(before, after)}
                                    />
                                </div>
                            )}
                        </div>
                    </li>
                )
            })}
        </ol>
    )
}
