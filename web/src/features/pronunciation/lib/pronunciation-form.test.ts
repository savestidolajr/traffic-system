import { describe, expect, it } from 'vitest'
import { newRule, ruleSetProblem } from './pronunciation-form'
import type { RuleInput } from '../types'

function alias(word: string, replacement: string): RuleInput {
    return { ...newRule('alias'), stringToReplace: word, replacement }
}

function phoneme(word: string, replacement: string): RuleInput {
    return { ...newRule('phoneme'), stringToReplace: word, replacement }
}

// Mirrors PronunciationRuleMapping.ParseRules + PronunciationRule.CreateAlias. Reported live
// 2026-08-06: a second "Manunda" rule 400d the save and the studio said "try again", which the
// operator read as a 15-rule cap.
describe('ruleSetProblem', () => {
    it('passes a clean rule set', () => {
        expect(
            ruleSetProblem([
                alias('Manunda', 'Mahnoonduh'),
                alias('Kairi', 'Keer-Eye'),
            ])
        ).toBeNull()
    })

    it('names the word that carries two rules of the same type', () => {
        const problem = ruleSetProblem([
            alias('Manunda', 'Mahnoonduh'),
            alias('Kairi', 'Keer-Eye'),
            alias('Manunda', 'Man-under'),
        ])

        expect(problem).toContain('Manunda')
        expect(problem).toContain('more than one alias rule')
    })

    it('matches duplicates case-insensitively and on surrounding whitespace, as the backend does', () => {
        expect(
            ruleSetProblem([
                alias('Manoora', 'Man-oorah'),
                alias(' manoora ', 'Man-oora'),
            ])
        ).toContain('more than one alias rule')
    })

    it('allows one alias and one phoneme rule for the same word', () => {
        // Different rule types key separately server-side: alias applies on every model,
        // phoneme only on the v2 family, so both can coexist for one word.
        expect(
            ruleSetProblem([
                alias('Kanimbla', 'Can-Imbla'),
                phoneme('Kanimbla', 'kænˈɪmblə'),
            ])
        ).toBeNull()
    })

    it('reports a row whose respelling is still empty', () => {
        const problem = ruleSetProblem([
            alias('Machans', 'May-chans'),
            alias('Mena', '   '),
        ])

        expect(problem).toContain('Mena')
        expect(problem).toContain('no respelling')
    })

    it('reports an alias respelled as itself, case-insensitively, as the backend does', () => {
        // PronunciationRule.CreateAlias rejects this: matching is case-insensitive, so casing
        // alone is not a difference and the rule would change nothing on air.
        const problem = ruleSetProblem([alias('Manunda', 'manunda')])

        expect(problem).toContain('Manunda')
        expect(problem).toContain('respelled as itself')
    })

    it('allows a phoneme rule whose value equals the word', () => {
        // Only CreateAlias rejects the identity; a phoneme string is a different claim.
        expect(ruleSetProblem([phoneme('Kairi', 'Kairi')])).toBeNull()
    })

    it('ignores the empty row that "+ Alias rule" adds', () => {
        // The studio drops word-less rows on save, so they must not block the button.
        expect(
            ruleSetProblem([alias('Deeral', 'Dear-Al'), newRule('alias')])
        ).toBeNull()
    })
})
