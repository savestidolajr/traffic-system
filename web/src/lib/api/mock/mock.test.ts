// =============================================================================
// Mock Layer Contract Test — validates fixtures against real Zod schemas
// =============================================================================

import { describe, it, expect } from 'vitest'
import { AxiosHeaders, type InternalAxiosRequestConfig } from 'axios'
import {
    JobsListResponseSchema,
    JobSchema,
    RewriteResponseSchema,
} from '../../../features/jobs/api/schemas'
import { JobProgressSchema } from '../../../features/jobs/api/progress-schemas'
import {
    JobDeltaSchema,
    LedgerSchema,
} from '../../../features/ledger/api/schemas'
import { VoicesListResponseSchema } from '../../../features/voices/api/schemas'
import { VoiceProfilesListResponseSchema } from '../../../features/voices/api/profile-schemas'
import { SchedulesListResponseSchema } from '../../../features/schedules/api/schemas'
import {
    DeliveryStyleApiSchema,
    DeliveryStylesListResponseSchema,
    DeliveryStylePreviewResponseSchema,
} from '../../../features/delivery/api/delivery-schemas'
import {
    BookendApiSchema,
    BookendsListResponseSchema,
    BookendPreviewResponseSchema,
} from '../../../features/bookends/api/bookend-schemas'
import {
    DictionaryApiSchema,
    DictionariesListResponseSchema,
} from '../../../features/pronunciation/api/pronunciation-schemas'
import {
    JOB_FIXTURES,
    VOICE_FIXTURES,
    VOICE_PROFILE_FIXTURES,
    SCHEDULE_FIXTURES,
    DELIVERY_STYLE_FIXTURES,
    BOOKEND_FIXTURES,
    PRONUNCIATION_DICTIONARY_FIXTURES,
} from './fixtures'
import {
    GuidanceListSchema,
    GuidanceCandidateListSchema,
} from '../../../features/guidance/api/schemas'
import { ActivityLogListResponseApiSchema } from '../../../features/activity/api/activity-schemas'
import { OperatorDirectorySchema } from '../../../features/operators/api/schemas'
import { SpotChecksResponseSchema } from '../../../features/activity/api/ledger-periphery-hooks'
import { BulletinPlaybookApiSchema } from '../../../features/playbooks/api/playbook-schemas'
import { createMockAdapter } from './adapter'
import { sectionsFromSchema } from '../../../features/config-transfer/api/config-transfer-hooks'
import { SECTIONS } from '../../../features/config-transfer/lib/section-meta'

