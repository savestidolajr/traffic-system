import { describe, expect, it } from 'vitest'
import {
    JobProgressSchema,
    JobProgressEventSchema,
    isJobProgressUnavailable,
} from './progress-schemas'

describe('JobProgressSchema', () => {
    it('camelizes the wire document and event fields', () => {
        const progress = JobProgressSchema.parse({
            JobId: 'job-1',
            Status: 'SCRIPT_READY',
            Stage: 'Compose',
            StageStartedAt: '2026-09-02T00:00:00Z',
            Attempt: 1,
            MaxAttempts: 3,
            UpdatedAt: '2026-09-02T00:00:01Z',
            Version: 4,
            TypicalSeconds: { Compose: 4 },
            Events: [
                {
                    At: '2026-09-02T00:00:01Z',
                    Attempt: 1,
                    Stage: 'Compose',
                    Kind: 'SubStep',
                    Detail: 'Candidate returned.',
                    Counts: { candidate: 2, words: 86 },
                    DurationMs: 1500,
                    Model: 'gpt-5',
                    Route: 'compose',
                },
            ],
        })

        expect(progress.stage).toBe('Compose')
        expect(progress.typicalSeconds).toEqual({ Compose: 4 })
        expect(progress.events[0]).toMatchObject({
            at: '2026-09-02T00:00:01Z',
            attempt: 1,
            counts: { candidate: 2, words: 86 },
            durationMs: 1500,
            model: 'gpt-5',
            route: 'compose',
        })
    })

    it('maps unknown future stage and event members to Unknown', () => {
        const event = JobProgressEventSchema.parse({
            At: '2026-09-02T00:00:01Z',
            Attempt: 1,
            Stage: 'FutureStage',
            Kind: 'FutureKind',
            Detail: 'A future event.',
        })
        expect(event.stage).toBe('Unknown')
        expect(event.kind).toBe('Unknown')
    })

    it('keeps a missing progress document as a supported result', () => {
        expect(isJobProgressUnavailable({ unavailable: true })).toBe(true)
        expect(isJobProgressUnavailable(undefined)).toBe(false)
    })
})
