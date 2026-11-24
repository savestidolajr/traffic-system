import { useState } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { Button } from '@/components/ui/button'
import { diffEntities } from '@/lib/entity-diff'
import { EntityDiffView } from '@/components/EntityDiffView'
import { formatDateTime } from '@/lib/formatting'
import { AudioWaveformPlayer } from '../AudioWaveformPlayer'
import { hasScript, type Job } from '../../types'

interface SupersededTakeCardProps {
    readonly job: Job
    /** Supplied only for statuses where a fresh generation is a legitimate next
     *  action; its absence is what makes the card purely read-only. */
    readonly onUseAsDraft?: (text: string) => void
}

/**
 * The bulletin that did not air. A fallback destroys the generated script in
 * place (`ApplyFillerFallback` clears it, `ApplyFallback` overwrites it with the
 * re-aired source), so without this the operator sees an empty script card and
 * the writer cannot be reviewed after the fact.
 *
 * Collapsed by default — it is history, not the decision surface. Plain
 * transport and plain paragraph text on purpose: karaoke highlighting and
 * provenance marks belong to the aired script in `ReviewBulletin`, and wiring a
 * read-only historical take into that machinery buys nothing.
 */
export function SupersededTakeCard({
    job,
    onUseAsDraft,
}: SupersededTakeCardProps) {
    const [open, setOpen] = useState(false)
    const take = job.supersededTake
    if (!take) return null

    const airedText = hasScript(job) ? (job.script?.text ?? '') : ''
    // The two cases the operator must be able to tell apart. FallbackDetailsPanel
    // states the cause; this states only what happened to THIS take, so the two
    // do not repeat each other.
    const fallbackSourceJobId =
        job.status === 'FALLBACK_USED' || job.status === 'PUBLISHED'
            ? job.fallback?.sourceJobId
            : undefined
    const replacedBy = fallbackSourceJobId
        ? 'An earlier bulletin from today aired instead of this one.'
        : 'The station’s standby cart aired instead of this one.'

    return (
        <section className="flex flex-col gap-2 rounded-xl border border-border/60 p-4">
            <button
                type="button"
                onClick={() => setOpen((o) => !o)}
                aria-expanded={open}
                className="flex items-center gap-1.5 self-start rounded-md text-sm font-medium text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            >
                {open ? (
                    <ChevronDown className="size-4" aria-hidden />
                ) : (
                    <ChevronRight className="size-4" aria-hidden />
                )}
                <ICON.history className="size-4" aria-hidden />
                Bulletin that did not air
            </button>
            <p className="pl-5 text-xs text-muted-foreground">
                Generated {formatDateTime(take.capturedAt)} · {take.reason}
            </p>

            {open && (
                <div className="flex flex-col gap-3 pl-5">
                    <p className="text-sm text-muted-foreground">
                        {replacedBy} It is kept here so the read can still be
                        reviewed and heard.
                    </p>

                    {take.audioUrl && (
                        <AudioWaveformPlayer
                            url={take.audioUrl}
                            duration={take.measuredDurationSec ?? undefined}
                        />
                    )}

                    <p className="whitespace-pre-wrap text-sm leading-relaxed text-foreground/90">
                        {take.text}
                    </p>

                    {/* A diff against an empty aired script is noise — that is the
                        common standby-cart case, where the take text alone is the
                        whole story. */}
                    {airedText.trim().length > 0 && (
                        <div className="rounded-md border border-border/40 bg-muted/20 px-2 py-1.5">
                            <p className="mb-1 text-2xs uppercase tracking-wide text-muted-foreground">
                                Changes vs what aired
                            </p>
                            <EntityDiffView
                                entries={diffEntities(
                                    { Script: airedText },
                                    { Script: take.text }
                                )}
                            />
                        </div>
                    )}

                    {onUseAsDraft && (
                        <Button
                            type="button"
                            size="sm"
                            variant="outline"
                            className="self-start"
                            onClick={() => onUseAsDraft(take.text)}
                        >
                            <ICON.reset className="size-3.5" aria-hidden />
                            Use as working script
                        </Button>
                    )}
                </div>
            )}
        </section>
    )
}