describe('Mock fixtures satisfy real Zod schemas', () => {
    it('every JOB_FIXTURE parses as a full JobSchema', () => {
        for (const fixture of JOB_FIXTURES) {
            const result = JobSchema.safeParse(fixture)
            if (!result.success) {
                console.error(
                    `FAILED: ${fixture.JobId} (${fixture.Status})`,
                    result.error.flatten()
                )
            }
            expect(
                result.success,
                `Job ${fixture.JobId} [${fixture.Status}]`
            ).toBe(true)
        }
    })

    it('jobs list response parses as JobsListResponseSchema', () => {
        const listPayload = {
            Items: JOB_FIXTURES.map((j) => ({
                JobId: j.JobId,
                CreatedAt: j.CreatedAt,
                Station: j.Station,
                Market: j.Market,
                TargetDurationSec: j.TargetDurationSec,
                Status: j.Status,
                LastUpdatedAt: j.LastUpdatedAt,
                MeasuredDurationSec: j.MeasuredDurationSec ?? null,
                DeltaSec: j.DeltaSec ?? null,
            })),
            Total: JOB_FIXTURES.length,
        }
        const result = JobsListResponseSchema.safeParse(listPayload)
        if (!result.success) {
            console.error(
                'JobsListResponseSchema parse failed',
                result.error.flatten()
            )
        }
        expect(result.success).toBe(true)
    })

    it('voices list response parses as VoicesListResponseSchema', () => {
        const payload = { Voices: VOICE_FIXTURES }
        const result = VoicesListResponseSchema.safeParse(payload)
        if (!result.success) {
            console.error(
                'VoicesListResponseSchema parse failed',
                result.error.flatten()
            )
        }
        expect(result.success).toBe(true)
    })

    it('schedules list response parses as SchedulesListResponseSchema', () => {
        const payload = {
            Items: SCHEDULE_FIXTURES,
            Total: SCHEDULE_FIXTURES.length,
        }
        const result = SchedulesListResponseSchema.safeParse(payload)
        if (!result.success) {
            console.error(
                'SchedulesListResponseSchema parse failed',
                result.error.flatten()
            )
        }
        expect(result.success).toBe(true)
    })

    it('voice profiles list response parses as VoiceProfilesListResponseSchema', () => {
        const payload = {
            Items: VOICE_PROFILE_FIXTURES,
            Total: VOICE_PROFILE_FIXTURES.length,
        }
        const result = VoiceProfilesListResponseSchema.safeParse(payload)
        if (!result.success) {
            console.error(
                'VoiceProfilesListResponseSchema parse failed',
                result.error.flatten()
            )
        }
        expect(result.success).toBe(true)
    })

    it('every DELIVERY_STYLE_FIXTURE parses as DeliveryStyleApiSchema', () => {
        for (const fixture of DELIVERY_STYLE_FIXTURES) {
            const result = DeliveryStyleApiSchema.safeParse(fixture)
            if (!result.success) {
                console.error(
                    `FAILED: ${fixture.Id} (${fixture.Name})`,
                    result.error.flatten()
                )
            }
            expect(result.success, `DeliveryStyle ${fixture.Id}`).toBe(true)
        }
    })

    it('delivery styles list response parses as DeliveryStylesListResponseSchema', () => {
        const payload = {
            Items: DELIVERY_STYLE_FIXTURES,
            Total: DELIVERY_STYLE_FIXTURES.length,
        }
        const result = DeliveryStylesListResponseSchema.safeParse(payload)
        if (!result.success) {
            console.error(
                'DeliveryStylesListResponseSchema parse failed',
                result.error.flatten()
            )
        }
        expect(result.success).toBe(true)
    })

    it('every BOOKEND_FIXTURE parses as BookendApiSchema', () => {
        for (const fixture of BOOKEND_FIXTURES) {
            const result = BookendApiSchema.safeParse(fixture)
            if (!result.success) {
                console.error(
                    `FAILED: ${fixture.Id} (${fixture.Name})`,
                    result.error.flatten()
                )
            }
            expect(result.success, `Bookend ${fixture.Id}`).toBe(true)
        }
    })

    it('bookends list response parses as BookendsListResponseSchema', () => {
        const payload = {
            Items: BOOKEND_FIXTURES,
            Total: BOOKEND_FIXTURES.length,
        }
        const result = BookendsListResponseSchema.safeParse(payload)
        if (!result.success) {
            console.error(
                'BookendsListResponseSchema parse failed',
                result.error.flatten()
            )
        }
        expect(result.success).toBe(true)
    })

    it('every PRONUNCIATION_DICTIONARY_FIXTURE parses as DictionaryApiSchema', () => {
        for (const fixture of PRONUNCIATION_DICTIONARY_FIXTURES) {
            const result = DictionaryApiSchema.safeParse(fixture)
            if (!result.success) {
                console.error(
                    `FAILED: ${fixture.Id} (${fixture.Name})`,
                    result.error.flatten()
                )
            }
            expect(
                result.success,
                `PronunciationDictionary ${fixture.Id}`
            ).toBe(true)
        }
    })

    it('pronunciation dictionaries list response parses as DictionariesListResponseSchema', () => {
        const payload = {
            Items: PRONUNCIATION_DICTIONARY_FIXTURES,
            Total: PRONUNCIATION_DICTIONARY_FIXTURES.length,
        }
        const result = DictionariesListResponseSchema.safeParse(payload)
        if (!result.success) {
            console.error(
                'DictionariesListResponseSchema parse failed',
                result.error.flatten()
            )
        }
        expect(result.success).toBe(true)
    })
})

