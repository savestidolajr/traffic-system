import type { MarketSchedule } from '@/features/schedules/types'

export interface StationAttentionIssue {
    readonly key: string
    readonly label: string
}

/**
 * Derive configuration gaps that need operator attention.
 * `content: null` is the documented legacy traffic-only default and is
 * intentionally not flagged — only an explicit all-sources-off selection is.
 */
export function getStationAttentionIssues(
    schedule: MarketSchedule
): StationAttentionIssue[] {
    const issues: StationAttentionIssue[] = []

    if (!schedule.voiceProfileId) {
        issues.push({ key: 'voice', label: 'No voice selected' })
    }
    if (!schedule.cronExpression) {
        issues.push({ key: 'cron', label: 'No schedule configured' })
    }
    if (
        schedule.content &&
        !schedule.content.traffic &&
        !schedule.content.weather &&
        !schedule.content.fuel
    ) {
        issues.push({ key: 'content', label: 'No content sources enabled' })
    }

    return issues
}
