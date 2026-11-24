import { describe, it, expect } from 'vitest'
import type { JobSummary } from '../../types'
import { sameLocalDay, selectEarlierToday } from './earlier-today'

const mk = (
    over: Partial<JobSummary> & Pick<JobSummary, 'jobId' | 'createdAt'>
): JobSummary => ({
    station: 'KIIS1065',
    market: 'sydney',
    targetDurationSec: 30,
    status: 'PUBLISHED',
    lastUpdatedAt: over.createdAt,
    ...over,
})

// The bulletin under review: 2026-06-29 08:00 local, KIIS1065.
const anchor = {
    jobId: 'cur',
    station: 'KIIS1065',
    createdAt: '2026-06-29T08:00:00+10:00',
    timezone: 'Australia/Sydney',
}

describe('sameLocalDay', () => {
    it('matches two times on the same local calendar day', () => {
        expect(
            sameLocalDay(
                '2026-06-29T06:30:00+10:00',
                '2026-06-29T08:00:00+10:00',
                'Australia/Sydney'
            )
        ).toBe(true)
    })

    it('rejects times on different local days', () => {
        expect(
            sameLocalDay(
                '2026-06-28T23:59:00+10:00',
                '2026-06-29T08:00:00+10:00',
                'Australia/Sydney'
            )
        ).toBe(false)
    })
})

describe('selectEarlierToday', () => {
    it('includes earlier same-day, same-station, published bulletins newest-first', () => {
        const jobs = [
            mk({ jobId: 'a', createdAt: '2026-06-29T06:30:00+10:00' }),
            mk({
                jobId: 'b',
                createdAt: '2026-06-29T07:15:00+10:00',
                status: 'PUBLISHED',
            }),
        ]
        const result = selectEarlierToday(jobs, anchor)
        expect(result.map((j) => j.jobId)).toEqual(['b', 'a'])
    })

    it('excludes the current job itself', () => {
        const jobs = [
            mk({ jobId: 'cur', createdAt: '2026-06-29T08:00:00+10:00' }),
        ]
        expect(selectEarlierToday(jobs, anchor)).toEqual([])
    })

    it('excludes bulletins from a different station', () => {
        const jobs = [
            mk({
                jobId: 'x',
                station: 'GOLD96',
                createdAt: '2026-06-29T07:00:00+10:00',
            }),
        ]
        expect(selectEarlierToday(jobs, anchor)).toEqual([])
    })

    it('excludes bulletins from a different local day', () => {
        const jobs = [
            mk({ jobId: 'y', createdAt: '2026-06-28T07:00:00+10:00' }),
        ]
        expect(selectEarlierToday(jobs, anchor)).toEqual([])
    })

    it('excludes bulletins created at or after the current one', () => {
        const jobs = [
            mk({ jobId: 'same', createdAt: '2026-06-29T08:00:00+10:00' }),
            mk({ jobId: 'later', createdAt: '2026-06-29T08:30:00+10:00' }),
        ]
        expect(selectEarlierToday(jobs, anchor)).toEqual([])
    })

    it('excludes every non-published status, including waiting-approval, approved and fallback', () => {
        const jobs = [
            mk({
                jobId: 'failed',
                createdAt: '2026-06-29T06:00:00+10:00',
                status: 'FAILED',
            }),
            mk({
                jobId: 'synth',
                createdAt: '2026-06-29T06:10:00+10:00',
                status: 'SYNTHESIZING_AUDIO',
            }),
            mk({
                jobId: 'rejected',
                createdAt: '2026-06-29T06:20:00+10:00',
                status: 'REJECTED',
            }),
            mk({
                jobId: 'fallback',
                createdAt: '2026-06-29T06:30:00+10:00',
                status: 'FALLBACK_USED',
            }),
            mk({
                jobId: 'waiting',
                createdAt: '2026-06-29T06:40:00+10:00',
                status: 'WAITING_APPROVAL',
            }),
            mk({
                jobId: 'approved',
                createdAt: '2026-06-29T06:50:00+10:00',
                status: 'APPROVED',
            }),
            mk({
                jobId: 'published',
                createdAt: '2026-06-29T07:00:00+10:00',
                status: 'PUBLISHED',
            }),
        ]
        expect(selectEarlierToday(jobs, anchor).map((j) => j.jobId)).toEqual([
            'published',
        ])
    })

    it('uses the station timezone when the browser day differs', () => {
        const stationAnchor = {
            jobId: 'perth-current',
            station: 'PERTH',
            createdAt: '2026-06-29T15:30:00Z',
            timezone: 'Australia/Perth',
        }
        const jobs = [
            mk({
                jobId: 'perth-earlier',
                station: 'PERTH',
                createdAt: '2026-06-29T07:30:00Z',
            }),
        ]

        // Both instants are June 29 in Perth. Under the suite's Sydney browser
        // timezone they fall on June 29 and June 30, respectively.
        expect(
            selectEarlierToday(jobs, stationAnchor).map((j) => j.jobId)
        ).toEqual(['perth-earlier'])
    })
})
