import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/dialog'
import type {
    JobIncident,
    OfficialEvidence,
    TrafficCorridor,
} from '../../../types'
import { sourceDisplayName } from '../../../lib/source-display-names'

const fmtTime = (iso?: string | null): string =>
    iso
        ? new Date(iso).toLocaleString(undefined, {
              dateStyle: 'medium',
              timeStyle: 'short',
          })
        : '-'

const matchBasisLabel = (evidence: OfficialEvidence): string => {
    const basis = evidence.matchBasis ?? ''
    if (basis === 'road-name') return 'Matched by road name'
    if (basis === 'official-only')
        return `Single-source record: only ${sourceDisplayName(evidence.sourceId)} carries this event`
    if (basis.startsWith('distance-'))
        return `Matched by proximity: ${basis.slice('distance-'.length)} apart`
    return 'Match basis not recorded'
}

/** A raw provider payload record for bottom-section inspection */
interface SourcePayload {
    readonly id: string
    readonly name: string
    readonly record: object | readonly object[] | null
}

/** A record rendered as tidy label→value rows: nulls omitted, dates humanized, geometry summarized cleanly. */
function FieldList({ record }: { readonly record: object | null | undefined }) {
    if (!record || typeof record !== 'object') return null
    const entries = Object.entries(record as Record<string, unknown>).filter(
        ([k, v]) =>
            v !== null &&
            v !== undefined &&
            v !== '' &&
            k !== 'shape' &&
            !(Array.isArray(v) && v.length === 0)
    )
    const hasShape =
        'shape' in record &&
        Array.isArray((record as Record<string, unknown>).shape)
    const shapeLength = hasShape
        ? ((record as Record<string, unknown>).shape as unknown[]).length
        : 0
    return (
        <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
            {entries.map(([k, v]) => (
                <div key={k} className="contents">
                    <dt className="text-muted-foreground font-medium">{k}</dt>
                    <dd className="wrap-break-word font-mono">
                        {typeof v === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(v)
                            ? fmtTime(v)
                            : Array.isArray(v)
                              ? v
                                    .map((x) =>
                                        typeof x === 'object'
                                            ? JSON.stringify(x)
                                            : String(x)
                                    )
                                    .join(', ')
                              : typeof v === 'object'
                                ? JSON.stringify(v)
                                : String(v)}
                    </dd>
                </div>
            ))}
            {hasShape && (
                <div className="contents">
                    <dt className="text-muted-foreground font-medium">shape</dt>
                    <dd className="wrap-break-word font-mono text-muted-foreground">
                        {shapeLength} point{shapeLength === 1 ? '' : 's'}{' '}
                        geometry
                    </dd>
                </div>
            )}
        </dl>
    )
}
/** The slice of a corridor the dialog actually reads — lets the Desk feed it a ledger event. */
export type EvidenceSubject = Partial<TrafficCorridor> & {
    readonly roadName: string
    readonly sources?: readonly string[] | null
    readonly officialEvidence?: OfficialEvidence | null
    readonly officialEvidences?: readonly OfficialEvidence[] | null
}

interface EvidenceDialogProps {
    readonly corridor: EvidenceSubject
    readonly incidents: readonly JobIncident[]
    readonly open: boolean
    readonly onOpenChange: (open: boolean) => void
    /** Optional extra sections after the records block (the Desk appends event history). */
    readonly children?: React.ReactNode
}

/**
 * The fact-check receipts behind a provenance chip, source-neutrally: one column per contributing
 * feed — whatever they are — field-aligned so disagreement is visible in seconds, with each
 * source's record rendered as tidy field rows below for the deep check. No feed is "the base":
 * a corridor with no HERE evidence simply has no HERE column.
 */
