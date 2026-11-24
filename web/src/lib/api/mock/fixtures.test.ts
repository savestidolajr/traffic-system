import { describe, it, expect } from 'vitest'
import {
    JOB_FIXTURES,
    SCHEDULE_FIXTURES,
    VOICE_PROFILE_FIXTURES,
    DELIVERY_STYLE_FIXTURES,
    BOOKEND_FIXTURES,
    PRONUNCIATION_DICTIONARY_FIXTURES,
    GUIDANCE_FIXTURES,
    SAFETY_TERM_FIXTURES,
} from './fixtures'
import DEV_DATA from './dev-data.json'
import { JobSchema } from '@/features/jobs/api/schemas'
import { SchedulesListResponseSchema } from '@/features/schedules/api/schemas'
import { MockStore } from './store'
import ADELAIDE_DEV_JOB from './dev-job-287d14a0.json'
import MELBOURNE_DEV_JOB from './dev-job-f034af33.json'

// The review showcase is a captured dev API payload, not hand-authored demo data.
const DEV_REVIEW_JOB_ID = '287d14a0b2484f10938b5f62b755d2cb'
const fixture = JOB_FIXTURES.find((job) => job.JobId === DEV_REVIEW_JOB_ID)

describe('dev job review fixture', () => {
    it('parses as a job and retains the captured bulletin evidence', () => {
        expect(fixture).toBeDefined()
        const job = JobSchema.parse(fixture)

        expect(job.jobId).toBe(DEV_REVIEW_JOB_ID)
        expect(job.station).toBe('KIIS 102.3')
        expect(job.market).toBe('adelaide')
        expect(job.status).toBe('WAITING_APPROVAL')
        expect(job.script?.text).toBe(ADELAIDE_DEV_JOB.Script.Data.Text)
        expect(job.logs.map((log) => log.message)).toEqual(
            ADELAIDE_DEV_JOB.Logs.map((log) => log.Message)
        )
        expect(job.traffic?.corridors).toHaveLength(
            ADELAIDE_DEV_JOB.Traffic.Corridors.length
        )
        expect(job.traffic?.incidents).toHaveLength(
            ADELAIDE_DEV_JOB.Traffic.Incidents.length
        )
        expect(job.weather?.conditions).toBe(
            ADELAIDE_DEV_JOB.Weather.Conditions
        )
        expect(job.fuel?.summaries).toHaveLength(
            ADELAIDE_DEV_JOB.Fuel.Summaries.length
        )
        expect(job.fuel?.summaries[0]?.latestModifiedUtc).toBe(
            fixture?.Fuel?.Summaries?.[0]?.LatestModifiedUtc
        )
        expect(job.audio?.wordTimings).toHaveLength(
            ADELAIDE_DEV_JOB.Audio.WordTimings.length
        )
        expect(job.audio?.data.url).toBe(
            '/mock/samples/kiis-adelaide-287d14a0.mp3'
        )
        expect(job.editingGuidance).toEqual({
            targetWordCount: 70,
            minWordCount: 70,
            maxWordCount: 80,
            wordsPerMinute: 140.5,
            targetDurationSeconds: 30,
        })
    })

    it('moves only timestamps and the protected audio URL for local use', () => {
        expect(fixture?.CreatedAt).not.toBe(ADELAIDE_DEV_JOB.CreatedAt)
        expect(fixture?.Logs.map((log) => log.Message)).toEqual(
            ADELAIDE_DEV_JOB.Logs.map((log) => log.Message)
        )
        expect(fixture?.Audio?.Data.Url).not.toBe(
            ADELAIDE_DEV_JOB.Audio.Data.Url
        )
    })

    it('does not replace captured editing guidance with mock defaults', async () => {
        const stored = await new MockStore().getJob(DEV_REVIEW_JOB_ID)
        expect(stored?.EditingGuidance).toEqual(
            ADELAIDE_DEV_JOB.EditingGuidance
        )
    })
})
describe('melbourne dev job review fixture', () => {
    const MELBOURNE_REVIEW_JOB_ID = 'f034af33f0ba4f6288f52e22bce8bb27'
    const melbourneFixture = JOB_FIXTURES.find(
        (job) => job.JobId === MELBOURNE_REVIEW_JOB_ID
    )

    it('parses as a job and retains the captured bulletin evidence', () => {
        expect(melbourneFixture).toBeDefined()
        const job = JobSchema.parse(melbourneFixture)

        expect(job.jobId).toBe(MELBOURNE_REVIEW_JOB_ID)
        expect(job.station).toBe('KIIS 101.1')
        expect(job.market).toBe('melbourne')
        expect(job.status).toBe('APPROVED')
        expect(job.script?.text).toBe(MELBOURNE_DEV_JOB.Script.Data.Text)
        expect(job.traffic?.corridors).toHaveLength(
            MELBOURNE_DEV_JOB.Traffic.Corridors.length
        )
        expect(job.traffic?.incidents).toHaveLength(
            MELBOURNE_DEV_JOB.Traffic.Incidents.length
        )
        expect(job.traffic?.flowRoads).toHaveLength(
            MELBOURNE_DEV_JOB.Traffic.FlowRoads.length
        )
        expect(job.weather?.conditions).toBe(
            MELBOURNE_DEV_JOB.Weather.Conditions
        )
        expect(job.fuel?.summaries).toHaveLength(
            MELBOURNE_DEV_JOB.Fuel.Summaries.length
        )
        expect(job.audio?.wordTimings).toHaveLength(
            MELBOURNE_DEV_JOB.Audio.WordTimings.length
        )
        expect(job.audio?.data.url).toBe(
            '/mock/samples/kiis-melbourne-f034af33.mp3'
        )
        expect(job.approval?.decidedBy).toBe(
            MELBOURNE_DEV_JOB.Approval.DecidedBy
        )
        expect(job.approval?.decidedAt).not.toBe(
            MELBOURNE_DEV_JOB.Approval.DecidedAt
        )
    })

    it('moves only timestamps and the protected audio URL for local use', () => {
        expect(melbourneFixture?.CreatedAt).not.toBe(
            MELBOURNE_DEV_JOB.CreatedAt
        )
        expect(melbourneFixture?.Logs.map((log) => log.Message)).toEqual(
            MELBOURNE_DEV_JOB.Logs.map((log) => log.Message)
        )
        expect(melbourneFixture?.Audio?.Data.Url).not.toBe(
            MELBOURNE_DEV_JOB.Audio.Data.Url
        )
    })
})
describe('dev-backed station schedule fixtures', () => {
    it('matches the schedule API contract and keeps pilot stations present', () => {
        const response = SchedulesListResponseSchema.parse({
            Items: SCHEDULE_FIXTURES,
            Total: SCHEDULE_FIXTURES.length,
        })

        expect(response.schedules).toHaveLength(SCHEDULE_FIXTURES.length)
        expect(new Set(response.schedules.map((s) => s.marketId)).size).toBe(
            response.schedules.length
        )
        expect(response.schedules.map((s) => s.station)).toEqual(
            expect.arrayContaining(['KIIS 90s', '4CA 846 AM'])
        )
        const sydney = response.schedules.find(
            (schedule) => schedule.marketId === 'sydney'
        )
        expect(sydney?.playbookId).toBe('pb-mock-sydney')
        expect(
            response.schedules
                .filter((schedule) => schedule.marketId !== 'sydney')
                .every((schedule) => schedule.playbookId === '')
        ).toBe(true)
    })
})
describe('dev-backed non-job fixtures', () => {
    it('keeps every configuration domain sourced from the dev snapshot', () => {
        expect(SCHEDULE_FIXTURES).toEqual(
            DEV_DATA.Schedules.map((schedule) => ({
                ...schedule,
                PlaybookId:
                    schedule.MarketId === 'sydney' ? 'pb-mock-sydney' : '',
            }))
        )
        expect(VOICE_PROFILE_FIXTURES).toEqual(DEV_DATA.VoiceProfiles)
        expect(DELIVERY_STYLE_FIXTURES).toEqual(DEV_DATA.DeliveryStyles)
        expect(BOOKEND_FIXTURES).toEqual(DEV_DATA.Bookends)
        expect(PRONUNCIATION_DICTIONARY_FIXTURES).toEqual(
            DEV_DATA.PronunciationDictionaries
        )
        expect(GUIDANCE_FIXTURES).toEqual(DEV_DATA.Guidance)
        expect(SAFETY_TERM_FIXTURES).toEqual(DEV_DATA.SafetyTerms)
    })
    it('seeds priority roads, filler statements, and operators from dev data', async () => {
        const store = new MockStore()
        for (const entry of DEV_DATA.PriorityRoads) {
            await expect(store.getPriorityRoads(entry.Market)).resolves.toEqual(
                entry
            )
        }
        for (const entry of DEV_DATA.FillerStatements) {
            await expect(
                store.getFillerStatements(entry.Market)
            ).resolves.toEqual(entry)
        }
        await expect(store.listOperators()).resolves.toEqual(DEV_DATA.Operators)
    })
})
