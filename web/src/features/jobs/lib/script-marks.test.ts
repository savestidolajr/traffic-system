import { describe, it, expect } from 'vitest'
import { buildScriptMarks, countUnresolved } from './script-marks'
import {
    findPronunciationMatches,
    type PronunciationMatchResult,
} from './pronunciation-matches'
import { padTags, resolveAiredRead } from './tone-marks'
import type { Dictionary, Rule } from '@/features/pronunciation/types'
import type { VerifiedPronunciationEntry } from '@/features/pronunciation/api/verified-pronunciation-hooks'

const TEXT =
    'Glen Iris and the Calder Freeway have minor delays at Toorak Road and Keilor Park Drive, 8 kays back.'

function dictionaryMatch(
    entries: ReadonlyArray<{ text: string; replacement: string }>
): PronunciationMatchResult {
    return {
        dictionaryName: 'Melbourne Roads',
        libraryId: 'pd-1',
        versionNumber: 7,
        providerVersionId: 'v7',
        resolution: 'exact',
        matches: entries.map((e) => {
            const start = TEXT.indexOf(e.text)
            return {
                canonicalText: e.text,
                rule: e.text,
                replacement: e.replacement,
                start,
                end: start + e.text.length,
            }
        }),
    }
}

function verdict(
    word: string,
    overrides: Partial<VerifiedPronunciationEntry> = {}
): VerifiedPronunciationEntry {
    return {
        word,
        note: null,
        enabled: true,
        verifiedBy: 'alice',
        verifiedAt: '2026-07-22T04:10:00.000Z',
        ...overrides,
    }
}

