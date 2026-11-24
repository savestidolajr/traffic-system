import { describe, it, expect } from 'vitest'
import {
    isFlaggedLog,
    resolveStageTitle,
    sourceDiagnosticLogs,
} from './system-audit-utils'

describe('resolveStageTitle', () => {
    it('keeps the completed-milestone label when the note describes a finished action', () => {
        expect(
            resolveStageTitle(
                'INCIDENTS_SELECTED',
                'Traffic fetched.',
                'Incidents Selected'
            )
        ).toBe('Incidents Selected')
    })

    it('keeps the completed-milestone label when there is no note at all', () => {
        expect(resolveStageTitle('HERE_FETCHED', '', 'Traffic Fetched')).toBe(
            'Traffic Fetched'
        )
    })

    it('swaps to the in-flight label when the note is still describing work in progress', () => {
        // The reported case: HERE_FETCHED is stamped at phase START with a
        // note describing the fetch that's about to run, not one that's done.
        expect(
            resolveStageTitle(
                'HERE_FETCHED',
                'Fetching traffic...',
                'Traffic Fetched'
            )
        ).toBe('Fetching traffic')
    })

    it('falls back to the completed label for a status with no in-flight vocabulary entry', () => {
        expect(
            resolveStageTitle('PUBLISHED', 'Publishing...', 'Published')
        ).toBe('Published')
    })
})

describe('isFlaggedLog', () => {
    it('flags ERROR and WARN levels regardless of message', () => {
        expect(isFlaggedLog({ at: '', level: 'ERROR', message: 'x' })).toBe(
            true
        )
        expect(isFlaggedLog({ at: '', level: 'WARN', message: 'x' })).toBe(true)
    })

    it('flags an escalation message even when logged at INFO', () => {
        // The reported case: "Job escalated: playout imminent" is real
        // production Level 1 (INFO), but is exactly the signal an operator
        // should not have to expand a fold to discover.
        expect(
            isFlaggedLog({
                at: '',
                level: 'INFO',
                message:
                    'Job escalated: playout imminent — inside the escalation window',
            })
        ).toBe(true)
    })

    it('does not flag ordinary INFO lines', () => {
        expect(
            isFlaggedLog({ at: '', level: 'INFO', message: 'Traffic fetched.' })
        ).toBe(false)
        expect(
            isFlaggedLog({
                at: '',
                level: 'INFO',
                message: 'Starting orchestration',
            })
        ).toBe(false)
    })
})

describe('sourceDiagnosticLogs', () => {
    it('creates flagged entries for failed and stale sources only', () => {
        const logs = sourceDiagnosticLogs(
            [
                {
                    sourceId: 'qldtraffic',
                    outcome: 'Failed',
                    detail: 'HTTP 429 (rate limited)',
                    dataAsOfUtc: null,
                    staleSeconds: null,
                },
                {
                    sourceId: 'mainroads-wa',
                    outcome: 'ServedStale',
                    detail: 'HTTP 503',
                    dataAsOfUtc: '2026-08-24T00:31:12Z',
                    staleSeconds: 420,
                },
                {
                    sourceId: 'victraffic',
                    outcome: 'Skipped',
                    detail: null,
                    dataAsOfUtc: null,
                    staleSeconds: null,
                },
            ],
            '2026-08-24T00:40:00Z'
        )
        expect(logs).toHaveLength(2)
        expect(logs[0]).toMatchObject({
            level: 'ERROR',
            message: 'QLDTraffic: unavailable — HTTP 429 (rate limited)',
        })
        expect(logs[1]).toMatchObject({
            level: 'WARN',
            message: 'Main Roads WA: served data from 7 minutes ago — HTTP 503',
        })
        expect(logs.every(isFlaggedLog)).toBe(true)
    })

    it('keeps an all-live or empty diagnostics list quiet', () => {
        expect(
            sourceDiagnosticLogs(
                [
                    {
                        sourceId: 'here',
                        outcome: 'Ok',
                        detail: null,
                        dataAsOfUtc: null,
                        staleSeconds: null,
                    },
                ],
                '2026-08-24T00:40:00Z'
            )
        ).toEqual([])
        expect(sourceDiagnosticLogs(undefined, '')).toEqual([])
    })
})
