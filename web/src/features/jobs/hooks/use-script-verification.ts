import { useMemo } from 'react'
import type { Job, TrafficCorridor } from '../types'
import { hasScript, hasTraffic } from '../types'
import {
    segmentScript,
    matchSentencesToIncidents,
    lineageFromScript,
    type ProvenanceSourceKind,
    type ProvenanceResult,
} from '../lib/verification/provenance'
import {
    checkSentenceClaims,
    type ClaimFlag,
    type LinkedCorridor,
} from '../lib/verification/claims'

export interface SentenceSourceInfo {
    readonly incidentKeys: readonly string[]
    readonly corridorMatched?: boolean
    readonly routeMatched?: boolean
    readonly sourceKinds?: readonly ProvenanceSourceKind[]
    readonly flags?: readonly ClaimFlag[]
    readonly matchedBy?: 'road-token' | 'model'
}

export interface ScriptVerification {
    readonly provenance: ProvenanceResult | null
    /** True when lineage is model-attributed; false = road-name heuristic. */
    readonly isModelProvenance: boolean
    /** Per-sentence sources with claim flags (model provenance only). */
    readonly extendedBySentence: readonly SentenceSourceInfo[] | undefined
    /** Contradiction notes per road, for the Coverage Board's claim tags. */
    readonly claimNotesByRoad:
        ReadonlyMap<string, readonly string[]> | undefined
    /** Sentences whose claims contradict source data — decision-critical. */
    readonly flaggedSentenceCount: number
}

/**
 * Script↔incident provenance plus claim checking, derived purely from the
 * job. Shared by the review cockpit (display) and the decision surface
 * (gating approve when contradicted claims remain).
 */
export function useScriptVerification(job: Job): ScriptVerification {
    const scriptText = hasScript(job) ? (job.script?.text ?? '') : ''
    const traffic = hasTraffic(job) ? job.traffic : undefined
    const scriptSentences = hasScript(job) ? job.script?.sentences : undefined

    const { provenance, isModelProvenance } = useMemo<{
        provenance: ProvenanceResult | null
        isModelProvenance: boolean
    }>(() => {
        if (!scriptText) return { provenance: null, isModelProvenance: false }
        const sentences = segmentScript(scriptText)
        const modelResult = lineageFromScript(
            sentences,
            scriptSentences ?? undefined,
            traffic?.corridors ?? [],
            traffic?.monitoredRoutes ?? [],
            traffic?.incidents ?? [],
            traffic?.flowRoads ?? []
        )

        if (modelResult)
            return { provenance: modelResult, isModelProvenance: true }
        return {
            provenance: matchSentencesToIncidents(
                sentences,
                traffic?.incidents ?? [],
                traffic?.corridors ?? [],
                traffic?.flowRoads ?? []
            ),
            isModelProvenance: false,
        }
    }, [scriptText, scriptSentences, traffic])

    // Claim flags per sentence (only for model-matched provenance with corridors)
    const { extendedBySentence, claimNotesByRoad } = useMemo<{
        extendedBySentence: readonly SentenceSourceInfo[] | undefined
        claimNotesByRoad: ReadonlyMap<string, readonly string[]> | undefined
    }>(() => {
        if (!provenance || !isModelProvenance || !traffic?.corridors)
            return {
                extendedBySentence: undefined,
                claimNotesByRoad: undefined,
            }

        // Build incident key → corridor lookup
        const corridorByKey = new Map<string, TrafficCorridor>()
        for (const c of traffic.corridors) {
            for (const key of c.incidentKeys ?? []) {
                corridorByKey.set(key, c)
            }
        }

        // Contradictions per road, for the Coverage Board's claim tags
        const notesByRoad = new Map<string, string[]>()

        const bySentence = provenance.bySentence.map((entry, i) => {
            const text = provenance.sentences[i]?.text ?? ''
            const linkedCorridors: readonly LinkedCorridor[] =
                entry.incidentKeys
                    .map((key) => corridorByKey.get(key))
                    .filter((c): c is TrafficCorridor => c != null)

            const flags =
                linkedCorridors.length > 0
                    ? checkSentenceClaims(text, linkedCorridors)
                    : []

            for (const flag of flags) {
                for (const c of linkedCorridors) {
                    const list = notesByRoad.get(c.roadName)
                    if (list) list.push(flag.note)
                    else notesByRoad.set(c.roadName, [flag.note])
                }
            }

            return {
                incidentKeys: entry.incidentKeys,
                corridorMatched: entry.corridorMatched,
                routeMatched: entry.routeMatched,
                sourceKinds: entry.sourceKinds,
                flags,
                matchedBy: 'model' as const,
            }
        })

        return { extendedBySentence: bySentence, claimNotesByRoad: notesByRoad }
    }, [provenance, isModelProvenance, traffic?.corridors])

    const flaggedSentenceCount = useMemo(
        () =>
            extendedBySentence?.filter((e) => (e.flags?.length ?? 0) > 0)
                .length ?? 0,
        [extendedBySentence]
    )

    return {
        provenance,
        isModelProvenance,
        extendedBySentence,
        claimNotesByRoad,
        flaggedSentenceCount,
    }
}
