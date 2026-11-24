import { cn } from '@/lib/utils'
import { Badge } from '@/components/ui/badge'
import { formatNextRun } from '../lib/schedule-utils'
import type { MarketSchedule } from '../types'

interface ScheduleNextRunProps {
    schedule: MarketSchedule
    showAbsolute?: boolean
    className?: string
}

export function ScheduleNextRun({
    schedule,
    showAbsolute = true,
    className,
}: ScheduleNextRunProps) {
    const { relative, absolute, urgent } = formatNextRun(
        schedule.nextRunAt,
        schedule.enabled
    )

    if (!schedule.enabled) {
        return (
            <span
                className={cn('text-muted-foreground font-medium', className)}
            >
                Paused
            </span>
        )
    }

    return (
        <span className={cn('inline-flex items-baseline gap-1', className)}>
            {urgent ? (
                <Badge variant="destructive">{relative}</Badge>
            ) : (
                <span className="text-foreground">{relative}</span>
            )}
            {showAbsolute && absolute && (
                <span className="text-muted-foreground text-xs">
                    ({absolute})
                </span>
            )}
        </span>
    )
}
