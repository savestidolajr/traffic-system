// =============================================================================
// Sentence verification status — pure derivation of the teleprompter gutter
// state from provenance + claim flags, plus the summary/jump helpers the
// script header uses to make that state actionable.
// =============================================================================

import {
    isAdviceSentence,
    isFuelSentence,
    isWeatherSentence,
    type ScriptSentence,
} from './provenance'
import { escapeRegExp } from '../pronunciation-matches'
import type {
    SentenceStatus,
    SentenceVerdict,
} from '../../components/ScriptRenderer'

/** Per-sentence source entry, as produced by the cockpit's provenance pass. */
export interface SentenceSourceEntry {
    readonly incidentKeys: readonly string[]
    /** Sentence cites a corridor present in the job's traffic data (may be flow-only). */
    readonly corridorMatched?: boolean
    /** Sentence cites a monitored drive-time run measured for this job. */
    readonly routeMatched?: boolean
    readonly flags?: ReadonlyArray<{
        readonly kind: string
        readonly note: string
    }>
    readonly matchedBy?: 'road-token' | 'model'
}

export interface SentenceStatusOptions {
    /**
     * The market's configured filler statements. Filler text is appended to
     * the script verbatim by the deterministic filler selector, so a sentence
     * matching one BY IDENTITY is ARN's own configured editorial content —
     * nothing to verify, regardless of what it says (branding, promos, tips).
     */
    readonly fillerStatementTexts?: readonly string[]
    /** Job carries weather/fuel content: route unverified sentences to Sources. */
    readonly hasGarnish?: boolean
    /** Weather data is on the job — a weather-reading sentence is sourced, not amber. */
    readonly hasWeatherData?: boolean
    /** Fuel summaries are on the job — a fuel-price sentence is sourced, not amber. */
    readonly hasFuelData?: boolean
    /** Roads the bulletin reported as flowing well (job.goodRuns). */
    readonly goodRunRoads?: readonly string[]
    /**
     * All corridor road names in the job's traffic data. A sentence naming a
     * known corridor is a traffic claim — never weather/fuel garnish, even
     * when it uses hazard vocabulary ("fog patches on the Kuranda Range") —
     * so garnish classification is suppressed for it.
     */
    readonly corridorRoadNames?: readonly string[]
}

/**
 * Derive the verification verdict for every sentence. Configured filler
 * statements (matched by identity, not by content heuristics) and pure
 * advice are quiet — there is nothing to verify. Claim flags outrank
 * everything (a named contradiction is worse than a missing source).
 * Sentences whose evidence is non-incident but ON SCREEN — weather, fuel,
 * flow-corridor, monitored drive-time run, good-run mentions — are sourced,
 * not amber: amber means "this claim has no visible evidence", and burning it
 * on every weather line teaches operators to skim past the one amber that
 * matters. Amber remains for claims with genuinely nothing backing them.
 */