describe('Voice profile store', () => {
    const baseSettings = {
        Speed: 1.0,
        Stability: 0.5,
        SimilarityBoost: 0.8,
        Style: 0.0,
        UseSpeakerBoost: false,
    }

    it('creates a variant minting version 1, retrievable by id', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        const created = await store.createVoiceProfile({
            Name: 'Test Variant',
            BaseVoiceId: 'oKDz3nEzNfwKIVvUwPs0',
            BaseVoiceName: 'ARN Hudson',
            Model: 'eleven_multilingual_v2',
            Settings: baseSettings,
        })
        expect(created.CurrentVersion).toBe(1)
        expect(created.Versions).toHaveLength(1)
        // Fresh version has no calibration memory until it airs.
        expect(created.Versions[0].Calibration).toBeNull()
        const fetched = await store.getVoiceProfile(created.Id)
        expect(fetched?.Name).toBe('Test Variant')
    })

    it('mints a new version when settings change', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        const created = await store.createVoiceProfile({
            Name: 'Versioning Test',
            BaseVoiceId: 'oKDz3nEzNfwKIVvUwPs0',
            BaseVoiceName: 'ARN Hudson',
            Model: 'eleven_v3',
            Settings: baseSettings,
        })
        const updated = await store.updateVoiceProfile(created.Id, {
            Name: created.Name,
            Model: 'eleven_v3',
            Settings: { ...baseSettings, Speed: 1.07 },
        })
        expect(updated?.CurrentVersion).toBe(2)
        expect(updated?.Versions).toHaveLength(2)
    })

    it('does not mint a version for metadata-only edits', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        const created = await store.createVoiceProfile({
            Name: 'Metadata Test',
            BaseVoiceId: 'oKDz3nEzNfwKIVvUwPs0',
            BaseVoiceName: 'ARN Hudson',
            Model: 'eleven_v3',
            Settings: baseSettings,
        })
        const updated = await store.updateVoiceProfile(created.Id, {
            Name: 'Renamed only',
            Description: 'new blurb',
            Model: 'eleven_v3',
            Settings: baseSettings,
        })
        expect(updated?.CurrentVersion).toBe(1)
        expect(updated?.Versions).toHaveLength(1)
        expect(updated?.Name).toBe('Renamed only')
    })

    it('restores an old version forward as the new current', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        const created = await store.createVoiceProfile({
            Name: 'Restore Test',
            BaseVoiceId: 'oKDz3nEzNfwKIVvUwPs0',
            BaseVoiceName: 'ARN Hudson',
            Model: 'eleven_v3',
            Settings: baseSettings,
        })
        await store.updateVoiceProfile(created.Id, {
            Name: created.Name,
            Model: 'eleven_v3',
            Settings: { ...baseSettings, Speed: 1.12 },
        })
        const restored = await store.restoreVoiceProfileVersion(created.Id, 1)
        expect(restored?.CurrentVersion).toBe(3)
        const newCurrent = restored?.Versions.find((v) => v.Version === 3)
        const v1 = restored?.Versions.find((v) => v.Version === 1)
        expect(newCurrent?.Settings).toEqual(v1?.Settings)
    })

    it('preview WPM scales with speed', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        const slow = await store.previewVoice({
            BaseVoiceId: 'oKDz3nEzNfwKIVvUwPs0',
            Model: 'eleven_multilingual_v2',
            Settings: { ...baseSettings, Speed: 1.0 },
        })
        const fast = await store.previewVoice({
            BaseVoiceId: 'oKDz3nEzNfwKIVvUwPs0',
            Model: 'eleven_multilingual_v2',
            Settings: { ...baseSettings, Speed: 1.15 },
        })
        expect(fast.WordsPerMinute).toBeGreaterThan(slow.WordsPerMinute)
    })

    it('returns null when updating an unknown profile', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        const result = await store.updateVoiceProfile('nope', {
            Name: 'x',
            Model: 'eleven_multilingual_v2',
            Settings: baseSettings,
        })
        expect(result).toBeNull()
    })
    it('preview with PaceBoostWpm returns higher WPM and EffectivePaceBoostWpm > 0', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        const noBoost = await store.previewVoice({
            BaseVoiceId: 'oKDz3nEzNfwKIVvUwPs0',
            Model: 'eleven_multilingual_v2',
            Settings: { ...baseSettings, Speed: 1.0 },
        })
        const withBoost = await store.previewVoice({
            BaseVoiceId: 'oKDz3nEzNfwKIVvUwPs0',
            Model: 'eleven_multilingual_v2',
            Settings: { ...baseSettings, Speed: 1.0 },
            PaceBoostWpm: 20,
        })
        expect(withBoost.WordsPerMinute).toBeGreaterThan(noBoost.WordsPerMinute)
        expect(withBoost.EffectivePaceBoostWpm).toBeGreaterThan(0)
    })

    it('preview with PaceBoostWpm omitted or 0 returns EffectivePaceBoostWpm === 0', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        const omitted = await store.previewVoice({
            BaseVoiceId: 'oKDz3nEzNfwKIVvUwPs0',
            Model: 'eleven_multilingual_v2',
            Settings: { ...baseSettings, Speed: 1.0 },
        })
        const zero = await store.previewVoice({
            BaseVoiceId: 'oKDz3nEzNfwKIVvUwPs0',
            Model: 'eleven_multilingual_v2',
            Settings: { ...baseSettings, Speed: 1.0 },
            PaceBoostWpm: 0,
        })
        expect(omitted.EffectivePaceBoostWpm).toBe(0)
        expect(zero.EffectivePaceBoostWpm).toBe(0)
        expect(omitted.WordsPerMinute).toBe(zero.WordsPerMinute)
    })

    it('updateVoiceProfile with only PaceBoostWpm changed still mints a new version', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        const created = await store.createVoiceProfile({
            Name: 'Pace Boost Only',
            BaseVoiceId: 'oKDz3nEzNfwKIVvUwPs0',
            BaseVoiceName: 'ARN Hudson',
            Model: 'eleven_v3',
            Settings: baseSettings,
            PaceBoostWpm: 0,
        })
        expect(created.CurrentVersion).toBe(1)
        const updated = await store.updateVoiceProfile(created.Id, {
            Name: created.Name,
            Model: 'eleven_v3',
            Settings: baseSettings,
            PaceBoostWpm: 15,
        })
        expect(updated?.CurrentVersion).toBe(2)
        expect(updated?.Versions).toHaveLength(2)
        expect(updated?.Versions[1].PaceBoostWpm).toBe(15)
    })

    it('restoreVoiceProfileVersion carries the restored version PaceBoostWpm', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        const created = await store.createVoiceProfile({
            Name: 'Restore Pace',
            BaseVoiceId: 'oKDz3nEzNfwKIVvUwPs0',
            BaseVoiceName: 'ARN Hudson',
            Model: 'eleven_v3',
            Settings: baseSettings,
            PaceBoostWpm: 10,
        })
        await store.updateVoiceProfile(created.Id, {
            Name: created.Name,
            Model: 'eleven_v3',
            Settings: baseSettings,
            PaceBoostWpm: 0,
        })
        const restored = await store.restoreVoiceProfileVersion(created.Id, 1)
        expect(restored?.CurrentVersion).toBe(3)
        const newCurrent = restored?.Versions.find((v) => v.Version === 3)
        expect(newCurrent?.PaceBoostWpm).toBe(10)
    })
})

