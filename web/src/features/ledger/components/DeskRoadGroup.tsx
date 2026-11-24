import { useState } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { formatRoadName } from '@/lib/formatting'
import type { LedgerEvent } from '../api/schemas'
import { DeskRow } from './DeskRow'

interface DeskRoadGroupProps {
    readonly roadName: string
    readonly events: readonly LedgerEvent[]
    readonly mode: 'in-play' | 'omitted'
    readonly onOmit: (event: LedgerEvent) => void
    readonly onRestore: (event: LedgerEvent) => void
}

/**
 * Same-road records group under one disclosure row, exactly as corridors group incidents
 * in the rundown: seventeen "South Road" worksite segments read as one road with seventeen
 * records, not seventeen copies of the same row. Single-record roads render directly.
 */
export function DeskRoadGroup({
    roadName,
    events,
    mode,
    onOmit,
    onRestore,
}: DeskRoadGroupProps) {
    const [expanded, setExpanded] = useState(false)

    if (events.length === 1) {
        return (
            <DeskRow
                event={events[0]}
                mode={mode}
                onOmit={() => onOmit(events[0])}
                onRestore={() => onRestore(events[0])}
            />
        )
    }

    return (
        <div>
            <div
                className="flex cursor-pointer items-center gap-1.5 rounded-md px-2 py-1.5 transition-colors hover:bg-muted/20"
                onClick={() => setExpanded((v) => !v)}
                tabIndex={0}
                onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault()
                        setExpanded((v) => !v)
                    }
                }}
            >
                {expanded ? (
                    <ChevronDown
                        className="size-3.5 shrink-0 text-muted-foreground"
                        aria-hidden
                    />
                ) : (
                    <ChevronRight
                        className="size-3.5 shrink-0 text-muted-foreground"
                        aria-hidden
                    />
                )}
                <span className="text-xs font-medium">
                    {formatRoadName(roadName)}
                </span>
                <span className="text-2xs text-muted-foreground">
                    {events.length} records
                </span>
            </div>
            {expanded && (
                <div className="flex flex-col gap-0.5">
                    {events.map((event) => (
                        <DeskRow
                            key={event.key}
                            event={event}
                            mode={mode}
                            onOmit={() => onOmit(event)}
                            onRestore={() => onRestore(event)}
                        />
                    ))}
                </div>
            )}
        </div>
    )
}
