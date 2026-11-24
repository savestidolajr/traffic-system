import { useRef, useState, useCallback, useMemo } from 'react'
import { Loader2 } from 'lucide-react'
import type { Job, BulletinEditingGuidance } from '../../types'
import { hasAudio, hasScript, hasTraffic, isTerminal } from '../../types'
import { SCRIPT_STATUSES } from '../../config/status-values'
import { Placeholder } from '@/components/Placeholder'
import {
    AudioWaveformPlayer,
    type AudioWaveformPlayerHandle,
} from '../AudioWaveformPlayer'
import { AudioSeekContext } from '../canvas/use-audio-seek'
import { ReviewScript } from './ReviewScript'
import { AudioPreReviewBadge } from './AudioPreReviewBadge'
import type { SentenceStatus } from '../ScriptRenderer'
import { useWordTimings } from '../../hooks/use-word-timings'
import type {
    ProvenanceSourceKind,
    ScriptSentence,
} from '../../lib/verification/provenance'
import { buildSentenceStatuses } from '../../lib/verification/sentence-status'
import { useFillerStatements } from '@/features/schedules/api/filler-statements-hooks'
import { usePronunciationDictionaries } from '@/features/pronunciation/api/pronunciation-hooks'
import { findPronunciationMatches } from '../../lib/pronunciation-matches'
import { usePronunciationVerdicts } from '../../hooks/use-pronunciation-verdicts'
import {
    buildScriptMarks,
    countUnresolved,
    type PendingPronunciation,
} from '../../lib/script-marks'
import { padTags, resolveAiredRead } from '../../lib/tone-marks'

interface ReviewBulletinProps {
    readonly job: Job
    readonly canEdit: boolean
    readonly onRevoice: (text: string) => void
    readonly isRevoicing: boolean
    readonly editingGuidance?: BulletinEditingGuidance
    /** Sentence structure for karaoke sync and provenance display. */
    readonly sentences?: readonly ScriptSentence[]
    /** Per-sentence incident lookups and source kinds (parallel with sentences). */
    readonly bySentence?: ReadonlyArray<{
        readonly incidentKeys: readonly string[]
        readonly flags?: readonly {
            readonly kind: string
            readonly note: string
        }[]
        readonly matchedBy?: 'road-token' | 'model'
        readonly sourceKinds?: readonly ProvenanceSourceKind[]
    }>
    /** Callback when the active playback sentence changes. */
    readonly onActiveSentenceChange?: (index: number | null) => void
    /** Callback when user hovers a sentence in the script. */
    readonly onSentenceHover?: (index: number | null) => void
    /** Sentence indexes lit by hovering an incident in the evidence index. */
    readonly litSentenceIndexes?: readonly number[]
    /** Lineage alignment failed — heuristic matching, contradiction checks off. */
    readonly verificationDegraded?: boolean
    /** Queues a saved dictionary rule as a pending change on this bulletin. */
    readonly onPronunciationEdit?: (word: string, replacement: string) => void
    /** Rules saved during this review, not yet applied to this bulletin. */
    readonly pendingPronunciation?: readonly PendingPronunciation[]
}
/**
 * The bulletin itself: audio player (sticky at top) and editable script,
 * sharing one seek context so clicking a (timed) word jumps the audio.
 * Karaoke-style playback sync highlights the current word and sentence.
 */