describe('Mock store routes', () => {
    it('getPriorityRoads returns seeded roads for known market', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        // Seeded by marketId — the canonical key the app queries with
        const result = await store.getPriorityRoads('sydney')
        expect(result.Market).toBe('sydney')
        expect(result.Roads.length).toBeGreaterThan(0)
        expect(result.Roads.map((r) => r.Name)).toContain(
            'Sydney Harbour Bridge'
        )
    })

    it('getPriorityRoads returns empty for unknown market', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        const result = await store.getPriorityRoads('Unknown')
        expect(result.Roads).toEqual([])
    })

    it('setPriorityRoads round-trips', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        await store.setPriorityRoads('Brisbane', [
            { name: 'M1' },
            { name: 'Gateway' },
        ])
        const result = await store.getPriorityRoads('Brisbane')
        expect(result.Roads.map((r) => r.Name)).toEqual(['M1', 'Gateway'])
    })

    it('getOperatorScope returns unscoped', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        const result = await store.getOperatorScope()
        expect(result.Scoped).toBe(false)
        expect(result.Markets).toBeNull()
    })

    it('listOperators returns upserted directory entries with full shape', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        await store.upsertOperator('aad-operator-001', {
            Markets: ['sydney'],
        })
        await store.upsertOperator('aad-operator-002', {
            Markets: ['melbourne', 'brisbane'],
        })
        const operators = await store.listOperators()
        expect(operators.length).toBeGreaterThanOrEqual(2)
        expect(operators[0]).toHaveProperty('ObjectId')
        expect(operators[0]).toHaveProperty('DisplayName')
        expect(operators[0]).toHaveProperty('UserPrincipalName')
        expect(operators[0]).toHaveProperty('Roles')
        expect(operators[0]).toHaveProperty('Source')
        expect(operators[0]).toHaveProperty('Markets')
        expect(operators[0]).toHaveProperty('IsAdmin')
        expect(operators[0]).toHaveProperty('UpdatedAtUtc')
        expect(operators[0]).toHaveProperty('UpdatedBy')
    })

    it('upsertOperator creates new assignment', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        const result = await store.upsertOperator('aad-operator-new', {
            Markets: ['Perth'],
        })
        expect(result.ObjectId).toBe('aad-operator-new')
        expect(result.Markets).toEqual(['Perth'])
    })

    it('upsertOperator updates existing assignment', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        const result = await store.upsertOperator('aad-operator-001', {
            Markets: ['Sydney'],
        })
        expect(result.Markets).toEqual(['Sydney'])
    })

    it('deleteOperator removes assignment', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        await store.deleteOperator('aad-operator-001')
        const operators = await store.listOperators()
        expect(
            operators.find((o) => o.ObjectId === 'aad-operator-001')
        ).toBeUndefined()
    })
})

