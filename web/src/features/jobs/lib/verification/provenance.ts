// =============================================================================
// Provenance — heuristic sentence↔incident matching for the review cockpit
//
// Naming is honest: this is a road-name heuristic, not verified lineage.
// =============================================================================

import type { JobIncident } from '../../types'
import { escapeRegExp } from '../pronunciation-matches'
import { normalizeSpokenNumbers } from './spoken-numbers'

/** A single sentence extracted from the bulletin script, excluding cues. */
export interface ScriptSentence {
    readonly index: number
    readonly text: string
    /** Inclusive start index into the flattened word-timing array. */
    readonly startWord: number
    /** Exclusive end index into the flattened word-timing array. */
    readonly endWord: number
}

/** Per-sentence and per-incident provenance lookups. */
export interface ProvenanceResult {
    readonly sentences: readonly ScriptSentence[]
    /** Per-sentence incident keys (parallel to sentences). */
    readonly bySentence: ReadonlyArray<{
        readonly incidentKeys: readonly string[]
        /**
         * The sentence cites a corridor that exists in the job's traffic data,
         * even when that corridor carries no discrete incidents (flow/congestion
         * corridors — "X is moving slowly"). The corridor row in the rail IS the
         * displayed evidence, so the verdict layer treats these as sourced.
         * Absent only on older persisted objects that predate this field.
         */
        readonly corridorMatched?: boolean
        /**
         * The sentence cites a monitored drive-time run measured for this job.
         * The drive-time row in the ADJUST tab IS the displayed evidence — the
         * reading column's Sources panel covers non-traffic claims only.
         */
        readonly routeMatched?: boolean
        /**
         * Source categories carried by the sentence's persisted evidence and
         * verified road/route matches. Older jobs may not have this field.
         */
        readonly sourceKinds?: readonly ProvenanceSourceKind[]
    }>
    /** Per-incident sentence indexes. */
    readonly byIncidentKey: Record<
        string,
        {
            readonly sentenceIndexes: readonly number[]
            readonly matchedBy: 'road-token' | 'model'
        }
    >
    /**
     * Per-corridor and per-flow-road sentence indexes, keyed by LOWERCASED
     * road name. Flow roads carry no incident row of their own, but are still
     * displayed evidence for clear-run and measured-flow claims.
     */
    readonly byCorridorRoad: Record<
        string,
        {
            readonly sentenceIndexes: readonly number[]
            readonly matchedBy: 'road-token' | 'model'
        }
    >
}
/** A flow road supplied separately from incident corridors. */
export type FlowRoadEvidence = Readonly<{ roadName: string }>

/** Kinds of evidence a sentence can expose in the review cockpit. */
export type ProvenanceSourceKind =
    'traffic' | 'route' | 'good-run' | 'weather' | 'fuel'

const SOURCE_KIND_ORDER: readonly ProvenanceSourceKind[] = [
    'traffic',
    'route',
    'good-run',
    'weather',
    'fuel',
]

const NON_SOURCE_EVIDENCE_PREFIXES = [
    'filler',
    'wrapper',
    'branding',
    'intro',
    'outro',
    'advice',
] as const

function roadKey(roadName: string): string {
    return roadName.trim().toLowerCase()
}

function evidenceSourceKind(
    evidenceId: string
): ProvenanceSourceKind | undefined {
    const id = evidenceId.trim().toLowerCase()
    if (!id) return undefined
    if (id === 'weather' || id.startsWith('weather:')) return 'weather'
    if (id === 'fuel' || id.startsWith('fuel:')) return 'fuel'
    if (
        id === 'goodrun' ||
        id.startsWith('goodrun:') ||
        id === 'good-run' ||
        id.startsWith('good-run:')
    )
        return 'good-run'
    if (id === 'route' || id.startsWith('route:')) return 'route'
    if (
        id === 'corridor' ||
        id.startsWith('corridor:') ||
        id === 'cleared' ||
        id.startsWith('cleared:')
    )
        return 'traffic'
    if (
        NON_SOURCE_EVIDENCE_PREFIXES.some(
            (prefix) => id === prefix || id.startsWith(`${prefix}:`)
        )
    )
        return undefined

    // The pre-workflow path used compact ids such as "r1:location:0". Any
    // remaining non-wrapper id is an evidence fact too; keeping it traffic
    // preserves source attribution for legacy jobs without naming a fake
    // category for filler text.
    return 'traffic'
}

