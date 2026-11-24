import { useState } from 'react'
import { Radio, Satellite } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import {
    Tooltip,
    TooltipContent,
    TooltipProvider,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import type { JobIncident, TrafficCorridor } from '../../../types'
import { EvidenceDialog } from './EvidenceDialog'
import { sourceDisplayName } from '../../../lib/source-display-names'

interface SourceChipsProps {
    readonly corridor: TrafficCorridor
    /** The corridor's own evidence rows, for the fact-check dialog's HERE column. */
    readonly incidents?: readonly JobIncident[]
}

/**
 * Provenance chips: the feeds this corridor's evidence came from, source-neutrally — and the
 * door to the fact-check dialog. Every corridor gets a chip (HERE-only rows a quiet grey one),
 * because "no authority record within the match radius" is itself a finding an operator should
 * be able to open and read; multi-source and official-only rows carry the louder treatments.
 */
export function SourceChips({ corridor, incidents = [] }: SourceChipsProps) {
    const [open, setOpen] = useState(false)
    // `sources` is authoritative when present. The fallback derives provenance from the evidence
    // receipts themselves for rows persisted before `sources` was populated; there is no singular
    // `officialEvidence` to consider, since that property was replaced by the `officialEvidences`
    // list in a clean cutover.
    const sources = corridor.sources?.length
        ? corridor.sources
        : corridor.officialEvidences?.length
          ? ['here', ...corridor.officialEvidences.map((e) => e.sourceId)]
          : ['here']
    const officials = sources.filter((s) => s.toLowerCase() !== 'here')
    const multiSource =
        officials.length > 0 && sources.length > officials.length
    const officialOnly = officials.length > 0 && !multiSource
    const chipClass = multiSource
        ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-500/20'
        : officialOnly
          ? 'bg-sky-500/10 text-sky-600 dark:text-sky-400 border-sky-500/20'
          : 'bg-muted/40 text-muted-foreground border-border/60'
    const testId = multiSource
        ? 'source-chips-corroborated'
        : officialOnly
          ? 'source-chips-official-only'
          : 'source-chips-here-only'
    const tooltip = multiSource
        ? `Reported by ${sources.map(sourceDisplayName).join(' and ')}: click to fact-check`
        : officialOnly
          ? `Official alert from ${officials.map(sourceDisplayName).join(' and ')}: click to fact-check`
          : 'HERE only: no road-authority record matched. Click to see the evidence.'

    return (
        <>
            <TooltipProvider>
                <Tooltip>
                    <TooltipTrigger asChild>
                        <button
                            type="button"
                            data-testid={testId}
                            onClick={(e) => {
                                e.stopPropagation()
                                setOpen(true)
                            }}
                            className={`inline-flex items-center gap-1 rounded border px-1.5 py-0.5 text-[10px] font-medium cursor-pointer ${chipClass}`}
                        >
                            {multiSource ? (
                                <ICON.safety className="h-3 w-3" />
                            ) : officialOnly ? (
                                <Radio className="h-3 w-3" />
                            ) : (
                                <Satellite className="h-3 w-3" />
                            )}
                            <span>
                                {sources.map(sourceDisplayName).join(' + ')}
                            </span>
                        </button>
                    </TooltipTrigger>
                    <TooltipContent side="top" className="text-xs">
                        {tooltip}
                    </TooltipContent>
                </Tooltip>
            </TooltipProvider>
            <EvidenceDialog
                corridor={corridor}
                incidents={incidents}
                open={open}
                onOpenChange={setOpen}
            />
        </>
    )
}

/**
 * Display-only provenance badge: the same treatments as SourceChips (corroborated green,
 * official-only sky, HERE-only quiet grey) for surfaces whose row already owns the click,
 * like the Desk queue.
 */
export function ProvenanceBadge({
    sources,
}: {
    readonly sources: readonly string[]
}) {
    const list = sources.length ? sources : ['here']
    const officials = list.filter((s) => s.toLowerCase() !== 'here')
    const multiSource = officials.length > 0 && list.length > officials.length
    const officialOnly = officials.length > 0 && !multiSource
    const chipClass = multiSource
        ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-500/20'
        : officialOnly
          ? 'bg-sky-500/10 text-sky-600 dark:text-sky-400 border-sky-500/20'
          : 'bg-muted/40 text-muted-foreground border-border/60'
    return (
        <span
            className={`inline-flex max-w-full items-center gap-1 rounded border px-1.5 py-0.5 text-[10px] font-medium ${chipClass}`}
        >
            {multiSource ? (
                <ICON.safety className="h-3 w-3" />
            ) : officialOnly ? (
                <Radio className="h-3 w-3" />
            ) : (
                <Satellite className="h-3 w-3 shrink-0" />
            )}
            <span className="truncate">
                {list.map(sourceDisplayName).join(' + ')}
            </span>
        </span>
    )
}