describe('Mock adapter aggregate + filtered routes', () => {
    const get = async (url: string) => {
        const { createMockAdapter } = await import('./adapter')
        const adapter = createMockAdapter()
        return adapter({
            method: 'get',
            url,
            headers: {},
        } as unknown as InternalAxiosRequestConfig)
    }

    it('operators route returns the live directory envelope', async () => {
        const parsed = OperatorDirectorySchema.parse(
            (await get('/api/operators')).data
        )
        expect(parsed.directoryStatus).toBe('Ok')
        expect(parsed.directoryError).toBeNull()
        expect(parsed.operators).toEqual(
            expect.arrayContaining([
                expect.objectContaining({
                    displayName: 'Avery Admin',
                    roles: ['Admin'],
                    isAdmin: true,
                }),
                expect.objectContaining({
                    displayName: 'Casey New Operator',
                    roles: ['Operator'],
                    markets: [],
                }),
            ])
        )
    })

    /**
     * The export picker reads its section list from `GET /config/schema` at
     * runtime, so mock mode and the deployed contract must agree on what a
     * bundle contains. The mock used to hardcode its own eight-section subset —
     * the same drift that made four sections invisible in the picker, in a third
     * place. Its properties are now derived from `SECTIONS`; this proves it.
     */
    it('config schema publishes exactly the known bundle sections', async () => {
        const res = await get('/api/config/schema')
        expect(sectionsFromSchema(res.data)).toEqual([...SECTIONS])
    })

    it('guidance inventory merges global + requested markets in one response', async () => {
        const { MockStore } = await import('./store')
        const store = new MockStore()
        const expectedGlobal = await store.listGlobalGuidance()
        const expectedSyd = await store.listMarketGuidance('syd-1')

        const res = await get('/api/guidance/inventory?markets=syd-1')
        const parsed = GuidanceListSchema.parse(res.data)

        expect(parsed.length).toBe(expectedGlobal.length + expectedSyd.length)
        const ids = new Set(parsed.map((g) => g.id))
        for (const g of [...expectedGlobal, ...expectedSyd])
            expect(ids.has(g.Id)).toBe(true)
    })

    it('guidance candidates retain lifecycle metadata from the backend DTO', async () => {
        const { MockStore } = await import('./store')
        const candidates = GuidanceCandidateListSchema.parse(
            await new MockStore().listCandidates('sydney')
        )
        expect(candidates[0]).toEqual(
            expect.objectContaining({
                status: 'Active',
                origin: 'Operator',
                conflictWithLessonId: null,
            })
        )
    })

    it('guidance inventory with no markets returns only global', async () => {
        const { MockStore } = await import('./store')
        const expectedGlobal = await new MockStore().listGlobalGuidance()
        const parsed = GuidanceListSchema.parse(
            (await get('/api/guidance/inventory?markets=')).data
        )
        expect(parsed.length).toBe(expectedGlobal.length)
    })

    it('priority-roads inventory returns one entry per requested market', async () => {
        const res = await get('/api/priority-roads/inventory?markets=sydney')
        expect(Array.isArray(res.data)).toBe(true)
        expect(res.data).toHaveLength(1)
        expect(res.data[0].Market).toBe('sydney')
        expect(
            res.data[0].Roads.map((r: { Name: string }) => r.Name)
        ).toContain('Sydney Harbour Bridge')
    })

    it('jobs list status filter returns only jobs of the requested status', async () => {
        const all = JobsListResponseSchema.parse((await get('/api/jobs')).data)
        expect(all.jobs.length).toBeGreaterThan(0)
        const target = all.jobs[0].status
        const expected = all.jobs.filter((j) => j.status === target)

        const filtered = JobsListResponseSchema.parse(
            (await get(`/api/jobs?status=${target}`)).data
        )
        expect(filtered.jobs.length).toBe(expected.length)
        expect(filtered.jobs.every((j) => j.status === target)).toBe(true)
    })

    it('jobs list without ?limit keeps the old unbounded shape (no HasMore)', async () => {
        const res = await get('/api/jobs')
        expect(res.data.HasMore).toBeUndefined()
    })

    it('jobs list with ?limit pages newest-first and flags HasMore correctly', async () => {
        const unbounded = JobsListResponseSchema.parse(
            (await get('/api/jobs')).data
        )
        expect(unbounded.jobs.length).toBeGreaterThan(1)

        const page1 = JobsListResponseSchema.parse(
            (await get('/api/jobs?limit=1')).data
        )
        expect(page1.jobs.length).toBe(1)
        expect(page1.hasMore).toBe(unbounded.jobs.length > 1)

        // Cursor correctness: paging with `to` = the oldest loaded job's exact
        // lastUpdatedAt must exclude that job, not re-return it.
        const cursor = page1.jobs[0].lastUpdatedAt
        const page2 = JobsListResponseSchema.parse(
            (await get(`/api/jobs?limit=5&to=${encodeURIComponent(cursor)}`))
                .data
        )
        expect(page2.jobs.every((j) => j.jobId !== page1.jobs[0].jobId)).toBe(
            true
        )
        expect(
            page2.jobs.every(
                (j) =>
                    new Date(j.lastUpdatedAt).getTime() <
                    new Date(cursor).getTime()
            )
        ).toBe(true)
    })

    it('activity list without ?limit keeps the old unbounded shape (no HasMore)', async () => {
        const res = await get('/api/activity')
        expect(res.data.HasMore).toBeUndefined()
    })

    it('activity list with ?limit pages newest-first and flags HasMore correctly', async () => {
        const unbounded = ActivityLogListResponseApiSchema.parse(
            (await get('/api/activity')).data
        )
        expect(unbounded.items.length).toBeGreaterThan(1)

        const page1 = ActivityLogListResponseApiSchema.parse(
            (await get('/api/activity?limit=1')).data
        )
        expect(page1.items.length).toBe(1)
        expect(page1.hasMore).toBe(unbounded.items.length > 1)

        const cursor = page1.items[0].atUtc
        const page2 = ActivityLogListResponseApiSchema.parse(
            (
                await get(
                    `/api/activity?limit=5&to=${encodeURIComponent(cursor)}`
                )
            ).data
        )
        expect(
            page2.items.every(
                (e) => new Date(e.atUtc).getTime() < new Date(cursor).getTime()
            )
        ).toBe(true)
    })
})
describe('Mock adapter parity routes', () => {
    function makeClient() {
        const adapter = createMockAdapter()
        const request = (method: string, url: string, data?: unknown) =>
            adapter({
                method,
                url,
                headers: {},
                data,
            } as InternalAxiosRequestConfig)
        return {
            get: (url: string) => request('get', url),
            post: (url: string, data?: unknown) => request('post', url, data),
            del: (url: string) => request('delete', url),
        }
    }

    it('serves the market ledger', async () => {
        const { get } = makeClient()
        const ledger = LedgerSchema.parse(
            (await get('/api/ledger/sydney')).data
        )
        expect(ledger.market).toBe('sydney')
    })

    it('accepts an event verdict', async () => {
        const { post } = makeClient()
        const response = await post(
            '/api/ledger/sydney/events/event-1/verdict',
            { Verdict: 'Omitted', Notes: 'duplicate' }
        )
        expect(response.status).toBe(204)
    })

    it('serves a job delta', async () => {
        const { get } = makeClient()
        const delta = JobDeltaSchema.parse(
            (await get(`/api/jobs/${JOB_FIXTURES[0].JobId}/delta`)).data
        )
        expect(delta.jobId).toBe(JOB_FIXTURES[0].JobId)
    })

    it('serves spot checks and records a spot check', async () => {
        const { get, post } = makeClient()
        const jobId = JOB_FIXTURES[0].JobId
        expect(
            SpotChecksResponseSchema.parse((await get('/api/spot-checks')).data)
                .items
        ).toEqual([])
        expect((await post(`/api/spot-checks/${jobId}`)).status).toBe(204)
        const checks = SpotChecksResponseSchema.parse(
            (await get('/api/spot-checks')).data
        )
        expect(checks.items).toEqual([expect.objectContaining({ jobId })])
    })

    it('supports playbook archive, unarchive, and delete', async () => {
        const { post, del } = makeClient()
        const id = 'pb-mock-sydney'
        const archived = BulletinPlaybookApiSchema.parse(
            (await post(`/api/playbooks/${id}/archive`)).data
        )
        expect(archived.status).toBe('archived')
        const active = BulletinPlaybookApiSchema.parse(
            (await post(`/api/playbooks/${id}/unarchive`)).data
        )
        expect(active.status).toBe('active')
        expect((await del(`/api/playbooks/${id}`)).status).toBe(204)
    })

    it('serves and deletes a schedule by market id', async () => {
        const { get, del } = makeClient()
        const marketId = 'sydney'
        const schedule = SchedulesListResponseSchema.parse({
            Items: [(await get(`/api/schedules/${marketId}`)).data],
            Total: 1,
        }).schedules[0]
        expect(schedule.marketId).toBe(marketId)
        expect((await del(`/api/schedules/${marketId}`)).status).toBe(204)
        expect((await get(`/api/schedules/${marketId}`)).status).toBe(404)
    })
    it('delivery preview retains the backend TagsSupported field', async () => {
        const { post } = makeClient()
        const preview = DeliveryStylePreviewResponseSchema.parse(
            (
                await post('/api/delivery-styles/preview', {
                    Policy: {
                        ToneDescription: 'clear',
                        AllowedTags: ['[excited]'],
                        Intensity: 0.5,
                    },
                })
            ).data
        )
        expect(preview.tagsSupported).toBe(true)
    })
    it('serves monotonic progress and honours If-None-Match', async () => {
        const adapter = createMockAdapter()
        const jobId = JOB_FIXTURES[0].JobId
        const request = (headers: Record<string, string> = {}) =>
            adapter({
                method: 'get',
                url: `/api/jobs/${jobId}/progress`,
                headers,
            } as unknown as InternalAxiosRequestConfig)

        const first = await request()
        expect(first.status).toBe(200)
        const progress = JobProgressSchema.parse(first.data)
        expect(progress.events.length).toBeGreaterThan(0)
        const etag = new AxiosHeaders(first.headers).get('etag')
        expect(etag).toBeTruthy()

        const unchanged = await request({ 'If-None-Match': etag ?? '' })
        expect(unchanged.status).toBe(304)
        expect(unchanged.data).toEqual({})

        const terminalJob = JOB_FIXTURES.find(
            (fixture) => fixture.Status === 'APPROVED'
        )
        expect(terminalJob).toBeDefined()
        const terminal = await adapter({
            method: 'get',
            url: `/api/jobs/${terminalJob?.JobId}/progress`,
            headers: {},
        } as unknown as InternalAxiosRequestConfig)
        expect(terminal.status).toBe(404)
    })
})

