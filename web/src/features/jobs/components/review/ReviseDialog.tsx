import { useState, useEffect, useRef } from 'react'
import { Headphones } from 'lucide-react'
import { formatDuration } from '@/lib/formatting'
import { InfoHint } from '@/components/InfoHint'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { StudioDialog, StudioFooter } from '@/components/studio/Studio'
import {
    Select,
    SelectContent,
    SelectGroup,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'
import { useVoiceProfiles } from '@/features/voices/api/voice-profile-hooks'
import { currentVersion } from '@/features/voices/lib/voice-profile-utils'
import { voiceModelLabel } from '@/features/voices/lib/model-labels'
import { SamplePlayButton } from '@/features/voices/components/SamplePlayButton'
import { ICON } from '@/lib/design/icons'
import { playSolo, claimSolo } from '@/lib/audio-solo'
import {
    auditionScript,
    type RejectDecision,
    type RejectionReasonCode,
} from '../../api/job-hooks'
import { stripTags } from '../../lib/tone-marks'
import { fitVerdictFor } from '../../lib/voice-fit'
import { hasScript, type Job } from '../../types'
import type { BulletinReview } from '../../hooks/use-bulletin-review'
import { ChangeChips } from './ChangeChips'

type FixRoute = 'pronunciation' | 'sources' | 'script'

/** Closed rejection taxonomy; fix routes steer correctable issues to the right tool. */
const REJECTION_REASONS: readonly {
    code: RejectionReasonCode
    label: string
    fixRoute?: FixRoute
}[] = [
    {
        code: 'Pronunciation',
        label: 'Pronunciation',
        fixRoute: 'pronunciation',
    },
    {
        code: 'WrongOrMissingIncident',
        label: 'Wrong or missing incident',
        fixRoute: 'sources',
    },
    {
        code: 'EditorialPriority',
        label: 'Important item was not prioritised',
        fixRoute: 'sources',
    },
    {
        code: 'ContentSelection',
        label: 'Wrong content included or omitted',
        fixRoute: 'sources',
    },
    {
        code: 'WordingTone',
        label: 'Unsuitable station wording or tone',
        fixRoute: 'script',
    },
    {
        code: 'ScriptStructure',
        label: 'Confusing or repetitive script',
        fixRoute: 'script',
    },
    { code: 'AudioUnusable', label: 'Audio is unusable' },
    {
        code: 'TimingLength',
        label: 'Not enough time or wrong length',
        fixRoute: 'script',
    },
]

/** Worst-case wall clock for a full regenerate, from the button's own estimate. */
const REGENERATE_ESTIMATE_SECS = 60
/** Worst-case wall clock for a re-voice only. */
const REVOICE_ESTIMATE_SECS = 15

interface ReviseDialogProps {
    readonly open: boolean
    readonly onOpenChange: (open: boolean) => void
    /** The bulletin under review — carries the current voice, slot target and
     *  script words the voice override section needs. */
    readonly job: Job
    readonly review: BulletinReview
    readonly onReject: (decision: RejectDecision) => Promise<void>
    readonly onRejectFallback: (decision: RejectDecision) => Promise<void>
    readonly isRejecting: boolean
    readonly isRejectingFallback: boolean
    /** Leave rejection and open the existing correction tool for this problem. */
    readonly onResolveIssue?: (route: FixRoute) => void
    /** Navigate to the pronunciation dictionary page with a suggested word pre-filled. */
    readonly onAddToDictionary?: (word: string) => void
    /**
     * The bulletin's binding deadline, so the dialog can compare its own
     * "~1 min" estimate against the time actually left. Without it the operator
     * was asked to choose between regenerate and fallback with the countdown
     * hidden behind the dialog.
     */
    readonly secondsToDeadline?: number | null
}

/**
 * The single decision surface for applying queued source, pronunciation and
 * voice changes. Wording changes happen in the script editor, where the
 * operator can edit directly or preview an AI-assisted rewrite before
 * re-voicing the exact text. The rare destructive path remains separate for
 * bulletins that must not air (fallback audio or outright rejection).
 */
export function ReviseDialog({
    open,
    onOpenChange,
    job,
    review,
    onReject,
    onRejectFallback,
    isRejecting,
    isRejectingFallback,
    onResolveIssue,
    onAddToDictionary,
    secondsToDeadline = null,
}: ReviseDialogProps) {
    const [killZoneOpen, setKillZoneOpen] = useState(false)
    const [killConfirmPending, setKillConfirmPending] = useState(false)
    const [reason, setReason] = useState<RejectionReasonCode | ''>('')
    const [notes, setNotes] = useState('')
    // Audition of the STAGED voice over the job's current script words — the
    // same POST the script editor uses, plus a duration verdict. Cleared when
    // the staged voice changes or the dialog closes so a verdict can never
    // outlive the choice it was measured on.
    const [auditioning, setAuditioning] = useState(false)
    const [auditionUrl, setAuditionUrl] = useState<string | null>(null)
    const auditionAudioRef = useRef<HTMLAudioElement>(null)
    useEffect(() => {
        const audio = auditionAudioRef.current
        if (audio) void playSolo(audio)
    }, [auditionUrl])
    const [auditionError, setAuditionError] = useState<string | null>(null)
    const [auditionDurationSec, setAuditionDurationSec] = useState<
        number | null
    >(null)
    // One-shot escalation: a voice-only batch applies as a re-voice, but a
    // failing fit verdict means the exact approved words physically cannot
    // fill the slot on the staged voice — the only real remedy is a rewrite.
    // The warning offers that escalation in place; it never happens silently
    // (a regenerate is ~4x slower and rewrites words the operator approved).
    const [escalateToRegenerate, setEscalateToRegenerate] = useState(false)

    const { data: voiceData, isLoading: voicesLoading } = useVoiceProfiles({
        enabled: open,
    })
    const activeProfiles = (voiceData?.profiles ?? []).filter(
        (p) => p.status === 'active'
    )

    // The current script WORDS (tags stripped, the same canonical text the
    // script editor's audition measures). A candidate voice read must be
    // judged on words alone: tone tags are v3-only, and a different model
    // could otherwise speak "[slow]" as a word instead of acting on it.
    // `script` is optional even on scripted statuses (a WAITING_APPROVAL job
    // can be mid-generation), so audition is gated on words actually existing.
    const scriptWords = hasScript(job)
        ? stripTags(job.script?.taggedText ?? job.script?.text ?? '')
        : ''

    // Resolve the staged profile for its sample clip (the version the operator
    // hears is the one the backend will resolve at apply — same pointer).
    const stagedProfile = review.voiceOverride
        ? activeProfiles.find((p) => p.id === review.voiceOverride?.profileId)
        : undefined
    const stagedVersion = stagedProfile ? currentVersion(stagedProfile) : null
    const busy = isRejecting || isRejectingFallback

    const selectedReason = REJECTION_REASONS.find(
        (item) => item.code === reason
    )
    const killReady = !busy

    const handleOpenChange = (next: boolean) => {
        if (!next) {
            setKillZoneOpen(false)
            setKillConfirmPending(false)
            setReason('')
            setNotes('')
            // Audition results are tied to a staged choice and a script that
            // may have changed since; don't let a stale verdict survive a
            // reopen.
            setAuditionUrl(null)
            setAuditionDurationSec(null)
            setAuditionError(null)
            // Escalation was a response to a verdict that no longer exists.
            setEscalateToRegenerate(false)
        }
        onOpenChange(next)
    }

    const handleVoiceChange = (profileId: string) => {
        const profile = activeProfiles.find((p) => p.id === profileId)
        if (!profile) return
        // Resolve the profile's CURRENT version for the chip's model label —
        // the same server-side resolution the backend will repeat at apply.
        const version = currentVersion(profile)
        review.setVoiceOverride({
            profileId: profile.id,
            name: profile.name,
            model: version.model,
        })
        // A different voice invalidates the previous audition's verdict.
        setAuditionUrl(null)
        setAuditionDurationSec(null)
        setAuditionError(null)
        // The verdict that justified escalating was measured on the previous
        // voice; a different voice may fit fine.
        setEscalateToRegenerate(false)
    }

    const runVoiceAudition = async () => {
        const override = review.voiceOverride
        const text = scriptWords.trim()
        if (!text || !override || busy) return
        setAuditioning(true)
        setAuditionError(null)
        try {
            const res = await auditionScript({
                jobId: job.jobId,
                text,
                voiceProfileId: override.profileId,
            })
            if (!res.url) throw new Error('No audio came back for that read.')
            setAuditionUrl(res.url)
            setAuditionDurationSec(res.durationSec)
        } catch (err) {
            // ApiError carries the backend's message (e.g. an unresolvable
            // profile id), so surface it verbatim rather than a bare "try
            // again" that would send the operator hunting.
            setAuditionError(
                err instanceof Error
                    ? err.message
                    : 'Could not audition that voice.'
            )
        } finally {
            setAuditioning(false)
        }
    }

    // Fit verdict for the most recent audition, if any. The backend's editing
    // guidance exposes word-count bounds and a target duration but no min/max
    // SECONDS band, so the fit band is the slot target ±10%.
    const verdict =
        auditionDurationSec !== null
            ? fitVerdictFor(auditionDurationSec, job.targetDurationSec)
            : null

    // Pronunciation-only batches need the audio made again, not the script
    // rewritten — a quarter of the wait, and the operator keeps the exact words
    // they already read and approved.
    const isRevoiceOnly =
        review.applyKind === 'revoice' && !escalateToRegenerate

    // The estimate on the primary button is worthless unless it can be compared
    // with the time left. If a regenerate cannot finish before the deadline, say
    // so where the operator is deciding, rather than letting them find out.
    const applyEstimateSecs = isRevoiceOnly
        ? REVOICE_ESTIMATE_SECS
        : REGENERATE_ESTIMATE_SECS
    const willMissDeadline =
        review.applyKind !== null &&
        secondsToDeadline !== null &&
        secondsToDeadline < applyEstimateSecs

    const handleApply = () => {
        review.regenerate(isRevoiceOnly)
        handleOpenChange(false)
    }

    const canApply = !busy && review.applyKind !== null

    const submitRejection = (useFallback: boolean) => {
        const trimmedNotes = notes.trim()
        const decision: RejectDecision = {
            ...(reason ? { reasonCode: reason } : {}),
            ...(trimmedNotes ? { notes: trimmedNotes } : {}),
        }
        void (useFallback ? onRejectFallback(decision) : onReject(decision))
    }
    const dialogDescription =
        review.applyKind === null
            ? 'Edit the script or adjust source information first. You can also change the voice here.'
            : isRevoiceOnly
              ? 'These changes keep the current script and make a new recording.'
              : 'These source changes require the bulletin to be rewritten before it is recorded.'
    const applyLabel =
        review.applyKind === null
            ? 'No changes to apply'
            : isRevoiceOnly
              ? 'Re-voice · ~15 sec'
              : 'Regenerate · ~1 min'

    return (
        <StudioDialog
            open={open}
            onOpenChange={handleOpenChange}
            title="Revise bulletin"
            description={dialogDescription}
            size="compact"
        >
            <div
                className="flex min-h-0 flex-1 flex-col"
                onKeyDown={(e) => {
                    // Keyboard parity with `R`-to-open: Cmd/Ctrl+Enter fires
                    // the primary action without reaching for the mouse.
                    if (
                        e.key === 'Enter' &&
                        (e.metaKey || e.ctrlKey) &&
                        canApply
                    ) {
                        e.preventDefault()
                        handleApply()
                    }
                }}
            >
                <div className="min-h-0 flex-1 overflow-y-auto p-5 md:p-6">
                    <div className="flex flex-col gap-6">
                        {/* Everything queued so far, from any tab */}
                        {review.changes.length > 0 ? (
                            <div className="flex flex-col gap-2">
                                <div className="flex items-center gap-1.5">
                                    <span className="text-xs font-semibold text-muted-foreground">
                                        Queued changes · {review.changes.length}
                                    </span>
                                    {review.changes.some(
                                        (c) => c.kind === 'corridors-curated'
                                    ) && (
                                        <InfoHint label="How do corridor picks apply?">
                                            Corridor picks apply to a fresh
                                            traffic check: roads that have
                                            cleared since drop out
                                            automatically; if every pick has
                                            cleared, standard ranking takes
                                            over.
                                        </InfoHint>
                                    )}
                                </div>
                                <ChangeChips
                                    changes={review.changes}
                                    onRemove={review.removeChange}
                                    onAddToDictionary={onAddToDictionary}
                                />
                            </div>
                        ) : (
                            <p className="text-xs text-muted-foreground">
                                No changes queued. Edit the script or adjust
                                source information first, or choose a different
                                voice below.
                            </p>
                        )}

                        {/* Voice override — re-voice THIS bulletin onto a
                            different profile, heard before committing. The
                            station schedule keeps its configured voice; only
                            the queued apply carries the override. */}
                        <div className="flex flex-col gap-3 border-t border-border pt-4">
                            <div className="flex items-center justify-between gap-2">
                                <span className="text-xs font-semibold text-muted-foreground">
                                    Voice
                                </span>
                                <span
                                    className="inline-flex min-w-0 items-center gap-1.5 text-xs text-muted-foreground"
                                    title={`Voice: ${job.input.voice.voiceName}`}
                                >
                                    <ICON.voice
                                        className="size-3 shrink-0 opacity-60"
                                        aria-hidden
                                    />
                                    <span className="truncate">
                                        {job.input.voice.voiceName}
                                    </span>
                                    <span
                                        className="shrink-0 rounded-full border bg-background/60 px-1.5 py-px text-2xs"
                                        title={job.input.voice.model}
                                    >
                                        {voiceModelLabel(job.input.voice.model)}
                                    </span>
                                </span>
                            </div>
                            <div className="flex flex-col gap-2">
                                <Label>Change to</Label>
                                <Select
                                    value={
                                        review.voiceOverride?.profileId ?? ''
                                    }
                                    onValueChange={handleVoiceChange}
                                    disabled={busy || voicesLoading}
                                >
                                    <SelectTrigger
                                        className="h-9"
                                        aria-label="Different voice"
                                    >
                                        <SelectValue placeholder="Keep current voice" />
                                    </SelectTrigger>
                                    <SelectContent>
                                        <SelectGroup>
                                            {activeProfiles.map((profile) => (
                                                <SelectItem
                                                    key={profile.id}
                                                    value={profile.id}
                                                >
                                                    {profile.name}
                                                    <span className="ml-1 text-muted-foreground">
                                                        ·{' '}
                                                        {profile.baseVoiceName}
                                                    </span>
                                                </SelectItem>
                                            ))}
                                        </SelectGroup>
                                    </SelectContent>
                                </Select>
                                {voicesLoading && (
                                    <p className="text-2xs text-muted-foreground">
                                        Loading voices…
                                    </p>
                                )}
                                <p className="text-2xs text-muted-foreground">
                                    Applies to this bulletin only - the station
                                    schedule keeps its configured voice.
                                </p>
                            </div>
                            {review.voiceOverride && (
                                <div className="flex flex-col gap-2 rounded-lg border bg-background/60 p-3">
                                    <div className="flex items-center gap-1.5">
                                        <span className="min-w-0 flex-1 truncate text-sm font-medium">
                                            {review.voiceOverride.name}
                                        </span>
                                        <span
                                            className="shrink-0 rounded-full border bg-background/60 px-1.5 py-px text-2xs"
                                            title={review.voiceOverride.model}
                                        >
                                            {voiceModelLabel(
                                                review.voiceOverride.model
                                            )}
                                        </span>
                                        {stagedVersion?.sampleUrl && (
                                            <SamplePlayButton
                                                url={stagedVersion.sampleUrl}
                                                size="sm"
                                                variant="ghost"
                                                label="Sample"
                                            />
                                        )}
                                    </div>
                                    <div className="flex items-center gap-2">
                                        <Button
                                            variant="outline"
                                            size="sm"
                                            onClick={runVoiceAudition}
                                            disabled={
                                                auditioning ||
                                                busy ||
                                                !scriptWords.trim()
                                            }
                                        >
                                            {auditioning ? (
                                                <Spinner data-icon="inline-start" />
                                            ) : (
                                                <Headphones data-icon="inline-start" />
                                            )}
                                            {auditioning
                                                ? 'Auditioning…'
                                                : 'Audition with this voice'}
                                        </Button>
                                    </div>
                                    {auditionUrl && (
                                        <audio
                                            key={auditionUrl}
                                            ref={auditionAudioRef}
                                            controls
                                            preload="metadata"
                                            src={auditionUrl}
                                            className="h-8 w-full"
                                            aria-label="Audition of the staged voice"
                                            onPlay={(event) =>
                                                claimSolo(event.currentTarget)
                                            }
                                        />
                                    )}
                                    {auditionError && (
                                        <p className="text-2xs text-destructive">
                                            {auditionError}
                                        </p>
                                    )}
                                    {verdict && (
                                        <div className="flex flex-col gap-1">
                                            {verdict.fits ? (
                                                <p className="text-2xs text-success">
                                                    Fits the slot at{' '}
                                                    {Math.round(
                                                        verdict.durationSec
                                                    )}
                                                    s
                                                </p>
                                            ) : (
                                                <>
                                                    <p className="text-2xs text-warning">
                                                        {Math.round(
                                                            verdict.durationSec
                                                        )}
                                                        s vs{' '}
                                                        {Math.round(
                                                            verdict.targetSec
                                                        )}
                                                        s slot. Re-voice can't
                                                        stretch this far;
                                                        Regenerate will rewrite
                                                        to fit.
                                                    </p>
                                                    {review.applyKind ===
                                                        'revoice' &&
                                                        (escalateToRegenerate ? (
                                                            <div className="flex flex-wrap items-center gap-2">
                                                                <p className="text-2xs text-muted-foreground">
                                                                    This apply
                                                                    will
                                                                    regenerate:
                                                                    the words
                                                                    are
                                                                    rewritten to
                                                                    fit the
                                                                    slot.
                                                                </p>
                                                                <Button
                                                                    variant="ghost"
                                                                    size="sm"
                                                                    className="h-6 px-2 text-2xs"
                                                                    onClick={() =>
                                                                        setEscalateToRegenerate(
                                                                            false
                                                                        )
                                                                    }
                                                                >
                                                                    Keep
                                                                    re-voice
                                                                </Button>
                                                            </div>
                                                        ) : (
                                                            <div className="flex flex-wrap items-center gap-2">
                                                                <p className="text-2xs text-warning">
                                                                    This batch
                                                                    is queued to
                                                                    re-voice
                                                                    only, so the
                                                                    end of the
                                                                    read would
                                                                    be trimmed.
                                                                </p>
                                                                <Button
                                                                    variant="outline"
                                                                    size="sm"
                                                                    className="h-6 px-2 text-2xs"
                                                                    onClick={() =>
                                                                        setEscalateToRegenerate(
                                                                            true
                                                                        )
                                                                    }
                                                                >
                                                                    Regenerate
                                                                    instead
                                                                </Button>
                                                            </div>
                                                        ))}
                                                </>
                                            )}
                                        </div>
                                    )}
                                </div>
                            )}
                        </div>

                        {/* Destructive zone — separated from category pills,
                            red by default, requires confirmation before
                            expanding so a stray tap can't reach it. */}
                        <div className="border-t border-destructive/20 pt-4">
                            {killConfirmPending && !killZoneOpen ? (
                                <div className="flex flex-col gap-2">
                                    <p className="text-xs font-medium text-destructive-on-tint">
                                        This will reject the bulletin. Are you
                                        sure?
                                    </p>
                                    <div className="flex gap-2">
                                        <Button
                                            variant="destructive"
                                            size="sm"
                                            onClick={() => {
                                                setKillZoneOpen(true)
                                                setKillConfirmPending(false)
                                            }}
                                        >
                                            Yes, reject
                                        </Button>
                                        <Button
                                            variant="ghost"
                                            size="sm"
                                            onClick={() =>
                                                setKillConfirmPending(false)
                                            }
                                        >
                                            Cancel
                                        </Button>
                                    </div>
                                </div>
                            ) : (
                                <button
                                    type="button"
                                    onClick={() => {
                                        if (killZoneOpen) {
                                            setKillZoneOpen(false)
                                        } else {
                                            setKillConfirmPending(true)
                                        }
                                    }}
                                    aria-expanded={killZoneOpen}
                                    className="inline-flex items-center gap-1.5 text-xs font-medium text-destructive-on-tint transition-colors hover:text-destructive-on-tint"
                                >
                                    <ICON.expand
                                        className={cn(
                                            'size-3.5 transition-transform duration-150 motion-reduce:transition-none',
                                            !killZoneOpen && '-rotate-90'
                                        )}
                                        aria-hidden
                                    />
                                    This bulletin must not air
                                </button>
                            )}

                            {killZoneOpen && (
                                <div className="mt-3 flex flex-col gap-3 rounded-lg border border-destructive/30 bg-destructive/5 p-3">
                                    <div className="flex flex-col gap-2">
                                        <Label>
                                            Reason{' '}
                                            <span className="font-normal text-muted-foreground">
                                                (optional)
                                            </span>
                                        </Label>
                                        <Select
                                            value={reason}
                                            onValueChange={(value) =>
                                                setReason(
                                                    value as RejectionReasonCode
                                                )
                                            }
                                            disabled={busy}
                                        >
                                            <SelectTrigger
                                                className="h-9"
                                                aria-label="Reason"
                                            >
                                                <SelectValue placeholder="Select a reason..." />
                                            </SelectTrigger>
                                            <SelectContent>
                                                <SelectGroup>
                                                    {REJECTION_REASONS.map(
                                                        (item) => (
                                                            <SelectItem
                                                                key={item.code}
                                                                value={
                                                                    item.code
                                                                }
                                                            >
                                                                {item.label}
                                                            </SelectItem>
                                                        )
                                                    )}
                                                </SelectGroup>
                                            </SelectContent>
                                        </Select>
                                    </div>

                                    {selectedReason?.fixRoute &&
                                        onResolveIssue && (
                                            <div className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-warning/30 bg-warning/5 p-2.5">
                                                <p className="text-xs text-muted-foreground">
                                                    Fix this directly instead of
                                                    rejecting.
                                                </p>
                                                <Button
                                                    type="button"
                                                    variant="outline"
                                                    size="sm"
                                                    onClick={() => {
                                                        const route =
                                                            selectedReason.fixRoute
                                                        handleOpenChange(false)
                                                        if (route)
                                                            onResolveIssue(
                                                                route
                                                            )
                                                    }}
                                                >
                                                    {selectedReason.fixRoute ===
                                                    'pronunciation'
                                                        ? 'Fix pronunciation'
                                                        : selectedReason.fixRoute ===
                                                            'sources'
                                                          ? 'Adjust sources'
                                                          : 'Edit script'}
                                                </Button>
                                            </div>
                                        )}

                                    <div className="flex flex-col gap-2">
                                        <Label>
                                            Notes{' '}
                                            <span className="font-normal text-muted-foreground">
                                                (optional)
                                            </span>
                                        </Label>
                                        <Textarea
                                            value={notes}
                                            onChange={(event) =>
                                                setNotes(event.target.value)
                                            }
                                            rows={3}
                                            maxLength={1000}
                                            disabled={busy}
                                            placeholder="Why can this bulletin not air?"
                                        />
                                    </div>

                                    <p className="flex items-start gap-1.5 text-2xs text-muted-foreground">
                                        <span>
                                            <span className="font-medium text-foreground">
                                                Use fallback audio
                                            </span>{' '}
                                            airs the standby bulletin;{' '}
                                            <span className="font-medium text-foreground">
                                                Reject
                                            </span>{' '}
                                            drops this bulletin.
                                        </span>
                                        <Tooltip>
                                            <TooltipTrigger asChild>
                                                <span
                                                    tabIndex={0}
                                                    className="inline-flex shrink-0 translate-y-px hover:text-foreground"
                                                >
                                                    <ICON.info
                                                        className="size-3"
                                                        aria-hidden
                                                    />
                                                </span>
                                            </TooltipTrigger>
                                            <TooltipContent side="top">
                                                Nothing airs unless a new one is
                                                generated in time.
                                            </TooltipContent>
                                        </Tooltip>
                                    </p>
                                    <div className="flex flex-wrap justify-end gap-2">
                                        <Button
                                            variant="outline"
                                            size="sm"
                                            onClick={() =>
                                                submitRejection(true)
                                            }
                                            disabled={!killReady}
                                        >
                                            {isRejectingFallback && (
                                                <Spinner data-icon="inline-start" />
                                            )}
                                            <ICON.rejectToFallback data-icon="inline-start" />
                                            {isRejectingFallback
                                                ? 'Applying fallback…'
                                                : 'Use fallback audio'}
                                        </Button>
                                        <Button
                                            variant="destructive"
                                            size="sm"
                                            onClick={() =>
                                                submitRejection(false)
                                            }
                                            disabled={!killReady}
                                        >
                                            {isRejecting && (
                                                <Spinner data-icon="inline-start" />
                                            )}
                                            <ICON.reject data-icon="inline-start" />
                                            {isRejecting
                                                ? 'Rejecting bulletin…'
                                                : 'Reject bulletin'}
                                        </Button>
                                    </div>
                                </div>
                            )}
                        </div>
                    </div>
                </div>
                <StudioFooter>
                    {willMissDeadline && (
                        <p className="flex items-center gap-1.5 text-xs text-warning">
                            <ICON.warning
                                className="size-3.5 shrink-0"
                                aria-hidden
                            />
                            {secondsToDeadline !== null && secondsToDeadline < 0
                                ? 'The slot has passed. The standby bulletin covers it; a fresh one airs late.'
                                : `Only ${formatDuration(Math.max(secondsToDeadline ?? 0, 0))} left, so this will not be ready in time.`}
                        </p>
                    )}
                    <Button
                        className={willMissDeadline ? '' : 'ml-auto'}
                        disabled={!canApply}
                        onClick={handleApply}
                    >
                        {review.isRegenerating ? (
                            <Spinner data-icon="inline-start" />
                        ) : (
                            <ICON.regenerate data-icon="inline-start" />
                        )}
                        {applyLabel}
                        {review.applyKind !== null && (
                            <kbd
                                className="pointer-coarse:hidden ml-1.5 rounded border border-primary-foreground/30 bg-primary-foreground/10 px-1 font-mono text-3xs font-normal"
                                title="Cmd/Ctrl + Enter"
                            >
                                ⌘↵
                            </kbd>
                        )}
                    </Button>
                </StudioFooter>
            </div>
        </StudioDialog>
    )
}
