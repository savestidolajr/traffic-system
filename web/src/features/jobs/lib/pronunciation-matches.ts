import type { Dictionary, Rule } from '@/features/pronunciation/types'

export type PronunciationMatchResolution =
    'exact' | 'missing-dictionary' | 'missing-version' | 'none'

export interface PronunciationTextMatch {
    /** The exact substring from the untouched canonical script. */
    readonly canonicalText: string
    /** The dictionary rule's source text (before replacement). */
    readonly rule: string
    readonly replacement: string
    /** UTF-16 offsets, matching String.prototype.slice semantics. */
    readonly start: number
    readonly end: number
}

export interface PronunciationMatchResult {
    readonly dictionaryName: string | null
    /** The saved library record id, not the ElevenLabs provider dictionary id. */
    readonly libraryId: string | null
    readonly versionNumber: number | null
    /** The exact ElevenLabs version id used to resolve the historical version. */
    readonly providerVersionId: string | null
    readonly resolution: PronunciationMatchResolution
    readonly matches: readonly PronunciationTextMatch[]
}

const V3_MODEL = 'eleven_v3'

export function escapeRegExp(value: string): string {
    return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

function findRuleMatches(
    text: string,
    rule: Rule,
    ruleIndex: number
): Array<PronunciationTextMatch & { readonly ruleIndex: number }> {
    if (rule.stringToReplace.length === 0) return []

    const escaped = escapeRegExp(rule.stringToReplace)
    const boundaryPattern = rule.wordBoundaries
        ? `(?<![\\p{L}\\p{N}_])${escaped}(?![\\p{L}\\p{N}_])`
        : escaped
    const flags = rule.caseSensitive ? 'gu' : 'giu'
    const matcher = new RegExp(boundaryPattern, flags)
    const matches: Array<
        PronunciationTextMatch & { readonly ruleIndex: number }
    > = []

    for (const match of text.matchAll(matcher)) {
        const start = match.index
        if (start === undefined) continue
        const canonicalText = match[0]
        matches.push({
            canonicalText,
            rule: rule.stringToReplace,
            replacement: rule.replacement,
            start,
            end: start + canonicalText.length,
            ruleIndex,
        })
    }

    return matches
}

/**
 * Resolve the persisted dictionary version and find its textual substitutions.
 *
 * This is deliberately a pure, read-only operation: it never uses a
 * dictionary's current `rules`, never edits the script, and returns no match
 * when the persisted provider version cannot be found.
 */
export function findPronunciationMatches(
    canonicalText: string,
    providerDictionaryId: string | null | undefined,
    providerVersionId: string | null | undefined,
    voiceModel: string,
    dictionaries: readonly Dictionary[]
): PronunciationMatchResult {
    if (providerDictionaryId == null) {
        return {
            dictionaryName: null,
            libraryId: null,
            versionNumber: null,
            providerVersionId: null,
            resolution: 'none',
            matches: [],
        }
    }

    const dictionary = dictionaries.find(
        (candidate) => candidate.dictionaryId === providerDictionaryId
    )
    if (!dictionary) {
        return {
            dictionaryName: null,
            libraryId: null,
            versionNumber: null,
            providerVersionId: providerVersionId ?? null,
            resolution: 'missing-dictionary',
            matches: [],
        }
    }

    if (providerVersionId == null) {
        return {
            dictionaryName: dictionary.name,
            libraryId: dictionary.id,
            versionNumber: null,
            providerVersionId: null,
            resolution: 'missing-version',
            matches: [],
        }
    }

    const version = dictionary.versions.find(
        (candidate) => candidate.elevenLabsVersionId === providerVersionId
    )
    if (!version) {
        return {
            dictionaryName: dictionary.name,
            libraryId: dictionary.id,
            versionNumber: null,
            providerVersionId,
            resolution: 'missing-version',
            matches: [],
        }
    }

    const candidates = version.rules
        .filter((rule) => !(voiceModel === V3_MODEL && rule.type === 'phoneme'))
        .flatMap((rule, index) => findRuleMatches(canonicalText, rule, index))

    // Select the longest candidate first so overlapping rules are stable even
    // when the shorter rule starts earlier in the script. Ties use source
    // position, then dictionary order; the final presentation is script order.
    const selected: Array<
        PronunciationTextMatch & { readonly ruleIndex: number }
    > = []
    for (const candidate of candidates.sort((a, b) => {
        const lengthDelta = b.end - b.start - (a.end - a.start)
        if (lengthDelta !== 0) return lengthDelta
        if (a.start !== b.start) return a.start - b.start
        return a.ruleIndex - b.ruleIndex
    })) {
        if (
            !selected.some(
                (match) =>
                    match.start < candidate.end && candidate.start < match.end
            )
        ) {
            selected.push(candidate)
        }
    }

    selected.sort((a, b) => a.start - b.start)

    return {
        dictionaryName: dictionary.name,
        libraryId: dictionary.id,
        versionNumber: version.version,
        providerVersionId: version.elevenLabsVersionId,
        resolution: 'exact',
        matches: selected.map((match) => ({
            canonicalText: match.canonicalText,
            rule: match.rule,
            replacement: match.replacement,
            start: match.start,
            end: match.end,
        })),
    }
}