describe('Job approve/publish optimistic etag precondition (mock store)', () => {
    const JOB_ID = 'arn-job-00007'

    function makeClient() {
        const adapter = createMockAdapter()
        const get = (url: string) =>
            adapter({
                method: 'get',
                url,
                headers: {},
            } as unknown as InternalAxiosRequestConfig)
        const post = (url: string, data?: unknown) =>
            adapter({
                method: 'post',
                url,
                headers: {},
                data,
            } as unknown as InternalAxiosRequestConfig)
        return { get, post }
    }

    it('approve with a stale expectedEtag rejects with 409 and refresh guidance, leaving the job unchanged', async () => {
        const { get, post } = makeClient()
        const before = JobSchema.parse((await get(`/api/jobs/${JOB_ID}`)).data)
        expect(before.etag).toBeTruthy()

        await expect(
            post(`/api/jobs/${JOB_ID}/approve`, {
                expectedEtag: 'stale-etag-does-not-match',
            })
        ).rejects.toMatchObject({
            response: {
                status: 409,
                data: expect.objectContaining({
                    Error: expect.stringContaining('Refresh and review'),
                }),
            },
        })

        const after = JobSchema.parse((await get(`/api/jobs/${JOB_ID}`)).data)
        expect(after.status).not.toBe('APPROVED')
        expect(after.etag).toBe(before.etag)
    })

    it('approve with the current expectedEtag succeeds and bumps the etag', async () => {
        const { get, post } = makeClient()
        const before = JobSchema.parse((await get(`/api/jobs/${JOB_ID}`)).data)

        await post(`/api/jobs/${JOB_ID}/approve`, {
            expectedEtag: before.etag,
        })

        const after = JobSchema.parse((await get(`/api/jobs/${JOB_ID}`)).data)
        expect(after.status).toBe('APPROVED')
        expect(after.etag).toBeTruthy()
        expect(after.etag).not.toBe(before.etag)
    })

    it('approve with no expectedEtag (legacy caller) still succeeds', async () => {
        const { get, post } = makeClient()
        await post(`/api/jobs/${JOB_ID}/approve`)
        const after = JobSchema.parse((await get(`/api/jobs/${JOB_ID}`)).data)
        expect(after.status).toBe('APPROVED')
    })

    it('publish with a stale expectedEtag rejects with 409 and does not publish', async () => {
        const { get, post } = makeClient()
        const before = JobSchema.parse((await get(`/api/jobs/${JOB_ID}`)).data)

        await expect(
            post(`/api/jobs/${JOB_ID}/publish`, {
                expectedEtag: 'stale-etag-does-not-match',
            })
        ).rejects.toMatchObject({
            response: {
                status: 409,
                data: expect.objectContaining({
                    Error: expect.stringContaining('Refresh and review'),
                }),
            },
        })

        const after = JobSchema.parse((await get(`/api/jobs/${JOB_ID}`)).data)
        expect(after.status).not.toBe('PUBLISHED')
        expect(after.etag).toBe(before.etag)
    })

    it('publish with the current expectedEtag succeeds and bumps the etag', async () => {
        const { get, post } = makeClient()
        const before = JobSchema.parse((await get(`/api/jobs/${JOB_ID}`)).data)

        await post(`/api/jobs/${JOB_ID}/publish`, {
            expectedEtag: before.etag,
        })

        const after = JobSchema.parse((await get(`/api/jobs/${JOB_ID}`)).data)
        expect(after.status).toBe('PUBLISHED')
        expect(after.etag).not.toBe(before.etag)
    })
})

