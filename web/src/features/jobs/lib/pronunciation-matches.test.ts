import { describe, expect, it } from 'vitest'
import type { Dictionary, Rule } from '@/features/pronunciation/types'
import { findPronunciationMatches } from './pronunciation-matches'

const makeRule = (overrides: Partial<Rule> = {}): Rule => ({
    type: 'alias',
    stringToReplace: 'Greensborough',
    replacement: 'Greenybro',
    alphabet: null,
    caseSensitive: false,
    wordBoundaries: true,
    appliesOnAllModels: true,
    ...overrides,
})

const makeDictionary = (
    versions: Dictionary['versions'],
    currentRules: Dictionary['rules'] = versions[0]?.rules ?? []
): Dictionary => ({
    id: 'library-1',
    dictionaryId: 'provider-dictionary-1',
    name: 'Melbourne place names',
    description: null,
    status: 'active',
    currentVersion: versions[versions.length - 1]?.version ?? 1,
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
    createdBy: null,
    assignedStations: [],
    rules: currentRules,
    versions,
})

const makeVersion = (
    version: number,
    elevenLabsVersionId: string,
    rules: Rule[]
): Dictionary['versions'][number] => ({
    version,
    elevenLabsVersionId,
    rules,
    changeNote: null,
    createdAt: '2026-07-01T00:00:00Z',
    createdBy: null,
})

const exactVersion = makeVersion(1, 'provider-version-1', [makeRule()])

function resolve(
    text: string,
    versionId = 'provider-version-1',
    model = 'eleven_multilingual_v2',
    dictionary = makeDictionary([exactVersion])
) {
    return findPronunciationMatches(
        text,
        'provider-dictionary-1',
        versionId,
        model,
        [dictionary]
    )
}

describe('findPronunciationMatches', () => {
    it('finds Greensborough and leaves the canonical script untouched', () => {
        const script = 'Traffic is heavy near Greensborough this morning.'
        const result = resolve(script)

        expect(result).toMatchObject({
            dictionaryName: 'Melbourne place names',
            libraryId: 'library-1',
            versionNumber: 1,
            providerVersionId: 'provider-version-1',
            resolution: 'exact',
        })
        expect(result.matches).toEqual([
            {
                canonicalText: 'Greensborough',
                rule: 'Greensborough',
                replacement: 'Greenybro',
                start: script.indexOf('Greensborough'),
                end: script.indexOf('Greensborough') + 'Greensborough'.length,
            },
        ])
        expect(script).toBe('Traffic is heavy near Greensborough this morning.')
    })

    it('matches case-insensitively when the rule is not case-sensitive', () => {
        expect(
            resolve('GREENSBOROUGH is slow.').matches[0]?.canonicalText
        ).toBe('GREENSBOROUGH')
    })

    it('does not match a case-sensitive rule with different casing', () => {
        const dictionary = makeDictionary([
            makeVersion(1, 'provider-version-1', [
                makeRule({ caseSensitive: true }),
            ]),
        ])

        expect(
            resolve('greensborough is slow.', undefined, undefined, dictionary)
                .matches
        ).toEqual([])
    })

    it('honours word boundaries', () => {
        const dictionary = makeDictionary([
            makeVersion(1, 'provider-version-1', [
                makeRule({ stringToReplace: 'boro', replacement: 'bro' }),
            ]),
        ])

        expect(
            resolve(
                'Boro is open; Greensborough is nearby.',
                undefined,
                undefined,
                dictionary
            ).matches
        ).toEqual([expect.objectContaining({ canonicalText: 'Boro' })])
    })

    it('prefers the longer rule when candidates overlap', () => {
        const dictionary = makeDictionary([
            makeVersion(1, 'provider-version-1', [
                makeRule({ stringToReplace: 'borough', replacement: 'bro' }),
                makeRule({
                    stringToReplace: 'Greensborough',
                    replacement: 'Greenybro',
                }),
            ]),
        ])

        expect(
            resolve('Greensborough', undefined, undefined, dictionary).matches
        ).toEqual([
            expect.objectContaining({
                canonicalText: 'Greensborough',
                replacement: 'Greenybro',
            }),
        ])
    })

    it('uses the exact historical version instead of the current rules', () => {
        const historical = makeVersion(1, 'provider-version-1', [makeRule()])
        const currentRule = makeRule({ replacement: 'Green-s-borough' })
        const dictionary = makeDictionary([historical], [currentRule])

        expect(
            resolve('Greensborough', undefined, undefined, dictionary)
                .matches[0]?.replacement
        ).toBe('Greenybro')
    })

    it('reports a missing version without falling back to current rules', () => {
        const dictionary = makeDictionary(
            [makeVersion(1, 'provider-version-1', [makeRule()])],
            [makeRule({ replacement: 'Green-s-borough' })]
        )

        const result = resolve(
            'Greensborough',
            'provider-version-missing',
            undefined,
            dictionary
        )
        expect(result.resolution).toBe('missing-version')
        expect(result.matches).toEqual([])
    })

    it('skips phoneme rules for eleven_v3', () => {
        const dictionary = makeDictionary([
            makeVersion(1, 'provider-version-1', [
                makeRule({
                    type: 'phoneme',
                    replacement: 'ˈɡriːn',
                    alphabet: 'ipa',
                }),
                makeRule(),
            ]),
        ])

        const result = resolve(
            'Greensborough',
            undefined,
            'eleven_v3',
            dictionary
        )
        expect(result.matches).toHaveLength(1)
        expect(result.matches[0]?.replacement).toBe('Greenybro')
    })

    it('returns none for a legacy job without a pronunciation locator', () => {
        expect(
            findPronunciationMatches('Greensborough', null, null, 'eleven_v3', [
                makeDictionary([exactVersion]),
            ])
        ).toMatchObject({ resolution: 'none', matches: [] })
    })

    it('reports a dictionary locator that is absent from the library', () => {
        expect(
            findPronunciationMatches(
                'Greensborough',
                'provider-dictionary-missing',
                'provider-version-1',
                'eleven_v3',
                [makeDictionary([exactVersion])]
            )
        ).toMatchObject({ resolution: 'missing-dictionary', matches: [] })
    })
})
