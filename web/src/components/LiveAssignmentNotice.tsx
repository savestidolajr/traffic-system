import { ICON } from '@/lib/design/icons'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'

/** Above this, the station list becomes a count with the names in a tooltip. */
const INLINE_STATION_LIMIT = 3

interface LiveAssignmentNoticeProps {
    /** Station names this entity is live on. Never empty at the call sites. */
    readonly stations: readonly string[]
    readonly className?: string
}

/**
 * The one "this is live, saving affects air" banner for every versioned library
 * studio (voices, delivery styles, bookends, pronunciation dictionaries).
 *
 * Four studios had drifted into three phrasings of the same warning, and the
 * voice studio's 12-station list wrapped to three lines before the operator
 * reached a single field. The names are load-bearing, so past
 * {@link INLINE_STATION_LIMIT} they collapse to a count and move into a
 * tooltip rather than being dropped.
 */
export function LiveAssignmentNotice({
    stations,
    className,
}: LiveAssignmentNoticeProps) {
    const overflowing = stations.length > INLINE_STATION_LIMIT
    const subject = overflowing
        ? `${stations.length} stations`
        : stations.join(', ')

    return (
        <div
            className={cn(
                'flex items-start gap-2 rounded-lg border border-warning/30 bg-warning/10 p-3 text-sm text-warning',
                className
            )}
        >
            <ICON.warning className="mt-0.5 size-4 shrink-0" />
            <p>
                <span className="font-medium">
                    Live on{' '}
                    {overflowing ? (
                        <Tooltip>
                            <TooltipTrigger asChild>
                                <button
                                    type="button"
                                    className="underline decoration-dotted underline-offset-2"
                                >
                                    {subject}
                                </button>
                            </TooltipTrigger>
                            <TooltipContent side="bottom" className="max-w-xs">
                                {stations.join(', ')}
                            </TooltipContent>
                        </Tooltip>
                    ) : (
                        subject
                    )}
                    .
                </span>{' '}
                Saving affects the next bulletin.
            </p>
        </div>
    )
}
