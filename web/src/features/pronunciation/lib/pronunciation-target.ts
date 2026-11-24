import { PRONUNCIATION_WORD_MAX } from './pronunciation-form'

/**
 * The dictionary-rule target for the word under the pointer.
 *
 * The script carries punctuation a rule must not: "Mooringe," and "(Keilor)" are
 * the same target as the bare name, and a rule saved with the comma attached
 * silently stops matching — the provider looks for the literal string. Returns
 * null when there is nothing teachable: punctuation alone, or a token too long
 * for the domain's word limit.
 */
export function resolvePronunciationTarget(raw: string): string | null {
    const trimmed = raw
        .trim()
        .replace(/^[^\p{L}\p{N}]+/u, '')
        // Strip trailing possessives, contractions and punctuation: a rule for "Pittwater Road"
        // matches both "Pittwater Road" and "Pittwater Road's" (word boundaries), but saving
        // with the contraction attached would make the rule fail on plain mentions.
        .replace(/['\u2019]s?$/iu, '')
        .replace(/[^\p{L}\p{N}]+$/u, '')
    if (!trimmed) return null
    if (trimmed.length > PRONUNCIATION_WORD_MAX) return null
    return trimmed
}
