import { useRef, useState } from 'react'
import { Play, Pause, ArrowRight, Check, Radio } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { playSolo } from '@/lib/audio-solo'
import { resolveAudioSrc } from '@/lib/api/client'
import { formatDuration } from '@/lib/formatting'
import { useJobDetail, useSetAirSource } from '@/features/jobs/api/job-hooks'
import { hasAudio } from '@/features/jobs/types'
import { cn } from '@/lib/utils'
import type { AirCandidate, AirPlanRow } from '../../lib/air-plan'
import { usePlayoutTray } from '../../hooks/use-playout-tray'
const NO_SLOT_JOB_REASON = 'No bulletin in this slot to redirect'

/**
 * One audio option for a slot: hear it and commit it on the same line.
 *
 * Auditioning used to mean leaving the board — open the bulletin, listen,
 * navigate back, then pick — which is three moves and two context switches for
 * one decision, and the only reason for it was that the player happened to be
 * built on the review page. Nothing about a 30-second listen needs a page.
 *
 * The play button is deliberately NOT the row's primary action: an operator
 * scanning this list is choosing, and playback is how they choose, so the
 * commit button stays visually dominant and never moves as rows play.
 */
function AuditionRow({
    candidate,
    slotJobId,
    slotEtag,
    slotClosed,
    station,
    onSelectJob,
    onCommitted,
}: {
    readonly candidate: AirCandidate
    readonly slotJobId: string | null
    readonly slotEtag: string | null
    /** The slot's own job is already Published or FallbackUsed, so the backend will
     *  refuse any air-source change to it. */
    readonly slotClosed: boolean
    readonly station: string
    readonly onSelectJob: (jobId: string) => void
    readonly onCommitted: () => void
}) {
    const audioRef = useRef<HTMLAudioElement>(null)
    const [playing, setPlaying] = useState(false)
    const [active, setActive] = useState(false)
    const airSource = useSetAirSource()

    // Candidate details are deliberately interaction-triggered. A closed or
    // merely visible tray must not fan out one detail request per candidate.
    const isCart = candidate.kind === 'filler'
    const detail = useJobDetail(active && !isCart ? candidate.id : '')
    const job = detail.data
    const audio = job && hasAudio(job) ? job.audio : null
    const audioUrl = active && !isCart ? (audio?.data.url ?? null) : null
    const durationSec = audio?.data.durationSec ?? null
    const toggle = async () => {
        const el = audioRef.current
        if (!el) return
        if (!el.paused) {
            el.pause()
            return
        }
        // `playing` is driven by the element's own events, never optimistically:
        // playSolo resolves false on a blocked or missing file, and the button
        // must not sit on "Pause" over silence.
        await playSolo(el)
    }

    const commit = () => {
        if (!slotJobId) return
        airSource.mutate(
            {
                jobId: slotJobId,
                kind: isCart ? 'cart' : 'repeat',
                sourceJobId: isCart ? null : candidate.id,
                expectedEtag: slotEtag,
            },
            { onSuccess: onCommitted }
        )
    }

    return (
        <li
            className="flex items-center gap-2 border-b border-border/40 px-3 py-2 last:border-b-0"
            tabIndex={isCart ? -1 : 0}
            onFocus={() => setActive(true)}
            onPointerDown={() => setActive(true)}
        >
            {audioUrl ? (
                <>
                    <audio
                        ref={audioRef}
                        src={resolveAudioSrc(audioUrl)}
                        preload="none"
                        onPlay={() => setPlaying(true)}
                        onPause={() => setPlaying(false)}
                        onEnded={() => setPlaying(false)}
                    />
                    <Button
                        variant="outline"
                        size="icon"
                        className="size-7 shrink-0"
                        aria-label={`${playing ? 'Pause' : 'Play'} ${candidate.label}`}
                        onClick={() => {
                            setActive(true)
                            void toggle()
                        }}
                    >
                        {playing ? (
                            <Pause className="size-3.5" />
                        ) : (
                            <Play className="size-3.5" />
                        )}
                    </Button>
                </>
            ) : (
                <span
                    className="flex size-7 shrink-0 items-center justify-center text-muted-foreground-subtle"
                    aria-hidden
                >
                    <Radio className="size-3.5" />
                </span>
            )}

            <div className="min-w-0 flex-1 leading-tight">
                <p className="truncate text-xs font-medium">
                    {candidate.label}
                </p>
                {/* The label and the button already say what this is and what
                    pressing it does. This line exists only for the fact neither
                    carries: how long it runs, and its one distinguishing note. */}
                <p className="truncate text-2xs text-muted-foreground">
                    {durationSec ? (
                        <span className="font-mono tabular-nums">
                            {formatDuration(durationSec)}
                            {' · '}
                        </span>
                    ) : null}
                    {candidate.detail}
                </p>
                {airSource.isError && (
                    <p className="text-2xs text-destructive">
                        Not changed. {String(airSource.error)}
                    </p>
                )}
            </div>

            {/* Four different rows, four different truths about what a click means.
                Airing already: nothing to do. The slot is settled: the backend refuses
                any change, so offer none. Awaiting review: airing it requires a human to
                read the script, so the only honest action is to go and read it. Anything
                else: commit it here. */}
            {candidate.current ? (
                <span className="flex shrink-0 items-center gap-1 text-2xs text-success">
                    <Check className="size-3" />
                    Airing
                </span>
            ) : slotClosed ? (
                <span
                    className="shrink-0 text-2xs text-muted-foreground"
                    title={`This ${station} slot is already settled, so its air source cannot be changed.`}
                >
                    Slot closed
                </span>
            ) : candidate.kind === 'fresh' ? (
                <Button
                    size="sm"
                    className="h-7 shrink-0 px-2 text-2xs"
                    aria-label={`Review the ${station} bulletin`}
                    onClick={() => {
                        setActive(true)
                        onSelectJob(candidate.id)
                    }}
                >
                    Review
                    <ArrowRight className="ml-0.5 size-3" />
                </Button>
            ) : (
                <Button
                    variant="outline"
                    size="sm"
                    className={cn(
                        'h-7 shrink-0 px-2 text-2xs',
                        airSource.isPending && 'pointer-events-none'
                    )}
                    aria-label={`Air ${candidate.label} on ${station}`}
                    title={!slotJobId ? NO_SLOT_JOB_REASON : undefined}
                    disabled={!slotJobId || airSource.isPending}
                    onClick={commit}
                >
                    {airSource.isPending ? (
                        <Spinner className="size-3" />
                    ) : (
                        'Air this'
                    )}
                </Button>
            )}
        </li>
    )
}

