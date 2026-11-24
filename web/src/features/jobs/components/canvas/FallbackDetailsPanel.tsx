import type { FallbackData } from '../../types'
import { Radio } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { formatDateTime } from '@/lib/formatting'

interface FallbackDetailsPanelProps {
    readonly fallback: FallbackData
    readonly targetDurationSec: number
}

/**
 * Explains WHY a standby bulletin aired for this slot, instead of leaving the
 * operator with a bare "Fallback Used". Mirrors the empty-hero treatment (icon
 * medallion + calm copy) but warning-toned: a fallback is a heads-up, not the
 * alarm state a hard failure is. The cause comes from the job's recorded
 * `reason` token; the kind (re-aired same-day bulletin vs generic filler cart)
 * is derived from whether a source job is present.
 */
function causeSentence(reason: string, targetDurationSec: number): string {
    switch (reason) {
        case 'content-limited':
            return `There wasn’t enough live traffic to fill the ${Math.round(targetDurationSec)}-second slot, so a full bulletin couldn’t be generated to length.`
        case 'missed-window':
            return 'The live bulletin didn’t finish before its airtime, so the slot was covered automatically.'
        case 'operator-rejected':
            return 'An operator sent this slot to a standby bulletin.'
        default:
            return 'The freshly generated bulletin couldn’t be used for this slot.'
    }
}

export function FallbackDetailsPanel({
    fallback,
    targetDurationSec,
}: FallbackDetailsPanelProps) {
    const fromSameDay = fallback.sourceJobId.trim().length > 0
    const kindSentence = fromSameDay
        ? 'The most recent on-air bulletin for this market was re-aired in its place.'
        : 'The station’s generic standby bulletin (filler cart) aired in its place.'

    return (
        <div className="relative overflow-hidden rounded-xl border border-warning/40 bg-warning/5 p-4 shadow-[0_0_40px_-16px_hsl(var(--warning)/0.45)]">
            <div
                aria-hidden
                className="pointer-events-none absolute inset-x-0 top-0 h-px bg-linear-to-r from-transparent via-warning/60 to-transparent"
            />
            <div className="flex items-start gap-3">
                <div className="flex size-11 shrink-0 items-center justify-center rounded-2xl border border-warning/30 bg-warning/10 text-warning shadow-glow">
                    <ICON.rejectToFallback className="size-5" aria-hidden />
                </div>
                <div className="flex min-w-0 flex-1 flex-col gap-1.5">
                    <h2 className="text-sm font-semibold text-warning">
                        Standby bulletin aired
                    </h2>
                    <p className="text-sm font-medium text-foreground/90">
                        {causeSentence(fallback.reason, targetDurationSec)}
                    </p>
                    <p className="text-sm text-muted-foreground">
                        {kindSentence}
                    </p>
                    <div className="mt-1 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground-subtle">
                        {fallback.appliedAt && (
                            <span className="inline-flex items-center gap-1.5">
                                <ICON.time className="size-3.5" aria-hidden />
                                Applied · {formatDateTime(fallback.appliedAt)}
                            </span>
                        )}
                        {fromSameDay && (
                            <span className="inline-flex items-center gap-1.5">
                                <Radio className="size-3.5" aria-hidden />
                                Source bulletin{' '}
                                <code className="font-mono">
                                    {fallback.sourceJobId}
                                </code>
                            </span>
                        )}
                    </div>
                    {fallback.streak > 1 && (
                        <p className="mt-1 inline-flex items-center gap-1.5 text-xs font-medium text-destructive">
                            <ICON.warning
                                className="size-3.5 shrink-0"
                                aria-hidden
                            />
                            Chronic: {fallback.streak} consecutive fallbacks for
                            this market: no fresh bulletin has aired since this
                            streak began.
                        </p>
                    )}
                </div>
            </div>
        </div>
    )
}
