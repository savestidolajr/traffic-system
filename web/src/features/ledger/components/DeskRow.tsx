import { useState } from 'react'
import { Ban, Undo2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { ProvenanceBadge } from '@/features/jobs/components/review/adjust/SourceChips'
import {
    incidentAnchor,
    incidentEvent,
    incidentWindowLabel,
} from '@/features/jobs/lib/incidents/incident-labels'
import { formatRelativeTime, formatRoadName } from '@/lib/formatting'
import { cn } from '@/lib/utils'
import type { LedgerEvent } from '../api/schemas'
import { EvidenceDialog } from '@/features/jobs/components/review/adjust/EvidenceDialog'
import { Link } from 'react-router'

/**
 * One shared column template so the header and every row stay aligned, exactly as the
 * rundown does with its COLS constant. Location gets the widest track: it is the column
 * that tells two worksites on the same road apart.
 */
export const DESK_COLS =
    'grid grid-cols-[minmax(0,1.1fr)_minmax(0,1.5fr)_auto] items-center gap-x-3 ' +
    'md:grid-cols-[minmax(0,1.1fr)_minmax(0,1.5fr)_6rem_minmax(0,10rem)_6rem_3rem_3.5rem_4.5rem]'

/** The rundown-style header row for the Desk queue. */
export function DeskHeader() {
    return (
        <div
            className={cn(
                DESK_COLS,
                'border-b border-border px-2 pb-1.5 text-left text-2xs font-medium uppercase tracking-wider text-muted-foreground-subtle'
            )}
        >
            <span>Road</span>
            <span>Location</span>
            <span className="hidden md:block">Type</span>
            <span className="hidden md:block">Sources</span>
            <span className="hidden md:block">Window</span>
            <span className="hidden text-right md:block">Age</span>
            <span className="hidden text-right md:block">Aired</span>
            <span aria-hidden />
        </div>
    )
}

interface DeskRowProps {
    readonly event: LedgerEvent
    /** in-play rows offer Omit; omitted rows offer Restore. */
    readonly mode: 'in-play' | 'omitted'
    readonly onOmit: () => void
    readonly onRestore: () => void
}

/**
 * One event in the evidence view. The row opens the full record; the single action is
 * Omit (or Restore, on the omitted list) — everything airs by default, so the control
 * surface is exactly one reversible button.
 */
export function DeskRow({ event, mode, onOmit, onRestore }: DeskRowProps) {
    const [open, setOpen] = useState(false)
    const [now] = useState(() => Date.now())
    const days = Math.max(
        1,
        Math.ceil((now - new Date(event.firstSeenUtc).getTime()) / 86_400_000)
    )
    const window = incidentWindowLabel(event)
    const location = event.detail
        ? (incidentAnchor(event.detail) ?? event.detail)
        : null

    return (
        <>
            <div
                onClick={() => setOpen(true)}
                className={cn(
                    DESK_COLS,
                    'group cursor-pointer rounded-md px-2 py-1.5 transition-colors hover:bg-muted/20',
                    mode === 'omitted' && 'opacity-70'
                )}
                data-testid={`desk-row-${event.key}`}
            >
                <span className="min-w-0">
                    <span className="block truncate text-xs font-medium">
                        {formatRoadName(event.roadName)}
                        {event.roadClosed && (
                            <span className="ml-1.5 text-2xs font-semibold uppercase tracking-wide text-destructive">
                                closed
                            </span>
                        )}
                    </span>
                    {event.verdict === 'Omitted' && (
                        <span className="block truncate text-2xs text-muted-foreground">
                            Omitted by {event.verdictBy}
                            {event.verdictAtUtc
                                ? `, ${formatRelativeTime(event.verdictAtUtc)}`
                                : ''}
                        </span>
                    )}
                </span>

                <span
                    className="truncate text-xs text-muted-foreground"
                    title={event.detail ?? undefined}
                >
                    {location ?? '-'}
                </span>

                <span className="hidden truncate text-xs text-muted-foreground md:block">
                    {incidentEvent(event.headline)}
                </span>

                <span className="hidden min-w-0 md:block">
                    <ProvenanceBadge sources={event.sources} />
                </span>

                <span className="hidden truncate font-mono text-2xs tabular-nums text-muted-foreground md:block">
                    {window ?? '-'}
                </span>

                <span
                    className="hidden text-right font-mono text-2xs tabular-nums text-muted-foreground md:block"
                    title={`First seen ${formatRelativeTime(event.firstSeenUtc)}, ${event.sightingCount} sighting${event.sightingCount === 1 ? '' : 's'}`}
                >
                    {days}d
                </span>

                <span
                    className="hidden text-right font-mono text-2xs tabular-nums text-muted-foreground md:block"
                    title={`Voiced in ${event.airings.length} published bulletin${event.airings.length === 1 ? '' : 's'}`}
                >
                    {event.airings.length}×
                </span>

                <span className="flex items-center justify-end">
                    <Tooltip>
                        <TooltipTrigger asChild>
                            <Button
                                variant="ghost"
                                size="icon"
                                className={cn(
                                    'size-7 text-muted-foreground opacity-0 transition-opacity focus-visible:opacity-100 group-hover:opacity-100',
                                    mode === 'in-play'
                                        ? 'hover:text-destructive'
                                        : 'hover:text-foreground'
                                )}
                                aria-label={
                                    mode === 'in-play'
                                        ? `Omit ${formatRoadName(event.roadName)} from bulletins`
                                        : `Restore ${formatRoadName(event.roadName)} to bulletins`
                                }
                                onClick={(e) => {
                                    e.stopPropagation()
                                    if (mode === 'in-play') onOmit()
                                    else onRestore()
                                }}
                                onKeyDown={(e) => e.stopPropagation()}
                            >
                                {mode === 'in-play' ? <Ban /> : <Undo2 />}
                            </Button>
                        </TooltipTrigger>
                        <TooltipContent side="top">
                            {mode === 'in-play'
                                ? 'Omit from every bulletin until restored'
                                : 'Restore to bulletins'}
                        </TooltipContent>
                    </Tooltip>
                </span>
            </div>
            <EvidenceDialog
                corridor={{
                    roadName: event.roadName,
                    sources: event.sources,
                    officialEvidences: event.officialEvidences ?? undefined,
                }}
                incidents={[event.record]}
                open={open}
                onOpenChange={setOpen}
            >
                {(event.observations.length > 0 ||
                    event.airings.length > 0) && (
                    <div className="space-y-2 text-xs">
                        {event.observations.length > 0 && (
                            <div>
                                <p className="mb-1 font-medium">
                                    Changes between fetches
                                </p>
                                <ol className="space-y-1 text-muted-foreground">
                                    {event.observations.map((o, i) => (
                                        <li
                                            key={i}
                                            title={new Date(
                                                o.atUtc
                                            ).toLocaleString()}
                                        >
                                            {formatRelativeTime(o.atUtc)}:{' '}
                                            {o.summary}
                                        </li>
                                    ))}
                                </ol>
                            </div>
                        )}
                        {event.airings.length > 0 && (
                            <div>
                                <p className="mb-1 font-medium">Aired in</p>
                                <ol className="max-h-40 space-y-1 overflow-auto">
                                    {event.airings.map((a, i) => (
                                        <li key={i}>
                                            <Link
                                                to={`/jobs/${a.jobId}`}
                                                className="text-muted-foreground underline decoration-border underline-offset-2 hover:text-foreground"
                                                title={new Date(
                                                    a.atUtc
                                                ).toLocaleString()}
                                            >
                                                {formatRelativeTime(a.atUtc)}
                                            </Link>{' '}
                                            <span className="italic text-muted-foreground">
                                                "{a.sentence}"
                                            </span>
                                        </li>
                                    ))}
                                </ol>
                            </div>
                        )}
                    </div>
                )}
            </EvidenceDialog>
        </>
    )
}
