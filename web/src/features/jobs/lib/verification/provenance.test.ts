// =============================================================================
// Provenance unit tests — sentence segmentation and heuristic matching
// =============================================================================

import { describe, it, expect } from 'vitest'
import {
    segmentScript,
    matchSentencesToIncidents,
    lineageFromScript,
    tokenizeHeadline,
    isAdviceSentence,
    ROAD_ABBREVIATIONS,
    type ScriptSentence,
} from './provenance'
import { buildSentenceStatuses } from './sentence-status'
import { makeIncident } from '@/test/factories'
import type { JobIncident } from '../../types'

// ---------------------------------------------------------------------------
// Sentence segmentation
// ---------------------------------------------------------------------------

describe('segmentScript', () => {
    it('splits simple sentences on . ! ? boundaries', () => {
        const text = 'First sentence. Second sentence! Third one?'
        const result = segmentScript(text)
        expect(result).toHaveLength(3)
        expect(result[0].text).toBe('First sentence.')
        expect(result[1].text).toBe('Second sentence!')
        expect(result[2].text).toBe('Third one?')
    })

    it('handles trailing text with no final punctuation', () => {
        const text = 'Go now. The end'
        const result = segmentScript(text)
        expect(result).toHaveLength(2)
        expect(result[1].text).toBe('The end')
    })

    it('excludes bracketed direction cues', () => {
        const text = '[urgent] Crash on the M4. [friendly] Drive safely.'
        const result = segmentScript(text)
        expect(result).toHaveLength(2)
        expect(result[0].text).toBe('Crash on the M4.')
        expect(result[1].text).toBe('Drive safely.')
    })

    it('assigns correct word indices skipping cues', () => {
        // "[urgent] Hello world. [calm] Good day."
        // Words: Hello(0), world(1), Good(2), day(3)
        const text = '[urgent] Hello world. [calm] Good day.'
        const result = segmentScript(text)
        expect(result).toHaveLength(2)
        expect(result[0]).toMatchObject({
            startWord: 0,
            endWord: 2,
        })
        expect(result[1]).toMatchObject({
            startWord: 2,
            endWord: 4,
        })
    })

    it('counts hyphenated and punctuated tokens as single words', () => {
        // Tokenisation must match the renderer's whitespace split:
        // "multi-vehicle" is one token, "3AW.com.au" is one token.
        const text = 'A multi-vehicle crash on the M1. Visit 3AW.com.au now.'
        const result = segmentScript(text)
        expect(result).toHaveLength(2)
        // A(0) multi-vehicle(1) crash(2) on(3) the(4) M1.(5)
        expect(result[0]).toMatchObject({ startWord: 0, endWord: 6 })
        // Visit(6) 3AW.com.au(7) now.(8)
        expect(result[1]).toMatchObject({ startWord: 6, endWord: 9 })
    })

    it('handles abbreviations like St. and Dr. correctly (no false split)', () => {
        const text = 'At St Peters the road is closed. Dr Smith reported it.'
        const result = segmentScript(text)
        // "St" followed by space + capital P → not a sentence boundary because
        // the pattern requires lookbehind for .!? and St has no period before
        // the space in this context. Actually "St Peters" doesn't have a period
        // so this is fine.
        expect(result).toHaveLength(2)
        expect(result[0].text).toBe('At St Peters the road is closed.')
    })

    it('returns empty array for empty text', () => {
        expect(segmentScript('')).toEqual([])
    })

    it('returns empty array for whitespace-only text', () => {
        expect(segmentScript('   \n  ')).toEqual([])
    })

    it('splits on an ellipsis followed by a capital letter', () => {
        // Verbatim from deployed dev job 71568e4eac444b6db2a06fe24f92f2bc
        // (Port Lincoln, 15s): the ellipsis terminator keeps sentence-level
        // lineage in step with the Domain splitter.
        const text =
            'The Birdseye Highway and Tod Highway are moving nicely, with a clear run on Port Kenny Road… Road construction and contraflow are underway on Porter Street and Railway Place. Cool in Port Lincoln, eleven degrees.'
        const result = segmentScript(text)
        expect(result).toHaveLength(3)
        expect(result[0].text).toBe(
            'The Birdseye Highway and Tod Highway are moving nicely, with a clear run on Port Kenny Road…'
        )
        expect(result[1].text).toBe(
            'Road construction and contraflow are underway on Porter Street and Railway Place.'
        )
        expect(result[2].text).toBe('Cool in Port Lincoln, eleven degrees.')
    })

    it('does not split on an ellipsis followed by a lowercase word', () => {
        // Verbatim from deployed dev job 69be87bf4b0641309881b904c2bb1e90
        // (River 1467): a mid-sentence ellipsis is a delivery-pause device,
        // not a sentence boundary.
        const text =
            'Walker Road, Old Mail Road and Lock 10 Road are all closed… citybound on the first two, and outbound.'
        const result = segmentScript(text)
        expect(result).toHaveLength(1)
        expect(result[0].text).toBe(
            'Walker Road, Old Mail Road and Lock 10 Road are all closed… citybound on the first two, and outbound.'
        )
    })

    it('works with the M4 job script', () => {
        const text =
            'On the M4 westbound at Strathfield, a multi-vehicle crash has lanes reduced near the Homebush Bay Drive exit. Emergency services are on site. Expect delays of 10 minutes. On the Sydney Harbour Bridge citybound, a breakdown is blocking the left lane on the approach. Allow extra time for your commute. At St Peters on the Princes Highway, roadworks are causing slowdowns in both directions.'
        const result = segmentScript(text)
        expect(result).toHaveLength(6)
        expect(result[0].text).toBe(
            'On the M4 westbound at Strathfield, a multi-vehicle crash has lanes reduced near the Homebush Bay Drive exit.'
        )
        expect(result[2].text).toBe('Expect delays of 10 minutes.')
        expect(result[3].text).toBe(
            'On the Sydney Harbour Bridge citybound, a breakdown is blocking the left lane on the approach.'
        )
        expect(result[5].text).toBe(
            'At St Peters on the Princes Highway, roadworks are causing slowdowns in both directions.'
        )
    })
})

