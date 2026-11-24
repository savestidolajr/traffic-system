import type { Dictionary, DictionaryVersion, DictionaryScope } from '../types'
import type { RuleInput } from '../types'

/**
 * Mirrors `PronunciationRule.MaxWordLength` / `MaxReplacementLength` in the domain.
 * Enforced server-side regardless; these stop the round trip at the two places an
 * operator types a rule — the studio table and the review-cockpit respelling field.
 */
export const PRONUNCIATION_WORD_MAX = 80
export const PRONUNCIATION_REPLACEMENT_MAX = 200

// ---------------------------------------------------------------------------
// Editor working value
// ---------------------------------------------------------------------------
export interface PronunciationFormValue {
    name: string
    description: string
    rules: RuleInput[]
    changeNote: string
    /** Reach. `Global` applies to every station, under whatever that station carries. */
    scope: DictionaryScope
}

export function newRule(type: 'alias' | 'phoneme'): RuleInput {
    return {
        type,
        stringToReplace: '',
        replacement: '',
        alphabet: type === 'phoneme' ? 'ipa' : null,
        caseSensitive: false,
        wordBoundaries: true,
        _uid: crypto.randomUUID(),
    }
}

/**
 * The rejection the backend makes on a rule set, computed before the round trip.
 *
 * `PronunciationRuleMapping.ParseRules` refuses two rules for the same word and type (the
 * provider applies one and the other is silently dead), and `PronunciationRule.CreateAlias`
 * refuses a rule with no respelling. Both come back as HTTP 400 naming the word — but a save
 * that fails after the operator typed 15 rules reads as "the dictionary is full", which is
 * exactly how it was reported (2026-08-06). Naming the offending word on the disabled Save
 * button turns the same rejection into an instruction.
 *
 * Rows with no word at all are not a problem: they are the empty row "+ Alias rule" adds, and
 * the studio drops them on save.
 */
export function ruleSetProblem(rules: readonly RuleInput[]): string | null {
    // Faithful walk, exactly like PronunciationRuleMapping.ParseRules: a row's OWN problem
    // (missing respelling, self-respell) fails before the dedup check is ever reached, and a
    // collision is reported when the SECOND rule for the word is encountered. Sharing
    // ownRuleProblem with ruleRowProblems keeps the Save gate and the row marks the same
    // judgement, so the alert above the table and the message under a row can never disagree.
    const seen = new Set<string>()
    for (const rule of rules) {
        const own = ownRuleProblem(rule)
        if (own) return own.message
        const word = rule.stringToReplace.trim()
        const key = `${rule.type}|${word.toLowerCase()}`
        if (seen.has(key))
            return `"${word}" has more than one ${rule.type} rule. Only one would be applied, so keep the correct one and remove the rest.`
        seen.add(key)
    }
    return null
}

export type RuleRowProblemKind =
    'missingReplacement' | 'selfRespell' | 'duplicate'

export interface RuleRowProblem {
    readonly kind: RuleRowProblemKind
    readonly message: string
}

/**
 * A single row's own validation problem — the checks the backend makes on the rule BEFORE the
 * cross-rule dedup pass: a replacement is required, and an alias must not be respelled as
 * itself (case-insensitively; matching is case-insensitive by default, so casing alone is not
 * a difference). Phoneme rules are exempt from the identity check: an IPA string that happens
 * to equal the word is not the same claim.
 */
function ownRuleProblem(rule: RuleInput): RuleRowProblem | null {
    const word = rule.stringToReplace.trim()
    if (word.length === 0) return null
    if (rule.replacement.trim().length === 0)
        return {
            kind: 'missingReplacement',
            message: `"${word}" has no respelling yet. Fill it in or remove the row.`,
        }
    if (
        rule.type === 'alias' &&
        word.toLowerCase() === rule.replacement.trim().toLowerCase()
    )
        return {
            kind: 'selfRespell',
            message: `"${word}" is respelled as itself, which changes nothing. Enter how the word should sound, or remove the row.`,
        }
    return null
}

/**
 * Per-row problems for the rule table, so every row in a colliding group is marked at
 * type-time instead of save-time. Mirrors `PronunciationRuleMapping.ParseRules` /
 * `PronunciationRule.CreateAlias` exactly: two rules of the SAME type whose trimmed word
 * matches case-insensitively collide (the backend keys on `{type}|{word}` with
 * OrdinalIgnoreCase), and rows that would fail their own validation are excluded from
 * duplicate groups — the backend fails on them before it ever reaches the dedup check.
 */
