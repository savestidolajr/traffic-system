import { describe, it, expect } from 'vitest'
import {
    formatStaleAge,
    visibleSourceDiagnostics,
} from '../../lib/source-diagnostics'
import { resolvePronunciationDisplay } from './pronunciation-display'
import { resolveProviderLabel } from './provider-label'
import type { Dictionary } from '@/features/pronunciation/types'

const melbourneDictionary = {
    id: 'library-melbourne',
    dictionaryId: 'provider-dictionary-melbourne',
    name: 'Melbourne Roads',
    description: null,
    status: 'active',
    currentVersion: 3,
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
    createdBy: null,
    assignedStations: [],
    rules: [],
    versions: [
        {
            version: 3,
            elevenLabsVersionId: 'provider-version-melbourne-3',
            rules: [],
            changeNote: null,
            createdAt: '2026-07-01T00:00:00Z',
            createdBy: null,
        },
    ],
} satisfies Dictionary

describe('resolveProviderLabel', () => {
    it('maps a known provider id to its display name', () => {
        expect(resolveProviderLabel('elevenlabs', 'eleven_v3')).toBe(
            'ElevenLabs'
        )
        expect(resolveProviderLabel('azure', 'azure-neural')).toBe(
            'Azure Speech'
        )
    })

    it('prettifies an unrecognised but non-empty provider id', () => {
        expect(resolveProviderLabel('google_cloud', 'x')).toBe('Google cloud')
    })

    it('infers ElevenLabs from the model prefix when the provider is blank', () => {
        // The reported case: real dev job 287d14a0... recorded
        // Input.Voice.Provider as "" even though the model is eleven_v3.
        expect(resolveProviderLabel('', 'eleven_v3')).toBe('ElevenLabs')
    })

    it('falls back to Unknown when neither provider nor model gives a clue', () => {
        expect(resolveProviderLabel('', 'some-custom-model')).toBe('Unknown')
    })
})

describe('resolvePronunciationDisplay', () => {
    it('shows the exact historical version when both provider ids resolve', () => {
        const display = resolvePronunciationDisplay(
            'provider-dictionary-melbourne',
            'provider-version-melbourne-3',
            'eleven_v3',
            [melbourneDictionary]
        )

        expect(display.label).toBe('Melbourne Roads · v3')
        expect(display.title).toContain(
            'Provider dictionary ID: provider-dictionary-melbourne'
        )
        expect(display.title).toContain(
            'provider version ID: provider-version-melbourne-3'
        )
    })

    it('does not fall back to the current version when the persisted version is unavailable', () => {
        const display = resolvePronunciationDisplay(
            'provider-dictionary-melbourne',
            'provider-version-melbourne-2',
            'eleven_v3',
            [melbourneDictionary]
        )

        expect(display.label).toBe('Melbourne Roads · version unavailable')
    })

    it('shows unavailable provenance when the provider dictionary cannot be resolved', () => {
        const display = resolvePronunciationDisplay(
            'provider-dictionary-missing',
            'provider-version-missing',
            'eleven_v3',
            [melbourneDictionary]
        )

        expect(display.label).toBe('Unknown dictionary · version unavailable')
        expect(display.title).toContain(
            'Provider dictionary ID: provider-dictionary-missing'
        )
        expect(display.title).toContain(
            'provider version ID: provider-version-missing'
        )
    })

    it('shows None only when no dictionary was attached', () => {
        const display = resolvePronunciationDisplay(null, null, 'eleven_v3', [
            melbourneDictionary,
        ])

        expect(display.label).toBe('None')
        expect(display.title).toBeUndefined()
    })
})

describe('source diagnostics presentation', () => {
    it('keeps the all-live state compact', () => {
        const diagnostics = [
            {
                sourceId: 'here',
                outcome: 'Ok',
                detail: null,
                dataAsOfUtc: null,
                staleSeconds: null,
            },
        ] as const
        expect(visibleSourceDiagnostics(diagnostics)).toHaveLength(1)
        expect(formatStaleAge(undefined)).toBe(
            'served data from less than a minute ago'
        )
    })

    it('humanises served-stale ages', () => {
        expect(formatStaleAge(420)).toBe('served data from 7 minutes ago')
        expect(formatStaleAge(60)).toBe('served data from 1 minute ago')
    })

    it('removes skipped jurisdiction sources without treating them as errors', () => {
        const visible = visibleSourceDiagnostics([
            {
                sourceId: 'victraffic',
                outcome: 'Skipped',
                detail: null,
                dataAsOfUtc: null,
                staleSeconds: null,
            },
            {
                sourceId: 'qldtraffic',
                outcome: 'Failed',
                detail: 'HTTP 429 (rate limited)',
                dataAsOfUtc: null,
                staleSeconds: null,
            },
        ])
        expect(visible).toHaveLength(1)
        expect(visible[0]?.outcome).toBe('Failed')
    })

    it('handles legacy jobs with no diagnostics array', () => {
        expect(visibleSourceDiagnostics(undefined)).toEqual([])
        expect(visibleSourceDiagnostics([])).toEqual([])
    })
})
