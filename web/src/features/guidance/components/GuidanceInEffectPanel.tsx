import { useState } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { InfoHint } from '@/components/InfoHint'
import { Skeleton } from '@/components/ui/skeleton'
import { Badge } from '@/components/ui/badge'
import { ICON } from '@/lib/design/icons'
import { useComposedGuidance } from '../api/guidance-hooks'
import { TYPE_BADGE, TYPE_LABEL } from '../lib/display'
import type { GuidanceTypeName } from '../api/schemas'

/** Scope chip for one in-effect directive — the panel's whole point is that
 *  the operator can see which scope each rule came from and who wins. */
function ScopeChip({ scope, scopeId }: { scope: string; scopeId: string }) {
    if (scope === 'Global') {
        return <Badge variant="outline">Global</Badge>
    }
    const label = scope === 'Station' ? 'Station' : 'Market'
    return (
        <Badge variant="outline">
            {label}
            {scopeId ? ` \u00B7 ${scopeId}` : ''}
        </Badge>
    )
}

const InEffectIcon = ICON.ai

interface GuidanceInEffectPanelProps {
    readonly marketId: string
    readonly station: string | null
    readonly label: string
}

/**
 * "What actually reaches the writer" for the selected station — the composed
 * result of scope precedence (Station > Market > Global) and the directive cap.
 *
 * Sits at the TOP of the Editorial guidance tab, above the rules it summarises:
 * it is the answer to the question the page is opened to ask, and it previously
 * sat below the fold underneath its own inputs. Collapsed by default to a
 * one-line count so it never pushes the authoring list down.
 */
export function GuidanceInEffectPanel({
    marketId,
    station,
    label,
}: GuidanceInEffectPanelProps) {
    const [open, setOpen] = useState(false)
    const composed = useComposedGuidance(marketId, station)

    const inEffect = composed.data?.inEffect ?? []
    const dropped = composed.data?.droppedCount ?? 0

    const summary = composed.isLoading
        ? 'Checking which rules apply…'
        : inEffect.length === 0
          ? 'No guidance reaches this station yet.'
          : `${inEffect.length} ${inEffect.length === 1 ? 'rule' : 'rules'} in effect${
                dropped > 0 ? `, ${dropped} not used` : ''
            }.`

    return (
        <section className="flex flex-col gap-2">
            <button
                type="button"
                onClick={() => setOpen((o) => !o)}
                aria-expanded={open}
                className="flex items-center gap-1.5 self-start rounded-md text-sm font-medium text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            >
                {open ? (
                    <ChevronDown className="size-4" />
                ) : (
                    <ChevronRight className="size-4" />
                )}
                <InEffectIcon className="size-4 text-muted-foreground" />
                Reaching the writer: {label}
            </button>
            {!open && (
                <p className="pl-5 text-xs text-muted-foreground">{summary}</p>
            )}
            {open &&
                (composed.isLoading ? (
                    <Skeleton className="ml-5 h-16 w-full" />
                ) : inEffect.length > 0 ? (
                    <div className="flex flex-col gap-1 pl-5">
                        <div className="flex items-center gap-1.5">
                            <span className="text-2xs uppercase tracking-wide text-muted-foreground">
                                In effect
                            </span>
                            <InfoHint label="How is precedence decided?">
                                Most specific first: Station beats Market beats
                                Global.
                            </InfoHint>
                        </div>
                        <ol className="space-y-1 text-sm">
                            {inEffect.map((g, i) => (
                                <li key={g.id} className="flex gap-2">
                                    <span className="w-4 shrink-0 pt-0.5 text-right text-2xs tabular-nums text-muted-foreground">
                                        {i + 1}
                                    </span>
                                    <span className="flex min-w-0 flex-col gap-1">
                                        <span className="flex flex-wrap items-center gap-1.5">
                                            <ScopeChip
                                                scope={g.scope}
                                                scopeId={g.scopeId}
                                            />
                                            <Badge
                                                variant="outline"
                                                className={
                                                    TYPE_BADGE[
                                                        g.type as GuidanceTypeName
                                                    ] ?? ''
                                                }
                                            >
                                                {TYPE_LABEL[
                                                    g.type as GuidanceTypeName
                                                ] ?? g.type}
                                            </Badge>
                                        </span>
                                        <span>{g.text}</span>
                                    </span>
                                </li>
                            ))}
                        </ol>
                        {dropped > 0 && (
                            <div className="flex items-center gap-1.5 pt-1">
                                <span className="text-xs text-muted-foreground">
                                    {dropped} more not used
                                </span>
                                <InfoHint label="Why are some rules not used?">
                                    Only the {inEffect.length} most specific
                                    make it into a bulletin.
                                </InfoHint>
                            </div>
                        )}
                    </div>
                ) : (
                    <p className="pl-5 text-sm text-muted-foreground">
                        No guidance applies to this station's bulletins yet.
                    </p>
                ))}
        </section>
    )
}