function hasActiveTrafficEvidence(evidenceIds: readonly string[]): boolean {
    return evidenceIds.some((id) => {
        const normalized = id.trim().toLowerCase()
        return (
            !normalized.startsWith('cleared:') &&
            normalized !== 'cleared' &&
            evidenceSourceKind(id) === 'traffic'
        )
    })
}

function deriveSourceKinds(
    evidenceIds: readonly string[] | undefined,
    incidentKeys: readonly string[],
    corridorMatched: boolean | undefined,
    routeMatched: boolean | undefined
): readonly ProvenanceSourceKind[] {
    const kinds = new Set<ProvenanceSourceKind>()
    for (const id of evidenceIds ?? []) {
        const kind = evidenceSourceKind(id)
        if (kind) kinds.add(kind)
    }
    if (incidentKeys.length > 0 || corridorMatched) kinds.add('traffic')
    if (routeMatched) kinds.add('route')
    return SOURCE_KIND_ORDER.filter((kind) => kinds.has(kind))
}

function normalizedRoadTokens(text: string): string[] {
    return tokenizeHeadline(normalizeSpokenNumbers(text)).map((token) =>
        token.replace(/'s?$/i, '')
    )
}

function splitRouteCode(token: string): string[] | undefined {
    const match = /^([a-z]{1,2})(\d+)([a-z]?)$/i.exec(token)
    if (!match) return undefined
    return [
        match[1].toLowerCase(),
        match[2],
        ...(match[3] ? [match[3].toLowerCase()] : []),
    ]
}

/** Strict whole-road matching used only to recover flow-only lineage. */
function containsRoadPhrase(
    sentenceTokens: readonly string[],
    roadTokens: readonly string[]
): boolean {
    if (roadTokens.length === 0 || roadTokens.length > sentenceTokens.length)
        return false

    // Match the full road phrase, allowing the same small amount of connective
    // wording the server mapper allows between road-name words.
    for (
        let start = 0;
        start <= sentenceTokens.length - roadTokens.length;
        start++
    ) {
        if (sentenceTokens[start] !== roadTokens[0]) continue
        let cursor = start
        let matched = 1
        while (matched < roadTokens.length) {
            const limit = Math.min(sentenceTokens.length - 1, cursor + 3)
            let found = -1
            for (let index = cursor + 1; index <= limit; index++) {
                if (sentenceTokens[index] === roadTokens[matched]) {
                    found = index
                    break
                }
            }
            if (found < 0) break
            cursor = found
            matched++
        }
        if (matched === roadTokens.length) return true
    }
    return false
}

function matchFlowRoad(sentence: ScriptSentence, roadName: string): boolean {
    const sentenceTokens = normalizedRoadTokens(sentence.text)
    const roadTokens = normalizedRoadTokens(roadName)
    if (roadTokens.length === 0) return false

    // A route code is the stable identity for names such as "M5 South West
    // Motorway". The complete display name may not be spoken, but matching
    // the code as a whole word remains conservative and avoids M5/M50
    // substring collisions.
    const routeCode = roadTokens.find((token) => splitRouteCode(token))
    if (
        routeCode &&
        (sentenceTokens.includes(routeCode) ||
            containsRoadPhrase(sentenceTokens, splitRouteCode(routeCode) ?? []))
    )
        return true

    return containsRoadPhrase(sentenceTokens, roadTokens)
}

function matchFlowRoadToSentences(
    sentences: readonly ScriptSentence[],
    roadName: string
): number[] {
    const indexes: number[] = []
    for (let i = 0; i < sentences.length; i++) {
        if (matchFlowRoad(sentences[i], roadName)) indexes.push(i)
    }
    return indexes
}

/** Common road-name abbreviation expansions. */
export const ROAD_ABBREVIATIONS: Record<string, string> = {
    fwy: 'freeway',
    rd: 'road',
    hwy: 'highway',
    st: 'street',
    ave: 'avenue',
    dr: 'drive',
    ln: 'lane',
    pl: 'place',
    cres: 'crescent',
    pde: 'parade',
    tce: 'terrace',
    ct: 'court',
    cl: 'close',
}

/** Captures bracketed direction cues, e.g. [friendly, helpful]. */
export const DIRECTION_CUE_PATTERN = /(\[[^\]]+\])/g