// ---------------------------------------------------------------------------
// tokenizeHeadline
// ---------------------------------------------------------------------------

describe('tokenizeHeadline', () => {
    it('lowercases and strips punctuation', () => {
        expect(tokenizeHeadline('M4 — crash at Strathfield!')).toEqual([
            'm4',
            'crash',
            'at',
            'strathfield',
        ])
    })

    it('expands road abbreviations', () => {
        expect(
            tokenizeHeadline('Princes Hwy — roadworks at St Peters')
        ).toContain('highway')
        expect(
            tokenizeHeadline('West Gate Fwy — accident near Bolte Bridge')
        ).toContain('freeway')
    })

    it('handles Rd, St, Ave expansions', () => {
        const tokens = tokenizeHeadline('Smith St — crash at King Rd')
        expect(tokens).toContain('street')
        expect(tokens).toContain('road')
    })

    it('exports the ROAD_ABBREVIATIONS table', () => {
        expect(ROAD_ABBREVIATIONS.fwy).toBe('freeway')
        expect(ROAD_ABBREVIATIONS.hwy).toBe('highway')
    })
})

// ---------------------------------------------------------------------------
// isAdviceSentence — spoken figures are claims too
// ---------------------------------------------------------------------------

describe('isAdviceSentence', () => {
    it('mutes pure advice with no figure', () => {
        expect(isAdviceSentence('Allow extra time for your commute.')).toBe(
            true
        )
    })

    it('a figure is a claim whether written or spoken', () => {
        expect(
            isAdviceSentence('Allow an extra 15 minutes of travel time.')
        ).toBe(false)
        expect(
            isAdviceSentence('Allow an extra fifteen minutes of travel time.')
        ).toBe(false)
    })
})