export function ReviewBulletin({
    job,
    canEdit,
    onRevoice,
    isRevoicing,
    editingGuidance,
    sentences,
    bySentence,
    onActiveSentenceChange,
    onSentenceHover,
    litSentenceIndexes,
    verificationDegraded,
    onPronunciationEdit,
    pendingPronunciation,
}: ReviewBulletinProps) {
    const playerRef = useRef<AudioWaveformPlayerHandle>(null)
    const seek = { seekTo: (ms: number) => playerRef.current?.seekTo(ms) }

    const audioReady = hasAudio(job) && job.audio
    const synthesizing = (SCRIPT_STATUSES as readonly string[]).includes(
        job.status
    )
    const scriptText = hasScript(job) ? (job.script?.text ?? '') : ''
    const taggedText = hasScript(job) ? (job.script?.taggedText ?? null) : null
    // Every mark is an offset pair, and offsets only mean something against the
    // text they were resolved in. The script renders the AIRED read, so marks
    // are resolved there too: tags kept (their characters shift everything
    // after them) but their labels blanked, so no rule can match inside one.
    // Resolving against the untagged script instead put a `[brightly]` opener's
    // 11 characters between every later match and its word — the KIIS mark
    // landed on "slow", the Warringah mark on "underway on the" (dev job
    // 17c0b28e, 2026-08-11).
    const markText = padTags(resolveAiredRead(scriptText, taggedText))
    const measuredSec = hasAudio(job)
        ? job.audio?.measuredDurationSec
        : undefined
    const durSec = measuredSec ? Math.round(measuredSec) : job.targetDurationSec
    const wordTimings = useWordTimings(
        scriptText,
        durSec,
        hasAudio(job) ? job.audio?.wordTimings : undefined
    )

    // Pronunciation: resolve the exact historical dictionary version used to
    // synthesise this bulletin's audio (never the dictionary's *current*
    // rules) so the operator can see what the voice actually said on air.
    const { data: pronunciationDictionariesData } =
        usePronunciationDictionaries()
    const pronunciationDictionaries = useMemo(
        () => pronunciationDictionariesData?.items ?? [],
        [pronunciationDictionariesData]
    )
    const pronunciationMatch = useMemo(
        () =>
            findPronunciationMatches(
                markText,
                job.pronunciationDictionaryId,
                job.pronunciationVersionId,
                job.input.voice.model,
                pronunciationDictionaries
            ),
        [
            markText,
            job.pronunciationDictionaryId,
            job.pronunciationVersionId,
            job.input.voice.model,
            pronunciationDictionaries,
        ]
    )
    const pronunciationContext = useMemo(
        () => ({
            providerDictionaryId: job.pronunciationDictionaryId ?? null,
            providerVersionId: job.pronunciationVersionId ?? null,
            voiceModel: job.input.voice.model,
            dictionaries: pronunciationDictionaries,
        }),
        [
            job.pronunciationDictionaryId,
            job.pronunciationVersionId,
            job.input.voice.model,
            pronunciationDictionaries,
        ]
    )

    // The two verdicts an operator can record against a flagged pronunciation:
    // "sounded fine" (a standing per-market suppression) and "say it this way"
    // (an alias rule on the dictionary this bulletin aired with).
    const airedDictionary = useMemo(
        () =>
            pronunciationDictionaries.find(
                (d) => d.id === pronunciationMatch.libraryId
            ) ?? null,
        [pronunciationDictionaries, pronunciationMatch.libraryId]
    )
    const verdicts = usePronunciationVerdicts(
        job.jobId,
        job.market,
        airedDictionary,
        // A terminal bulletin cannot be re-voiced, so a pending change would be
        // a chip with no way to apply it. The dictionary edit still lands and
        // helps the next bulletin; the toast says exactly that.
        canEdit ? onPronunciationEdit : undefined
    )

    // Counted from the SAME marks the script renders, so the status line can
    // never disagree with what is highlighted below it.
    const { markCounts, unlocatedFlags } = useMemo(() => {
        const built = buildScriptMarks(
            markText,
            pronunciationMatch,
            job.preReview?.suspectWords ?? [],
            job.preReview?.numberMismatches ?? [],
            verdicts.verified
        )
        return {
            markCounts: countUnresolved(built.marks),
            unlocatedFlags: [
                ...built.unlocatedWords,
                ...built.unlocatedFigures,
            ],
        }
    }, [markText, pronunciationMatch, job.preReview, verdicts.verified])

    // Verification state per sentence, for the teleprompter gutter.
    // Terminal bulletins are a record, not a review: the decision already
    // happened, so "verify manually" chrome would be noise — no dots.
    // Configured filler statements are fetched for identity matching — a
    // sentence that IS a configured filler is editorial content, nothing to
    // verify. Fail-soft: if the query errors (or the caller lacks access),
    // filler sentences simply stay amber.
    const wantStatuses = Boolean(sentences && bySentence && !isTerminal(job))
    const { data: fillerStatements } = useFillerStatements(
        job.market,
        wantStatuses
    )
    const sentenceStatus = useMemo<SentenceStatus[] | undefined>(() => {
        if (!sentences || !bySentence || isTerminal(job)) return undefined
        return buildSentenceStatuses(sentences, bySentence, {
            fillerStatementTexts: fillerStatements?.statements.map(
                (s) => s.text
            ),
            hasGarnish:
                job.content?.weather === true || job.content?.fuel === true,
            hasWeatherData: job.weather != null,
            hasFuelData: (job.fuel?.summaries?.length ?? 0) > 0,
            goodRunRoads: job.goodRuns,
            corridorRoadNames: hasTraffic(job)
                ? job.traffic.corridors.map((c) => c.roadName)
                : undefined,
        })
    }, [sentences, bySentence, fillerStatements, job])
    // Throttled karaoke state: only update when active word/sentence changes
    const [currentMs, setCurrentMs] = useState<number | null>(null)
    const lastActiveRef = useRef<{
        wordIdx: number
        sentenceIdx: number
    } | null>(null)

    const handleTimeUpdate = useCallback(
        (ms: number) => {
            if (!wordTimings) return
            // Find the active word index (latest startMs ≤ ms)
            let activeWordIdx = -1
            for (let i = 0; i < wordTimings.length; i++) {
                if (wordTimings[i].startMs <= ms) activeWordIdx = i
                else break
            }
            // Find the sentence containing this word
            let activeSentenceIdx = -1
            if (activeWordIdx >= 0 && sentences) {
                for (let i = 0; i < sentences.length; i++) {
                    if (
                        activeWordIdx >= sentences[i].startWord &&
                        activeWordIdx < sentences[i].endWord
                    ) {
                        activeSentenceIdx = i
                        break
                    }
                }
            }
            const last = lastActiveRef.current
            if (
                !last ||
                last.wordIdx !== activeWordIdx ||
                last.sentenceIdx !== activeSentenceIdx
            ) {
                lastActiveRef.current = {
                    wordIdx: activeWordIdx,
                    sentenceIdx: activeSentenceIdx,
                }
                setCurrentMs(ms)
                onActiveSentenceChange?.(
                    activeSentenceIdx >= 0 ? activeSentenceIdx : null
                )
            }
        },
        [wordTimings, sentences, onActiveSentenceChange]
    )

    // Derive active word ms from currentMs
    const activeWordMs = useMemo<number | null>(() => {
        if (currentMs == null || !wordTimings) return null
        let lastTiming: number | null = null
        for (const t of wordTimings) {
            if (t.startMs <= currentMs) lastTiming = t.startMs
            else break
        }
        return lastTiming
    }, [currentMs, wordTimings])

    // Derive active sentence word range from currentMs
    const activeSentenceWordRange = useMemo<
        { startWord: number; endWord: number } | undefined
    >(() => {
        if (currentMs == null || !wordTimings || !sentences) return undefined
        let activeWordIdx = -1
        for (let i = 0; i < wordTimings.length; i++) {
            if (wordTimings[i].startMs <= currentMs) activeWordIdx = i
            else break
        }
        if (activeWordIdx < 0) return undefined
        const sent = sentences.find(
            (s) => activeWordIdx >= s.startWord && activeWordIdx < s.endWord
        )
        return sent
            ? { startWord: sent.startWord, endWord: sent.endWord }
            : undefined
    }, [currentMs, wordTimings, sentences])

    return (
        <AudioSeekContext.Provider value={seek}>
            <div className="flex flex-col gap-6">
                {/* Audio player pinned to the top of the card. The card
                    (SpotlightCard) is `overflow-hidden`, which scopes any
                    sticky to the card itself — so `top` is measured from the
                    card's edge, not the pane header. It must stay `top-0`:
                    a larger offset (e.g. top-24) just pushes the player down,
                    leaving a phantom gap above it. */}
                {audioReady ? (
                    <div className="sticky top-0 z-10 -mx-5 -mt-5 rounded-t-xl border-b border-border/40 bg-card px-5 pb-3 pt-2">
                        <AudioWaveformPlayer
                            ref={playerRef}
                            url={job.audio.data.url}
                            duration={job.audio.data.durationSec}
                            preload="auto"
                            onTimeUpdate={handleTimeUpdate}
                        />
                        <AudioPreReviewBadge
                            preReview={job.preReview}
                            suspectCount={markCounts.suspect}
                            verifiedCount={markCounts.verified}
                            figureCount={markCounts.figures}
                            unlocated={unlocatedFlags}
                        />
                    </div>
                ) : synthesizing ? (
                    <Placeholder
                        icon={Loader2}
                        message="Synthesizing audio narrative…"
                        detail="This usually takes 30-60 seconds."
                        variant="loading"
                    />
                ) : null}

                {scriptText ? (
                    <ReviewScript
                        jobId={job.jobId}
                        text={scriptText}
                        canEdit={canEdit}
                        // The resolved model decides whether tone controls exist
                        // at all: a non-v3 voice speaks tags, so it gets none.
                        toneCapability={{ model: job.input.voice.model }}
                        onRevoice={onRevoice}
                        isRevoicing={isRevoicing}
                        editingGuidance={editingGuidance}
                        wordTimings={wordTimings}
                        onWordSeek={seek.seekTo}
                        activeWordMs={activeWordMs}
                        activeSentenceWordRange={activeSentenceWordRange}
                        sentenceStatus={sentenceStatus}
                        litSentenceIndexes={litSentenceIndexes}
                        sentences={sentences}
                        bySentence={bySentence}
                        onSentenceHover={onSentenceHover}
                        verificationDegraded={verificationDegraded}
                        pronunciationMatch={pronunciationMatch}
                        pronunciationContext={pronunciationContext}
                        suspectWords={job.preReview?.suspectWords}
                        numberMismatches={job.preReview?.numberMismatches}
                        verifiedPronunciations={verdicts.verified}
                        pendingPronunciation={pendingPronunciation}
                        markActions={
                            verdicts.canManage
                                ? {
                                      onVerify: verdicts.verify,
                                      onUnverify: verdicts.unverify,
                                      onAddRule: verdicts.addRule,
                                  }
                                : undefined
                        }
                        markDictionaryName={airedDictionary?.name ?? null}
                        roadPhraseSpans={
                            hasScript(job)
                                ? (job.script?.roadPhraseSpans ?? null)
                                : null
                        }
                        taggedText={taggedText}
                    />
                ) : (
                    <p className="text-sm text-muted-foreground">
                        Script not generated yet.
                    </p>
                )}
            </div>
        </AudioSeekContext.Provider>
    )
}
