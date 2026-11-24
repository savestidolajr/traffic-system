// =============================================================================
// Mock history-pagination cursor handling — composite `LastUpdatedAt|JobId`
// cursors must page like the real backend (finding 11), not re-serve the
// newest page. `useJobsHistory` sends the composite form; the real backend
// parses both forms. Reported by JobsListAudit, proven here.
// =============================================================================

import { describe, it, expect } from 'vitest'
import type { InternalAxiosRequestConfig } from 'axios'
import { createMockAdapter } from './adapter'

const get = (url: string) =>
    createMockAdapter()({
        method: 'get',
        url,
        headers: {},
    } as unknown as InternalAxiosRequestConfig)

describe('mock jobs pagination with composite cursors', () => {
    it('pages strictly older than a composite to=<LastUpdatedAt>|<JobId> cursor', async () => {
        // Page 1: the 5 newest jobs.
        const page1 = await get('/jobs?limit=5')
        const page1Ids = (page1.data.Items as { JobId: string }[]).map(
            (j) => j.JobId
        )
        const first = (
            page1.data.Items as { JobId: string; LastUpdatedAt: string }[]
        )[0]

        // "Load older" sends the composite cursor the client pins in
        // use-jobs-history.test.tsx. Date.parse chokes on the trailing
        // |JobId (NaN), and `NaN || Date.now()` fell back to "now" — which
        // re-serves the newest page forever once >limit jobs exist.
        const page2 = await get(
            `/jobs?limit=5&to=${encodeURIComponent(`${first.LastUpdatedAt}|${first.JobId}`)}`
        )
        const page2Ids = (page2.data.Items as { JobId: string }[]).map(
            (j) => j.JobId
        )

        expect(page2Ids.length).toBeGreaterThan(0)
        expect(page2Ids).not.toEqual(page1Ids)
        expect(page2Ids.includes(first.JobId)).toBe(false)
        expect(
            (page2.data.Items as { LastUpdatedAt: string }[]).every(
                (j) =>
                    new Date(j.LastUpdatedAt).getTime() <
                    new Date(first.LastUpdatedAt).getTime()
            )
        ).toBe(true)
    })

    it('still pages a bare timestamp cursor (regression guard)', async () => {
        const page1 = await get('/jobs?limit=5')
        const first = (page1.data.Items as { LastUpdatedAt: string }[])[0]
        const page2 = await get(
            `/jobs?limit=5&to=${encodeURIComponent(first.LastUpdatedAt)}`
        )
        const page2Ids = (page2.data.Items as { JobId: string }[]).map(
            (j) => j.JobId
        )
        expect(page2Ids.includes(first.JobId)).toBe(false)
    })
})
