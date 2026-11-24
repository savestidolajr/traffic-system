import { describe, it, expect } from 'vitest'
import {
    buildSentenceStatuses,
    countVerdicts,
    nextSentenceOfVerdict,
} from './sentence-status'
import type { ScriptSentence } from './provenance'
import type { SentenceStatus } from '../../components/ScriptRenderer'

const sentence = (text: string, index = 0): ScriptSentence => ({
    index,
    text,
    startWord: 0,
    endWord: 1,
})

describe('buildSentenceStatuses', () => {
    it('configured filler statements are quiet, matched by identity', () => {
        const result = buildSentenceStatuses(
            [sentence('Download the iHeart app for traffic on demand.')],
            [{ incidentKeys: [] }],
            {
                fillerStatementTexts: [
                    'Download the iHeart app for traffic on demand.',
                ],
            }
        )
        expect(result[0].kind).toBe('branding')
        expect(result[0].note).toContain('filler')
    })

    it('filler matching survives sentence splitting of a multi-sentence statement', () => {
        // The configured statement contains two sentences; the splitter
        // yields them separately — each must still match by identity.
        const statement =
            'This is 3AW 693 Traffic. Brought to you by the iHeart app.'
        const result = buildSentenceStatuses(
            [
                sentence('This is 3AW 693 Traffic.'),
                sentence('Brought to you by the iHeart app.', 1),
            ],
            [{ incidentKeys: [] }, { incidentKeys: [] }],
            { fillerStatementTexts: [statement] }
        )
        expect(result[0].kind).toBe('branding')
        expect(result[1].kind).toBe('branding')
    })

    it('branding-looking text is NOT special without a configured filler match', () => {
        const result = buildSentenceStatuses(
            [sentence('This is 3AW 693 Traffic.')],
            [{ incidentKeys: [] }]
        )
        expect(result[0].kind).toBe('unverified')
    })

    it('claim flags outrank source links and join their notes', () => {
        const result = buildSentenceStatuses(
            [sentence('Delays of 10 minutes on the freeway.')],
            [
                {
                    incidentKeys: ['inc-1'],
                    flags: [
                        { kind: 'delay', note: 'Script says 10 — source 18' },
                        { kind: 'direction', note: 'Direction differs' },
                    ],
                },
            ]
        )
        expect(result[0].kind).toBe('check')
        expect(result[0].note).toBe(
            'Script says 10 — source 18 · Direction differs'
        )
    })

    it('sentences with incident keys and no flags are sourced', () => {
        const result = buildSentenceStatuses(
            [sentence('Crash on the M1.')],
            [{ incidentKeys: ['inc-1'] }]
        )
        expect(result[0]).toEqual({ kind: 'sourced' })
    })

    it('distinguishes model vs heuristic unverified notes', () => {
        const [model, heuristic] = buildSentenceStatuses(
            [sentence('Sunny skies today.'), sentence('Roads look fine.')],
            [
                { incidentKeys: [], matchedBy: 'model' },
                { incidentKeys: [], matchedBy: 'road-token' },
            ]
        )
        expect(model.kind).toBe('unverified')
        expect(model.note).toContain('Model reported no source')
        expect(heuristic.kind).toBe('unverified')
        expect(heuristic.note).toContain('road-name heuristic')
    })

    it('routes unverified sentences to Sources when garnish is on', () => {
        const withGarnish = buildSentenceStatuses(
            [sentence('Sunny skies today.')],
            [{ incidentKeys: [], matchedBy: 'model' }],
            { hasGarnish: true }
        )
        expect(withGarnish[0].note).toContain('Sources panel')

        const missingEntry = buildSentenceStatuses(
            [sentence('Fuel is cheap.')],
            [undefined],
            { hasGarnish: true }
        )
        expect(missingEntry[0].kind).toBe('unverified')
        expect(missingEntry[0].note).toContain('Sources panel')

        const without = buildSentenceStatuses(
            [sentence('Sunny skies today.')],
            [{ incidentKeys: [], matchedBy: 'model' }]
        )
        expect(without[0].note).not.toContain('Sources panel')
    })

    it('mutes sourceless pure-advice lines instead of burning amber', () => {
        const [noEntry, noKeys] = buildSentenceStatuses(
            [
                sentence('Allow extra time for your commute.'),
                sentence('Take it easy out there.', 1),
            ],
            [undefined, { incidentKeys: [], matchedBy: 'model' }]
        )
        expect(noEntry.kind).toBe('branding')
        expect(noEntry.note).toContain('Nothing to verify')
        expect(noKeys.kind).toBe('branding')
    })

    it('keeps advice-shaped lines with figures amber — a number is a claim', () => {
        const result = buildSentenceStatuses(
            [sentence('Allow an extra 15 minutes of travel time.')],
            [undefined]
        )
        expect(result[0].kind).toBe('unverified')
    })

    it('never downgrades a flagged claim to advice', () => {
        const result = buildSentenceStatuses(
            [sentence('Take it easy on the freeway.')],
            [
                {
                    incidentKeys: ['inc-1'],
                    flags: [{ kind: 'delay', note: 'differs' }],
                },
            ]
        )
        expect(result[0].kind).toBe('check')
    })

    it('weather sentences are sourced when the job carries weather data', () => {
        const [withData] = buildSentenceStatuses(
            [sentence('More clouds than sun, 25 degrees.')],
            [{ incidentKeys: [], matchedBy: 'model' }],
            { hasWeatherData: true }
        )
        expect(withData.kind).toBe('sourced')
        expect(withData.note).toContain('Weather')

        // No weather data on the job -> the claim has nothing on screen: amber.
        const [withoutData] = buildSentenceStatuses(
            [sentence('More clouds than sun, 25 degrees.')],
            [{ incidentKeys: [], matchedBy: 'model' }]
        )
        expect(withoutData.kind).toBe('unverified')
    })

    it('fuel sentences are sourced when the job carries fuel summaries', () => {
        const [withData] = buildSentenceStatuses(
            [sentence("Unleaded's cheapest at a dollar seventy-two in Woree.")],
            [undefined],
            { hasFuelData: true }
        )
        expect(withData.kind).toBe('sourced')
        expect(withData.note).toContain('Fuel')
    })

    it('flow-only corridors are sourced — the corridor row is the evidence', () => {
        const [flow] = buildSentenceStatuses(
            [sentence("Martyn Street's a bit slow.")],
            [{ incidentKeys: [], corridorMatched: true, matchedBy: 'model' }]
        )
        expect(flow.kind).toBe('sourced')
        expect(flow.note).toContain('flow data')
    })

    it('measured drive-time runs are sourced with the Sources-panel note', () => {
        const [route] = buildSentenceStatuses(
            [sentence('The M4 run is taking 33 minutes.')],
            [{ incidentKeys: [], routeMatched: true, matchedBy: 'model' }]
        )
        expect(route.kind).toBe('sourced')
        expect(route.note).toBe(
            'Monitored drive-time run — the live and typical figures are in the Adjust tab.'
        )
    })

    it('unmeasured and legacy route citations stay unverified', () => {
        const statuses = buildSentenceStatuses(
            [
                sentence('The M7 run is taking 33 minutes.'),
                sentence('The M4 run is taking 33 minutes.', 1),
            ],
            [
                { incidentKeys: [], routeMatched: false, matchedBy: 'model' },
                { incidentKeys: [], matchedBy: 'model' },
            ]
        )
        expect(statuses[0].kind).toBe('unverified')
        expect(statuses[1].kind).toBe('unverified')
    })

    it('good-run mentions are sourced against job.goodRuns', () => {
        const [goodRun] = buildSentenceStatuses(
            [sentence('The Captain Cook Highway is moving well.')],
            [{ incidentKeys: [], matchedBy: 'model' }],
            { goodRunRoads: ['Captain Cook Highway'] }
        )
        expect(goodRun.kind).toBe('sourced')
        expect(goodRun.note).toContain('good run')
    })

    it('claim flags outrank on-screen garnish evidence', () => {
        const [flagged] = buildSentenceStatuses(
            [sentence('Unleaded is cheapest at a dollar fifty.')],
            [
                {
                    incidentKeys: [],
                    flags: [{ kind: 'price', note: 'differs from source' }],
                },
            ],
            { hasFuelData: true }
        )
        expect(flagged.kind).toBe('check')
    })

    it('a fuel-hazard traffic sentence never goes green as garnish', () => {
        // "fuel"/"petrol" without a price signal is hazard vocabulary, and a
        // sentence naming a known corridor is a traffic claim regardless.
        const [spill] = buildSentenceStatuses(
            [sentence('A truck has spilled fuel on the Bruce Highway.')],
            [{ incidentKeys: [], matchedBy: 'model' }],
            {
                hasFuelData: true,
                corridorRoadNames: ['Bruce Highway'],
            }
        )
        expect(spill.kind).toBe('unverified')

        // Even without the corridor guard, no price signal -> not a fuel read.
        const [tanker] = buildSentenceStatuses(
            [sentence('A petrol tanker has rolled over near the ramp.')],
            [{ incidentKeys: [], matchedBy: 'model' }],
            { hasFuelData: true }
        )
        expect(tanker.kind).toBe('unverified')
    })

    it('weather-hazard words alone never classify a sentence as weather', () => {
        const [fog] = buildSentenceStatuses(
            [sentence('Fog patches are slowing things on the range.')],
            [{ incidentKeys: [], matchedBy: 'model' }],
            { hasWeatherData: true }
        )
        expect(fog.kind).toBe('unverified')

        // Hazard word WITH a temperature is a genuine weather read.
        const [wet] = buildSentenceStatuses(
            [sentence('Rain easing through the morning, 8 degrees.')],
            [{ incidentKeys: [], matchedBy: 'model' }],
            { hasWeatherData: true }
        )
        expect(wet.kind).toBe('sourced')
    })

    it('a corridor-naming weather-flavoured sentence stays amber', () => {
        const [rangeFog] = buildSentenceStatuses(
            [
                sentence(
                    'Cloudy conditions over the Kuranda Range this morning.'
                ),
            ],
            [{ incidentKeys: [], matchedBy: 'model' }],
            {
                hasWeatherData: true,
                corridorRoadNames: ['Kuranda Range'],
            }
        )
        expect(rangeFog.kind).toBe('unverified')
    })

    it('good-run matching is whole-word — M1 must not match M15', () => {
        const [m15] = buildSentenceStatuses(
            [sentence('The M15 is heavy northbound at the interchange.')],
            [{ incidentKeys: [], matchedBy: 'model' }],
            { goodRunRoads: ['M1'] }
        )
        expect(m15.kind).toBe('unverified')
    })
})