describe('buildScriptMarks', () => {
    it('marks a dictionary substitution with its replacement and version', () => {
        const { marks } = buildScriptMarks(
            TEXT,
            dictionaryMatch([{ text: 'Calder', replacement: 'Call-der' }]),
            [],
            [],
            []
        )
        expect(marks).toHaveLength(1)
        expect(marks[0].text).toBe('Calder')
        expect(marks[0].dictionary).toEqual({
            replacement: 'Call-der',
            dictionaryName: 'Melbourne Roads',
            versionNumber: 7,
        })
        expect(marks[0].review).toBeUndefined()
    })

    it('locates a suspect word in the script', () => {
        const { marks } = buildScriptMarks(TEXT, null, ['Keilor'], [], [])
        expect(marks).toHaveLength(1)
        expect(marks[0].text).toBe('Keilor')
        expect(marks[0].review?.kind).toBe('suspect')
        expect(TEXT.slice(marks[0].start, marks[0].end)).toBe('Keilor')
    })

    // The whole reason for this module: a word with a rule AND a flag used to
    // render twice — inline as a dictionary mark, and again as a badge chip.
    it('merges a dictionary rule and a suspect flag into ONE mark', () => {
        const { marks } = buildScriptMarks(
            TEXT,
            dictionaryMatch([{ text: 'Keilor', replacement: 'KEE-ler' }]),
            ['Keilor'],
            [],
            []
        )
        expect(marks).toHaveLength(1)
        expect(marks[0].dictionary?.replacement).toBe('KEE-ler')
        expect(marks[0].review?.kind).toBe('suspect')
    })

    it('downgrades a suspect word to verified when a verdict covers it', () => {
        const { marks } = buildScriptMarks(
            TEXT,
            null,
            ['Keilor'],
            [],
            [verdict('Keilor')]
        )
        expect(marks[0].review?.kind).toBe('verified')
        expect(marks[0].review?.verdict?.verifiedBy).toBe('alice')
    })

    it('matches a verdict case- and punctuation-insensitively', () => {
        const { marks } = buildScriptMarks(
            "Gate's is closed",
            null,
            ["Gate's"],
            [],
            [verdict('GATES')]
        )
        expect(marks[0].review?.kind).toBe('verified')
    })

    it('ignores a suspended verdict so the word still needs an ear', () => {
        const { marks } = buildScriptMarks(
            TEXT,
            null,
            ['Keilor'],
            [],
            [verdict('Keilor', { enabled: false })]
        )
        expect(marks[0].review?.kind).toBe('suspect')
    })

    it('marks every occurrence of a flagged word', () => {
        const { marks } = buildScriptMarks(
            'Toorak Road meets Toorak Avenue',
            null,
            ['Toorak'],
            [],
            []
        )
        expect(marks).toHaveLength(2)
    })

    it('respects word boundaries', () => {
        // "Glen" must not mark the "Glen" inside "Glenroy".
        const { marks } = buildScriptMarks(
            'Glenroy Road is clear',
            null,
            ['Glen'],
            [],
            []
        )
        expect(marks).toHaveLength(0)
    })

    it('marks an unconfirmed figure in situ', () => {
        const { marks } = buildScriptMarks(TEXT, null, [], ['8'], [])
        expect(marks).toHaveLength(1)
        expect(marks[0].review?.kind).toBe('figure')
        expect(marks[0].text).toBe('8')
    })

    it('reports a flagged word the script no longer contains', () => {
        const { marks, unlocatedWords } = buildScriptMarks(
            TEXT,
            null,
            ['Woolloongabba'],
            [],
            []
        )
        expect(marks).toHaveLength(0)
        expect(unlocatedWords).toEqual(['Woolloongabba'])
    })

    it('reports an unconfirmed figure the script no longer contains', () => {
        const { unlocatedFigures } = buildScriptMarks(
            TEXT,
            null,
            [],
            ['$1.63'],
            []
        )
        expect(unlocatedFigures).toEqual(['$1.63'])
    })

    it('returns marks in document order, never overlapping', () => {
        const { marks } = buildScriptMarks(
            TEXT,
            dictionaryMatch([
                { text: 'Calder', replacement: 'Call-der' },
                { text: 'Toorak', replacement: 'Too-rack' },
            ]),
            ['Keilor'],
            ['8'],
            []
        )
        expect(marks.map((m) => m.text)).toEqual([
            'Calder',
            'Toorak',
            'Keilor',
            '8',
        ])
        for (let i = 1; i < marks.length; i++) {
            expect(marks[i].start).toBeGreaterThanOrEqual(marks[i - 1].end)
        }
    })

    it('handles an empty pre-review as plain dictionary marking', () => {
        const { marks, unlocatedWords, unlocatedFigures } = buildScriptMarks(
            TEXT,
            dictionaryMatch([{ text: 'Calder', replacement: 'Call-der' }]),
            [],
            [],
            []
        )
        expect(marks).toHaveLength(1)
        expect(unlocatedWords).toEqual([])
        expect(unlocatedFigures).toEqual([])
    })

    it('ignores a dictionary match that did not resolve to an exact version', () => {
        const unresolved: PronunciationMatchResult = {
            ...dictionaryMatch([{ text: 'Calder', replacement: 'Call-der' }]),
            resolution: 'missing-version',
        }
        const { marks } = buildScriptMarks(TEXT, unresolved, [], [], [])
        expect(marks).toHaveLength(0)
    })
})

describe('countUnresolved', () => {
    it('counts each review kind, ignoring dictionary-only marks', () => {
        const { marks } = buildScriptMarks(
            TEXT,
            dictionaryMatch([{ text: 'Calder', replacement: 'Call-der' }]),
            ['Keilor', 'Toorak'],
            ['8'],
            [verdict('Toorak')]
        )
        expect(countUnresolved(marks)).toEqual({
            suspect: 1,
            verified: 1,
            figures: 1,
        })
    })
})