describe('Bookends mock adapter (shared intro+outro pair library)', () => {
    function makeClient() {
        const adapter = createMockAdapter()
        const get = (url: string) =>
            adapter({
                method: 'get',
                url,
                headers: {},
            } as unknown as InternalAxiosRequestConfig)
        const post = (url: string, data?: unknown) =>
            adapter({
                method: 'post',
                url,
                headers: {},
                data,
            } as unknown as InternalAxiosRequestConfig)
        const put = (url: string, data?: unknown) =>
            adapter({
                method: 'put',
                url,
                headers: {},
                data,
            } as unknown as InternalAxiosRequestConfig)
        const del = (url: string) =>
            adapter({
                method: 'delete',
                url,
                headers: {},
            } as unknown as InternalAxiosRequestConfig)
        return { get, post, put, del }
    }

    it('lists the single bookend library including pairs with an empty side', async () => {
        const { get } = makeClient()
        const result = BookendsListResponseSchema.parse(
            (await get('/api/bookends')).data
        )
        expect(result.bookends.length).toBeGreaterThan(0)
        // One pair leaves the outro blank so the empty-side rendering is exercised.
        expect(result.bookends.some((b) => b.outroText === '')).toBe(true)
        // The token union spans both lines, intro then outro.
        const standard = result.bookends.find((b) => b.id === 'bk-standard')
        expect(standard?.tokens).toEqual(['station', 'city'])
    })

    it('preview derives {city} from the market id, warts and all', async () => {
        const { post } = makeClient()
        const preview = (
            await post('/api/bookends/preview', {
                IntroText: "Looking over {city} — you're on {station}.",
                MarketId: 'goldcoast',
                Station: 'Hot Tomato 102.9',
                Tokens: null,
            })
        ).data
        // "goldcoast" is one slug word, so the derived default is WRONG on air. The studio
        // preview showing it is the whole point — there is no server-side heuristic to save it.
        expect(preview.ResolvedIntro).toBe(
            "Looking over Goldcoast — you're on Hot Tomato 102.9."
        )
        expect(preview.ResolvedOutro).toBe('')
        expect(preview.UnresolvedTokens).toEqual([])
    })

    it('a token override replaces the built-in regardless of key casing', async () => {
        const { post } = makeClient()
        const preview = (
            await post('/api/bookends/preview', {
                IntroText: 'Looking over {city} today.',
                OutroText: 'Thanks {city} listeners.',
                MarketId: 'goldcoast',
                Station: 'Hot Tomato 102.9',
                Tokens: { CITY: 'Gold Coast' },
            })
        ).data
        // Mirrors the backend's OrdinalIgnoreCase merge. Keyed case-sensitively, "CITY" would
        // land beside "city" and the derived "Goldcoast" would keep winning the lookup — so the
        // operator's fix would appear to do nothing in demo mode.
        expect(preview.ResolvedIntro).toBe('Looking over Gold Coast today.')
        expect(preview.ResolvedOutro).toBe('Thanks Gold Coast listeners.')
        expect(Object.keys(preview.Values)).toHaveLength(3)
        expect(preview.Values.city).toBe('Gold Coast')
    })

    it('preview reports a token with no value instead of airing braces', async () => {
        const { post } = makeClient()
        const preview = (
            await post('/api/bookends/preview', {
                IntroText: '',
                OutroText: 'Brought to you by {sponsor}. Safe travels.',
                MarketId: 'sydney',
                Station: 'KIIS 1065',
                Tokens: null,
            })
        ).data
        expect(preview.UnresolvedTokens).toEqual(['sponsor'])
        expect(preview.ResolvedOutro).not.toContain('{')
    })

    it('creates a bookend minting version 1 with tokens extracted across both lines', async () => {
        const { post } = makeClient()
        const created = BookendApiSchema.parse(
            (
                await post('/api/bookends', {
                    Name: 'Afternoon pair',
                    Description: 'Warm pair for the afternoon drive.',
                    IntroText:
                        'Afternoon drive on {station} — here is the traffic.',
                    OutroText: 'Thanks for {city}, safe travels.',
                    ChangeNote: null,
                })
            ).data
        )
        expect(created.id).toMatch(/^bk-/)
        expect(created.currentVersion).toBe(1)
        expect(created.tokens).toEqual(['station', 'city'])
    })

    it('updating a line mints a new version and re-extracts tokens', async () => {
        const { get, put } = makeClient()
        const before = BookendApiSchema.parse(
            (await get('/api/bookends/bk-standard')).data
        )
        const updated = BookendApiSchema.parse(
            (
                await put('/api/bookends/bk-standard', {
                    Name: before.name,
                    Description: before.description,
                    IntroText: 'You are on {station} with {city} traffic.',
                    OutroText: before.outroText,
                    ChangeNote: 'Swapped the wording.',
                })
            ).data
        )
        expect(updated.currentVersion).toBe(before.currentVersion + 1)
        expect(updated.introText).toBe(
            'You are on {station} with {city} traffic.'
        )
        expect(updated.tokens).toEqual(['station', 'city'])
    })

    it('restore mints a new version from the chosen historical pair', async () => {
        const { get, post } = makeClient()
        const before = BookendApiSchema.parse(
            (await get('/api/bookends/bk-sydney-morning')).data
        )
        const restored = BookendApiSchema.parse(
            (await post('/api/bookends/bk-sydney-morning/versions/1/restore'))
                .data
        )
        expect(restored.currentVersion).toBe(before.currentVersion + 1)
        expect(restored.introText).toBe(
            'Good morning, and welcome to the network traffic read.'
        )
    })

    it('preview resolves built-in tokens against the market', async () => {
        const { post } = makeClient()
        const preview = BookendPreviewResponseSchema.parse(
            (
                await post('/api/bookends/preview', {
                    IntroText: 'Looking over {city} today on {station}.',
                    OutroText: 'That is all from {market}.',
                    MarketId: 'sydney',
                    Station: 'KIIS 1065',
                    Tokens: null,
                })
            ).data
        )
        expect(preview.resolvedIntro).toBe(
            'Looking over Sydney today on KIIS 1065.'
        )
        expect(preview.resolvedOutro).toBe('That is all from sydney.')
        expect(preview.unresolvedTokens).toEqual([])
    })

    it('preview reports unresolved custom tokens instead of airing braces', async () => {
        const { post } = makeClient()
        const preview = BookendPreviewResponseSchema.parse(
            (
                await post('/api/bookends/preview', {
                    IntroText: 'Welcome to {station}.',
                    OutroText: 'Brought to you by {sponsor}. Safe travels.',
                    MarketId: 'melbourne',
                    Station: 'KIIS 101.1',
                    Tokens: null,
                })
            ).data
        )
        // Unresolved tokens are REMOVED (never aired as braces); the leftover
        // space matches the backend's CollapseWhitespace behaviour exactly.
        expect(preview.resolvedIntro).toBe('Welcome to KIIS 101.1.')
        expect(preview.resolvedOutro).toBe('Brought to you by . Safe travels.')
        expect(preview.unresolvedTokens).toEqual(['sponsor'])
    })

    it('rejects deleting a bookend still assigned to a schedule with 409', async () => {
        const { del } = makeClient()
        await expect(
            del('/api/bookends/bk-sydney-morning')
        ).rejects.toMatchObject({
            response: {
                status: 409,
                data: expect.objectContaining({
                    Error: expect.stringContaining('assigned'),
                }),
            },
        })
    })

    it('deletes an unassigned bookend with 204', async () => {
        const { post, del } = makeClient()
        const created = BookendApiSchema.parse(
            (
                await post('/api/bookends', {
                    Name: 'Delete me',
                    Description: null,
                    IntroText: 'This pair goes away.',
                    OutroText: '',
                    ChangeNote: null,
                })
            ).data
        )
        const response = await del(`/api/bookends/${created.id}`)
        expect(response.status).toBe(204)
    })
})