export function buildSentenceStatuses(
    sentences: readonly ScriptSentence[],
    bySentence: ReadonlyArray<SentenceSourceEntry | undefined>,
    options: SentenceStatusOptions = {}
): SentenceStatus[] {
    const {
        fillerStatementTexts,
        hasGarnish,
        hasWeatherData,
        hasFuelData,
        goodRunRoads,
        corridorRoadNames,
    } = options
    const garnishHint = hasGarnish
        ? ' If it reads as weather or fuel, check it against the Sources panel.'
        : ''

    // Whole-word road matcher: "M1" must not match "M15", "Kennedy" must not
    // match "McKennedy".
    const mentionsRoad = (lowerText: string, road: string): boolean => {
        const escaped = escapeRegExp(road.toLowerCase())
        return new RegExp(`\\b${escaped}\\b`).test(lowerText)
    }

    // Non-incident evidence that is nonetheless displayed in the cockpit.
    // Gated on the DATA being present, never on the text alone — a weather-ish
    // sentence on a station with weather disabled stays amber — and suppressed
    // for sentences naming a known corridor (those are traffic claims that
    // FAILED attribution; a false green is worse than a false amber).
    const garnishStatus = (text: string): SentenceStatus | null => {
        const lower = text.toLowerCase()
        const namesCorridor = corridorRoadNames?.some(
            (r) => r && mentionsRoad(lower, r)
        )
        if (!namesCorridor) {
            if (hasWeatherData && isWeatherSentence(text)) {
                return {
                    kind: 'sourced' as const,
                    note: 'Weather sentence — provider data is in the Sources panel.',
                }
            }
            if (hasFuelData && isFuelSentence(text)) {
                return {
                    kind: 'sourced' as const,
                    note: 'Fuel sentence — provider prices are in the Sources panel.',
                }
            }
        }
        if (goodRunRoads?.some((r) => r && mentionsRoad(lower, r))) {
            return {
                kind: 'sourced' as const,
                note: 'Reported good run — backed by priority-road flow data.',
            }
        }
        return null
    }

    // Filler identity: normalize whitespace/case and compare. A statement may
    // span multiple sentences after splitting, so a sentence also matches
    // when it appears inside a configured statement.
    const normalizedFillers = (fillerStatementTexts ?? [])
        .map((t) => t.replace(/\s+/g, ' ').trim().toLowerCase())
        .filter((t) => t.length > 0)

    return sentences.map((s, i) => {
        const normalizedSentence = s.text
            .replace(/\s+/g, ' ')
            .trim()
            .toLowerCase()
        if (
            normalizedFillers.some(
                (f) =>
                    f === normalizedSentence || f.includes(normalizedSentence)
            )
        ) {
            return {
                kind: 'branding' as const,
                note: 'Configured filler statement — station editorial content, nothing to verify.',
            }
        }
        // Pure advice with no checkable fact is quiet, not amber — amber
        // burned on "Allow extra time for your commute" every bulletin
        // teaches the operator to skim past the one amber that matters.
        const adviceStatus = {
            kind: 'branding' as const,
            note: 'General advice. Nothing to verify.',
        }

        const entry = bySentence[i]
        if (!entry) {
            if (isAdviceSentence(s.text)) return adviceStatus
            const garnish = garnishStatus(s.text)
            if (garnish) return garnish
            return {
                kind: 'unverified' as const,
                note: `No source found. Verify manually.${garnishHint}`,
            }
        }

        const flags = entry.flags ?? []
        if (flags.length > 0) {
            return {
                kind: 'check' as const,
                note: flags.map((f) => f.note).join(' · '),
            }
        }

        if ((entry.incidentKeys?.length ?? 0) > 0) {
            return { kind: 'sourced' as const }
        }

        // Cited corridor exists in the traffic data but carries no discrete
        // incidents — a flow/congestion corridor ("X is moving slowly"). The
        // corridor row in the rail is the displayed evidence.
        if (entry.corridorMatched) {
            return {
                kind: 'sourced' as const,
                note: 'Congestion corridor — flow data is in the traffic rail (no discrete incident).',
            }
        }
        if (entry.routeMatched) {
            return {
                kind: 'sourced' as const,
                note: 'Monitored drive-time run — the live and typical figures are in the Adjust tab.',
            }
        }

        if (isAdviceSentence(s.text)) return adviceStatus

        const garnish = garnishStatus(s.text)
        if (garnish) return garnish

        const base =
            entry.matchedBy === 'model'
                ? 'Model reported no source for this sentence. Verify before approving.'
                : 'No source incident matched (road-name heuristic). Verify manually.'
        return { kind: 'unverified' as const, note: `${base}${garnishHint}` }
    })
}

/** Tally of sentences per verdict, for the script-header summary chips. */
export function countVerdicts(
    statuses: readonly SentenceStatus[]
): Record<SentenceVerdict, number> {
    const counts: Record<SentenceVerdict, number> = {
        sourced: 0,
        check: 0,
        unverified: 0,
        branding: 0,
    }
    for (const st of statuses) counts[st.kind]++
    return counts
}

/**
 * The next sentence index with the given verdict after `after`, wrapping to
 * the start so repeated clicks cycle through every flagged line. Null when
 * no sentence carries the verdict.
 */
export function nextSentenceOfVerdict(
    statuses: readonly SentenceStatus[],
    verdict: SentenceVerdict,
    after: number
): number | null {
    const n = statuses.length
    if (n === 0) return null
    for (let step = 1; step <= n; step++) {
        const i = (((after + step) % n) + n) % n
        if (statuses[i].kind === verdict) return i
    }
    return null
}
