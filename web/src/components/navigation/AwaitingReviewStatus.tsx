import { Clock3 } from 'lucide-react'
import { Link } from 'react-router'
import type { WaitingBadgeState } from '@/features/jobs/hooks/use-waiting-badge'
import { ROUTE_JOBS } from '@/lib/routes'
import { cn } from '@/lib/utils'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'

interface AwaitingReviewStatusProps {
    readonly state: WaitingBadgeState
    readonly showText?: boolean
    readonly className?: string
    readonly labelClassName?: string
    readonly onNavigate?: () => void
}

/**
 * One global route back to the time-critical review queue. The desktop rail
 * expands to count + deadline; compact shells retain the same state as a
 * count badge with a complete accessible label. The tooltip names the icon
 * only while the label is hidden: once the rail expands, the visible text
 * says it all, so a hover hint would just restate it.
 */
export function AwaitingReviewStatus({
    state,
    showText = false,
    className,
    labelClassName,
    onNavigate,
}: AwaitingReviewStatusProps) {
    const { waitingCount, urgencyBand, badgeAriaLabel, countdownLabel } = state
    if (waitingCount === 0) return null

    const hot = urgencyBand === 'critical' || urgencyBand === 'overdue'

    const link = (
        <Link
            to={ROUTE_JOBS}
            aria-label={badgeAriaLabel}
            onClick={onNavigate}
            className={cn(
                'flex h-9 items-center gap-2.5 rounded-md px-1.5 text-sm font-medium transition-colors',
                hot
                    ? 'bg-destructive/10 text-destructive-on-tint hover:bg-destructive/15'
                    : 'bg-warning/10 text-foreground hover:bg-warning/15',
                className
            )}
        >
            <span className="relative shrink-0">
                <Clock3 className="size-5" aria-hidden />
                {!showText && (
                    <span
                        aria-hidden
                        className={cn(
                            'absolute -right-2 -top-2 flex h-4 min-w-4 items-center justify-center rounded-full px-1 font-mono text-3xs font-semibold leading-none',
                            hot
                                ? 'bg-destructive text-destructive-foreground'
                                : 'bg-warning text-warning-foreground'
                        )}
                    >
                        {waitingCount}
                    </span>
                )}
            </span>
            {showText && (
                <span
                    className={cn(
                        'flex min-w-0 flex-1 items-baseline justify-between gap-2',
                        labelClassName
                    )}
                >
                    <span className="truncate">
                        {waitingCount} awaiting review
                    </span>
                    <span className="shrink-0 font-mono text-2xs tabular-nums">
                        {countdownLabel}
                    </span>
                </span>
            )}
        </Link>
    )

    if (showText) return link

    return (
        <Tooltip>
            <TooltipTrigger asChild>{link}</TooltipTrigger>
            <TooltipContent side="right">
                <p>Awaiting review</p>
            </TooltipContent>
        </Tooltip>
    )
}
