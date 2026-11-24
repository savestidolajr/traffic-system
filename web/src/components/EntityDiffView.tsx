import { cn } from '@/lib/utils'
import {
    formatDiffValue,
    humanizePathSegment,
    summarizeEntity,
    type DiffEntry,
} from '@/lib/entity-diff'
import { ICON } from '@/lib/design/icons'

/**
 * Renders one value's diff: a field row shows before → after (git-style
 * strike/highlight), a whole array item added/removed collapses to one
 * tinted `+`/`−` line instead of exploding into its own fields.
 */
export function EntityDiffView({ entries }: { readonly entries: DiffEntry[] }) {
    if (entries.length === 0) {
        return (
            <p className="py-1 text-xs italic text-muted-foreground">
                No field-level differences.
            </p>
        )
    }

    return (
        <ul className="flex flex-col divide-y divide-border/60 text-sm">
            {entries.map((entry, i) => {
                const breadcrumb = entry.path
                    .slice(0, -1)
                    .map(humanizePathSegment)
                    .join(' \u203a ')
                const label = humanizePathSegment(
                    entry.path[entry.path.length - 1]
                )

                if (entry.scope === 'item') {
                    const added = entry.kind === 'added'
                    return (
                        <li
                            key={i}
                            className={cn(
                                'flex items-baseline gap-2 rounded px-2 py-1.5',
                                added ? 'bg-success/5' : 'bg-destructive/5'
                            )}
                        >
                            <span
                                className={cn(
                                    'font-mono text-xs font-bold',
                                    added
                                        ? 'text-success'
                                        : 'text-destructive-on-tint'
                                )}
                                aria-hidden
                            >
                                {added ? '+' : '\u2212'}
                            </span>
                            <span className="flex min-w-0 flex-1 flex-col">
                                <span className="font-medium">{label}</span>
                                <span className="truncate text-xs text-muted-foreground">
                                    {summarizeEntity(
                                        added ? entry.after : entry.before
                                    )}
                                </span>
                            </span>
                        </li>
                    )
                }

                return (
                    <li key={i} className="flex flex-col gap-0.5 px-2 py-1.5">
                        <span className="text-xs text-muted-foreground">
                            {breadcrumb && `${breadcrumb} \u203a `}
                            {label}
                        </span>
                        <span className="flex flex-wrap items-center gap-1.5">
                            {entry.kind !== 'added' && (
                                <span className="text-destructive-on-tint line-through decoration-destructive/50">
                                    {formatDiffValue(entry.before)}
                                </span>
                            )}
                            {entry.kind === 'changed' && (
                                <ICON.forward
                                    className="size-3 shrink-0 text-muted-foreground"
                                    aria-hidden
                                />
                            )}
                            {entry.kind !== 'removed' && (
                                <span className="font-medium text-success">
                                    {formatDiffValue(entry.after)}
                                </span>
                            )}
                        </span>
                    </li>
                )
            })}
        </ul>
    )
}
