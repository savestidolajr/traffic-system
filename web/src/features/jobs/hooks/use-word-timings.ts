import { useMemo } from 'react'
import type { WordTiming } from '../components/ScriptRenderer'
import type { AudioWordTiming } from '../types'
import { DIRECTION_CUE_PATTERN } from '../lib/verification/provenance'

/**
 * Per-word timestamps for the teleprompter karaoke and click-to-seek.
 *
 * Prefers the backend's measured timings (real ElevenLabs alignment, rescaled
 * across duration fine-tuning — accurate to ~0.3s). Falls back to the legacy
 * per-character estimate for jobs synthesised before alignment was captured.
 *
 * Alignment: the measured words are whitespace tokens of the synthesised text
 * (cue tokens dropped on both sides). The TTS provider occasionally splits a
 * single script token across several timing entries — a motorway code like
 * "M80" spoken with a micro-pause is reported as "M" + "80" — so a naive
 * count match would drop the whole clip's accurate timings on one split.
 * Instead we realign: consecutive measured words are merged back onto the
 * token whose text they concatenate to (keeping the first fragment's start),
 * and a measured word spanning several tokens is split by interpolation. Only
 * when the two sequences cannot be reconciled 1:1 do we fall back to the
 * per-character heuristic rather than misalign.
 */
export function buildWordTimings(
    text: string,
    durationSec: number,
    measured?: readonly AudioWordTiming[]
): WordTiming[] | undefined {
    if (!text) return undefined

    if (measured && measured.length > 0) {
        const spoken = measured.filter(
            (t) => !(t.word.startsWith('[') && t.word.endsWith(']'))
        )
        const tokens: string[] = []
        for (const segment of text.split(DIRECTION_CUE_PATTERN)) {
            if (segment.startsWith('[') && segment.endsWith(']')) continue
            for (const match of segment.matchAll(/\S+/g)) {
                tokens.push(match[0])
            }
        }
        const aligned = alignMeasuredToTokens(tokens, spoken)
        if (aligned) return aligned
    }

    if (durationSec <= 0) return undefined
    const totalChars = text.length
    if (totalChars === 0) return undefined

    const msPerChar = (durationSec * 1000) / totalChars
    const timings: WordTiming[] = []
    const segments = text.split(DIRECTION_CUE_PATTERN)
    let offset = 0
    for (const segment of segments) {
        const isCue = segment.startsWith('[') && segment.endsWith(']')
        if (!isCue) {
            const tokenRe = /\S+/g
            let match: RegExpExecArray | null
            while ((match = tokenRe.exec(segment)) !== null) {
                timings.push({
                    word: match[0],
                    startMs: Math.round((offset + match.index) * msPerChar),
                })
            }
        }
        offset += segment.length
    }
    return timings.length > 0 ? timings : undefined
}

/**
 * Reconciles the backend's measured word timings with the displayed script
 * tokens, tolerating a token that the TTS alignment split into several timing
 * entries (and, defensively, the reverse). Returns null when the sequences
 * cannot be matched 1:1, signalling the caller to use the heuristic.
 */
function alignMeasuredToTokens(
    tokens: readonly string[],
    spoken: readonly AudioWordTiming[]
): WordTiming[] | null {
    if (tokens.length === 0 || spoken.length === 0) return null

    const out: WordTiming[] = []
    let i = 0
    let j = 0
    while (i < tokens.length && j < spoken.length) {
        const token = tokens[i]

        // Case 1: one or more consecutive measured words concatenate to this
        // token (the observed "M80" -> "M" + "80" split). Merge them onto the
        // token, keeping the first fragment's start.
        let acc = ''
        let k = j
        while (k < spoken.length && acc.length < token.length) {
            acc += spoken[k].word
            k++
            if (acc === token) break
        }
        if (acc === token) {
            out.push({
                word: token,
                startMs: Math.round(spoken[j].startSec * 1000),
            })
            i += 1
            j = k
            continue
        }

        // Case 2: one measured word concatenates to several tokens (the
        // reverse). Distribute its span across the tokens by interpolation.
        const word = spoken[j].word
        let accTok = ''
        let t = i
        while (t < tokens.length && accTok.length < word.length) {
            accTok += tokens[t]
            t++
            if (accTok === word) break
        }
        if (accTok === word && t > i) {
            const startMs = spoken[j].startSec * 1000
            const span = Math.max(0, spoken[j].endSec * 1000 - startMs)
            const count = t - i
            for (let c = 0; c < count; c++) {
                out.push({
                    word: tokens[i + c],
                    startMs: Math.round(startMs + (span * c) / count),
                })
            }
            i = t
            j += 1
            continue
        }

        return null // irreconcilable — caller falls back to the heuristic
    }

    if (i !== tokens.length || j !== spoken.length) return null
    return out
}

export function useWordTimings(
    text: string,
    durationSec: number,
    measured?: readonly AudioWordTiming[]
): WordTiming[] | undefined {
    return useMemo(
        () => buildWordTimings(text, durationSec, measured),
        [text, durationSec, measured]
    )
}
