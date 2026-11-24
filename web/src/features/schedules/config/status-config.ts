import { Radio, Clock, PowerOff, type LucideIcon } from 'lucide-react'
import type { BadgeProps } from '@/components/ui/badge'
import type { MarketSchedule } from '../types'

export type ScheduleStatus = 'broadcasting' | 'scheduled' | 'paused'

export function getScheduleStatus(schedule: MarketSchedule): ScheduleStatus {
    if (!schedule.enabled) return 'paused'
    if (schedule.currentJobId) return 'broadcasting'
    return 'scheduled'
}

interface StatusConfig {
    readonly icon: LucideIcon
    readonly variant: NonNullable<BadgeProps['variant']>
    readonly label: string
}

export const SCHEDULE_STATUS_CONFIG: Record<ScheduleStatus, StatusConfig> = {
    broadcasting: {
        label: 'Broadcasting',
        icon: Radio,
        variant: 'broadcasting',
    },
    scheduled: {
        label: 'Scheduled',
        icon: Clock,
        variant: 'scheduled',
    },
    paused: {
        label: 'Paused',
        icon: PowerOff,
        variant: 'secondary',
    },
}
