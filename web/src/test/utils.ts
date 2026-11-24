import { expect } from 'vitest'

// ── Cron calendar assertion helpers ──
export function expectIntervalParse(
    result: {
        scheduleMode: string
        startTime: string
        endTime: string
        intervalMinutes: number
        daysOfWeek: number[]
    },
    expected: {
        startTime: string
        endTime: string
        intervalMinutes: number
        daysOfWeek: number[]
    }
) {
    expect(result.scheduleMode).toBe('interval')
    expect(result.startTime).toBe(expected.startTime)
    expect(result.endTime).toBe(expected.endTime)
    expect(result.intervalMinutes).toBe(expected.intervalMinutes)
    expect(result.daysOfWeek).toEqual(expected.daysOfWeek)
}
