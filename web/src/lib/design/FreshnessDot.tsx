import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
    TooltipProvider,
} from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'

interface FreshnessDotProps {
    /** Age of the data, milliseconds. */
    readonly ageMs: number
    readonly className?: string
}

const MIN = 60_000

/**
 * Traffic-data staleness at a glance: <5m fresh (success), 5–15m aging
 * (warning), >15m stale (destructive). Pairs with the "traffic Xm ago" text.
 */
export function FreshnessDot({ ageMs, className }: FreshnessDotProps) {
    const minutes = ageMs / MIN
    const color =
        minutes < 5
            ? 'bg-success'
            : minutes <= 15
              ? 'bg-warning'
              : 'bg-destructive'
    const label = minutes < 5 ? 'fresh' : minutes <= 15 ? 'aging' : 'stale'

    return (
        <TooltipProvider>
            <Tooltip>
                <TooltipTrigger asChild>
                    <span
                        tabIndex={0}
                        role="img"
                        aria-label={`Traffic data ${label}`}
                        className={cn(
                            'inline-block size-2 shrink-0 cursor-default rounded-full',
                            color,
                            className
                        )}
                    />
                </TooltipTrigger>
                <TooltipContent side="top">Traffic data {label}</TooltipContent>
            </Tooltip>
        </TooltipProvider>
    )
}