describe('countVerdicts', () => {
    it('tallies every verdict kind', () => {
        const statuses: SentenceStatus[] = [
            { kind: 'sourced' },
            { kind: 'sourced' },
            { kind: 'check', note: 'x' },
            { kind: 'unverified', note: 'y' },
            { kind: 'branding' },
        ]
        expect(countVerdicts(statuses)).toEqual({
            sourced: 2,
            check: 1,
            unverified: 1,
            branding: 1,
        })
    })
})

describe('nextSentenceOfVerdict', () => {
    const statuses: SentenceStatus[] = [
        { kind: 'sourced' },
        { kind: 'check', note: 'a' },
        { kind: 'sourced' },
        { kind: 'check', note: 'b' },
    ]

    it('finds the next matching index after the cursor', () => {
        expect(nextSentenceOfVerdict(statuses, 'check', -1)).toBe(1)
        expect(nextSentenceOfVerdict(statuses, 'check', 1)).toBe(3)
    })

    it('wraps around to cycle through flagged sentences', () => {
        expect(nextSentenceOfVerdict(statuses, 'check', 3)).toBe(1)
    })

    it('returns null when no sentence carries the verdict', () => {
        expect(nextSentenceOfVerdict(statuses, 'unverified', -1)).toBeNull()
        expect(nextSentenceOfVerdict([], 'check', -1)).toBeNull()
    })
})
