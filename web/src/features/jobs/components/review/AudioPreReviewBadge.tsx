import { ICON } from '@/lib/design/icons'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import type { PreReviewData } from '../../types'

interface AudioPreReviewBadgeProps {
    /** Absent = pre-feature job or transcription unavailable — renders nothing. */
    readonly preReview: PreReviewData | undefined
    /** Words still needing an ear, after standing verdicts are applied. */
    readonly suspectCount: number
    readonly verifiedCount: number
    readonly figureCount: number
    /** Flagged items with no match in the rendered script — normally empty. A
     *  script edited after synthesis can orphan one, and dropping it silently
     *  would hide a real warning, so it is named here instead. */
    readonly unlocated: readonly string[]
}

function pluralize(count: number, noun: string): string {
    return `${count} ${noun}${count === 1 ? '' : 's'}`
}

/**
 * One quiet line under the audio player saying whether the transcript check
 * found anything, and nothing else.
 *
 * It deliberately carries NO per-word chips. Every flagged word is marked in
 * the script itself, which is the only place it exists and already handles
 * click-to-seek — chips here repeated the same word with a second, competing
 * affordance, which is what operators found confusing. This line is a counter
 * and a "did the check run at all" signal; the script is the workspace.
 */
export function AudioPreReviewBadge({
    preReview,
    suspectCount,
    verifiedCount,
    figureCount,
    unlocated,
}: AudioPreReviewBadgeProps) {
    if (!preReview) return null

    const matchPercent = Math.round(preReview.matchRatio * 100)
    const outstanding = suspectCount + figureCount

    if (outstanding === 0) {
        return (
            <div className="flex flex-wrap items-center gap-1.5 pt-2 text-2xs">
                <Tooltip>
                    <TooltipTrigger asChild>
                        <div
                            tabIndex={0}
                            className="flex cursor-default items-center gap-1 text-muted-foreground"
                        >
                            <ICON.success
                                className="size-3 shrink-0"
                                aria-hidden
                            />
                            <span className="tabular-nums">
                                {matchPercent}%
                            </span>
                            {verifiedCount > 0 && (
                                <span>
                                    · {pluralize(verifiedCount, 'word')}{' '}
                                    verified
                                </span>
                            )}
                        </div>
                    </TooltipTrigger>
                    <TooltipContent side="top">
                        {verifiedCount > 0
                            ? 'Nothing left to check. The remaining flags are verified. They stay marked in the script.'
                            : `Audio verified. Transcript matches the script (${matchPercent}%)`}
                    </TooltipContent>
                </Tooltip>
            </div>
        )
    }

    const parts: string[] = []
    if (suspectCount > 0)
        parts.push(`${pluralize(suspectCount, 'word')} to verify by ear`)
    if (figureCount > 0)
        parts.push(`${pluralize(figureCount, 'figure')} unconfirmed`)

    return (
        <div className="flex flex-wrap items-center gap-1.5 pt-2 text-2xs">
            <Tooltip>
                <TooltipTrigger asChild>
                    <span
                        tabIndex={0}
                        className="inline-flex cursor-default items-center gap-1 font-medium text-warning"
                    >
                        <ICON.warning className="size-3 shrink-0" aria-hidden />
                        {parts.join(' · ')}
                    </span>
                </TooltipTrigger>
                <TooltipContent side="top">
                    Highlighted in the script below. Hover one to listen or
                    resolve it.
                </TooltipContent>
            </Tooltip>
            {verifiedCount > 0 && (
                <span className="text-muted-foreground">
                    · {pluralize(verifiedCount, 'word')} verified
                </span>
            )}
            {unlocated.length > 0 && (
                <Tooltip>
                    <TooltipTrigger asChild>
                        <span
                            tabIndex={0}
                            className="cursor-default rounded-full border border-warning/30 bg-warning/10 px-2 py-0.5 font-medium text-warning"
                        >
                            {unlocated.join(', ')}
                        </span>
                    </TooltipTrigger>
                    <TooltipContent side="top">
                        Flagged when the audio was made, but no longer in the
                        script. It was edited since.
                    </TooltipContent>
                </Tooltip>
            )}
        </div>
    )
}
