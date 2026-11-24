import type { LogEntry, SourceFetchDiagnostic, Status } from '../../types'
import { sourceDisplayName } from '../../lib/source-display-names'
import { IN_PROGRESS_STAGE_LABELS } from './constants'
const TRAILING_ELLIPSIS = /(\.\.\.|…)\s*$/

/** Log lines that deserve visual attention even below WARN — playout
 * escalation is logged at INFO but is exactly the kind of signal an
 * operator should not have to expand a fold to discover. */
const ESCALATION_PATTERN = /\bescalat/i

export function isFlaggedLog(log: LogEntry): boolean {
    return (
        log.level === 'ERROR' ||
        log.level === 'WARN' ||
        ESCALATION_PATTERN.test(log.message)
    )
}

function staleAgeLabel(staleSeconds: number | null | undefined): string {
    if (staleSeconds == null || staleSeconds < 60) {
        return 'served data from less than a minute ago'
    }
    const minutes = Math.max(1, Math.floor(staleSeconds / 60))
    return `served data from ${minutes} minute${minutes === 1 ? '' : 's'} ago`
}

/** Turn non-routine feed outcomes into the flagged log lines shown in the audit timeline. */
export function sourceDiagnosticLogs(
    diagnostics: readonly SourceFetchDiagnostic[] | null | undefined,
    at: string
): readonly LogEntry[] {
    return (diagnostics ?? [])
        .filter(
            (diagnostic) =>
                diagnostic.outcome !== 'Ok' && diagnostic.outcome !== 'Skipped'
        )
        .map((diagnostic) => {
            const source = sourceDisplayName(diagnostic.sourceId)
            const message =
                diagnostic.outcome === 'Failed'
                    ? `${source}: unavailable${diagnostic.detail ? ` — ${diagnostic.detail}` : ''}`
                    : `${source}: ${staleAgeLabel(diagnostic.staleSeconds)}${diagnostic.detail ? ` — ${diagnostic.detail}` : ''}`
            return {
                at,
                level: diagnostic.outcome === 'Failed' ? 'ERROR' : 'WARN',
                message,
            }
        })
}

/**
 * A note ending in an ellipsis ("Fetching traffic...") describes work still
 * in flight, not the completed milestone the row's status name implies --
 * some pipeline statuses are stamped at phase START rather than completion
 * (see BroadcastOrchestrator's UpdateJobStatus calls). Swap in the matching
 * in-flight title so the row doesn't read as self-contradictory ("Traffic
 * Fetched" next to "Fetching traffic...").
 */
export function resolveStageTitle(
    status: Status,
    note: string,
    completedLabel: string
): string {
    if (!TRAILING_ELLIPSIS.test(note)) return completedLabel
    return IN_PROGRESS_STAGE_LABELS[status] ?? completedLabel
}
