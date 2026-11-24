import type { VerifiedPronunciationEntry } from '@/features/pronunciation/api/verified-pronunciation-hooks'
import { pronunciationKey } from '@/features/pronunciation/api/verified-pronunciation-hooks'
import {
    escapeRegExp,
    type PronunciationMatchResult,
} from './pronunciation-matches'

/** What the pronunciation dictionary does to this word on air. */
export interface DictionaryFacet {
    readonly replacement: string
    readonly dictionaryName: string
    readonly versionNumber: number
}

/** What the audio pre-review concluded about this word or figure. */
export interface ReviewFacet {
    /** `figure` is an aired quantity the transcript couldn't confirm — informational, no verdict. */
    readonly kind: 'suspect' | 'verified' | 'figure'
    /** The standing operator verdict, when `kind` is `verified`. */
    readonly verdict: VerifiedPronunciationEntry | null
}

/**
 * One marked span of the script. A span can carry BOTH facets — a word can have
 * a dictionary rule AND still be flagged by the pre-review, which is exactly the
 * case that used to render twice (once inline, once as a chip in the badge row).
 * Merging them into a single mark is what keeps the script the only place an
 * operator has to look.
 */
export interface ScriptMark {
    /** UTF-16 offset into the untouched canonical script, inclusive. */
    readonly start: number
    /** UTF-16 offset into the untouched canonical script, exclusive. */
    readonly end: number
    readonly text: string
    readonly dictionary?: DictionaryFacet
    readonly review?: ReviewFacet
    /**
     * A rule the operator saved during THIS review. The audio still carries the
     * old pronunciation until the bulletin is re-voiced, so the mark says
     * "queued" rather than pretending the change is already on air.
     */
    readonly pendingReplacement?: string
}

/**
 * A dictionary rule the operator saved during this review. Saved in the library
 * already; pending only against THIS bulletin, whose audio still carries the old
 * pronunciation until it is re-voiced.
 */
export interface PendingPronunciation {
    readonly word: string
    readonly replacement: string
}

export interface ScriptMarksResult {
    readonly marks: readonly ScriptMark[]
    /**
     * Flagged items with no match in the rendered script — normally empty. A
     * script edited after synthesis can orphan a word, and silently dropping it
     * would hide a real warning, so the caller still surfaces these.
     */
    readonly unlocatedWords: readonly string[]
    readonly unlocatedFigures: readonly string[]
}

interface Span {
    readonly start: number
    readonly end: number
}

/** Every whole-word occurrence, case-insensitive. Apostrophes are part of the word ("Gate's"). */
function findWordSpans(text: string, word: string): Span[] {
    if (!word) return []
    const escaped = escapeRegExp(word)
    const matcher = new RegExp(
        `(?<![\\p{L}\\p{N}_])${escaped}(?![\\p{L}\\p{N}_])`,
        'giu'
    )
    const spans: Span[] = []
    for (const match of text.matchAll(matcher)) {
        if (match.index === undefined) continue
        spans.push({ start: match.index, end: match.index + match[0].length })
    }
    return spans
}

/**
 * Figures are literal script substrings (the backend captures `Match.Value`
 * verbatim), but a bare `indexOf` finds them INSIDE larger tokens — a flagged
 * "8" matched the 8 in "M80" and put a figure warning on a road name. The
 * boundaries mirror the backend's own number regex.
 */
function findFigureSpans(text: string, figure: string): Span[] {
    if (!figure) return []
    const matcher = new RegExp(
        `(?<![\\p{L}\\p{N}])${escapeRegExp(figure)}(?![\\p{L}\\p{N}])`,
        'gu'
    )
    const spans: Span[] = []
    for (const match of text.matchAll(matcher)) {
        if (match.index === undefined) continue
        spans.push({ start: match.index, end: match.index + match[0].length })
    }
    return spans
}

/**
 * Collapses the dictionary matches and the audio pre-review into one ordered,
 * non-overlapping set of script marks.
 *
 * Precedence when facets collide on the same span: the review facet decides the
 * mark's urgency (a flagged word needs an ear even if a rule already rewrites
 * it), while the dictionary facet rides along so the popover can explain both.
 */