describe('buildScriptMarks — figure boundaries', () => {
    // The live report: the backend flagged the figure "8" (from "8 Mile Bridge")
    // and a bare indexOf then painted the same warning onto the 8 inside "M80".
    const liveScript =
        "The West Gate's under the pump citybound from 8 Mile Bridge through to " +
        "Cook Street, and Plenty Road's busy northbound at the M80 ramp."

    it('never marks a figure that sits inside a longer token', () => {
        const { marks } = buildScriptMarks(liveScript, null, [], ['8'], [])
        expect(marks).toHaveLength(1)
        expect(liveScript.slice(marks[0].start - 1, marks[0].end + 1)).toBe(
            ' 8 '
        )
    })

    it('marks nothing when the only occurrence is inside a road designation', () => {
        const { marks, unlocatedFigures } = buildScriptMarks(
            'Plenty Road is busy at the M80 ramp',
            null,
            [],
            ['8'],
            []
        )
        expect(marks).toHaveLength(0)
        expect(unlocatedFigures).toEqual(['8'])
    })

    it('still marks a decimal figure with punctuation around it', () => {
        const { marks } = buildScriptMarks(
            "Unleaded's cheapest at $1.63 today",
            null,
            [],
            ['$1.63'],
            []
        )
        expect(marks).toHaveLength(1)
        expect(marks[0].text).toBe('$1.63')
    })

    it('marks a standalone figure followed by a unit', () => {
        const { marks } = buildScriptMarks(
            'Passing clouds, 14 degrees.',
            null,
            [],
            ['14'],
            []
        )
        expect(marks).toHaveLength(1)
        expect(marks[0].text).toBe('14')
    })
})

/**
 * A rule saved during the review is not on air until the bulletin is re-voiced.
 * The mark says "queued" so the operator can tell a landed change from a pending
 * one — and it must say so at EVERY occurrence, not just the first.
 */
describe('buildScriptMarks — pending pronunciation edits', () => {
    const queued = [{ word: 'Keilor', replacement: 'Kee-lor' }]

    it('marks a queued edit on the flagged word', () => {
        const { marks } = buildScriptMarks(
            TEXT,
            null,
            ['Keilor'],
            [],
            [],
            queued
        )
        const keilor = marks.find((m) => m.text.startsWith('Keilor'))
        expect(keilor?.pendingReplacement).toBe('Kee-lor')
    })

    it('marks EVERY occurrence of a repeated word as queued', () => {
        const repeated = 'Keilor Road is slow, and Keilor Park Drive is worse.'
        const { marks } = buildScriptMarks(
            repeated,
            null,
            ['Keilor'],
            [],
            [],
            queued
        )
        const hits = marks.filter((m) => m.pendingReplacement === 'Kee-lor')
        expect(hits).toHaveLength(2)
    })

    it('leaves unrelated marks alone', () => {
        const { marks } = buildScriptMarks(
            TEXT,
            null,
            ['Keilor', 'Toorak'],
            [],
            [],
            queued
        )
        const toorak = marks.find((m) => m.text.startsWith('Toorak'))
        expect(toorak?.pendingReplacement).toBeUndefined()
    })

    it('has no pending facet when nothing is queued', () => {
        const { marks } = buildScriptMarks(TEXT, null, ['Keilor'], [], [])
        expect(marks.every((m) => m.pendingReplacement === undefined)).toBe(
            true
        )
    })

    // The click-to-respell case: nothing flagged this word, so without a mark of
    // its own the operator's own fix would be invisible in the script.
    it('marks a word NOTHING flagged once a rule is queued for it', () => {
        const { marks } = buildScriptMarks(
            TEXT,
            null,
            [],
            [],
            [],
            [{ word: 'Calder', replacement: 'Call-der' }]
        )
        expect(marks).toHaveLength(1)
        expect(marks[0].text).toBe('Calder')
        expect(marks[0].pendingReplacement).toBe('Call-der')
        expect(marks[0].review).toBeUndefined()
        expect(marks[0].dictionary).toBeUndefined()
    })

    it('marks a queued multi-word phrase as one span', () => {
        const { marks } = buildScriptMarks(
            'Delays on Bell St through Coburg.',
            null,
            [],
            [],
            [],
            [{ word: 'Bell St', replacement: 'Bell Street' }]
        )
        expect(marks).toHaveLength(1)
        expect(marks[0].text).toBe('Bell St')
        expect(marks[0].pendingReplacement).toBe('Bell Street')
    })

    it('reports no queued mark when the word is not in the script', () => {
        const { marks } = buildScriptMarks(
            TEXT,
            null,
            [],
            [],
            [],
            [{ word: 'Woolloongabba', replacement: 'Wool-un-gabba' }]
        )
        expect(marks).toHaveLength(0)
    })
})

