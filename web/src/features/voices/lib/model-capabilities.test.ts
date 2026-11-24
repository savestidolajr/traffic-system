import { describe, it, expect } from 'vitest'
import { getModelCapabilities, getModelDisplayName } from './model-capabilities'

describe('getModelCapabilities', () => {
    it('v3 uses discrete stability and supports neither speed, style, nor speaker boost', () => {
        // Drives the workshop UI: v3 → 3-mode stability presets, no speed slider
        // (v3 ignores `speed` — verified empirically), no style/boost controls.
        expect(getModelCapabilities(['eleven_v3'])).toEqual({
            supportsStyle: false,
            supportsSpeakerBoost: false,
            supportsSpeed: false,
            discreteStability: true,
        })
    })

    it('multilingual_v2 supports speed/style/boost with continuous stability', () => {
        expect(getModelCapabilities(['eleven_multilingual_v2'])).toEqual({
            supportsStyle: true,
            supportsSpeakerBoost: true,
            supportsSpeed: true,
            discreteStability: false,
        })
    })

    it('resolves by the first known model id', () => {
        expect(
            getModelCapabilities(['unknown_model', 'eleven_v3'])
        ).toMatchObject({ discreteStability: true, supportsSpeed: false })
    })

    it('falls back to all-enabled when no model id is known', () => {
        expect(getModelCapabilities(['totally_unknown'])).toEqual({
            supportsStyle: true,
            supportsSpeakerBoost: true,
            supportsSpeed: true,
            discreteStability: false,
        })
    })

    it('names known models for display', () => {
        expect(getModelDisplayName('eleven_v3')).toBe('V3 (Latest)')
        expect(getModelDisplayName('eleven_multilingual_v2')).toBe(
            'Multilingual V2'
        )
        expect(getModelDisplayName('mystery')).toBe('mystery')
    })
})
