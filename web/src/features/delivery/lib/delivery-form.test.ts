import { describe, expect, it } from 'vitest'
import { policyHash, presetForm, styleForm } from './delivery-form'
import { PRESETS } from '../presets'
import { normalizeTag, sanitizeTags } from '../cues'
import { toStrength } from '../strength'
import type { DeliveryStyle } from '../types'

function makeStyle(policy: {
    toneDescription: string
    allowedTags: string[]
    intensity: number
}): DeliveryStyle {
    return {
        id: 's1',
        name: 'Peak',
        description: 'summary',
        status: 'active',
        currentVersion: 1,
        createdAt: '2026-06-22T00:00:00Z',
        updatedAt: '2026-06-22T00:00:00Z',
        createdBy: null,
        assignedStations: [],
        versions: [
            {
                version: 1,
                policy,
                changeNote: null,
                createdAt: '2026-06-22T00:00:00Z',
                createdBy: null,
            },
        ],
    }
}

describe('policyHash (audition staleness)', () => {
    const base = {
        toneDescription: 'Punchy',
        allowedTags: ['[urgent]', '[calm]'],
        intensity: 2 as const,
    }

    it('is stable when cues are reordered', () => {
        expect(policyHash(base)).toBe(
            policyHash({ ...base, allowedTags: ['[calm]', '[urgent]'] })
        )
    })

    it('changes when strength changes', () => {
        expect(policyHash(base)).not.toBe(policyHash({ ...base, intensity: 3 }))
    })

    it('changes when the cue set changes', () => {
        expect(policyHash(base)).not.toBe(
            policyHash({ ...base, allowedTags: ['[urgent]'] })
        )
    })

    it('changes when the tone changes', () => {
        expect(policyHash(base)).not.toBe(
            policyHash({ ...base, toneDescription: 'Calm' })
        )
    })

    it('ignores surrounding tone whitespace', () => {
        expect(policyHash(base)).toBe(
            policyHash({ ...base, toneDescription: '  Punchy  ' })
        )
    })
})

describe('presetForm', () => {
    it('seeds tone, cues, and strength from the preset', () => {
        const peak = PRESETS.find((p) => p.id === 'peak')!
        const form = presetForm(peak)
        expect(form.toneDescription).toBe(peak.toneSeed)
        expect(form.allowedTags).toEqual([...peak.defaultCues])
        expect(form.intensity).toBe(peak.defaultStrength)
        expect(form.name).toBe('')
    })
})

describe('styleForm (seeding the editor from a saved style)', () => {
    it('keeps a valid stored intensity', () => {
        const form = styleForm(
            makeStyle({
                toneDescription: 'Steady',
                allowedTags: ['[serious]'],
                intensity: 3,
            })
        )
        expect(form.intensity).toBe(3)
        expect(form.allowedTags).toEqual(['[serious]'])
    })

    it('clamps a neutral/legacy intensity (0) to Balanced and seeds default cues when empty', () => {
        const form = styleForm(
            makeStyle({
                toneDescription: 'Legacy',
                allowedTags: [],
                intensity: 0,
            })
        )
        expect(form.intensity).toBe(2)
        expect(form.allowedTags.length).toBeGreaterThan(0)
    })

    it('keeps a custom cue but drops malformed tags', () => {
        const form = styleForm(
            makeStyle({
                toneDescription: 'Mixed',
                allowedTags: ['[serious]', '[deadpan]', '[]', 'junk!!'],
                intensity: 2,
            })
        )
        expect(form.allowedTags).toEqual(['[serious]', '[deadpan]'])
    })
})

describe('tag normalisation', () => {
    it('normalizeTag wraps, lowercases, and accepts 1-2 words', () => {
        expect(normalizeTag('Deadpan')).toBe('[deadpan]')
        expect(normalizeTag('[Calm]')).toBe('[calm]')
        expect(normalizeTag('  very calm ')).toBe('[very calm]')
    })

    it('normalizeTag rejects junk', () => {
        expect(normalizeTag('')).toBeNull()
        expect(normalizeTag('a b c')).toBeNull()
        expect(normalizeTag('has!punct')).toBeNull()
    })

    it('sanitizeTags keeps custom cues, de-dupes, and drops malformed', () => {
        expect(
            sanitizeTags(['[urgent]', 'URGENT', '[nope!]', 'deadpan'])
        ).toEqual(['[urgent]', '[deadpan]'])
    })
})

describe('strength helper', () => {
    it('toStrength clamps out-of-range intensities to Balanced', () => {
        expect(toStrength(0)).toBe(2)
        expect(toStrength(5)).toBe(2)
        expect(toStrength(1)).toBe(1)
        expect(toStrength(3)).toBe(3)
    })
})
