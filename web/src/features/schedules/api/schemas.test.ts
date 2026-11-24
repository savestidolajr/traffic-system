import { describe, expect, it } from 'vitest'
import { MarketScheduleSchema } from './schemas'
import { SCHEDULE_FIXTURES } from '@/lib/api/mock/fixtures'

describe('MarketScheduleSchema nullable backend fields', () => {
    it('accepts null PlaybookId and Coverage from the schedule DTO', () => {
        const parsed = MarketScheduleSchema.parse({
            ...SCHEDULE_FIXTURES[0],
            PlaybookId: null,
            Coverage: null,
        })

        expect(parsed.playbookId).toBe('')
        expect(parsed.coverage).toBeNull()
    })
})
