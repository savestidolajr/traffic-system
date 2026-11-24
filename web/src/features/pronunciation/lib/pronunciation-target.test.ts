import { describe, it, expect } from 'vitest'
import { resolvePronunciationTarget } from './pronunciation-target'

/**
 * A rule saved with the script's punctuation attached silently stops matching:
 * the provider looks for the literal string, so "Mooringe," never fires again.
 * This resolver is the only thing standing between a hovered word and that bug.
 */
describe('resolvePronunciationTarget', () => {
    it('keeps a plain word untouched', () => {
        expect(resolvePronunciationTarget('Greensborough')).toBe(
            'Greensborough'
        )
    })

    it('drops the punctuation the script carries around a word', () => {
        expect(resolvePronunciationTarget('morning.')).toBe('morning')
        expect(resolvePronunciationTarget('(Keilor)')).toBe('Keilor')
        expect(resolvePronunciationTarget('“Toorak”,')).toBe('Toorak')
    })

    it('strips trailing possessives and contractions so alias rules match all mentions', () => {
        expect(resolvePronunciationTarget("Gate's")).toBe('Gate')
        expect(resolvePronunciationTarget("Pittwater Road's")).toBe(
            'Pittwater Road'
        )
        expect(resolvePronunciationTarget('drivers’')).toBe('drivers')
    })

    it('rejects a token with no word in it', () => {
        expect(resolvePronunciationTarget('   ')).toBeNull()
        expect(resolvePronunciationTarget('—')).toBeNull()
        expect(resolvePronunciationTarget('')).toBeNull()
    })

    it('rejects a target past the domain word length', () => {
        expect(resolvePronunciationTarget('a'.repeat(81))).toBeNull()
        expect(resolvePronunciationTarget('a'.repeat(80))).toBe('a'.repeat(80))
    })
})
