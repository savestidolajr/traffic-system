// =============================================================================
// Shared Test Factories
// =============================================================================

import type { JobSummary, JobSummaryStatus } from '@/features/jobs/types'
import type { MarketSchedule } from '@/features/schedules/types'
import type { JobIncident, TrafficCorridor } from '@/features/jobs/types'

// ── m(): offset a Date by minutes ──
export function m(base: Date, offsetMin: number): string {
    return new Date(base.getTime() + offsetMin * 60_000).toISOString()
}

// ── Base test dates ──
export const BASE_NOW = new Date('2026-06-02T06:58:00Z')
export const EARLY_NOW = new Date('2026-06-02T06:30:00Z')
export const LATE_NOW = new Date('2026-06-02T07:00:00Z')

// ── Job factories ──
let _jobIdCounter = 0
export function makeJob(overrides: Partial<JobSummary> = {}): JobSummary {
    const id = ++_jobIdCounter
    return {
        jobId: `test-job-${id}`,
        createdAt: '2026-06-02T06:00:00Z',
        station: '2GB 873 Sydney News/Talk',
        market: 'Sydney',
        targetDurationSec: 30,
        status: 'REQUESTED' as JobSummaryStatus,
        lastUpdatedAt: '2026-06-02T06:00:00Z',
        ...overrides,
    }
}

// ── Schedule factories ──
export function makeSchedule(
    overrides: Partial<MarketSchedule> = {}
): MarketSchedule {
    return {
        marketId: 'test-market',
        station: 'TEST',
        name: 'Test Schedule',
        timezone: 'Australia/Sydney',
        cronExpression: '0 7 * * *',
        enabled: true,
        playoutEnabled: true,
        targetDurationSeconds: 30,
        notifyBeforePlayoutMinutes: 15,
        escalationBeforePlayoutMinutes: 10,
        voiceProfileId: 'vp-test',
        deliveryStyleId: null,
        playbookId: '',
        pronunciationDictionaryId: null,
        bookendId: null,
        nextRunAt: null,
        currentJobId: null,
        coverage: {
            latitude: -27.4698,
            longitude: 153.0251,
            radiusKm: 40,
            shapeType: 'circle',
        },
        content: null,
        voiceRotation: null,
        bookendRotation: null,
        bookendTokens: null,
        fillOrder: null,
        calendar: null,
        createdAt: '2026-06-01T00:00:00Z',
        updatedAt: '2026-06-01T00:00:00Z',
        ...overrides,
    }
}

// ── Daily slot schedule builder ──
export function dailySlot(baseMinutes = 7 * 60) {
    return makeSchedule({
        timezone: 'UTC',
        cronExpression: `0 ${baseMinutes % 60} ${Math.floor(baseMinutes / 60)} * * *`,
        station: 'TEST',
    })
}
// ── Calendar event builders ──
export function intervalInput(overrides: Record<string, unknown> = {}) {
    return {
        scheduleMode: 'interval' as const,
        startTime: '06:00',
        endTime: '18:00',
        intervalMinutes: 15,
        daysOfWeek: [1, 2, 3, 4, 5],
        specificTimes: [],
        ...overrides,
    }
}

export function specificInput(overrides: Record<string, unknown> = {}) {
    return {
        scheduleMode: 'specific' as const,
        specificTimes: ['09:00', '12:00', '15:00'],
        startTime: '06:00',
        endTime: '18:00',
        intervalMinutes: 60,
        daysOfWeek: [1, 2, 3, 4, 5],
        ...overrides,
    }
}

// ── Incident factories (JobIncident for provenance tests) ──
export function makeIncident(
    key: string,
    headline: string,
    overrides: Partial<JobIncident> = {}
): JobIncident {
    return {
        key,
        priority: 4,
        headline,
        detail: '',
        ...overrides,
    }
}

// ── Corridor factories (TrafficCorridor for evidence/provenance tests) ──
export function makeCorridor(
    roadName: string,
    overrides: Partial<TrafficCorridor> = {}
): TrafficCorridor {
    return {
        roadName,
        severity: 3,
        impactScore: 100,
        primaryType: 'congestion',
        description: 'moving slowly',
        incidentCount: 0,
        totalLengthKm: 1,
        rank: 1,
        isSelected: true,
        functionalClass: 3,
        corroborationCount: 1,
        incidentKeys: [],
        ...overrides,
    }
}
