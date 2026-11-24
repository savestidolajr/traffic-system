import { useMemo, useState } from 'react'
import { History, ChevronDown, Loader2 } from 'lucide-react'
import type { Job, JobSummary } from '../../types'
import { hasAudio, hasScript } from '../../types'
import { useJobs, useJobDetail } from '../../api/job-hooks'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import { STATUS_CONFIG } from '../../config/status-config'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { Badge } from '@/components/ui/badge'
import { AudioWaveformPlayer } from '../AudioWaveformPlayer'
import { formatSlotClock } from '@/features/onair/lib/air-plan'
import { selectEarlierToday } from '../../lib/job-meta/earlier-today'
import { cn } from '@/lib/utils'

/**
 * "Earlier today": the day's previous bulletins for this station, each
 * expandable to read its script and play its audio. Generated bulletins carry
 * context from earlier same-day bulletins, so this lets the operator see and
 * hear exactly what aired before. Read-only; renders nothing when there are no
 * prior bulletins. Detail (script + audio) is fetched lazily per row on expand.
 */
export function EarlierToday({ job }: { readonly job: Job }) {
    const [isSectionOpen, setIsSectionOpen] = useState(false)
    const { data } = useJobs({})
    const { data: scheduleData } = useSchedules()
    const timezone =
        scheduleData?.schedules.find(
            (schedule) =>
                (schedule.station ?? schedule.marketId) === job.station
        )?.timezone ?? 'UTC'
    const earlier = useMemo(
        () =>
            selectEarlierToday(data?.jobs ?? [], {
                jobId: job.jobId,
                station: job.station,
                createdAt: job.createdAt,
                timezone,
            }),
        [data, job.jobId, job.station, job.createdAt, timezone]
    )

    if (earlier.length === 0) return null

    return (
        <SpotlightCard className="flex flex-col p-4">
            <button
                type="button"
                onClick={() => setIsSectionOpen((o) => !o)}
                aria-expanded={isSectionOpen}
                className="flex w-full cursor-pointer items-center justify-between text-left transition-colors hover:opacity-80"
            >
                <span className="flex items-center gap-1.5 text-xs font-semibold">
                    <History
                        className="size-3.5 text-muted-foreground"
                        aria-hidden
                    />
                    Earlier today
                    <span className="font-normal text-muted-foreground">
                        · {earlier.length}{' '}
                        {earlier.length === 1 ? 'bulletin' : 'bulletins'}
                    </span>
                </span>
                <ChevronDown
                    className={cn(
                        'size-4 shrink-0 text-muted-foreground transition-transform',
                        isSectionOpen && 'rotate-180'
                    )}
                    aria-hidden
                />
            </button>

            {isSectionOpen && (
                <ul className="mt-3 flex flex-col divide-y divide-border/40 border-t border-border/40 pt-1">
                    {earlier.map((summary: JobSummary) => (
                        <EarlierRow
                            key={summary.jobId}
                            summary={summary}
                            timezone={timezone}
                        />
                    ))}
                </ul>
            )}
        </SpotlightCard>
    )
}

/** One prior bulletin: a clickable time + status header that expands to its
 *  script and audio (loaded on demand). */
function EarlierRow({
    summary,
    timezone,
}: {
    readonly summary: JobSummary
    readonly timezone: string
}) {
    const [open, setOpen] = useState(false)
    const config = STATUS_CONFIG[summary.status]

    return (
        <li>
            <button
                type="button"
                onClick={() => setOpen((o) => !o)}
                aria-expanded={open}
                className="flex w-full cursor-pointer items-center gap-2 rounded-md py-2.5 text-left text-sm transition-colors hover:bg-muted/40"
            >
                <span className="font-medium tabular-nums">
                    {formatSlotClock(new Date(summary.createdAt), timezone)}
                </span>
                <Badge variant={config.variant} className="text-[10px]">
                    {config.label}
                </Badge>
                <ChevronDown
                    className={cn(
                        'ml-auto size-4 shrink-0 text-muted-foreground transition-transform',
                        open && 'rotate-180'
                    )}
                    aria-hidden
                />
            </button>
            {open && <EarlierRowBody jobId={summary.jobId} />}
        </li>
    )
}

/** The lazily-fetched body of a prior bulletin: audio transport + script text. */
function EarlierRowBody({ jobId }: { readonly jobId: string }) {
    const { data: detail, isLoading, isError } = useJobDetail(jobId)

    if (isError) {
        return (
            <p className="pb-3 pl-1 text-xs text-muted-foreground">
                Couldn't load this bulletin. Open it from History for the full
                record.
            </p>
        )
    }

    if (isLoading || !detail) {
        return (
            <div className="flex items-center gap-2 pb-3 pl-1 text-xs text-muted-foreground">
                <Loader2 className="size-3.5 animate-spin" aria-hidden />
                Loading bulletin…
            </div>
        )
    }

    const audio = hasAudio(detail) ? detail.audio : undefined
    const scriptText = hasScript(detail) ? (detail.script?.text ?? '') : ''

    return (
        <div className="flex flex-col gap-3 pb-3">
            {audio && (
                <AudioWaveformPlayer
                    url={audio.data.url}
                    duration={audio.data.durationSec}
                />
            )}
            {scriptText ? (
                <p className="whitespace-pre-wrap text-sm leading-relaxed text-muted-foreground">
                    {scriptText}
                </p>
            ) : (
                <p className="text-xs text-muted-foreground">
                    No script recorded for this bulletin.
                </p>
            )}
        </div>
    )
}