export function ruleRowProblems(
    rules: readonly RuleInput[]
): ReadonlyMap<number, RuleRowProblem> {
    const problems = new Map<number, RuleRowProblem>()
    const groups = new Map<string, number[]>()

    for (let i = 0; i < rules.length; i += 1) {
        const rule = rules[i]
        const own = ownRuleProblem(rule)
        if (own) {
            problems.set(i, own)
            continue
        }

        const word = rule.stringToReplace.trim()
        const key = `${rule.type}|${word.toLowerCase()}`
        const group = groups.get(key) ?? []
        group.push(i)
        groups.set(key, group)
    }

    for (const indexes of groups.values()) {
        if (indexes.length < 2) continue
        const first = rules[indexes[0]]
        const word = first.stringToReplace.trim()
        const message = `"${word}" has more than one ${first.type} rule. Only one would be applied, so keep the correct one and remove the rest.`
        for (const i of indexes) {
            problems.set(i, { kind: 'duplicate', message })
        }
    }
    return problems
}

/**
 * The one v2-class model documented to render inline SSML <phoneme> tags.
 * Mirrors `PronunciationRule.InlineXmlPhonemeModel` in the domain.
 */
export const INLINE_XML_PHONEME_MODEL = 'eleven_flash_v2'

/**
 * Why a PHONEME rule cannot be previewed inline on the given model, or null when it can.
 * Mirrors `PronunciationRule.BuildInlinePhonemeText`: the v3 family reads slash-wrapped IPA
 * (IPA only — no CMU slash form), eleven_flash_v2 reads SSML <phoneme> (ipa or cmu-arpabet),
 * and every other model silently skips phoneme tags. This is a DIFFERENT capability from
 * where a dictionary-ATTACHED phoneme rule takes effect (the backend's AppliesOn /
 * PhonemeRulesSafeFor answer that), so the row buttons must not reuse the studio's
 * "phoneme rules apply on v2 only" framing: a rule previews inline on v3, where its
 * dictionary form would be skipped.
 */
export function phonemePreviewProblem(
    alphabet: string | null,
    model: string | null
): string | null {
    if (!model) return 'Select a preview voice first.'
    const normalizedAlphabet = (alphabet ?? 'ipa').toLowerCase()
    if (model.toLowerCase().includes('v3')) {
        return normalizedAlphabet === 'ipa'
            ? null
            : `${model} reads inline IPA only — a CMU phoneme cannot render as a v3 tag. Switch the rule to IPA, or save it and preview the whole dictionary.`
    }
    if (model === INLINE_XML_PHONEME_MODEL) return null
    return `${model} does not render inline phoneme tags — phoneme rules are silently ignored there. Preview with an eleven_v3 voice (inline IPA) or ${INLINE_XML_PHONEME_MODEL} (SSML <phoneme>), or use an alias respelling.`
}

/**
 * Why this rule row's play button is disabled, or null when it can be previewed. The reasons
 * mirror the backend's rejections one-for-one (a rule that would fail the save cannot
 * audition, a phoneme rule needs a model that renders inline tags, and the preview always
 * needs a chosen voice) so the tooltip teaches the operator the actual fix instead of a
 * generic "cannot preview".
 */
export function rulePreviewProblem(
    rule: RuleInput,
    model: string | null,
    rowProblem: string | null
): string | null {
    if (rowProblem) return rowProblem
    if (!rule.stringToReplace.trim()) return 'Type a word to preview.'
    if (!rule.replacement.trim()) return 'Type a replacement before previewing.'
    if (rule.type === 'phoneme')
        return phonemePreviewProblem(rule.alphabet, model)
    if (!model) return 'Select a preview voice first.'
    return null
}

export const DEFAULT_FORM: PronunciationFormValue = {
    name: '',
    description: '',
    rules: [],
    changeNote: '',
    scope: 'Station',
}

/** Seed the editor from a dictionary's current version (for Edit). */
export function dictionaryForm(dict: Dictionary): PronunciationFormValue {
    return {
        name: dict.name,
        description: dict.description ?? '',
        rules: dict.rules.map((r) => ({
            type: r.type,
            stringToReplace: r.stringToReplace,
            replacement: r.replacement,
            alphabet: r.alphabet,
            caseSensitive: r.caseSensitive,
            wordBoundaries: r.wordBoundaries,
            _uid: crypto.randomUUID(),
        })),
        changeNote: '',
        scope: dict.scope,
    }
}

/** Seed the editor from a historical version (for Restore). Name stays current. */
export function versionForm(
    dict: Dictionary,
    version: DictionaryVersion
): PronunciationFormValue {
    return {
        name: dict.name,
        description: dict.description ?? '',
        rules: version.rules.map((r) => ({
            type: r.type,
            stringToReplace: r.stringToReplace,
            replacement: r.replacement,
            alphabet: r.alphabet,
            caseSensitive: r.caseSensitive,
            wordBoundaries: r.wordBoundaries,
            _uid: crypto.randomUUID(),
        })),
        changeNote: '',
        // Restoring rules must not silently change the dictionary's reach.
        scope: dict.scope,
    }
}