// Sentence boundaries: . ! ? and ellipsis (…) followed by whitespace and a
// capital, digit, quote or bracket. A mid-sentence ellipsis followed by a
// lowercase word is a delivery-pause device, not a boundary.
const SENTENCE_BOUNDARY = /(?<=[.!?…])\s+(?=[A-Z0-9"[({])/g
/** Words not considered distinctive when matching. */
const STOPWORDS: Record<string, true> = {
    the: true,
    a: true,
    an: true,
    on: true,
    at: true,
    in: true,
    near: true,
    to: true,
    for: true,
    and: true,
    is: true,
    are: true,
    of: true,
    by: true,
    with: true,
    from: true,
    as: true,
    but: true,
    or: true,
    its: true,
    it: true,
    this: true,
    that: true,
}

/**
 * Tokens too generic to identify a road on their own: road-type words,
 * compass directions, and traffic-event vocabulary. They appear in most
 * headlines, so matching on them would tie every freeway sentence to every
 * freeway incident.
 */
const GENERIC_TOKENS: Record<string, true> = {
    // Road types (the expansion targets of ROAD_ABBREVIATIONS, plus extras)
    freeway: true,
    road: true,
    highway: true,
    street: true,
    avenue: true,
    drive: true,
    lane: true,
    lanes: true,
    place: true,
    crescent: true,
    parade: true,
    terrace: true,
    court: true,
    close: true,
    motorway: true,
    bridge: true,
    tunnel: true,
    bypass: true,
    ring: true,
    ramp: true,
    exit: true,
    // Directions
    north: true,
    south: true,
    east: true,
    west: true,
    northern: true,
    southern: true,
    eastern: true,
    western: true,
    northbound: true,
    southbound: true,
    eastbound: true,
    westbound: true,
    inbound: true,
    outbound: true,
    citybound: true,
    // Traffic-event vocabulary
    crash: true,
    collision: true,
    breakdown: true,
    congestion: true,
    roadworks: true,
    closure: true,
    closed: true,
    blocked: true,
    delay: true,
    delays: true,
    incident: true,
    vehicle: true,
    multivehicle: true,
    multi: true,
    truck: true,
    stalled: true,
    heavy: true,
    major: true,
    minor: true,
    works: true,
}

/**
 * Split script text into sentences, excluding bracketed direction cues.
 * Word indices align with ScriptRenderer's word-timing array (cues skipped).
 */
export function segmentScript(text: string): ScriptSentence[] {
    // Split into cue / non-cue segments (same pattern as ScriptRenderer)
    const rawSegments = text.split(DIRECTION_CUE_PATTERN)

    // Weave segments back into a full-sentence view, tracking word indices
    // that skip cue words
    const sentences: ScriptSentence[] = []
    let wordIndex = 0

    for (const segment of rawSegments) {
        const isCue = segment.startsWith('[') && segment.endsWith(']')
        if (isCue) {
            continue // direction cues contribute no words
        }

        // Split this non-cue chunk into sentences
        const parts = segment.split(SENTENCE_BOUNDARY)
        for (const part of parts) {
            const trimmed = part.trim()
            if (!trimmed) continue

            const startWord = wordIndex
            // Count whitespace-separated tokens — the SAME tokenisation the
            // ScriptRenderer and useWordTimings use. One canonical scheme:
            // "multi-vehicle" is one word, "3AW.com.au" is one word.
            const count = trimmed.split(/\s+/).filter(Boolean).length
            wordIndex += count

            sentences.push({
                index: sentences.length,
                text: trimmed,
                startWord,
                endWord: wordIndex,
            })
        }
    }

    return sentences
}

/**
 * Road-token match one piece of source text (an incident headline+detail, or a
 * corridor road name) against every sentence: split into words, then
 * normalise/expand road abbreviations and drop stopwords, short words and
 * generic road/event vocabulary, then return the sentences with at least one
 * distinctive token.
 *
 * Identifying tokens only. Text that is all-generic ("congestion delays
 * roadworks") matches NOTHING — the old degrade-to-all-candidates fallback let
 * it attach to any sentence containing "delays" and show a green tick it never
 * earned, in exactly the heuristic mode where claim checks are off.
 */
function matchByRoadTokens(
    sentences: readonly ScriptSentence[],
    text: string
): number[] {
    const distinctive = tokenizeHeadline(text).filter(
        (t) => t.length > 2 && !STOPWORDS[t] && !GENERIC_TOKENS[t]
    )

    // De-duplicate so one source doesn't match the same sentence twice
    const matched = new Set<number>()
    for (const token of distinctive) {
        // Whole-word match: "gate" must not match "investigate"
        const pattern = new RegExp(`\\b${escapeRegExp(token)}\\b`)
        for (let i = 0; i < sentences.length; i++) {
            if (matched.has(i)) continue
            if (pattern.test(sentences[i].text.toLowerCase())) matched.add(i)
        }
    }
    return [...matched].sort((a, b) => a - b)
}

/**
 * Heuristic matching: road-token match every incident and every corridor road
 * name against the script's sentences. Corridors are matched as well as
 * incidents because a flow/congestion corridor has no incident to stand in for
 * it, and an unmatched corridor reads to the operator as "the script cited
 * something that isn't in the data".
 */
export function matchSentencesToIncidents(
    sentences: readonly ScriptSentence[],
    incidents: readonly Pick<JobIncident, 'key' | 'headline' | 'detail'>[],
    corridors: readonly { roadName: string }[] = [],
    flowRoads: readonly FlowRoadEvidence[] = []
): ProvenanceResult {
    // Build mutable arrays during construction, then freeze at the end
    const sentenceIncidentKeys: string[][] = sentences.map(() => [])
    const byIncidentKey: Record<
        string,
        {
            readonly sentenceIndexes: readonly number[]
            readonly matchedBy: 'road-token'
        }
    > = {}

    for (const incident of incidents) {
        // Match from headline first (the primary descriptive field), then
        // fall back to detail (which HERE often puts the actual road name
        // in, e.g. headline "congestion on Citylink" / detail "Ramp to West
        // Gate Freeway (Eastbound)").
        const combined = [incident.headline, incident.detail]
            .filter(Boolean)
            .join(' ')
        const indexes = matchByRoadTokens(sentences, combined)
        byIncidentKey[incident.key] = {
            sentenceIndexes: indexes,
            matchedBy: 'road-token' as const,
        }
        for (const idx of indexes) {
            sentenceIncidentKeys[idx].push(incident.key)
        }
    }

    // Road names are NOT unique per job — one road yields several corridors
    // (segments/directions) that collapse to a single evidence row, so the
    // first entry for a road wins and the rest are skipped.
    const byCorridorRoad: Record<
        string,
        { sentenceIndexes: number[]; matchedBy: 'road-token' }
    > = {}
    for (const corridor of corridors) {
        const road = roadKey(corridor.roadName)
        if (!road || byCorridorRoad[road]) continue
        byCorridorRoad[road] = {
            sentenceIndexes: matchByRoadTokens(sentences, corridor.roadName),
            matchedBy: 'road-token' as const,
        }
    }

    // Flow roads are a separate source from incidents. They may populate the
    // same evidence row as a corridor, but they can never add incident keys.
    for (const flowRoad of flowRoads) {
        const road = roadKey(flowRoad.roadName)
        if (!road) continue
        const indexes = matchFlowRoadToSentences(sentences, flowRoad.roadName)
        const existing = byCorridorRoad[road]
        if (existing) {
            for (const index of indexes) {
                if (!existing.sentenceIndexes.includes(index))
                    existing.sentenceIndexes.push(index)
            }
        } else {
            byCorridorRoad[road] = {
                sentenceIndexes: indexes,
                matchedBy: 'road-token' as const,
            }
        }
    }

    const bySentence = sentenceIncidentKeys.map((keys, index) => {
        const corridorMatched = Object.values(byCorridorRoad).some((entry) =>
            entry.sentenceIndexes.includes(index)
        )
        return {
            incidentKeys: keys,
            corridorMatched,
            sourceKinds: deriveSourceKinds(
                undefined,
                keys,
                corridorMatched,
                undefined
            ),
        }
    })

    return { sentences, bySentence, byIncidentKey, byCorridorRoad }
}

/**
 * Normalize an incident headline into lowercased, punctuation-stripped,
 * abbreviation-expanded tokens.
 */
export function tokenizeHeadline(headline: string): string[] {
    // Strip punctuation except hyphens between words
    const cleaned = headline.replace(/[—–-]+/g, ' ').replace(/[^\w\s']/g, '')
    const words = cleaned.toLowerCase().split(/\s+/).filter(Boolean)
    return words.map((w) => ROAD_ABBREVIATIONS[w] ?? w)
}

// Conservative filler detector: pure listener advice carries nothing to
// verify, so it must not burn amber (amber = "this claim needs you"; cry
// wolf every bulletin and operators stop looking). Any digit disqualifies —
// "allow an extra 15 minutes" states a checkable figure.
const ADVICE_PATTERNS: readonly RegExp[] = [
    /\ballow (?:some |a little )?extra (?:travel )?time\b/,
    /\bgive yourself (?:some |a little )?extra time\b/,
    /\bplan (?:ahead|for (?:delays|extra time))\b/,
    /\btake it (?:easy|slow)\b/,
    /\bdrive safe(?:ly)?\b/,
    /\btake (?:extra )?care (?:on the roads|out there)\b/,
]

/** True for generic no-fact advice lines ("Allow extra time for your commute"). */
export function isAdviceSentence(sentence: string): boolean {
    // A figure is a claim, whether written "15" or spoken "fifteen".
    if (/\d/.test(normalizeSpokenNumbers(sentence))) return false
    const lower = sentence.toLowerCase()
    return ADVICE_PATTERNS.some((p) => p.test(lower))
}

// Weather/fuel sentence detectors — deliberately lexical and conservative.
// They only matter for sentences that already failed incident/corridor
// attribution, and the verdict layer additionally gates them on the job
// actually carrying weather/fuel data AND on the sentence naming no known
// corridor, so a false positive cannot mark a traffic claim green.
//
// Two tiers: report words (sky/temperature vocabulary) identify a weather
// read on their own; hazard words (rain, fog, wind, hail…) are ALSO common
// traffic-incident content ("fog patches on the range", "hail damage
// blocking a lane"), so alone they are not enough — they need a report word
// or an explicit temperature beside them.
const WEATHER_REPORT_PATTERN =
    /\b(?:degrees?|sunny|sunshine|clouds?|cloudy|overcast|humidity|humid|forecast|temperature|top of \d+)\b/
const WEATHER_HAZARD_PATTERN =
    /\b(?:rain|rainy|raining|showers?|drizzle|storms?|stormy|thunder|hail|fog|foggy|mist|misty|wind|windy|gusts?)\b/

/** True for sentences reading the weather ("More clouds than sun, 25 degrees."). */
export function isWeatherSentence(sentence: string): boolean {
    const lower = sentence.toLowerCase()
    if (WEATHER_REPORT_PATTERN.test(lower)) return true
    return (
        WEATHER_HAZARD_PATTERN.test(lower) &&
        /\b\d{1,2}\s*degrees?\b/.test(lower)
    )
}

// Fuel TYPES read as prices; bare "fuel"/"petrol" also appears in traffic
// hazards ("truck spilled fuel", "petrol tanker rollover"), so those two
// need a price signal beside them.
const FUEL_TYPE_PATTERN = /\b(?:unleaded|diesel|e10|lpg)\b/
const FUEL_GENERIC_PATTERN = /\b(?:fuel|petrol)\b/
const FUEL_PRICE_PATTERN =
    /\b(?:\$?\d+(?:\.\d{1,3})?|dollars?|cents?|(?:a|per)\s+litre)\b/

/** True for a fuel-price read, but not a fuel-spill or petrol-tanker incident. */
export function isFuelSentence(sentence: string): boolean {
    const lower = sentence.toLowerCase()
    return (
        FUEL_TYPE_PATTERN.test(lower) ||
        (FUEL_GENERIC_PATTERN.test(lower) && FUEL_PRICE_PATTERN.test(lower))
    )
}

/**
 * Build provenance from backend-persisted sentence lineage.
 * Returns null (caller falls back to heuristic) unless:
 * - scriptSentences exists, same length as sentences, and every text aligns.
 * On success: corridorKeys → incident keys via roadName, corridor and flow
 * road keys → byCorridorRoad, routeKeys → measured monitored-route evidence,
 * and evidence prefixes → sourceKinds; matchedBy: 'model'.
 *
 * Older jobs persisted the Draft's atomic evidenceIds but not the corridor owner
 * derived from those ids. For those records, an unresolved traffic sentence is
 * conservatively matched against incident source text, while flow-only roads
 * are recovered only by a strict whole-road token match.
 */
export function lineageFromScript(
    sentences: readonly ScriptSentence[],
    scriptSentences:
        | readonly {
              text: string
              corridorKeys?: readonly string[]
              readonly evidenceIds?: readonly string[]
              readonly routeKeys?: readonly string[]
          }[]
        | undefined,
    corridors: readonly {
        roadName: string
        incidentKeys?: readonly string[] | null
    }[],
    monitoredRoutes: readonly { name: string }[] = [],
    incidents: readonly Pick<JobIncident, 'key' | 'headline' | 'detail'>[] = [],
    flowRoads: readonly FlowRoadEvidence[] = []
): ProvenanceResult | null {
    if (!scriptSentences) return null
    if (scriptSentences.length !== sentences.length) return null

    // Verify text alignment — both sides produce trimmed output
    for (let i = 0; i < sentences.length; i++) {
        if (scriptSentences[i].text.trim() !== sentences[i].text) return null
    }

    // Build roadName → incidentKeys lookup (case-insensitive). Road names
    // are NOT unique per job — one road can yield several corridors
    // (segments/directions), so same-named entries union their keys. A
    // separate name map covers ALL corridors, including flow-only ones with
    // zero incidents, which is what the evidence rail lists them from.
    const byCorridorRoad: Record<
        string,
        { sentenceIndexes: number[]; matchedBy: 'model' }
    > = {}
    const roadToKeys = new Map<string, string[]>()
    for (const c of corridors) {
        const road = roadKey(c.roadName)
        if (!road) continue
        byCorridorRoad[road] ??= {
            sentenceIndexes: [],
            matchedBy: 'model' as const,
        }
        if (!c.incidentKeys || c.incidentKeys.length === 0) continue
        const existing = roadToKeys.get(road)
        if (existing) {
            for (const k of c.incidentKeys) {
                if (!existing.includes(k)) existing.push(k)
            }
        } else {
            roadToKeys.set(road, [...c.incidentKeys])
        }
    }

    // Flow roads are evidence rows, not incidents. Keep their names in the
    // road map so the Adjust/review rails can show a voiced flow claim, while
    // leaving roadToKeys untouched so no incident lineage is invented.
    const flowOnlyRoadNames: string[] = []
    for (const flowRoad of flowRoads) {
        const road = roadKey(flowRoad.roadName)
        if (!road) continue
        byCorridorRoad[road] ??= {
            sentenceIndexes: [],
            matchedBy: 'model' as const,
        }
        if (!roadToKeys.has(road) && !flowOnlyRoadNames.includes(road))
            flowOnlyRoadNames.push(road)
    }

    const monitoredRouteNames = new Set(
        monitoredRoutes
            .map((route) => route.name.trim().toLowerCase())
            .filter(Boolean)
    )

    const sentenceIncidentKeys: string[][] = sentences.map(() => [])
    const byIncidentKey: Record<
        string,
        {
            readonly sentenceIndexes: readonly number[]
            readonly matchedBy: 'model'
        }
    > = {}

    for (let i = 0; i < scriptSentences.length; i++) {
        const keys: string[] = []
        for (const corridorKey of scriptSentences[i].corridorKeys ?? []) {
            const key = corridorKey.trim().toLowerCase()
            if (!key) continue
            // The model cited this corridor by name: record it whether or not
            // the corridor carries incidents. A citation of a road the job
            // never fetched stays unrecorded — it is not evidence.
            const cited = byCorridorRoad[key]
            if (cited && !cited.sentenceIndexes.includes(i)) {
                cited.sentenceIndexes.push(i)
            }
            const incidentKeys = roadToKeys.get(key) ?? []
            for (const ik of incidentKeys) {
                if (!keys.includes(ik)) keys.push(ik)
                if (!byIncidentKey[ik]) {
                    byIncidentKey[ik] = {
                        sentenceIndexes: [],
                        matchedBy: 'model' as const,
                    }
                }
                if (!byIncidentKey[ik].sentenceIndexes.includes(i)) {
                    const prev = byIncidentKey[ik].sentenceIndexes
                    byIncidentKey[ik] = {
                        ...byIncidentKey[ik],
                        sentenceIndexes: [...prev, i],
                    }
                }
            }
        }
        sentenceIncidentKeys[i] = keys
    }

    // Before EvidenceIds reached the web contract, the backend could persist a
    // valid atomic citation without a CorridorKey when the sentence named only
    // an incident's cross street. Recover that historical shape conservatively:
    // only sentences with traffic evidence IDs and no explicit owner are
    // matched against incident source text.
    for (let i = 0; i < scriptSentences.length; i++) {
        const sentenceData = scriptSentences[i]
        const evidenceIds = sentenceData.evidenceIds ?? []
        const hasTrafficEvidence = hasActiveTrafficEvidence(evidenceIds)
        const corridorKeys = sentenceData.corridorKeys ?? []
        if (
            sentenceIncidentKeys[i].length === 0 &&
            corridorKeys.length === 0 &&
            hasTrafficEvidence &&
            incidents.length > 0
        ) {
            const fallback = matchSentencesToIncidents(
                [sentences[i]],
                incidents
            )
            for (const [incidentKey, entry] of Object.entries(
                fallback.byIncidentKey
            )) {
                if (!entry.sentenceIndexes.includes(0)) continue
                sentenceIncidentKeys[i].push(incidentKey)
                const existing = byIncidentKey[incidentKey]
                byIncidentKey[incidentKey] = {
                    sentenceIndexes: [...(existing?.sentenceIndexes ?? []), i],
                    matchedBy: 'model',
                }
            }
        }

        // Good-run and route sentences historically had no corridor key. A
        // strict whole-road match recovers only flow-only rows, and only when
        // the sentence has no explicit corridor/traffic/supplement citation.
        // This avoids turning a cross-street mention in an incident sentence
        // into a second, invented flow claim.
        const hasSupplementEvidence = evidenceIds.some((id) => {
            const kind = evidenceSourceKind(id)
            return kind === 'weather' || kind === 'fuel'
        })
        if (
            corridorKeys.length === 0 &&
            !hasTrafficEvidence &&
            !hasSupplementEvidence
        ) {
            for (const road of flowOnlyRoadNames) {
                if (
                    byCorridorRoad[road].sentenceIndexes.includes(i) ||
                    !matchFlowRoad(sentences[i], road)
                )
                    continue
                byCorridorRoad[road].sentenceIndexes.push(i)
            }
        }
    }

    const bySentence = sentenceIncidentKeys.map((keys, i) => {
        const sentenceData = scriptSentences[i]
        const corridorMatched = Object.values(byCorridorRoad).some((entry) =>
            entry.sentenceIndexes.includes(i)
        )
        const routeMatched = (sentenceData.routeKeys ?? []).some((k) =>
            monitoredRouteNames.has(k.trim().toLowerCase())
        )
        return {
            incidentKeys: keys,
            corridorMatched,
            routeMatched,
            sourceKinds: deriveSourceKinds(
                sentenceData.evidenceIds,
                keys,
                corridorMatched,
                routeMatched
            ),
        }
    })

    return { sentences, bySentence, byIncidentKey, byCorridorRoad }
}
