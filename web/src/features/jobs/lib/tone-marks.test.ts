import { describe, it, expect } from 'vitest'
import {
    modelSupportsTags,
    normaliseTag,
    padTags,
    stripTags,
    toneLabel,
} from './tone-marks'

describe('stripTags', () => {
    it('leaves plain text byte-for-byte alone', () => {
        const line = 'The M1 is clear at Melbourne.'
        expect(stripTags(line)).toBe(line)
    })

    it('removes tags and collapses the whitespace they leave behind', () => {
        expect(stripTags('[slow] The Monash Freeway is closed.')).toBe(
            'The Monash Freeway is closed.'
        )
    })

    it('strips tags wherever they sit, not just at the start', () => {
        expect(
            stripTags(
                'The West Gate is [slow] running well, [thoughtful] allow extra time.'
            )
        ).toBe('The West Gate is running well, allow extra time.')
    })

    it('strips multi-word tags whole', () => {
        expect(
            stripTags('Head up [strong australian accent] there is a crash.')
        ).toBe('Head up there is a crash.')
    })
})

describe('padTags', () => {
    it('keeps length and offsets identical to the tagged text', () => {
        const tagged = '[slow] Heads up, the Tullamarine Freeway.'
        const padded = padTags(tagged)
        expect(padded.length).toBe(tagged.length)
        // The tag becomes same-length spaces; words keep their positions.
        expect(padded.slice(0, 6)).toBe('      ')
        expect(padded.indexOf('Heads')).toBe(tagged.indexOf('Heads'))
    })

    it('pads every tag, including mid-sentence repeats', () => {
        const tagged = 'The M1 is [slow] backed up past [slow] the tunnel.'
        const padded = padTags(tagged)
        expect(padded).not.toContain('[')
        expect(padded).not.toContain('slow')
        expect(padded.length).toBe(tagged.length)
        // "backed" still starts where it did in the tagged string.
        expect(padded.indexOf('backed')).toBe(tagged.indexOf('backed'))
    })
})

describe('toneLabel', () => {
    it('strips brackets for display', () => {
        expect(toneLabel('[strong australian accent]')).toBe(
            'strong australian accent'
        )
    })
})

describe('normaliseTag', () => {
    it('accepts bare wording and brackets it', () => {
        expect(normaliseTag('solemn')).toEqual({ ok: true, tag: '[solemn]' })
    })

    it('accepts a tag the operator already bracketed', () => {
        expect(normaliseTag('[solemn]')).toEqual({ ok: true, tag: '[solemn]' })
    })

    it('rejects nesting, which the backend tag pattern cannot match', () => {
        expect(normaliseTag('[sol[emn]]').ok).toBe(false)
    })

    it('rejects an over-length tag rather than let it reach the voice as speech', () => {
        expect(normaliseTag('x'.repeat(90)).ok).toBe(false)
    })
})

describe('modelSupportsTags', () => {
    // Parity with the backend's DeliveryTags.SupportsTags. Getting this wrong in the
    // permissive direction puts the word "excitedly" to air on a v2 voice.
    it.each(['eleven_v3', 'eleven_V3', 'eleven_v3_preview'])(
        'accepts the v3 family: %s',
        (model) => {
            expect(modelSupportsTags(model)).toBe(true)
        }
    )

    it.each([
        'eleven_multilingual_v2',
        'eleven_turbo_v2_5',
        'eleven_flash_v2_5',
        'eleven_english_sts_v2',
        '',
    ])('rejects everything else: %s', (model) => {
        expect(modelSupportsTags(model)).toBe(false)
    })

    it('fails closed on an absent or unknown model', () => {
        expect(modelSupportsTags(undefined)).toBe(false)
        expect(modelSupportsTags(null)).toBe(false)
        expect(modelSupportsTags('some_future_model')).toBe(false)
    })
})
