import {
    getScheduleStatus,
    SCHEDULE_STATUS_CONFIG,
} from '../config/status-config'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'
import type { MarketSchedule } from '../types'

interface ScheduleStatusBadgeProps {
    schedule: MarketSchedule
    className?: string
}

export function ScheduleStatusBadge({
    schedule,
    className,
}: ScheduleStatusBadgeProps) {
    const status = getScheduleStatus(schedule)
    const config = SCHEDULE_STATUS_CONFIG[status]
    const Icon = config.icon

    return (
        <Badge variant={config.variant} className={cn('gap-1.5', className)}>
            <Icon className="size-3.5" aria-hidden="true" />
            {config.label}
        </Badge>
    )
}