// ---------------------------------------------------------------------------
// matchSentencesToIncidents
// ---------------------------------------------------------------------------

describe('matchSentencesToIncidents', () => {
    // makeIncident imported from @/test/factories

    it('matches sentence to incident by road name', () => {
        const sentences: ScriptSentence[] = [
            {
                index: 0,
                text: 'On the M4 westbound at Strathfield, a multi-vehicle crash has lanes reduced.',
                startWord: 0,
                endWord: 14,
            },
            {
                index: 1,
                text: 'On the Sydney Harbour Bridge, a breakdown is blocking the left lane.',
                startWord: 14,
                endWord: 26,
            },
        ]
        const incidents = [
            makeIncident(
                'inc-syd-01',
                'M4 — multi-vehicle crash at Strathfield'
            ),
            makeIncident('inc-syd-02', 'Harbour Bridge — breakdown citybound'),
        ]

        const result = matchSentencesToIncidents(sentences, incidents)

        // Sentence 0 should match incident 0 (M4 crash)
        expect(result.bySentence[0].incidentKeys).toContain('inc-syd-01')
        // Sentence 0 should NOT match Harbour Bridge incident
        expect(result.bySentence[0].incidentKeys).not.toContain('inc-syd-02')

        // Sentence 1 should match incident 1 (Harbour Bridge)
        expect(result.bySentence[1].incidentKeys).toContain('inc-syd-02')
        // Sentence 1 should NOT match M4 crash
        expect(result.bySentence[1].incidentKeys).not.toContain('inc-syd-01')

        // Per-incident lookup
        expect(result.byIncidentKey['inc-syd-01'].sentenceIndexes).toEqual([0])
        expect(result.byIncidentKey['inc-syd-02'].sentenceIndexes).toEqual([1])
        expect(result.byIncidentKey['inc-syd-01'].matchedBy).toBe('road-token')
    })

    it('does not tie a sentence to other incidents via generic road-type words', () => {
        // Regression: every headline here contains "Freeway"; the West Gate
        // sentence must match only the West Gate incident, not all three.
        const sentences: ScriptSentence[] = [
            {
                index: 0,
                text: 'A serious multi-vehicle crash on the West Gate Freeway at the Bolte Bridge has two right lanes blocked westbound.',
                startWord: 0,
                endWord: 20,
            },
        ]
        const incidents = [
            makeIncident(
                'inc-mel-01',
                'West Gate Freeway — multi-vehicle crash at the Bolte Bridge'
            ),
            makeIncident(
                'inc-mel-02',
                'Monash Freeway — congestion at Warrigal Road'
            ),
            makeIncident(
                'inc-mel-03',
                'Tullamarine Freeway — breakdown near the airport'
            ),
        ]

        const result = matchSentencesToIncidents(sentences, incidents)

        expect(result.bySentence[0].incidentKeys).toEqual(['inc-mel-01'])
        expect(result.byIncidentKey['inc-mel-02'].sentenceIndexes).toEqual([])
        expect(result.byIncidentKey['inc-mel-03'].sentenceIndexes).toEqual([])
    })

    it('matches whole words only, not substrings', () => {
        const sentences: ScriptSentence[] = [
            {
                index: 0,
                text: 'Police continue to investigate the earlier incident.',
                startWord: 0,
                endWord: 7,
            },
        ]
        const incidents = [makeIncident('inc-01', 'Gate Street — closure')]

        const result = matchSentencesToIncidents(sentences, incidents)
        // "gate" must not match inside "investigate"
        expect(result.bySentence[0].incidentKeys).toEqual([])
    })

    it('handles incident with zero matching sentences', () => {
        const sentences: ScriptSentence[] = [
            {
                index: 0,
                text: 'Weather is clear today.',
                startWord: 0,
                endWord: 5,
            },
        ]
        const incidents = [
            makeIncident('inc-01', 'M4 — multi-vehicle crash at Strathfield'),
        ]

        const result = matchSentencesToIncidents(sentences, incidents)
        expect(result.bySentence[0].incidentKeys).toEqual([])
        expect(result.byIncidentKey['inc-01'].sentenceIndexes).toEqual([])
    })

    it('handles sentence with zero matching incidents', () => {
        const sentences: ScriptSentence[] = [
            {
                index: 0,
                text: 'Weather is clear today.',
                startWord: 0,
                endWord: 5,
            },
            {
                index: 1,
                text: 'Crash on the M4 westbound.',
                startWord: 5,
                endWord: 11,
            },
        ]
        const incidents: JobIncident[] = []

        const result = matchSentencesToIncidents(sentences, incidents)
        expect(result.bySentence).toHaveLength(2)
        expect(result.bySentence[0].incidentKeys).toEqual([])
        expect(result.bySentence[1].incidentKeys).toEqual([])
        expect(result.byIncidentKey).toEqual({})
    })

    it('matches the Princes Highway sentence if incident exists', () => {
        const sentences: ScriptSentence[] = [
            {
                index: 0,
                text: 'At St Peters on the Princes Highway, roadworks are causing slowdowns in both directions.',
                startWord: 0,
                endWord: 12,
            },
        ]
        const incidents = [
            makeIncident('inc-syd-03', 'Princes Hwy — roadworks at St Peters'),
        ]

        const result = matchSentencesToIncidents(sentences, incidents)
        expect(result.bySentence[0].incidentKeys).toContain('inc-syd-03')
        expect(result.byIncidentKey['inc-syd-03'].sentenceIndexes).toEqual([0])
    })

    it('performs a realistic M4 bulletin test', () => {
        const text =
            'On the M4 westbound at Strathfield, a multi-vehicle crash has lanes reduced near the Homebush Bay Drive exit. Emergency services are on site. Expect delays of 10 minutes. On the Sydney Harbour Bridge citybound, a breakdown is blocking the left lane on the approach. Allow extra time for your commute. At St Peters on the Princes Highway, roadworks are causing slowdowns in both directions.'
        const sentences = segmentScript(text)
        expect(sentences).toHaveLength(6)

        const incidents = [
            makeIncident(
                'inc-syd-01',
                'M4 — multi-vehicle crash at Strathfield'
            ),
            makeIncident('inc-syd-02', 'Harbour Bridge — breakdown citybound'),
        ]

        const result = matchSentencesToIncidents(sentences, incidents)

        // Sentence 0 (M4) matches incident 0
        expect(result.bySentence[0].incidentKeys).toContain('inc-syd-01')
        expect(result.bySentence[0].incidentKeys).not.toContain('inc-syd-02')

        // Sentence 3 (Harbour Bridge) matches incident 1
        expect(result.bySentence[3].incidentKeys).toContain('inc-syd-02')

        // Sentence 4 (Allow extra time) matches nothing
        expect(result.bySentence[4].incidentKeys).toEqual([])

        // Sentence 5 (Princes Highway) matches nothing since Princes Hwy incident not in list
        expect(result.bySentence[5].incidentKeys).toEqual([])

        // Per-incident
        expect(result.byIncidentKey['inc-syd-01'].sentenceIndexes).toEqual([0])
        expect(result.byIncidentKey['inc-syd-02'].sentenceIndexes).toEqual([3])
    })

    it('does not claim exact lineage in result type', () => {
        const sentences: ScriptSentence[] = [
            { index: 0, text: 'Crash on the M4.', startWord: 0, endWord: 5 },
        ]
        const incidents = [makeIncident('inc-01', 'M4 — crash')]

        const result = matchSentencesToIncidents(sentences, incidents)
        for (const entry of Object.values(result.byIncidentKey)) {
            expect(entry.matchedBy).toBe('road-token')
        }
    })

    it('road-token matches corridors so flow-only roads carry lineage', () => {
        // A congestion corridor has no incident to stand in for it, so without
        // its own entry the evidence rail cannot account for the sentence.
        const sentences: ScriptSentence[] = [
            {
                index: 0,
                text: "Stanmore Road's moving slowly.",
                startWord: 0,
                endWord: 4,
            },
            {
                index: 1,
                text: 'Thanks for listening.',
                startWord: 4,
                endWord: 7,
            },
        ]
        const result = matchSentencesToIncidents(
            sentences,
            [],
            [{ roadName: 'Stanmore Road' }, { roadName: 'Wigram Road' }]
        )
        expect(result.byCorridorRoad['stanmore road']).toEqual({
            sentenceIndexes: [0],
            matchedBy: 'road-token',
        })
        expect(result.byCorridorRoad['wigram road'].sentenceIndexes).toEqual([])
    })
})

