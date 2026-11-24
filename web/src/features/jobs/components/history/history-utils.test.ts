import { describe, it, expect } from 'vitest'
import {
    LOG_OUTCOMES,
    countPipelineJobs,
    filterSettled,
    applyFilters,
    groupByDay,
} from './history-utils'
import type { JobSummary } from '../../types'

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function makeSummary(
    overrides: Partial<JobSummary> & { status: JobSummary['status'] }
): JobSummary {
    return {
        jobId: 'job-1',
        createdAt: '2026-06-11T06:00:00Z',
        station: 'ABC',
        market: 'Melbourne',
        targetDurationSec: 30,
        lastUpdatedAt: '2026-06-11T06:30:00Z',
        measuredDurationSec: 0,
        deltaSec: 0,
        ...overrides,
    }
}

// ---------------------------------------------------------------------------
// LOG_OUTCOMES
// ---------------------------------------------------------------------------

describe('LOG_OUTCOMES', () => {
    it('includes terminal + approved statuses', () => {
        expect(LOG_OUTCOMES.has('PUBLISHED')).toBe(true)
        expect(LOG_OUTCOMES.has('APPROVED')).toBe(true)
        expect(LOG_OUTCOMES.has('REJECTED')).toBe(true)
        expect(LOG_OUTCOMES.has('FAILED')).toBe(true)
        expect(LOG_OUTCOMES.has('FALLBACK_USED')).toBe(true)
        // Missed slots are settled outcomes and must be countable separately
        // from operator rejections in the broadcast log.
        expect(LOG_OUTCOMES.has('MISSED')).toBe(true)
    })

    it('excludes pipeline statuses', () => {
        expect(LOG_OUTCOMES.has('REQUESTED')).toBe(false)
        expect(LOG_OUTCOMES.has('HERE_FETCHED')).toBe(false)
        expect(LOG_OUTCOMES.has('WAITING_APPROVAL')).toBe(false)
        expect(LOG_OUTCOMES.has('AUDIO_READY')).toBe(false)
        expect(LOG_OUTCOMES.has('SCRIPT_READY')).toBe(false)
    })
})

// ---------------------------------------------------------------------------
// countPipelineJobs
// ---------------------------------------------------------------------------

describe('countPipelineJobs', () => {
    it('returns 0 when all jobs are settled', () => {
        const jobs = [
            makeSummary({ status: 'PUBLISHED' }),
            makeSummary({ status: 'APPROVED' }),
        ]
        expect(countPipelineJobs(jobs)).toBe(0)
    })

    it('counts in-flight statuses', () => {
        const jobs = [
            makeSummary({ status: 'PUBLISHED' }),
            makeSummary({ status: 'REQUESTED' }),
            makeSummary({ status: 'WAITING_APPROVAL' }),
        ]
        expect(countPipelineJobs(jobs)).toBe(2)
    })

    it('returns 0 for empty array', () => {
        expect(countPipelineJobs([])).toBe(0)
    })
})

// ---------------------------------------------------------------------------
// filterSettled
// ---------------------------------------------------------------------------

describe('filterSettled', () => {
    it('returns only settled outcomes', () => {
        const jobs = [
            makeSummary({ status: 'PUBLISHED' }),
            makeSummary({ status: 'REQUESTED' }),
            makeSummary({ status: 'REJECTED' }),
        ]
        const result = filterSettled(jobs)
        expect(result).toHaveLength(2)
        expect(result.every((j) => LOG_OUTCOMES.has(j.status))).toBe(true)
    })
})

// ---------------------------------------------------------------------------
// applyFilters
// ---------------------------------------------------------------------------

