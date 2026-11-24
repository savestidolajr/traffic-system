import { Timer, ListChecks, Mic2, RadioTower } from 'lucide-react'
import type { Job } from '../../types'
import { hasAudio, hasTraffic } from '../../types'
import {
    formatDuration,
    formatRelativeTime,
    formatClockTime,
} from '@/lib/formatting'
import { cn } from '@/lib/utils'
import { useNow } from '@/hooks/use-second-tick'
import { SLOT_MISS_THRESHOLD_MS } from '@/lib/constants/timing'
import { BudgetBar } from '@/lib/design/BudgetBar'
import { FreshnessDot } from '@/lib/design/FreshnessDot'
import { SourceGlyphs } from '@/lib/design/SourceGlyphs'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
interface ReviewVitalsProps {
    readonly job: Job
    readonly includedCount: number
    readonly availableCount: number
    /** Resolved broadcast slot for this bulletin, when known. */
    readonly airSlotAt?: Date | null
    /** When set, the "left out" count becomes a jump to the Adjust tab. */
    readonly onShowLeftOut?: () => void
    /** Events first seen since the market's previous bulletin (Event Ledger delta). */
    readonly newSinceLast?: number
}

function Vital({
    icon: Icon,
    label,
    children,
    className,
}: {
    readonly icon: typeof Timer
    readonly label: string
    readonly children: React.ReactNode
    readonly className?: string
}) {
    return (
        <span
            className={cn(
                'inline-flex items-center gap-1.5 text-xs tabular-nums text-muted-foreground',
                className
            )}
        >
            <Icon className="size-3 shrink-0 opacity-60" aria-hidden />
            <span className="sr-only">{label}: </span>
            {children}
        </span>
    )
}

/**
 * One quiet line of the bulletin's vital signs: when it airs, duration vs
 * target, incident funnel, traffic freshness, voice. Plain stats, not
 * controls; the single home for these numbers.
 */
export function ReviewVitals({
    job,
    includedCount,
    availableCount,
    airSlotAt,
    onShowLeftOut,
    newSinceLast,
}: ReviewVitalsProps) {
    const now = useNow()
    const audio = hasAudio(job) ? job.audio : undefined
    const onTarget = audio ? Math.abs(audio.deltaSec) <= 2 : true
    // The bulletin's unit of selection is the corridor (road), not the
    // incident: the script covers the top-ranked roads.
    const corridorsCovered = hasTraffic(job)
        ? (job.traffic?.corridors ?? []).filter((c) => c.isSelected).length
        : 0
    const fetchedAt =
        hasTraffic(job) && job.traffic?.fetchedAt ? job.traffic.fetchedAt : null

    return (
        <div className="flex flex-wrap items-center gap-x-5 gap-y-1.5">
            {airSlotAt && (
                <Vital icon={RadioTower} label="Air slot">
                    {/* Was a static clock time. It is the fact the operator is
                        racing, so it has to move: once the slot passes, the
                        station has already aired nothing. */}
                    {airSlotAt.getTime() < now - SLOT_MISS_THRESHOLD_MS ? (
                        <span className="font-medium text-destructive">
                            missed {formatClockTime(airSlotAt.toISOString())}
                        </span>
                    ) : (
                        <span className="font-medium text-foreground">
                            airs {formatClockTime(airSlotAt.toISOString())}
                        </span>
                    )}
                </Vital>
            )}
            <Vital icon={Timer} label="Duration">
                {audio ? (
                    <span className="inline-flex items-center gap-1.5">
                        <BudgetBar
                            measuredSec={audio.measuredDurationSec}
                            targetSec={job.targetDurationSec}
                        />
                        {!onTarget && (
                            <span
                                className={cn(
                                    'font-medium',
                                    audio.deltaSec > 0
                                        ? 'text-destructive'
                                        : 'text-warning'
                                )}
                            >
                                {Math.abs(Math.round(audio.deltaSec))}s{' '}
                                {audio.deltaSec > 0 ? 'over' : 'under'}
                            </span>
                        )}
                    </span>
                ) : (
                    <span>target {formatDuration(job.targetDurationSec)}</span>
                )}
            </Vital>
            <Vital icon={ListChecks} label="Coverage">
                {job.status === 'FALLBACK_USED' ? (
                    // A standby bulletin aired: the traffic snapshot's corridor
                    // and citation counts describe the draft that did NOT air,
                    // so they would contradict the Evidence panel's "Cited 0".
                    <span>standby bulletin aired</span>
                ) : (
                    <span>
                        {corridorsCovered > 0 && (
                            <>
                                {corridorsCovered} road
                                {corridorsCovered === 1 ? '' : 's'} covered
                                {' · '}
                            </>
                        )}
                        {includedCount} incident{includedCount === 1 ? '' : 's'}{' '}
                        cited
                        {availableCount > 0 && (
                            <>
                                {' · '}
                                {onShowLeftOut ? (
                                    <Tooltip>
                                        <TooltipTrigger asChild>
                                            <button
                                                type="button"
                                                onClick={onShowLeftOut}
                                                className="text-warning underline decoration-warning/40 underline-offset-2 transition-colors hover:decoration-warning"
                                            >
                                                {availableCount} below cut
                                            </button>
                                        </TooltipTrigger>
                                        <TooltipContent side="top">
                                            See below-cut incidents in the
                                            Adjust tab
                                        </TooltipContent>
                                    </Tooltip>
                                ) : (
                                    <span className="text-warning">
                                        {availableCount} below cut
                                    </span>
                                )}
                            </>
                        )}
                        {(newSinceLast ?? 0) > 0 && (
                            <>
                                {' · '}
                                <span className="text-primary-on-tint">
                                    {newSinceLast} new since last bulletin
                                </span>
                            </>
                        )}
                    </span>
                )}
            </Vital>
            {fetchedAt && (
                <span className="inline-flex items-center gap-1.5 text-xs tabular-nums text-muted-foreground">
                    <FreshnessDot ageMs={now - new Date(fetchedAt).getTime()} />
                    <span className="sr-only">Traffic data: </span>
                    traffic {formatRelativeTime(fetchedAt)}
                </span>
            )}
            {job.content && (job.content.weather || job.content.fuel) && (
                <span className="inline-flex items-center gap-1.5 text-xs text-muted-foreground">
                    <span className="sr-only">Sources: </span>
                    <SourceGlyphs
                        traffic={!!job.content.traffic}
                        weather={!!job.content.weather}
                        fuel={!!job.content.fuel}
                    />
                </span>
            )}
            <Tooltip>
                <TooltipTrigger asChild>
                    <span
                        role="img"
                        tabIndex={0}
                        aria-label={`Voice: ${job.input.voice.voiceName}`}
                        className="inline-flex cursor-default items-center gap-1.5 text-xs text-muted-foreground"
                    >
                        <Mic2
                            className="size-3 shrink-0 opacity-60"
                            aria-hidden
                        />
                        <span>{job.input.voice.voiceName}</span>
                    </span>
                </TooltipTrigger>
                <TooltipContent side="top">
                    Voice: {job.input.voice.voiceName}
                </TooltipContent>
            </Tooltip>
        </div>
    )
}
