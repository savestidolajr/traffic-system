import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
    TooltipProvider,
} from '@/components/ui/tooltip'
import { formatDuration } from '@/lib/formatting'
import { cn } from '@/lib/utils'

interface BudgetBarProps {
    /** Measured/produced duration, seconds. */
    readonly measuredSec: number
    /** Target duration, seconds. */
    readonly targetSec: number
    /** Within ±this many seconds counts as on-target. */
    readonly toleranceSec?: number
    readonly className?: string
}

/** Target sits at 80% of the track, leaving headroom to show an overrun. */
const TARGET_FRAC = 0.8

/**
 * Duration-vs-target at a glance: a fill that reaches a target tick.
 * Over target → destructive (will overrun the slot), under → warning
 * (dead air), on → success. Replaces "0:25 / 0:30 · 5s under" text.
 */
export function BudgetBar({
    measuredSec,
    targetSec,
    toleranceSec = 2,
    className,
}: BudgetBarProps) {
    const delta = measuredSec - targetSec
    const state =
        Math.abs(delta) <= toleranceSec ? 'on' : delta > 0 ? 'over' : 'under'
    const ratio = targetSec > 0 ? measuredSec / targetSec : 0
    const fillPct = Math.min(ratio * TARGET_FRAC, 1) * 100
    const fillColor =
        state === 'on'
            ? 'bg-success'
            : state === 'over'
              ? 'bg-destructive'
              : 'bg-warning'

    const title = `${formatDuration(Math.round(measuredSec))} / ${formatDuration(
        Math.round(targetSec)
    )}${delta !== 0 ? ` · ${Math.abs(Math.round(delta))}s ${delta > 0 ? 'over' : 'under'}` : ''}`

    return (
        <TooltipProvider>
            <Tooltip>
                <TooltipTrigger asChild>
                    <span
                        tabIndex={0}
                        role="img"
                        aria-label={title}
                        className={cn(
                            'relative inline-block h-1.5 w-14 cursor-default overflow-hidden rounded-full bg-muted/60',
                            className
                        )}
                    >
                        <span
                            className={cn(
                                'absolute inset-y-0 left-0 rounded-full',
                                fillColor
                            )}
                            style={{ width: `${fillPct}%` }}
                        />
                        <span
                            className="absolute inset-y-0 w-px bg-foreground/50"
                            style={{ left: `${TARGET_FRAC * 100}%` }}
                        />
                    </span>
                </TooltipTrigger>
                <TooltipContent side="top">{title}</TooltipContent>
            </Tooltip>
        </TooltipProvider>
    )
}