// ---------------------------------------------------------------------------
// lineageFromScript — backend-persisted sentence lineage
// ---------------------------------------------------------------------------

describe('lineageFromScript', () => {
    const sentences: ScriptSentence[] = [
        {
            index: 0,
            text: 'On the M4 westbound at Strathfield, a multi-vehicle crash has lanes reduced.',
            startWord: 0,
            endWord: 14,
        },
        {
            index: 1,
            text: 'Thanks for listening to 2GB.',
            startWord: 14,
            endWord: 19,
        },
    ]
    const scriptSentences = [
        {
            text: 'On the M4 westbound at Strathfield, a multi-vehicle crash has lanes reduced.',
            corridorKeys: ['M4'],
        },
        { text: 'Thanks for listening to 2GB.', corridorKeys: [] },
    ]
    const corridors = [
        { roadName: 'M4', incidentKeys: ['inc-m4-01'] },
        { roadName: 'M5', incidentKeys: ['inc-m5-01'] },
    ]

    it('returns model lineage when texts align and scriptSentences is provided', () => {
        const result = lineageFromScript(sentences, scriptSentences, corridors)
        expect(result).not.toBeNull()
        expect(result!.bySentence[0].incidentKeys).toEqual(['inc-m4-01'])
        expect(result!.bySentence[1].incidentKeys).toEqual([])
        expect(result!.byIncidentKey['inc-m4-01'].matchedBy).toBe('model')
        expect(result!.byIncidentKey['inc-m4-01'].sentenceIndexes).toEqual([0])
    })
    it('marks a measured monitored route as matched case-insensitively', () => {
        const routeScript = [
            {
                text: sentences[0].text,
                corridorKeys: [],
                routeKeys: ['THE M4 FROM PARRAMATTA TO THE CITY'],
            },
            { text: sentences[1].text, corridorKeys: [] },
        ]
        const result = lineageFromScript(
            sentences,
            routeScript,
            [],
            [{ name: 'the M4 from Parramatta to the city' }]
        )
        expect(result!.bySentence[0].routeMatched).toBe(true)
        expect(result!.bySentence[1].routeMatched).toBe(false)
        const [status] = buildSentenceStatuses(
            result!.sentences,
            result!.bySentence
        )
        expect(status.kind).toBe('sourced')
        expect(status.note).toBe(
            'Monitored drive-time run — the live and typical figures are in the Adjust tab.'
        )
    })

    it('does not match a monitored route name the job did not measure', () => {
        const unknownRoute = [
            {
                text: sentences[0].text,
                corridorKeys: [],
                routeKeys: ['the M7 from Blacktown to the city'],
            },
            { text: sentences[1].text, corridorKeys: [] },
        ]
        const result = lineageFromScript(
            sentences,
            unknownRoute,
            [],
            [{ name: 'the M4 from Parramatta to the city' }]
        )
        expect(result!.bySentence[0].routeMatched).toBe(false)
    })

    it('keeps legacy sentences without routeKeys unmatched', () => {
        const legacy = sentences.map((s) => ({
            text: s.text,
            corridorKeys: [],
        }))
        const result = lineageFromScript(
            sentences,
            legacy,
            [],
            [{ name: 'the M4 from Parramatta to the city' }]
        )
        expect(result!.bySentence[0].routeMatched).toBe(false)
    })

    it('returns null when scriptSentences is undefined', () => {
        expect(lineageFromScript(sentences, undefined, corridors)).toBeNull()
    })

    it('returns null when lengths differ', () => {
        const tooFew = scriptSentences.slice(0, 1)
        expect(lineageFromScript(sentences, tooFew, corridors)).toBeNull()
    })

    it('returns null when text does not align', () => {
        const differentText = [
            { text: 'Totally different sentence.', corridorKeys: ['M4'] },
            { text: 'Thanks for listening to 2GB.', corridorKeys: [] },
        ]
        expect(
            lineageFromScript(sentences, differentText, corridors)
        ).toBeNull()
    })

    it('handles empty corridorKeys — empty incidentKeys', () => {
        const noKeys = [
            { text: sentences[0].text, corridorKeys: [] },
            { text: sentences[1].text, corridorKeys: [] },
        ]
        const result = lineageFromScript(sentences, noKeys, corridors)
        expect(result).not.toBeNull()
        expect(result!.bySentence[0].incidentKeys).toEqual([])
        expect(result!.bySentence[1].incidentKeys).toEqual([])
    })
    it('recovers a location-only structured citation for legacy jobs', () => {
        const locationText =
            'At Shorter Avenue in Beverly Hills, a breakdown has one of three lanes closed, adding three minutes.'
        const result = lineageFromScript(
            [
                {
                    index: 0,
                    text: locationText,
                    startWord: 0,
                    endWord: 16,
                },
            ],
            [
                {
                    text: locationText,
                    corridorKeys: [],
                    evidenceIds: ['r1:location:0', 'r1:lanesClosed'],
                },
            ],
            [{ roadName: 'King Georges Road', incidentKeys: ['inc-kings'] }],
            [],
            [
                {
                    key: 'inc-kings',
                    headline: 'breakdown on King Georges Road',
                    detail: 'At Shorter Avenue - Breakdown',
                },
            ]
        )

        expect(result!.bySentence[0].incidentKeys).toEqual(['inc-kings'])
        expect(result!.byIncidentKey['inc-kings'].sentenceIndexes).toEqual([0])
    })

    it('maps corridorKey to incidentKeys via roadName (case-insensitive)', () => {
        const mixedCase = [{ roadName: 'm4', incidentKeys: ['inc-m4-01'] }]
        const result = lineageFromScript(sentences, scriptSentences, mixedCase)
        expect(result).not.toBeNull()
        expect(result!.bySentence[0].incidentKeys).toContain('inc-m4-01')
    })

    it('aggregates incidentKeys when corridor has multiple keys', () => {
        const multiKey = [
            { roadName: 'M4', incidentKeys: ['inc-m4-01', 'inc-m4-02'] },
        ]
        const result = lineageFromScript(sentences, scriptSentences, multiKey)
        expect(result).not.toBeNull()
        expect(result!.bySentence[0].incidentKeys).toEqual(
            expect.arrayContaining(['inc-m4-01', 'inc-m4-02'])
        )
    })
    it('unions incidentKeys across same-named corridors (segments share a road)', () => {
        // Real jobs produce several corridors per road (segments/directions),
        // including casing variants — a naive lookup overwrites all but one.
        const duplicateRoads = [
            { roadName: 'M4', incidentKeys: ['inc-m4-01'] },
            { roadName: 'M4', incidentKeys: ['inc-m4-02'] },
            { roadName: 'm4', incidentKeys: ['inc-m4-03', 'inc-m4-01'] },
        ]
        const result = lineageFromScript(
            sentences,
            scriptSentences,
            duplicateRoads
        )
        expect(result).not.toBeNull()
        expect(result!.bySentence[0].incidentKeys).toEqual([
            'inc-m4-01',
            'inc-m4-02',
            'inc-m4-03',
        ])
    })

    it('maps multiple sentences to the same incident when corridorKeys overlap', () => {
        const sameIncidentSentences: ScriptSentence[] = [
            {
                index: 0,
                text: 'On the M4 westbound.',
                startWord: 0,
                endWord: 4,
            },
            {
                index: 1,
                text: 'More on the M4 crash.',
                startWord: 4,
                endWord: 9,
            },
        ]
        const sameIncidentScript = [
            { text: 'On the M4 westbound.', corridorKeys: ['M4'] },
            { text: 'More on the M4 crash.', corridorKeys: ['M4'] },
        ]
        const result = lineageFromScript(
            sameIncidentSentences,
            sameIncidentScript,
            corridors
        )
        expect(result).not.toBeNull()
        expect(result!.bySentence[0].incidentKeys).toContain('inc-m4-01')
        expect(result!.bySentence[1].incidentKeys).toContain('inc-m4-01')
        expect(result!.byIncidentKey['inc-m4-01'].sentenceIndexes).toEqual([
            0, 1,
        ])
    })

    it('supports corridor without incidentKeys (no mapping)', () => {
        const noIncidents = [{ roadName: 'M4' }]
        const result = lineageFromScript(
            sentences,
            scriptSentences,
            noIncidents
        )
        expect(result).not.toBeNull()
        expect(result!.bySentence[0].incidentKeys).toEqual([])
    })

    it('records a flow-only corridor in byCorridorRoad', () => {
        // The regression this guards: a congestion corridor maps to no
        // incident, so an incident-only lineage left the sentence's evidence
        // invisible — the evidence rail reported "Cited 1" on a bulletin whose
        // script named ten roads.
        const noIncidents = [{ roadName: 'M4' }, { roadName: 'M5' }]
        const result = lineageFromScript(
            sentences,
            scriptSentences,
            noIncidents
        )
        expect(result!.byCorridorRoad['m4']).toEqual({
            sentenceIndexes: [0],
            matchedBy: 'model',
        })
        expect(result!.byCorridorRoad['m5'].sentenceIndexes).toEqual([])
        expect(result!.bySentence[0].corridorMatched).toBe(true)
        expect(result!.bySentence[1].corridorMatched).toBe(false)
    })

    it('ignores a cited road the job never fetched', () => {
        // A citation of a corridor absent from the traffic data is not
        // evidence, so it must not appear as a row or mark the sentence sourced.
        const citesUnknownRoad = [
            { text: sentences[0].text, corridorKeys: ['M7'] },
            { text: sentences[1].text, corridorKeys: [] },
        ]
        const result = lineageFromScript(sentences, citesUnknownRoad, corridors)
        expect(result!.byCorridorRoad['m7']).toBeUndefined()
        expect(result!.bySentence[0].corridorMatched).toBe(false)
    })

    it('collapses same-road corridor segments into one byCorridorRoad entry', () => {
        const segments = [
            { roadName: 'M4', incidentKeys: [] },
            { roadName: 'm4', incidentKeys: [] },
        ]
        const result = lineageFromScript(sentences, scriptSentences, segments)
        expect(Object.keys(result!.byCorridorRoad)).toEqual(['m4'])
        expect(result!.byCorridorRoad['m4'].sentenceIndexes).toEqual([0])
    })

    it('maps selected M5 and M7 good runs without inventing incidents', () => {
        const text =
            'Now looking over Sydney. A clean run on the M5 through Moorebank and the M7 at Len Waters Estate, with only minor movement elsewhere. Northbound Church Street at Granville has roadworks northbound, adding three minutes near the M4 Western Motorway. The Warringah Freeway into the city is just one minute slower than typical. Fuel is a dollar ninety-three for unleaded at Silverwater, diesel two dollars thirty-eight at Parramatta. Clear and mild, twenty-four degrees. That is KIIS traffic and weather together.'
        const actualSentences = segmentScript(text)
        const actualScriptSentences = [
            {
                text: actualSentences[0].text,
                corridorKeys: [],
                evidenceIds: [],
                routeKeys: [],
            },
            {
                text: actualSentences[1].text,
                corridorKeys: [],
                evidenceIds: ['goodrun:M5', 'goodrun:M7'],
                routeKeys: [],
            },
            {
                text: actualSentences[2].text,
                corridorKeys: ['Church Street'],
                evidenceIds: ['r1:road', 'r1:delayMinutes'],
                routeKeys: [],
            },
            {
                text: actualSentences[3].text,
                corridorKeys: [],
                evidenceIds: [
                    'route:the Warringah Freeway into the city:driveTime',
                ],
                routeKeys: ['the Warringah Freeway into the city'],
            },
            {
                text: actualSentences[4].text,
                corridorKeys: [],
                evidenceIds: ['fuel:unleaded 91:price', 'fuel:diesel:price'],
                routeKeys: [],
            },
            {
                text: actualSentences[5].text,
                corridorKeys: [],
                evidenceIds: ['weather:condition', 'weather:temperature'],
                routeKeys: [],
            },
            {
                text: actualSentences[6].text,
                corridorKeys: [],
                evidenceIds: [],
                routeKeys: [],
            },
        ]
        const result = lineageFromScript(
            actualSentences,
            actualScriptSentences,
            [
                { roadName: 'Church Street', incidentKeys: ['church-1'] },
                // M4 is named as Church Street's anchor, not voiced flow.
                { roadName: 'M4', incidentKeys: [] },
            ],
            [{ name: 'the Warringah Freeway into the city' }],
            [
                {
                    key: 'church-1',
                    headline: 'construction on Church St',
                    detail: 'Between M4 Western Motorway and Church Street - Roadworks',
                },
            ],
            [
                { roadName: 'M5' },
                { roadName: 'M7' },
                { roadName: 'M4' },
                { roadName: 'Warringah Fwy' },
            ]
        )

        expect(result).not.toBeNull()
        expect(result!.byCorridorRoad.m5.sentenceIndexes).toEqual([1])
        expect(result!.byCorridorRoad.m7.sentenceIndexes).toEqual([1])
        expect(result!.byCorridorRoad['warringah fwy'].sentenceIndexes).toEqual(
            [3]
        )
        expect(result!.byCorridorRoad.m4.sentenceIndexes).toEqual([])

        expect(result!.bySentence[0].sourceKinds).toEqual([])
        expect(result!.bySentence[1].sourceKinds).toEqual([
            'traffic',
            'good-run',
        ])
        expect(result!.bySentence[1].incidentKeys).toEqual([])
        expect(result!.bySentence[2].sourceKinds).toEqual(['traffic'])
        expect(result!.bySentence[2].incidentKeys).toEqual(['church-1'])
        expect(result!.bySentence[3].sourceKinds).toEqual(['traffic', 'route'])
        expect(result!.bySentence[3].routeMatched).toBe(true)
        expect(result!.bySentence[4].sourceKinds).toEqual(['fuel'])
        expect(result!.bySentence[5].sourceKinds).toEqual(['weather'])
        expect(result!.bySentence[6].sourceKinds).toEqual([])
    })

    it('uses flow roads on the heuristic path without incident lineage', () => {
        const sentences = segmentScript(
            'A clean run on the M5 and M7. Church Street has roadworks.'
        )
        const result = matchSentencesToIncidents(
            sentences,
            [
                {
                    key: 'church-1',
                    headline: 'construction on Church St',
                    detail: 'Roadworks',
                },
            ],
            [{ roadName: 'Church Street' }],
            [{ roadName: 'M5' }, { roadName: 'M7' }]
        )

        expect(result.byCorridorRoad.m5.sentenceIndexes).toEqual([0])
        expect(result.byCorridorRoad.m7.sentenceIndexes).toEqual([0])
        expect(result.bySentence[0].incidentKeys).toEqual([])
        expect(result.bySentence[0].sourceKinds).toEqual(['traffic'])
        expect(result.bySentence[1].incidentKeys).toEqual(['church-1'])
        expect(result.bySentence[1].sourceKinds).toEqual(['traffic'])
    })
})