describe('applyFilters', () => {
    const base: JobSummary[] = [
        makeSummary({
            jobId: 'abc-123',
            station: 'ABC',
            market: 'Melbourne',
            status: 'PUBLISHED',
            lastUpdatedAt: '2026-06-11T06:30:00Z',
        }),
        makeSummary({
            jobId: 'xyz-456',
            station: 'XYZ',
            market: 'Sydney',
            status: 'REJECTED',
            lastUpdatedAt: '2026-06-10T12:00:00Z',
        }),
        makeSummary({
            jobId: 'def-789',
            station: 'ABC',
            market: 'Melbourne',
            status: 'FAILED',
            lastUpdatedAt: '2026-06-09T03:00:00Z',
        }),
    ]

    it('returns all settled when no filters active', () => {
        const result = applyFilters(base, {
            search: '',
            outcome: 'all',
            station: 'all',
            market: 'all',
            period: 'all',
        })
        expect(result).toHaveLength(3)
    })

    it('filters by outcome', () => {
        const result = applyFilters(base, {
            search: '',
            outcome: 'REJECTED',
            station: 'all',
            market: 'all',
            period: 'all',
        })
        expect(result).toHaveLength(1)
        expect(result[0].status).toBe('REJECTED')
    })

    it('filters by station', () => {
        const result = applyFilters(base, {
            search: '',
            outcome: 'all',
            station: 'ABC',
            market: 'all',
            period: 'all',
        })
        expect(result).toHaveLength(2)
    })

    it('filters by market', () => {
        const result = applyFilters(base, {
            search: '',
            outcome: 'all',
            station: 'all',
            market: 'Sydney',
            period: 'all',
        })
        expect(result).toHaveLength(1)
    })

    it('filters by search (jobId)', () => {
        const result = applyFilters(base, {
            search: 'abc-123',
            outcome: 'all',
            station: 'all',
            market: 'all',
            period: 'all',
        })
        expect(result).toHaveLength(1)
        expect(result[0].jobId).toBe('abc-123')
    })

    it('filters by search (station)', () => {
        const result = applyFilters(base, {
            search: 'xyz',
            outcome: 'all',
            station: 'all',
            market: 'all',
            period: 'all',
        })
        expect(result).toHaveLength(1)
        expect(result[0].station).toBe('XYZ')
    })

    it('combines filters with AND', () => {
        const result = applyFilters(base, {
            search: '',
            outcome: 'PUBLISHED',
            station: 'ABC',
            market: 'all',
            period: 'all',
        })
        expect(result).toHaveLength(1)
        expect(result[0].status).toBe('PUBLISHED')
    })
})

// ---------------------------------------------------------------------------
// groupByDay
// ---------------------------------------------------------------------------

describe('groupByDay', () => {
    it('groups jobs by calendar day', () => {
        const jobs: JobSummary[] = [
            makeSummary({
                jobId: 'a',
                status: 'PUBLISHED',
                lastUpdatedAt: '2026-06-11T12:00:00Z',
            }),
            makeSummary({
                jobId: 'b',
                status: 'REJECTED',
                lastUpdatedAt: '2026-06-11T08:00:00Z',
            }),
            makeSummary({
                jobId: 'c',
                status: 'FAILED',
                lastUpdatedAt: '2026-06-10T22:00:00Z',
            }),
        ]
        const groups = groupByDay(jobs)
        expect(groups).toHaveLength(2)

        // Newest day first
        expect(groups[0].isoDate).toBe('2026-06-11')
        expect(groups[1].isoDate).toBe('2026-06-10')
    })

    it('sorts jobs newest first within a day', () => {
        const jobs: JobSummary[] = [
            makeSummary({
                jobId: 'early',
                status: 'PUBLISHED',
                lastUpdatedAt: '2026-06-11T08:00:00Z',
            }),
            makeSummary({
                jobId: 'late',
                status: 'REJECTED',
                lastUpdatedAt: '2026-06-11T18:00:00Z',
            }),
        ]
        const groups = groupByDay(jobs)
        expect(groups).toHaveLength(1)
        expect(groups[0].jobs[0].jobId).toBe('late')
        expect(groups[0].jobs[1].jobId).toBe('early')
    })

    it('returns empty array for empty input', () => {
        expect(groupByDay([])).toHaveLength(0)
    })
})
