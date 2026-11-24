import {
    AudioLines,
    BookOpen,
    BookOpenCheck,
    BookText,
    Drama,
    Ellipsis,
    MessageSquareText,
    Settings2,
    ShieldCheck,
    type LucideIcon,
} from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { getProp, isRecord } from '@/lib/json'

// =============================================================================
// Bundle section metadata. Section keys are wire-exact: they are literally
// `ConfigSections.*` on the backend and the property names on the
// exported/imported JSON bundle (`ConfigExportBuilder` / `ConfigImportService`
// on `src/ArnBulletinGenerator.Functions`). Never rename a key here without
// changing the backend contract too.
// =============================================================================

export const SECTION_LABELS = {
    VoiceProfiles: 'Voices',
    DeliveryStyles: 'Delivery styles',
    PronunciationDictionaries: 'Pronunciation',
    SafetyTerms: 'Banned words',
    Guidance: 'Editorial guidance',
    PriorityRoads: 'Priority roads',
    VerifiedPronunciations: 'Verified words',
    OperatorAssignments: 'Operators',
    VoiceCalibrations: 'Voice calibration',
    FillerStatements: 'Filler statements',
    Bookends: 'Bookends',
    Schedules: 'Stations',
} as const

export type Section = keyof typeof SECTION_LABELS

/** Dependency order — mirrors the backend's `ConfigSections.All`. */
export const SECTIONS = Object.keys(SECTION_LABELS) as Section[]

/** One icon per section, matching that concept's own page/nav icon where one exists. */
export const SECTION_ICON: Record<Section, LucideIcon> = {
    VoiceProfiles: AudioLines,
    DeliveryStyles: Drama,
    PronunciationDictionaries: BookText,
    SafetyTerms: ICON.safety,
    Guidance: MessageSquareText,
    PriorityRoads: ICON.traffic,
    VerifiedPronunciations: BookOpenCheck,
    OperatorAssignments: ShieldCheck,
    VoiceCalibrations: ICON.speed,
    FillerStatements: Ellipsis,
    Bookends: BookOpen,
    Schedules: Settings2,
}

/**
 * The field that identifies one entity within its section array — the same
 * field `ConfigImportService` upserts by.
 */
export const SECTION_KEY_FIELD: Record<Section, string> = {
    VoiceProfiles: 'Id',
    DeliveryStyles: 'Id',
    PronunciationDictionaries: 'Id',
    SafetyTerms: 'Id',
    Guidance: 'Id',
    PriorityRoads: 'Market',
    VerifiedPronunciations: 'Market',
    OperatorAssignments: 'ObjectId',
    VoiceCalibrations: 'ConfigurationKey',
    FillerStatements: 'Market',
    Bookends: 'Id',
    Schedules: 'MarketId',
}

export function isSection(value: string): value is Section {
    return Object.hasOwn(SECTION_LABELS, value)
}

/**
 * Display name for ANY section string the server sends, known or not.
 *
 * The maps above are hand-maintained, so they will lag the backend the next
 * time a section is added to `ConfigSections.All` — and the import report also
 * carries rows under pseudo-sections that are not in `ConfigSections` at all
 * (`Unknown` for a typo'd top-level property, `SchemaVersion` for a version
 * mismatch). Anything the review surface cannot name, it drops; a dropped row
 * still counts toward the metric cards, so the operator reads "Errors 1" with
 * nothing to look at. Never drop — humanise instead.
 */
export function sectionLabel(section: string): string {
    if (isSection(section)) return SECTION_LABELS[section]
    // PascalCase → sentence case: 'SchemaVersion' → 'Schema version'.
    const spaced = section.replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase()
}

/** Icon for any section string; unknown sections get the neutral file glyph. */
export function sectionIcon(section: string): LucideIcon {
    return isSection(section) ? SECTION_ICON[section] : ICON.fileJson
}

/** Upsert key field for any section string, or null when the section is unknown. */
export function sectionKeyField(section: string): string | null {
    return isSection(section) ? SECTION_KEY_FIELD[section] : null
}

/**
 * Parses `?sections=VoiceProfiles,Guidance` into a valid preselection.
 *
 * `available` is the section list actually offered by the server (see
 * `useConfigSections`), so a section the backend has but this build's maps do
 * not still round-trips through a deep link.
 */
export function sectionsFromQuery(
    params: URLSearchParams,
    available: readonly string[] = SECTIONS
): Set<string> {
    const raw = params.get('sections')
    if (!raw) return new Set(available)
    const requested = raw
        .split(',')
        .map((s) => s.trim().toLowerCase())
        .filter(Boolean)
    const matched = available.filter((s) => requested.includes(s.toLowerCase()))
    return matched.length > 0 ? new Set(matched) : new Set(available)
}

/**
 * How many entities a section carries in a bundle, for the export picker.
 * Per-market containers (roads/filler statements/verified words) count the
 * nested items they hold, not the number of markets — that is what an admin
 * means by "how much is in here".
 */
const NESTED_SECTION_KEYS: Partial<Record<Section, string>> = {
    PriorityRoads: 'Roads',
    FillerStatements: 'Statements',
    VerifiedPronunciations: 'Words',
}

export function sectionCount(bundle: unknown, section: string): number {
    if (!isRecord(bundle)) return 0
    const entities = bundle[section]
    if (!Array.isArray(entities)) return 0
    const nestedKey = isSection(section)
        ? NESTED_SECTION_KEYS[section]
        : undefined
    if (nestedKey) {
        return entities.reduce((sum: number, market) => {
            const nested = isRecord(market) ? market[nestedKey] : undefined
            return sum + (Array.isArray(nested) ? nested.length : 0)
        }, 0)
    }
    return entities.length
}

/**
 * Looks up one entity by section + upsert key across an export/import bundle.
 * An unknown section has no known key field, so there is nothing to match on:
 * the row still renders, just without a field-level diff.
 */
export function findEntityInBundle(
    bundle: unknown,
    section: string,
    key: string
): unknown {
    if (!isRecord(bundle)) return undefined
    const keyField = sectionKeyField(section)
    if (keyField === null) return undefined
    const entities = getProp(bundle, section)
    if (!Array.isArray(entities)) return undefined
    return entities.find(
        (item) =>
            isRecord(item) && String(getProp(item, keyField) ?? '') === key
    )
}