// ---------------------------------------------------------------------------
// Content-rewrite mock — the UX audit harness runs in mock mode, so the rewrite
// must answer the same PascalCase contract as the real API (a casing mismatch
// here is what once let a casing bug reach a deployed environment) and return a
// plausible rewrite the preview/apply loop can exercise.
// ---------------------------------------------------------------------------
describe('Content-rewrite mock adapter', () => {
    function makeClient() {
        const adapter = createMockAdapter()
        const post = (url: string, data?: unknown) =>
            adapter({
                method: 'post',
                url,
                headers: {},
                data,
            } as InternalAxiosRequestConfig)
        return { post }
    }

    it('answers the rewrite endpoint in PascalCase with a plausible reorder', async () => {
        const { post } = makeClient()
        const res = await post('/api/jobs/job-a/rewrite', {
            text: 'The Monash Freeway is closed citybound at Toorak Road. Partly cloudy, 12 degrees.',
            instruction: 'drop the weather and lead with the closure',
        })

        const parsed = RewriteResponseSchema.parse(res.data)
        expect(parsed.script).toContain(
            'Partly cloudy, 12 degrees. The Monash Freeway is closed citybound at Toorak Road.'
        )
        expect(parsed.wordsAfter).toBe(parsed.wordsBefore)
    })

    it('requires an instruction like the real endpoint', async () => {
        const { post } = makeClient()
        const res = await post('/api/jobs/job-a/rewrite', {
            text: 'The Monash Freeway is closed.',
            instruction: '',
        })
        expect(res.status).toBe(404)
    })
})
