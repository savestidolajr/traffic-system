// =============================================================================
// Presentation helpers for per-bulletin source diagnostics.
//
// Lives outside the component tree so the Configuration and System Audit tabs
// share one vocabulary, and so neither file has to export non-components (which
// breaks React Fast Refresh). Named SOURCE_OUTCOME_LABEL rather than
// OUTCOME_LABEL because history-utils.ts already owns that name for job
// statuses, and the two are unrelated.
// =============================================================================

import type { SourceFetchDiagnostic } from '../types'

export const SOURCE_OUTCOME_LABEL: Record<
    SourceFetchDiagnostic['outcome'],
    string
> = {
    Ok: 'Live data',
    ServedStale: 'Served cached data',
    Failed: 'Unavailable',
    Skipped: 'Not applicable',
}

/** Human-readable age for stale source payloads. */
export function formatStaleAge(
    staleSeconds: number | null | undefined
): string {
    if (staleSeconds == null || staleSeconds < 60) {
        return 'served data from less than a minute ago'
    }
    const minutes = Math.max(1, Math.floor(staleSeconds / 60))
    return `served data from ${minutes} minute${minutes === 1 ? '' : 's'} ago`
}

/**
 * Skipped means the source's jurisdiction does not cover this market, which is
 * normal operation and must never read as an error — so it is dropped here
 * rather than styled down at each render site.
 */
export function visibleSourceDiagnostics(
    diagnostics: readonly SourceFetchDiagnostic[] | null | undefined
): readonly SourceFetchDiagnostic[] {
    return (diagnostics ?? []).filter(
        (diagnostic) => diagnostic.outcome !== 'Skipped'
    )
}
