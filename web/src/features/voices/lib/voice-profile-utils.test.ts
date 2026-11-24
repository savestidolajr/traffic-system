import { describe, it, expect } from 'vitest'
import type { VoiceSettings } from '@/lib/api/schemas/shared'
import type { VoiceProfile } from '../profile-types'
import {
    deriveConfigurationKey,
    diffVoiceSettings,
    summarizeVoiceSettings,
    wordTargetForDuration,
    currentVersion,
    voiceCharacterItems,
    stabilityModeLabel,
} from './voice-profile-utils'

const baseSettings: VoiceSettings = {
    speed: 1.0,
    stability: 0.5,
    similarityBoost: 0.8,
    style: 0.0,
    useSpeakerBoost: false,
}

describe('deriveConfigurationKey', () => {
    it('formats voice id + settings deterministically', () => {
        expect(deriveConfigurationKey('vb1', baseSettings)).toBe(
            'vb1_1.00_0.50_0.80_0.00_false'
        )
    })

    it('identical settings yield the same key (shared calibration)', () => {
        expect(deriveConfigurationKey('vb1', { ...baseSettings })).toBe(
            deriveConfigurationKey('vb1', { ...baseSettings })
        )
    })

    it('a settings change changes the key', () => {
        expect(deriveConfigurationKey('vb1', baseSettings)).not.toBe(
            deriveConfigurationKey('vb1', { ...baseSettings, speed: 1.1 })
        )
    })

    it('encodes speaker boost', () => {
        expect(
            deriveConfigurationKey('vb1', {
                ...baseSettings,
                useSpeakerBoost: true,
            })
        ).toContain('_true')
    })
})

describe('diffVoiceSettings', () => {
    it('reports only changed fields with formatted from/to', () => {
        const changes = diffVoiceSettings(baseSettings, {
            ...baseSettings,
            speed: 1.12,
            stability: 0.4,
        })
        expect(changes).toHaveLength(2)
        const speed = changes.find((c) => c.key === 'speed')
        expect(speed).toMatchObject({ from: '1.00×', to: '1.12×' })
        const stability = changes.find((c) => c.key === 'stability')
        expect(stability).toMatchObject({ from: '50%', to: '40%' })
    })

    it('is empty when nothing changed', () => {
        expect(diffVoiceSettings(baseSettings, { ...baseSettings })).toEqual([])
    })

    it('reports speaker-boost flips as on/off', () => {
        const changes = diffVoiceSettings(baseSettings, {
            ...baseSettings,
            useSpeakerBoost: true,
        })
        expect(changes).toEqual([
            {
                key: 'useSpeakerBoost',
                label: 'Speaker boost',
                from: 'off',
                to: 'on',
            },
        ])
    })
})

describe('summarizeVoiceSettings', () => {
    it('renders a compact speed/stability/similarity line', () => {
        expect(summarizeVoiceSettings(baseSettings)).toBe(
            '1.00× · stab 50% · sim 80%'
        )
    })
})

describe('wordTargetForDuration', () => {
    it('scales words with wpm and seconds', () => {
        expect(wordTargetForDuration(180, 30)).toBe(90)
        expect(wordTargetForDuration(150, 60)).toBe(150)
    })

    it('guards against non-positive inputs', () => {
        expect(wordTargetForDuration(0, 30)).toBe(0)
        expect(wordTargetForDuration(180, 0)).toBe(0)
        expect(wordTargetForDuration(-5, 30)).toBe(0)
    })
})

describe('currentVersion', () => {
    const profile = {
        currentVersion: 2,
        versions: [{ version: 1 }, { version: 2 }, { version: 3 }],
    } as unknown as VoiceProfile

    it('resolves the version the pointer names, not the last one', () => {
        expect(currentVersion(profile).version).toBe(2)
    })

    it('falls back to the last version when the pointer is stale', () => {
        const stale = {
            currentVersion: 99,
            versions: [{ version: 1 }, { version: 2 }],
        } as unknown as VoiceProfile
        expect(currentVersion(stale).version).toBe(2)
    })
})

describe('stabilityModeLabel', () => {
    it('maps the three discrete values to mode names', () => {
        expect(stabilityModeLabel(0)).toBe('Creative')
        expect(stabilityModeLabel(0.5)).toBe('Natural')
        expect(stabilityModeLabel(1)).toBe('Robust')
    })

    it('snaps an off-grid value to the nearest mode', () => {
        expect(stabilityModeLabel(0.2)).toBe('Creative')
        expect(stabilityModeLabel(0.4)).toBe('Natural')
        expect(stabilityModeLabel(0.9)).toBe('Robust')
    })
})

describe('voiceCharacterItems', () => {
    const fullV2: VoiceSettings = {
        speed: 1.05,
        stability: 0.6,
        similarityBoost: 0.8,
        style: 0.3,
        useSpeakerBoost: true,
    }

    it('drops the inert knobs for v3 and shows stability as a mode', () => {
        // v3 ignores speed/style/speaker-boost; stability is a 3-mode bucket.
        const items = voiceCharacterItems(fullV2, 'eleven_v3')
        expect(items.map((i) => i.key)).toEqual(['stability', 'similarity'])
        expect(items.find((i) => i.key === 'stability')?.value).toBe('Natural')
        expect(items.find((i) => i.key === 'similarity')?.value).toBe('80%')
    })

    it('never shows speaker boost for v3 even when enabled', () => {
        const items = voiceCharacterItems(
            { ...fullV2, useSpeakerBoost: true },
            'eleven_v3'
        )
        expect(items.some((i) => i.key === 'boost')).toBe(false)
    })

    it('shows the full set for v2-family with continuous stability', () => {
        const items = voiceCharacterItems(fullV2, 'eleven_multilingual_v2')
        expect(items.map((i) => i.key)).toEqual([
            'speed',
            'stability',
            'similarity',
            'style',
            'boost',
        ])
        expect(items.find((i) => i.key === 'speed')?.value).toBe('1.05×')
        expect(items.find((i) => i.key === 'stability')?.value).toBe('60%')
    })

    it('omits speaker boost on v2 when disabled', () => {
        const items = voiceCharacterItems(
            { ...fullV2, useSpeakerBoost: false },
            'eleven_multilingual_v2'
        )
        expect(items.some((i) => i.key === 'boost')).toBe(false)
    })
})
