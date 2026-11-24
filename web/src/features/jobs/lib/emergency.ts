import type {
    EmergencyEvent,
    EmergencyWarningLevel,
    SourceFetchDiagnostic,
} from '../types'
import { corridorSeverityDotClass } from './incidents/incident-labels'

export type EmergencyTierTone = 'destructive' | 'warning' | 'muted'
export const EMERGENCY_TIER_LABEL: Record<EmergencyWarningLevel, string> = {
    None: 'Monitoring',
    Advice: 'Advice',

    WatchAndAct: 'Watch and Act',
    EmergencyWarning: 'Emergency Warning',
}

/** Higher warning tiers sort first, matching EmergencyEvent.CompareForOperator. */
export const EMERGENCY_TIER_RANK: Record<EmergencyWarningLevel, number> = {
    None: 0,
    Advice: 1,
    WatchAndAct: 2,
    EmergencyWarning: 3,
}

/** Emergency tiers reuse the existing incident severity dot palette. */
export function emergencyTierDotClass(
    warningLevel: EmergencyWarningLevel
): string {
    const severity = 4 - EMERGENCY_TIER_RANK[warningLevel]
    return corridorSeverityDotClass(severity)
}

export function emergencyTierTone(
    warningLevel: EmergencyWarningLevel
): EmergencyTierTone {
    if (warningLevel === 'EmergencyWarning') return 'destructive'
    if (warningLevel === 'WatchAndAct' || warningLevel === 'Advice') {
        return 'warning'
    }
    return 'muted'
}

/**
 * Stable operator ordering: warning tier, eligibility, distance, then identity.
 * Missing distance sorts after located events, just like the backend's
 * double.MaxValue fallback.
 */
export function sortEmergencyEvents(
    events: readonly EmergencyEvent[]
): EmergencyEvent[] {
    return [...events].sort((a, b) => {
        const byTier =
            EMERGENCY_TIER_RANK[b.warningLevel] -
            EMERGENCY_TIER_RANK[a.warningLevel]
        if (byTier !== 0) return byTier

        const byEligibility = Number(b.airEligible) - Number(a.airEligible)
        if (byEligibility !== 0) return byEligibility

        const distanceA = a.distanceKm ?? Number.POSITIVE_INFINITY
        const distanceB = b.distanceKm ?? Number.POSITIVE_INFINITY
        if (distanceA !== distanceB) return distanceA - distanceB

        return a.id < b.id ? -1 : a.id > b.id ? 1 : 0
    })
}

const CATEGORY_LABELS: Record<string, string> = {
    fire: 'Fire',
    hazmat: 'Hazmat',
    weather: 'Weather',
    rescue: 'Rescue',
    earth: 'Earthquake',
    medical: 'Medical',
    alarm: 'Alarm',
    planned: 'Planned burn',
    other: 'Unclassified',
}

export function emergencyCategoryLabel(category: string): string {
    return CATEGORY_LABELS[category.toLowerCase()] ?? 'Unclassified'
}

const SUPPRESSION_LABELS: Record<string, string> = {
    alarm: 'alarms',
    medical: 'medical',
    other: 'unclassified',
    planned: 'planned burns',
}

/** Summarise every category that was present but failed the airability gate. */
export function suppressionSummary(
    events: readonly EmergencyEvent[]
): string | null {
    const suppressed = events.filter((event) => !event.airEligible)
    if (suppressed.length === 0) return null

    const categories: string[] = []
    for (const event of suppressed) {
        const raw = event.eventCategory?.trim()
        const key = raw ? raw.toLowerCase() : 'other'
        const label =
            SUPPRESSION_LABELS[key] ?? emergencyCategoryLabel(key).toLowerCase()
        if (!categories.includes(label)) categories.push(label)
    }
    categories.sort((a, b) => a.localeCompare(b))
    return `${suppressed.length} not airable (${categories.join(', ')})`
}

export function mustAirCount(events: readonly EmergencyEvent[]): number {
    return events.filter((event) => event.warningLevel === 'EmergencyWarning')
        .length
}

/** What the emergency panel is actually looking at when it has no rows to show. */
export type EmergencyFeedState =
    | { readonly kind: 'events' }
    | { readonly kind: 'clear'; readonly message: string }
    | { readonly kind: 'off'; readonly message: string }
    | { readonly kind: 'broken'; readonly message: string }
    | { readonly kind: 'unknown'; readonly message: string }

/**
 * An empty list has four completely different meanings and an operator must never have to guess
 * which one they are looking at. "No incidents in the coverage area" is a factual claim about the
 * world; reporting it when the feed is switched off, unconfigured, or failing is a lie that reads
 * as reassurance. This is the same asymmetry RISK-37 records for traffic sources: off must never be
 * indistinguishable from broken.
 */
export function emergencyFeedState(
    events: readonly EmergencyEvent[],
    diagnostic: SourceFetchDiagnostic | null | undefined
): EmergencyFeedState {
    if (events.length > 0) return { kind: 'events' }

    if (!diagnostic) {
        return {
            kind: 'unknown',
            message: 'No emergency data was captured for this bulletin.',
        }
    }

    switch (diagnostic.outcome) {
        case 'Skipped':
            return {
                kind: 'off',
                message:
                    'The emergency feed is not switched on for this environment, so nothing was checked.',
            }
        case 'Failed':
            return {
                kind: 'broken',
                message: diagnostic.detail
                    ? `The emergency feed could not be reached (${diagnostic.detail}), so nothing was checked.`
                    : 'The emergency feed could not be reached, so nothing was checked.',
            }
        default:
            return {
                kind: 'clear',
                message: 'No emergency incidents in the coverage area.',
            }
    }
}

/** The one source id this panel reports on. */
export const EMERGENCY_SOURCE_ID = 'dataquoll'

export function findEmergencyDiagnostic(
    diagnostics: readonly SourceFetchDiagnostic[] | null | undefined
): SourceFetchDiagnostic | undefined {
    return (diagnostics ?? []).find(
        (diagnostic) => diagnostic.sourceId === EMERGENCY_SOURCE_ID
    )
}
