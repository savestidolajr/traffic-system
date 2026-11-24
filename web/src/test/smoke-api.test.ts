// Live API smoke test. Skipped unless ARN_SMOKE_BASE_URL is set, so it stays
// out of normal `npm test` / PR runs. The Azure DevOps pipeline invokes it
// against the production URL post-swap by exporting ARN_SMOKE_BASE_URL.
//
// Usage:
//   ARN_SMOKE_BASE_URL=https://func-arntraffic-prod-aue.azurewebsites.net \
//     [ARN_SMOKE_BEARER=<aad-token>] \
//     npx vitest run src/test/smoke-api.test.ts

import { describe, it, expect } from 'vitest'

const baseUrl = process.env.ARN_SMOKE_BASE_URL
const bearer = process.env.ARN_SMOKE_BEARER

const liveSuite = baseUrl ? describe : describe.skip

liveSuite('live API smoke', () => {
    const authHeaders: Record<string, string> = bearer
        ? { Authorization: `Bearer ${bearer}` }
        : {}

    it('GET /api/health returns 200 or 503 with JSON body', async () => {
        const r = await fetch(`${baseUrl}/api/health`)
        expect([200, 503]).toContain(r.status)
        const body = await r.json()
        expect(body).toHaveProperty('status')
        expect(body).toHaveProperty('checks')
    })

    it('GET /api/schedules requires auth and returns 200 with bearer', async () => {
        if (!bearer) {
            // Without a token, expect 401/403 — proves Easy Auth is in front.
            const r = await fetch(`${baseUrl}/api/schedules`)
            expect([401, 403]).toContain(r.status)
            return
        }
        const r = await fetch(`${baseUrl}/api/schedules`, {
            headers: authHeaders,
        })
        expect(r.status).toBe(200)
        const body = await r.json()
        expect(Array.isArray(body) || typeof body === 'object').toBe(true)
    })

    it('GET /api/jobs requires auth', async () => {
        const r = await fetch(`${baseUrl}/api/jobs`, { headers: authHeaders })
        if (bearer) expect(r.status).toBe(200)
        else expect([401, 403]).toContain(r.status)
    })
})