/**
 * A mark is a pair of offsets, and offsets only mean something against the text
 * they were resolved in. The script renders the AIRED read, so marks must be
 * resolved there — every audio tag before a match shifts that match right by
 * the tag's own characters.
 *
 * The live report (dev job 17c0b28e, 2026-08-11): a `[brightly] ` opener sat 11
 * characters into the aired read, matches were resolved against the untagged
 * script, and every later mark landed 11 characters early — the KIIS mark on
 * "slow", the Warringah mark spanning "underway on the".
 */
describe('script marks live in the aired read, not the untagged script', () => {
    const script =
        'Now looking over Sydney. Construction’s underway on the Warringah ' +
        'Freeway at Brook Street, and Flinders Road’s a bit slow. ' +
        "That's KIIS traffic and weather together."
    const taggedText = script.replace(
        'Construction’s',
        '[brightly] Construction’s'
    )

    const rule = (stringToReplace: string, replacement: string): Rule => ({
        type: 'alias',
        stringToReplace,
        replacement,
        alphabet: null,
        caseSensitive: false,
        wordBoundaries: true,
        appliesOnAllModels: true,
    })
    const dictionary: Dictionary = {
        id: 'library-sydney',
        dictionaryId: 'provider-sydney',
        name: 'Sydney Roads',
        description: null,
        status: 'active',
        currentVersion: 15,
        createdAt: '2026-08-01T00:00:00Z',
        updatedAt: '2026-08-01T00:00:00Z',
        createdBy: null,
        assignedStations: [],
        rules: [],
        versions: [
            {
                version: 15,
                elevenLabsVersionId: 'provider-version-15',
                rules: [
                    rule('Sydney', 'Sydknee'),
                    rule('Warringah', 'wa-ring-gah'),
                    rule('KIIS', 'kiss'),
                ],
                changeNote: null,
                createdAt: '2026-08-01T00:00:00Z',
                createdBy: null,
            },
        ],
    }

    /** Exactly the composition both the producer and the renderer perform. */
    function markSpace(tagged: string | null) {
        const airedRead = resolveAiredRead(script, tagged)
        const text = padTags(airedRead)
        const match = findPronunciationMatches(
            text,
            'provider-sydney',
            'provider-version-15',
            'eleven_v3',
            [dictionary]
        )
        return {
            airedRead,
            marks: buildScriptMarks(text, match, [], [], []).marks,
        }
    }

    it('places every mark on its own word once a tag precedes it', () => {
        const { airedRead, marks } = markSpace(taggedText)
        expect(marks.map((m) => airedRead.slice(m.start, m.end))).toEqual([
            'Sydney',
            'Warringah',
            'KIIS',
        ])
    })

    it('agrees with the untagged script when no tag was applied', () => {
        const { airedRead, marks } = markSpace(null)
        expect(airedRead).toBe(script)
        expect(marks.map((m) => airedRead.slice(m.start, m.end))).toEqual([
            'Sydney',
            'Warringah',
            'KIIS',
        ])
    })

    it('sets aside a STALE tagged read whose words no longer match', () => {
        const stale = '[brightly] A completely different read entirely.'
        const { airedRead, marks } = markSpace(stale)
        expect(airedRead).toBe(script)
        expect(marks.map((m) => airedRead.slice(m.start, m.end))).toEqual([
            'Sydney',
            'Warringah',
            'KIIS',
        ])
    })

    it('never matches a rule word hiding inside a tag label', () => {
        const { airedRead, marks } = markSpace(
            script.replace('Now looking', '[sydney] Now looking')
        )
        expect(marks.map((m) => airedRead.slice(m.start, m.end))).toEqual([
            'Sydney',
            'Warringah',
            'KIIS',
        ])
    })
})