export function buildScriptMarks(
    text: string,
    pronunciationMatch: PronunciationMatchResult | null | undefined,
    suspectWords: readonly string[],
    numberMismatches: readonly string[],
    verified: readonly VerifiedPronunciationEntry[],
    /** Rules saved during this review, not yet on air. */
    pendingEdits: readonly PendingPronunciation[] = []
): ScriptMarksResult {
    const pendingByKey = new Map(
        pendingEdits.map((e) => [pronunciationKey(e.word), e.replacement])
    )
    const byStart = new Map<number, ScriptMark>()
    const unlocatedWords: string[] = []
    const unlocatedFigures: string[] = []

    const put = (
        span: Span,
        spanText: string,
        facet: Partial<Pick<ScriptMark, 'dictionary' | 'review'>>
    ) => {
        const existing = byStart.get(span.start)
        byStart.set(span.start, {
            start: span.start,
            end: Math.max(span.end, existing?.end ?? span.end),
            text: existing?.text ?? spanText,
            dictionary: facet.dictionary ?? existing?.dictionary,
            review: facet.review ?? existing?.review,
            pendingReplacement:
                pendingByKey.get(pronunciationKey(spanText)) ??
                existing?.pendingReplacement,
        })
    }

    // 1. Dictionary substitutions — offsets already resolved against this text.
    if (
        pronunciationMatch?.resolution === 'exact' &&
        pronunciationMatch.dictionaryName &&
        pronunciationMatch.versionNumber != null
    ) {
        for (const entry of pronunciationMatch.matches) {
            put({ start: entry.start, end: entry.end }, entry.canonicalText, {
                dictionary: {
                    replacement: entry.replacement,
                    dictionaryName: pronunciationMatch.dictionaryName,
                    versionNumber: pronunciationMatch.versionNumber,
                },
            })
        }
    }

    // 2. Pre-review suspect words, split by whether a standing verdict clears them.
    const verdictByKey = new Map(
        verified
            .filter((v) => v.enabled)
            .map((v) => [pronunciationKey(v.word), v] as const)
    )
    for (const word of suspectWords) {
        const spans = findWordSpans(text, word)
        if (spans.length === 0) {
            unlocatedWords.push(word)
            continue
        }
        const verdict = verdictByKey.get(pronunciationKey(word)) ?? null
        for (const span of spans) {
            put(span, text.slice(span.start, span.end), {
                review: { kind: verdict ? 'verified' : 'suspect', verdict },
            })
        }
    }

    // 3. Aired quantities the transcript couldn't confirm.
    for (const figure of numberMismatches) {
        const spans = findFigureSpans(text, figure)
        if (spans.length === 0) {
            unlocatedFigures.push(figure)
            continue
        }
        for (const span of spans) {
            put(span, text.slice(span.start, span.end), {
                review: { kind: 'figure', verdict: null },
            })
        }
    }

    // 4. Rules saved during THIS review. Most target a word nobody flagged —
    // that is the whole point of click-to-respell — so without a mark of their
    // own the operator's fix would be invisible in the script they read from.
    for (const edit of pendingEdits) {
        for (const span of findWordSpans(text, edit.word)) {
            put(span, text.slice(span.start, span.end), {})
        }
    }

    // Drop any mark swallowed by an earlier, longer one — the renderer resolves a
    // word to at most one mark, so overlaps would otherwise render unpredictably.
    const ordered = [...byStart.values()].sort((a, b) => a.start - b.start)
    const marks: ScriptMark[] = []
    for (const mark of ordered) {
        const previous = marks[marks.length - 1]
        if (previous && mark.start < previous.end) continue
        marks.push(mark)
    }

    return { marks, unlocatedWords, unlocatedFigures }
}

/** Words still needing an ear — drives the status line's count and the empty state. */
export function countUnresolved(marks: readonly ScriptMark[]): {
    readonly suspect: number
    readonly verified: number
    readonly figures: number
} {
    let suspect = 0
    let verified = 0
    let figures = 0
    for (const mark of marks) {
        if (mark.review?.kind === 'suspect') suspect++
        else if (mark.review?.kind === 'verified') verified++
        else if (mark.review?.kind === 'figure') figures++
    }
    return { suspect, verified, figures }
}
