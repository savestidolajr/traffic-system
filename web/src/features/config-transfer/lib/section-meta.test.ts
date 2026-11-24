import { describe, expect, it } from 'vitest'
import {
    SECTIONS,
    SECTION_LABELS,
    SECTION_ICON,
    SECTION_KEY_FIELD,
    sectionCount,
    sectionIcon,
    sectionKeyField,
    sectionLabel,
} from './section-meta'

/**
 * Source of truth:
 * `src/ArnBulletinGenerator.Application/ConfigTransfer/ConfigTransferContracts.cs`
 * — `ConfigSections.All`, in dependency order (libraries before the schedules
 * that reference them). If the backend gains or reorders a section, this list
 * must change first.
 */
const BACKEND_SECTIONS = [
    'VoiceProfiles',
    'DeliveryStyles',
    'PronunciationDictionaries',
    'SafetyTerms',
    'Guidance',
    'PriorityRoads',
    'VerifiedPronunciations',
    'OperatorAssignments',
    'VoiceCalibrations',
    'FillerStatements',
    'Bookends',
    'Schedules',
] as const

describe('section-meta', () => {
    it('mirrors the backend ConfigSections.All, in order', () => {
        expect(SECTIONS).toEqual([...BACKEND_SECTIONS])
    })

    it('describes every section with a label, an icon and an upsert key field', () => {
        for (const section of BACKEND_SECTIONS) {
            expect(SECTION_LABELS[section], section).toBeTruthy()
            expect(SECTION_ICON[section], section).toBeTruthy()
            expect(SECTION_KEY_FIELD[section], section).toBeTruthy()
        }
    })

    it('uses the live-proven upsert key fields for the four previously hidden sections', () => {
        expect(SECTION_KEY_FIELD.VerifiedPronunciations).toBe('Market')
        expect(SECTION_KEY_FIELD.OperatorAssignments).toBe('ObjectId')
        expect(SECTION_KEY_FIELD.VoiceCalibrations).toBe('ConfigurationKey')
        expect(SECTION_KEY_FIELD.Bookends).toBe('Id')
    })

    it('counts VerifiedPronunciations by nested Words, like other per-market containers', () => {
        const bundle = {
            VerifiedPronunciations: [
                {
                    Market: 'Sydney',
                    Words: [{ Word: 'Parramatta' }, { Word: 'Hurstville' }],
                },
                { Market: 'Melbourne', Words: [{ Word: 'Prahran' }] },
            ],
        }
        expect(sectionCount(bundle, 'VerifiedPronunciations')).toBe(3)
    })

    it('humanises a section this build does not know instead of dropping it', () => {
        // The import report also carries pseudo-sections that are not in
        // ConfigSections at all: `Unknown` for a typo'd top-level property and
        // `SchemaVersion` for a version mismatch.
        expect(sectionLabel('SchemaVersion')).toBe('Schema version')
        expect(sectionLabel('Unknown')).toBe('Unknown')
        expect(sectionLabel('SomeFutureSection')).toBe('Some future section')
        expect(sectionIcon('SomeFutureSection')).toBeTruthy()
        expect(sectionKeyField('SomeFutureSection')).toBeNull()
        // Known sections keep their curated names.
        expect(sectionLabel('Schedules')).toBe('Stations')
    })
})
