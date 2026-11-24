import { describe, expect, it } from 'vitest'
import type { Job } from '../../types'
import { buildFallbackTimeline, buildTimeline } from './SystemAuditTab'
import type { JobProgress } from '../../api/progress-schemas'

function makeJob(): Job {
    return {
        jobId: 'job-1',
        createdAt: '2026-09-02T00:00:00Z',
        lastUpdatedAt: '2026-09-02T00:00:05Z',
        status: 'SCRIPT_READY',
        statusHistory: [
            {
                status: 'REQUESTED',
                at: '2026-09-02T00:00:01Z',
                by: 'system',
                note: '',
            },
            {
                status: 'SCRIPT_READY',
                at: '2026-09-02T00:00:04Z',
                by: 'ai',
                note: 'Attempt 2 started',
            },
        ],
        logs: [
            {
                at: '2026-09-02T00:00:03Z',
                level: 'INFO',
                message: 'Attempt 2 is writing.',
            },
            {
                at: '2026-09-02T00:00:02Z',
                level: 'INFO',
                message: 'Evidence fetched.',
            },
        ],
    } as unknown as Job
}

const progress = {
    jobId: 'job-1',
    status: 'SCRIPT_READY',
    stage: 'Write',
    stageStartedAt: '2026-09-02T00:00:03Z',
    attempt: 2,
    maxAttempts: 3,
    updatedAt: '2026-09-02T00:00:03Z',
    version: 4,
    events: [
        {
            at: '2026-09-02T00:00:00Z',
            attempt: 1,
            stage: 'Evidence',
            kind: 'StageStarted',
            detail: 'Evidence started.',
            counts: null,
            durationMs: null,
            model: null,
            route: null,
        },
        {
            at: '2026-09-02T00:00:02.500Z',
            attempt: 2,
            stage: 'Write',
            kind: 'AttemptStarted',
            detail: 'Attempt 2 started after a short read.',
            counts: null,
            durationMs: null,
            model: null,
            route: null,
        },
    ],
    typicalSeconds: null,
} as JobProgress

describe('progress audit timeline', () => {
    it('orders rows oldest first and groups progress with status and logs by attempt', () => {
        const groups = buildTimeline(makeJob(), progress)
        expect(groups.map((group) => group.attempt)).toEqual([1, 2])
        expect(groups[0]?.rows.map((row) => row.kind)).toEqual([
            'progress',
            'status',
            'log',
        ])
        expect(groups[1]?.rows.map((row) => row.kind)).toEqual([
            'progress',
            'log',
            'status',
        ])
    })

    it('uses one chronological fallback group and detects attempt text', () => {
        const groups = buildFallbackTimeline(makeJob())
        expect(groups.map((group) => group.attempt)).toEqual([1, 2])
        expect(
            groups.flatMap((group) => group.rows).map((row) => row.at)
        ).toEqual([
            '2026-09-02T00:00:01Z',
            '2026-09-02T00:00:02Z',
            '2026-09-02T00:00:03Z',
            '2026-09-02T00:00:04Z',
        ])
    })
})