export function EvidenceDialog({
    corridor,
    incidents,
    open,
    onOpenChange,
    children,
}: EvidenceDialogProps) {
    const rawEvidence =
        corridor.officialEvidences ??
        (corridor.officialEvidence ? [corridor.officialEvidence] : [])
    const evidence = rawEvidence.filter(Boolean)

    // Contributing sources list
    const sources = corridor.sources?.length
        ? corridor.sources
        : evidence.length
          ? ['here', ...evidence.map((e) => e.sourceId)]
          : ['here']

    // 1. Unified Canonical Corridor / Incident facts (Top Section)
    const leadIncident = incidents[0] ?? null
    const leadOfficial = evidence[0] ?? null

    const unifiedCondition =
        leadIncident?.type ??
        leadIncident?.headline ??
        corridor.description ??
        leadOfficial?.headline ??
        '-'

    const unifiedRoadClosed =
        leadIncident?.roadClosed != null
            ? leadIncident.roadClosed
                ? 'Closed'
                : 'Open'
            : leadOfficial?.isIncidentClass === false
              ? 'Planned works'
              : 'Open'

    const unifiedDirection =
        leadIncident?.direction ??
        corridor.direction ??
        leadOfficial?.direction ??
        '-'

    const unifiedLocation =
        leadIncident?.detail ||
        [leadOfficial?.crossStreet, leadOfficial?.suburb, corridor.suburb]
            .filter(Boolean)
            .join(' · ') ||
        '-'

    const unifiedImpact =
        leadIncident?.laneDetail ||
        [
            leadOfficial?.impactType,
            leadOfficial?.impactSubtype,
            leadOfficial?.qualitativeDelay,
            corridor.delayMinutes != null
                ? `${corridor.delayMinutes} min delay`
                : null,
        ]
            .filter(Boolean)
            .join(' · ') ||
        '-'

    const unifiedPriority =
        leadIncident != null
            ? `Priority ${leadIncident.priority}`
            : leadOfficial?.authorityPriority
              ? leadOfficial.authorityPriority
              : corridor.magnitude
                ? `Magnitude: ${corridor.magnitude}`
                : '-'

    const unifiedReported =
        leadIncident?.entryTimeUtc ??
        leadOfficial?.publishedTime ??
        leadOfficial?.lastUpdated ??
        null

    const unifiedStarts =
        leadIncident?.startTimeUtc ?? leadOfficial?.startTime ?? null
    const unifiedEnds =
        leadIncident?.endTimeUtc ?? leadOfficial?.endTime ?? null

    // 2. Separate Raw Contributing Source Payloads (Bottom Section)
    const payloads: SourcePayload[] = sources.map((src) => {
        const sLower = src.toLowerCase()
        if (sLower === 'here') {
            if (incidents.length > 0) {
                // Filter incidents that HERE contributed to, or pure HERE view
                const hereRecords = incidents.map((inc) => {
                    const hereId = inc.sourceRecordIdentities?.find(
                        (i) => i.sourceId.toLowerCase() === 'here'
                    )
                    return {
                        key: inc.key,
                        priority: inc.priority,
                        headline: inc.headline,
                        detail: inc.detail,
                        type: inc.type,
                        roadClosed: inc.roadClosed,
                        direction: inc.direction,
                        laneDetail: inc.laneDetail,
                        lengthMeters: inc.lengthMeters,
                        entryTimeUtc: inc.entryTimeUtc,
                        startTimeUtc: inc.startTimeUtc,
                        endTimeUtc: inc.endTimeUtc,
                        hereRecordId: hereId?.recordId ?? null,
                        shape: inc.shape,
                    }
                })
                return {
                    id: 'here',
                    name: 'HERE (Telematics & Incidents)',
                    record:
                        hereRecords.length === 1 ? hereRecords[0] : hereRecords,
                }
            }
            return {
                id: 'here',
                name: 'HERE (Traffic Flow Sensors)',
                record: {
                    telemetry: 'HERE Traffic Flow sensor data',
                    roadName: corridor.roadName,
                    condition:
                        corridor.description ??
                        (corridor.primaryType === 'congestion'
                            ? 'slow-moving'
                            : null),
                    direction: corridor.direction ?? null,
                    delayMinutes: corridor.delayMinutes ?? null,
                    totalLengthKm: corridor.totalLengthKm ?? null,
                    suburb: corridor.suburb ?? null,
                    magnitude: corridor.magnitude ?? null,
                    severity: corridor.severity ?? null,
                },
            }
        }

        // Authority feed payload (QLDTraffic, Live Traffic NSW, etc.)
        const authEvidence = evidence.filter(
            (e) => e.sourceId.toLowerCase() === sLower
        )
        if (authEvidence.length > 0) {
            return {
                id: src,
                name: sourceDisplayName(src),
                record:
                    authEvidence.length === 1 ? authEvidence[0] : authEvidence,
            }
        }

        // Fallback to normalized incident row if authority receipt is absent
        const ownIncidents = incidents.filter((i) =>
            (i.sources ?? []).some((s) => s.toLowerCase() === sLower)
        )
        return {
            id: src,
            name: sourceDisplayName(src),
            record:
                ownIncidents.length > 0
                    ? ownIncidents.length === 1
                        ? ownIncidents[0]
                        : ownIncidents
                    : null,
        }
    })

    const subtitle = leadOfficial
        ? evidence.length > 1
            ? `${matchBasisLabel(leadOfficial)}; ${evidence.length} authority receipts retained`
            : matchBasisLabel(leadOfficial)
        : sources.length === 1 && sources[0].toLowerCase() === 'here'
          ? 'Single-source record: only HERE reported this. No other feed matched within the radius.'
          : 'Multiple feeds contributed to this corridor.'

    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent
                className="max-w-2xl max-h-[85vh] flex flex-col p-0 overflow-hidden"
                data-testid="evidence-dialog"
                onClick={(e) => e.stopPropagation()}
                onPointerDown={(e) => e.stopPropagation()}
            >
                <DialogHeader className="p-5 pb-3 border-b border-border/50">
                    <DialogTitle className="text-base font-semibold">
                        Evidence: {corridor.roadName}
                    </DialogTitle>
                    <DialogDescription className="text-xs">
                        {subtitle}
                    </DialogDescription>
                </DialogHeader>

                <div className="flex-1 overflow-y-auto p-5 space-y-4">
                    {/* 1. Canonical Unified Pipeline Summary */}
                    <div className="rounded-lg border border-border/70 bg-card p-3.5 shadow-xs">
                        <div className="mb-2.5 flex items-center justify-between border-b border-border/40 pb-2">
                            <span className="text-2xs font-semibold uppercase tracking-wider text-muted-foreground">
                                Unified Corridor Truth (Pipeline Consensus)
                            </span>
                            <span className="text-2xs font-mono text-muted-foreground">
                                Sources:{' '}
                                {sources.map(sourceDisplayName).join(' + ')}
                            </span>
                        </div>

                        <div className="grid grid-cols-2 gap-x-4 gap-y-2 text-xs">
                            <div>
                                <span className="block text-2xs text-muted-foreground">
                                    Condition
                                </span>
                                <span className="font-medium">
                                    {unifiedCondition}
                                </span>
                            </div>
                            <div>
                                <span className="block text-2xs text-muted-foreground">
                                    Status / Road state
                                </span>
                                <span className="font-medium">
                                    {unifiedRoadClosed}
                                </span>
                            </div>
                            <div>
                                <span className="block text-2xs text-muted-foreground">
                                    Direction
                                </span>
                                <span className="font-mono">
                                    {unifiedDirection}
                                </span>
                            </div>
                            <div>
                                <span className="block text-2xs text-muted-foreground">
                                    Location detail
                                </span>
                                <span>{unifiedLocation}</span>
                            </div>
                            <div>
                                <span className="block text-2xs text-muted-foreground">
                                    Impact / Delay
                                </span>
                                <span>{unifiedImpact}</span>
                            </div>
                            <div>
                                <span className="block text-2xs text-muted-foreground">
                                    Priority
                                </span>
                                <span>{unifiedPriority}</span>
                            </div>
                            <div>
                                <span className="block text-2xs text-muted-foreground">
                                    Reported / Published
                                </span>
                                <span className="font-mono">
                                    {fmtTime(unifiedReported)}
                                </span>
                            </div>
                            <div>
                                <span className="block text-2xs text-muted-foreground">
                                    Schedule Window
                                </span>
                                <span className="font-mono">
                                    {unifiedStarts
                                        ? fmtTime(unifiedStarts)
                                        : '-'}{' '}
                                    → {unifiedEnds ? fmtTime(unifiedEnds) : '-'}
                                </span>
                            </div>
                        </div>
                    </div>

                    {/* 2. Contributing Source Payloads (Inspection) */}
                    <div>
                        <div className="mb-2 flex items-center gap-2">
                            <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                                Individual Source Payloads
                            </h4>
                            <span className="text-2xs text-muted-foreground">
                                ({payloads.length} provider
                                {payloads.length === 1 ? '' : 's'})
                            </span>
                        </div>

                        <div className="grid gap-3 sm:grid-cols-2">
                            {payloads.map((col) => (
                                <div
                                    key={col.id}
                                    className="flex flex-col rounded-md border border-border/60 bg-muted/20 p-2.5 text-xs"
                                >
                                    <div className="mb-1.5 flex items-center justify-between border-b border-border/30 pb-1 font-medium">
                                        <span className="text-xs">
                                            {col.name}
                                        </span>
                                        {Array.isArray(col.record) &&
                                            col.record.length > 1 && (
                                                <span className="text-3xs text-muted-foreground font-mono">
                                                    {col.record.length} records
                                                </span>
                                            )}
                                    </div>
                                    <div className="max-h-60 space-y-2 overflow-y-auto pr-1 text-[11px] leading-snug">
                                        {col.record === null ? (
                                            <span className="text-muted-foreground text-xs">
                                                No raw payload available
                                            </span>
                                        ) : Array.isArray(col.record) ? (
                                            col.record.map((r, i) => (
                                                <div
                                                    key={i}
                                                    className="border-b border-border/30 pb-1.5 last:border-0 last:pb-0"
                                                >
                                                    <FieldList record={r} />
                                                </div>
                                            ))
                                        ) : (
                                            <FieldList record={col.record} />
                                        )}
                                    </div>
                                </div>
                            ))}
                        </div>
                    </div>

                    {children}
                </div>
            </DialogContent>
        </Dialog>
    )
}
