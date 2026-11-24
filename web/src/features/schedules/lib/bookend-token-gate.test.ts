import { describe, expect, it } from 'vitest'
import type { Bookend } from '@/features/bookends/types'
import {
    computeBookendTokenBlockers,
    formatBookendBlocker,
    referencedBookendIds,
    type BookendTokenBlocker,
} from './bookend-token-gate'

function bookend(
    id: string,
    name: string,
    introText: string,
    outroText = ''
): Bookend {
    return {
        id,
        name,
        description: null,
        status: 'active',
        introText,
        outroText,
        currentVersion: 1,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
        createdBy: null,
        assignedStations: [],
        tokens: [],
        versions: [],
    }
}

const LIBRARY = [
    bookend(
        'bk-city',
        'City pair',
        'Looking over {city} today.',
        'Bye {market}.'
    ),
    bookend('bk-custom', 'Suburb pair', 'Welcome to {suburb}.', ''),
    bookend('bk-station', 'Station pair', 'Thanks {station}.', ''),
    bookend('bk-empty', 'No tokens', 'Plain line.', ''),
]

const NO_ROTATION = {
    bookendRotationMode: 'single' as const,
    bookendRotationIds: [],
    bookendWindows: [],
}

function blockersFor(
    overrides: Partial<{
        bookendId: string
        station: string
        tokenRows: Array<{ key: string; value: string }>
    }>
) {
    return computeBookendTokenBlockers({
        bookends: LIBRARY,
        ...NO_ROTATION,
        bookendId: overrides.bookendId ?? '',
        station: overrides.station ?? '4CA Gold Coast',
        tokenRows: overrides.tokenRows ?? [],
    })
}

describe('referencedBookendIds', () => {
    it('is empty when no pair and no rotation is configured', () => {
        expect(referencedBookendIds('', 'single', [], [])).toEqual([])
    })

    it('includes the primary pair', () => {
        expect(referencedBookendIds('bk-city', 'single', [], [])).toEqual([
            'bk-city',
        ])
    })

    it('includes round-robin members alongside the primary', () => {
        expect(
            referencedBookendIds(
                'bk-city',
                'roundRobin',
                ['bk-custom', 'bk-empty'],
                []
            )
        ).toEqual(['bk-city', 'bk-custom', 'bk-empty'])
    })

    it('includes time-of-day window members alongside the primary', () => {
        expect(
            referencedBookendIds(
                'bk-city',
                'timeOfDay',
                [],
                [
                    { start: '06:00', end: '09:00', bookendId: 'bk-custom' },
                    { start: '09:00', end: '12:00', bookendId: 'bk-empty' },
                ]
            )
        ).toEqual(['bk-city', 'bk-custom', 'bk-empty'])
    })

    it('drops blank ids', () => {
        expect(
            referencedBookendIds('  ', 'roundRobin', ['', 'bk-city', '  '], [])
        ).toEqual(['bk-city'])
    })
})

describe('computeBookendTokenBlockers', () => {
    it('returns no blockers when no bookend is referenced', () => {
        expect(blockersFor({})).toEqual([])
    })

    it('does not block the built-in city/market tokens', () => {
        expect(blockersFor({ bookendId: 'bk-city' })).toEqual([])
    })

    it('blocks a custom token with no override, naming the bookend and token', () => {
        expect(blockersFor({ bookendId: 'bk-custom' })).toEqual([
            {
                bookendId: 'bk-custom',
                bookendName: 'Suburb pair',
                unresolvedTokens: ['suburb'],
            },
        ])
    })

    it('clears a custom token when an override supplies it', () => {
        expect(
            blockersFor({
                bookendId: 'bk-custom',
                tokenRows: [{ key: 'suburb', value: 'Southport' }],
            })
        ).toEqual([])
    })

    it('ignores blank override rows and blank-key rows', () => {
        expect(
            blockersFor({
                bookendId: 'bk-custom',
                tokenRows: [
                    { key: '  ', value: 'x' },
                    { key: 'suburb', value: ' ' },
                ],
            })
        ).toEqual([
            {
                bookendId: 'bk-custom',
                bookendName: 'Suburb pair',
                unresolvedTokens: ['suburb'],
            },
        ])
    })

    it('matches token names case-insensitively, like the backend', () => {
        expect(
            blockersFor({
                bookendId: 'bk-custom',
                tokenRows: [{ key: 'SUBURB', value: 'Southport' }],
            })
        ).toEqual([])
    })

    it('blocks {station} only when the station field is empty', () => {
        expect(blockersFor({ bookendId: 'bk-station' })).toEqual([])
        expect(blockersFor({ bookendId: 'bk-station', station: '' })).toEqual([
            {
                bookendId: 'bk-station',
                bookendName: 'Station pair',
                unresolvedTokens: ['station'],
            },
        ])
    })

    it('blocks a round-robin member whose token is unfillable', () => {
        const blockers = computeBookendTokenBlockers({
            bookends: LIBRARY,
            bookendId: 'bk-city',
            bookendRotationMode: 'roundRobin',
            bookendRotationIds: ['bk-custom'],
            bookendWindows: [],
            station: '4CA Gold Coast',
            tokenRows: [],
        })
        expect(blockers).toEqual([
            {
                bookendId: 'bk-custom',
                bookendName: 'Suburb pair',
                unresolvedTokens: ['suburb'],
            },
        ])
    })

    it('blocks a time-of-day window member whose token is unfillable', () => {
        const blockers = computeBookendTokenBlockers({
            bookends: LIBRARY,
            bookendId: 'bk-city',
            bookendRotationMode: 'timeOfDay',
            bookendRotationIds: [],
            bookendWindows: [
                { start: '06:00', end: '09:00', bookendId: 'bk-custom' },
            ],
            station: '4CA Gold Coast',
            tokenRows: [],
        })
        expect(blockers).toEqual([
            {
                bookendId: 'bk-custom',
                bookendName: 'Suburb pair',
                unresolvedTokens: ['suburb'],
            },
        ])
    })

    it('reports one blocker per affected bookend', () => {
        const blockers = computeBookendTokenBlockers({
            bookends: LIBRARY,
            bookendId: 'bk-custom',
            bookendRotationMode: 'roundRobin',
            bookendRotationIds: ['bk-station'],
            bookendWindows: [],
            station: '',
            tokenRows: [],
        })
        expect(blockers).toHaveLength(2)
        expect(blockers.map((b) => b.bookendId)).toEqual([
            'bk-custom',
            'bk-station',
        ])
    })

    it('skips a referenced id missing from the library (selector guards existence)', () => {
        expect(
            computeBookendTokenBlockers({
                bookends: LIBRARY,
                bookendId: 'bk-ghost',
                ...NO_ROTATION,
                station: '4CA Gold Coast',
                tokenRows: [],
            })
        ).toEqual([])
    })
})

describe('formatBookendBlocker', () => {
    it('names the bookend and every unfillable token, and states the refusal', () => {
        const blocker: BookendTokenBlocker = {
            bookendId: 'bk-custom',
            bookendName: 'Suburb pair',
            unresolvedTokens: ['suburb', 'zone'],
        }
        const message = formatBookendBlocker(blocker)
        expect(message).toContain("Bookend 'Suburb pair'")
        expect(message).toContain('{suburb}, {zone}')
        expect(message).toContain('Save will be refused')
    })
})