/**
 * Everything that could air in one station's next slot, auditionable in place.
 *
 * The tray state is a status line, not an explanation. Zetta COLLECTS our WAV
 * from a drop folder, so "still there" means a change replaces what airs and
 * "collected" means it may already have gone out. Two states, two short
 * phrases; the operator learns them once, and a paragraph in a popover header
 * would only push the actual options below the fold.
 */
export function SlotAudition({
    row,
    onSelectJob,
    onCommitted,
}: {
    readonly row: AirPlanRow
    readonly onSelectJob: (jobId: string) => void
    readonly onCommitted: () => void
}) {
    const tray = usePlayoutTray(row.marketId)
    // ONE resolution for the id, the etag AND the status: they must come from the same
    // job, or the guard below can disagree with the request it is guarding.
    // Repeat candidates in row.job are display-only. The lifecycle endpoint
    // requires the bulletin generated for this slot as its target.
    const slotTarget = row.pending ?? row.slotJob
    const slotJobId = slotTarget?.jobId ?? null
    // SetAirSource applies a fallback to the SLOT's own job, and both Job.ApplyFallback and
    // Job.ApplyFillerFallback refuse Published and FallbackUsed. Offering "Air this" there was
    // a guaranteed rejection dressed as an action.
    const slotClosed =
        slotTarget?.status === 'PUBLISHED' ||
        slotTarget?.status === 'FALLBACK_USED'

    return (
        <div className="w-80">
            <div className="flex items-baseline justify-between gap-2 border-b border-border px-3 py-2">
                <p className="text-xs font-semibold">{row.station}</p>
                <p className="text-2xs text-muted-foreground">
                    {tray.data?.enabled === false
                        ? 'playout off'
                        : tray.data?.awaitingPickup
                          ? `in drop since ${tray.data.writtenAtLabel}`
                          : 'drop empty'}
                </p>
            </div>

            {row.candidates.length === 0 ? (
                <p className="px-3 py-3 text-2xs text-muted-foreground">
                    Nothing to air: no bulletin from today, no cart configured.
                </p>
            ) : (
                <>
                    {!slotJobId &&
                        row.candidates.some(
                            (candidate) =>
                                candidate.kind !== 'fresh' && !candidate.current
                        ) && (
                            <p className="border-b border-border/40 px-3 py-2 text-2xs text-muted-foreground">
                                {NO_SLOT_JOB_REASON}
                            </p>
                        )}
                    <ul>
                        {row.candidates.map((candidate) => (
                            <AuditionRow
                                key={candidate.id}
                                candidate={candidate}
                                slotJobId={slotJobId}
                                slotEtag={slotTarget?.etag ?? null}
                                slotClosed={slotClosed}
                                station={row.station}
                                onSelectJob={onSelectJob}
                                onCommitted={onCommitted}
                            />
                        ))}
                    </ul>
                </>
            )}
        </div>
    )
}
