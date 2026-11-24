import { describe, it, expect } from 'vitest'
import { VoiceProfileFormSchema, MAX_PACE_BOOST_WPM } from './form-schemas'

const validFormData = {
    name: 'Test Profile',
    description: null,
    voiceId: 'oKDz3nEzNfwKIVvUwPs0',
    voiceName: 'ARN Hudson',
    voiceSettings: {
        speed: 1.0,
        stability: 0.5,
        similarityBoost: 0.8,
        style: 0.0,
        useSpeakerBoost: false,
    },
    changeNote: null,
    paceBoostWpm: 0,
}

describe('VoiceProfileFormSchema — paceBoostWpm', () => {
    it('accepts 0 (no-op default)', () => {
        const result = VoiceProfileFormSchema.safeParse({
            ...validFormData,
            paceBoostWpm: 0,
        })
        expect(result.success).toBe(true)
    })

    it('accepts a mid-range value', () => {
        const result = VoiceProfileFormSchema.safeParse({
            ...validFormData,
            paceBoostWpm: 20,
        })
        expect(result.success).toBe(true)
        if (result.success) {
            expect(result.data.paceBoostWpm).toBe(20)
        }
    })

    it('accepts the upper boundary exactly', () => {
        const result = VoiceProfileFormSchema.safeParse({
            ...validFormData,
            paceBoostWpm: MAX_PACE_BOOST_WPM,
        })
        expect(result.success).toBe(true)
    })

    it('rejects a negative value', () => {
        const result = VoiceProfileFormSchema.safeParse({
            ...validFormData,
            paceBoostWpm: -1,
        })
        expect(result.success).toBe(false)
    })

    it('rejects a value exceeding the upper boundary', () => {
        const result = VoiceProfileFormSchema.safeParse({
            ...validFormData,
            paceBoostWpm: MAX_PACE_BOOST_WPM + 1,
        })
        expect(result.success).toBe(false)
    })
})
