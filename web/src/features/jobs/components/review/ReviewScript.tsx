import {
    useState,
    useCallback,
    useEffect,
    useLayoutEffect,
    useRef,
    useMemo,
} from 'react'
import { createPortal } from 'react-dom'
import { BookOpenText, AudioLines, Headphones } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { cn } from '@/lib/utils'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import {
    ScriptRenderer,
    type WordTiming,
    type SentenceStatus,
    type SentenceVerdict,
} from '../ScriptRenderer'
import { ICON } from '@/lib/design/icons'
import { InfoHint } from '@/components/InfoHint'
import {
    countVerdicts,
    nextSentenceOfVerdict,
} from '../../lib/verification/sentence-status'
import { isScriptTooLongToFit } from './review-script-utils'
import type {
    ProvenanceSourceKind,
    ScriptSentence,
} from '../../lib/verification/provenance'
import type { BulletinEditingGuidance, ScriptRoadPhrase } from '../../types'
import {
    findPronunciationMatches,
    type PronunciationMatchResult,
} from '../../lib/pronunciation-matches'
import type { Dictionary } from '@/features/pronunciation/types'
import type { ScriptMarkActions } from '../ScriptWordMark'
import {
    buildScriptMarks,
    type ScriptMark,
    type PendingPronunciation,
} from '../../lib/script-marks'
import type { VerifiedPronunciationEntry } from '@/features/pronunciation/api/verified-pronunciation-hooks'
import { PronunciationScriptEditor } from './PronunciationScriptEditor'
import {
    canonicalizeEditedScript,
    formatScriptForEditing,
} from './script-editor-text'
import { TonePalette, type ToneCapability } from './TonePalette'
import { ScriptAssist } from './ScriptAssist'
import { auditionScript } from '../../api/job-hooks'
import { useRecentTones } from '../../hooks/use-recent-tones'
import { playSolo, claimSolo } from '@/lib/audio-solo'
import {
    modelSupportsTags,
    padTags,
    resolveAiredRead,
    stripTags,
    TONE_TAG_PATTERN,
} from '../../lib/tone-marks'

/** Dictionary-only marks for the edit draft — the pre-review describes the
 *  AIRED audio, so it says nothing about text the operator is still typing. */
function buildDraftMarks(
    text: string,
    match: PronunciationMatchResult | null | undefined
): readonly ScriptMark[] {
    return buildScriptMarks(text, match, [], [], []).marks
}

/** Context needed to recompute exact-version pronunciation matches against
 *  an edit draft — never the dictionary's current rules. */
export interface PronunciationDraftContext {
    readonly providerDictionaryId: string | null
    readonly providerVersionId: string | null
    readonly voiceModel: string
    readonly dictionaries: readonly Dictionary[]
}

interface ReviewScriptProps {
    /** Job the draft belongs to — required to audition it. */
    readonly jobId?: string
    readonly text: string
    readonly canEdit: boolean
    /** Apply the revision: the approved words, plus the tagged read when tones are set. */
    readonly onRevoice: (text: string, tonedScript?: string) => void
    readonly isRevoicing: boolean
    readonly editingGuidance?: BulletinEditingGuidance
    readonly wordTimings?: WordTiming[]
    /** Seek audio to a timed word selected in the read-only script. */
    readonly onWordSeek?: (startMs: number) => void
    /** Karaoke: word ms for subtle primary-text highlight. */
    readonly activeWordMs?: number | null
    /** Word-index range of the current sentence (karaoke background). */
    readonly activeSentenceWordRange?: {
        readonly startWord: number
        readonly endWord: number
    }
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
    readonly sentences?: readonly ScriptSentence[]
    /** Per-sentence verification state for the teleprompter gutter. */
    readonly sentenceStatus?: readonly SentenceStatus[]
    /** Sentence indexes lit by hovering an incident in the evidence index. */
    readonly litSentenceIndexes?: readonly number[]
    readonly onSentenceHover?: (index: number | null) => void
    /** Lineage alignment failed — heuristic matching, contradiction checks off. */
    readonly verificationDegraded?: boolean
    /** Exact on-air pronunciation matches for the saved script text. */
    readonly pronunciationMatch?: PronunciationMatchResult
    /** Context to recompute exact-version matches against the edit draft. */
    readonly pronunciationContext?: PronunciationDraftContext
    /** Words the transcript of the AIRED audio couldn't match, marked in situ. */
    readonly suspectWords?: readonly string[]
    /** Aired quantities the transcript couldn't confirm, marked in situ. */
    readonly numberMismatches?: readonly string[]
    /** Standing operator verdicts that downgrade a suspect word to verified. */
    readonly verifiedPronunciations?: readonly VerifiedPronunciationEntry[]
    /** Rules saved during this review, not yet applied to this bulletin. */
    readonly pendingPronunciation?: readonly PendingPronunciation[]
    /** Verdict actions offered in a mark's popover. Omit for read-only viewers. */
    readonly markActions?: ScriptMarkActions
    /** Dictionary attached to this bulletin; enables the respelling action. */
    readonly markDictionaryName?: string | null
    /** Server-resolved road phrase ranges in the rendered script coordinate space. */
    readonly roadPhraseSpans?: readonly ScriptRoadPhrase[] | null
    /** Resolved voice/model facts that decide whether tone can land at all. */
    readonly toneCapability: ToneCapability
    /** The stored tagged read (`job.script.taggedText`): seeds the editor and
     *  renders tags inline in read mode. */
    readonly taggedText?: string | null
}
type SourceKind = ProvenanceSourceKind

const SOURCE_KIND_META: Record<
    SourceKind,
    {
        readonly label: string
        readonly ariaLabel: string
        readonly className: string
    }
> = {
    traffic: {
        label: 'Traffic',
        ariaLabel: 'Source: incident traffic',
        className: 'border-primary/25 bg-primary/5 text-primary-on-tint',
    },
    route: {
        label: 'Route',
        ariaLabel: 'Source: monitored route',
        className: 'border-info/25 bg-info/5 text-info',
    },
    'good-run': {
        label: 'Good run',
        ariaLabel: 'Source: good run',
        className: 'border-success/25 bg-success/5 text-success',
    },
    weather: {
        label: 'Weather',
        ariaLabel: 'Source: weather',
        className: 'border-warning/25 bg-warning/5 text-warning',
    },
    fuel: {
        label: 'Fuel',
        ariaLabel: 'Source: fuel',
        className:
            'border-status-approved/25 bg-status-approved/5 text-status-approved',
    },
}

interface SentenceSourceTarget {
    readonly index: number
    readonly target: HTMLElement
    readonly kinds: readonly SourceKind[]
}

interface SentenceSourceLabelsProps {
    readonly bySentence?: ReviewScriptProps['bySentence']
    readonly text: string
    readonly sentenceCount: number
    readonly children: React.ReactNode
}

/**
 * Adds source labels to the renderer's existing sentence lines without
 * reimplementing its word walker. The labels are portals into the line's
 * content span, so karaoke, seek and hover event delegation stay untouched.
 * Empty or absent sourceKinds intentionally produce no portal.
 */
function SentenceSourceLabels({
    bySentence,
    text,
    sentenceCount,
    children,
}: SentenceSourceLabelsProps) {
    const rootRef = useRef<HTMLDivElement>(null)
    const [targets, setTargets] = useState<readonly SentenceSourceTarget[]>([])
    const targetsRef = useRef(targets)

    useLayoutEffect(() => {
        const root = rootRef.current
        if (!root) return

        const next: SentenceSourceTarget[] = []
        bySentence?.slice(0, sentenceCount).forEach((entry, index) => {
            const kinds = (entry.sourceKinds ?? []).filter(
                (kind, kindIndex, all): kind is SourceKind =>
                    kind in SOURCE_KIND_META && all.indexOf(kind) === kindIndex
            )
            const line = root.querySelector<HTMLElement>(
                `[data-sentence-index="${index}"]`
            )
            const target = line
                ? Array.from(line.children).find(
                      (child): child is HTMLElement =>
                          child instanceof HTMLElement &&
                          child.classList.contains('min-w-0')
                  )
                : undefined
            if (target) next.push({ index, target, kinds })
        })

        const previous = targetsRef.current
        const changed =
            next.length !== previous.length ||
            next.some(
                (entry, index) =>
                    entry.index !== previous[index]?.index ||
                    entry.target !== previous[index]?.target ||
                    entry.kinds.join('|') !== previous[index]?.kinds.join('|')
            )
        if (changed) {
            targetsRef.current = next
            setTargets(next)
        }
    }, [bySentence, text, sentenceCount])

    return (
        <div ref={rootRef} className="contents">
            {children}
            {targets.flatMap(({ index, target, kinds }) =>
                kinds.map((kind) => {
                    const meta = SOURCE_KIND_META[kind]
                    return createPortal(
                        <span
                            key={`${index}-${kind}`}
                            role="img"
                            aria-label={meta.ariaLabel}
                            title={meta.ariaLabel}
                            data-source-kind={kind}
                            className={cn(
                                'pointer-events-none ml-1 inline-flex shrink-0 items-center whitespace-nowrap rounded border px-1 py-px align-middle font-sans text-2xs font-medium leading-tight',
                                meta.className
                            )}
                        >
                            {meta.label}
                        </span>,
                        target,
                        `${index}-${kind}`
                    )
                })
            )}
        </div>
    )
}

/**
 * Displays the bulletin script as sentence-based teleprompter lines.
 * Karaoke sync colours the current word and sentence, while exact historical
 * pronunciation substitutions remain visible without changing canonical copy.
 * Provenance decorations show per-sentence incident matching status.
 */
export function ReviewScript({
    jobId,
    text,
    canEdit,
    onRevoice,
    isRevoicing,
    editingGuidance,
    wordTimings,
    onWordSeek,
    activeWordMs,
    activeSentenceWordRange,
    sentences,
    bySentence,
    sentenceStatus,
    onSentenceHover,
    litSentenceIndexes,
    verificationDegraded,
    pronunciationMatch,
    pronunciationContext,
    suspectWords = [],
    numberMismatches = [],
    verifiedPronunciations = [],
    pendingPronunciation,
    markActions,
    markDictionaryName,
    roadPhraseSpans,
    toneCapability,
    taggedText,
}: ReviewScriptProps) {
    const displayText = text
    // A stored tagged read can outlive the words it was built on (a verbatim
    // re-voice updates the script but not the tags), so only trust it when its
    // spoken words still match — the backend's own guard. Shared with the
    // caller that resolves this script's marks: both must name the same text.
    const airedRead = resolveAiredRead(text, taggedText)
    // How many tags the aired read carries — shown beside Edit as a heads-up.
    const airedTagCount = airedRead.match(TONE_TAG_PATTERN)?.length ?? 0

    const [editing, setEditing] = useState(false)
    const [draft, setDraft] = useState(displayText)
    // The textarea itself, so the tone palette can insert a tag at the caret.
    const textareaRef = useRef<HTMLTextAreaElement>(null)

    // Audition: hear the draft verbatim without spending a re-voice. Which tags a voice
    // acts on is voice-dependent and undocumented, and ellipses/CAPS/fragments move the
    // read as much as the tags do - so the only way to steer delivery is to try it, and
    // that loop has to cost seconds rather than a full orchestration.
    const [auditioning, setAuditioning] = useState(false)
    const [auditionUrl, setAuditionUrl] = useState<string | null>(null)
    const auditionAudioRef = useRef<HTMLAudioElement>(null)
    useEffect(() => {
        const audio = auditionAudioRef.current
        if (audio) void playSolo(audio)
    }, [auditionUrl])
    const [auditionError, setAuditionError] = useState<string | null>(null)
    // The two LLM passes (delivery register, content rewrite) live in ScriptAssist: both are
    // previews, both hand their accepted text back through the ONE door below — the verbatim
    // re-voice path — so there is no second route to air.

    const runAudition = async () => {
        const text = draft.trim()
        if (!text || !jobId) return
        setAuditioning(true)
        setAuditionError(null)
        try {
            const res = await auditionScript({ jobId, text })
            if (!res.url) throw new Error('No audio came back for that read.')
            setAuditionUrl(res.url)
        } catch (err) {
            setAuditionError(
                err instanceof Error
                    ? err.message
                    : 'Could not audition that read.'
            )
        } finally {
            setAuditioning(false)
        }
    }

    // A non-v3 model SPEAKS an audio tag instead of acting on it, so every tone
    // control is withheld entirely rather than shown disabled — an operator cannot
    // be offered a lever that would put the word "excitedly" to air. Fail-closed:
    // the capability is required, and an unknown model counts as unsupported. The
    // backend refuses tags for these models too and strips them at the synthesis
    // boundary, so this gate is the courtesy, not the guarantee.
    const toneSupported = modelSupportsTags(toneCapability.model)
    // The aired read seeds the draft, tags and all: the operator edits the
    // exact text that will go to the voice, and every tag is visible inline.
    const startEdit = () => {
        setDraft(formatScriptForEditing(airedRead))
        setEditing(true)
    }
    // Insert a tone at the caret. The tag lands before whatever word follows,
    // exactly where the operator pointed; typing `[slow]` by hand works the
    // same way and is always an option.
    const insertTone = (tag: string) => {
        rememberTone(tag)
        const textarea = textareaRef.current
        const pos = textarea?.selectionStart ?? draft.length
        // The tag travels with a trailing space; swallow any whitespace the
        // caret was sitting in front of so `word [tag] next` stays single-spaced.
        const after = draft.slice(pos).replace(/^\s+/, '')
        const insertion = `${tag} `
        const next = `${draft.slice(0, pos)}${insertion}${after}`
        setDraft(next)
        // Restore the caret past the inserted tag on the next frame, once the
        // value prop has landed.
        const caret = pos + insertion.length
        requestAnimationFrame(() => {
            textarea?.focus()
            textarea?.setSelectionRange(caret, caret)
        })
    }
    const [recentTags, rememberTone] = useRecentTones()
    // Whether resting on a word offers a respelling at all — no dictionary is
    // attached to some bulletins, and then there is nothing to teach.
    const canRespell = Boolean(markActions && markDictionaryName)
    // Word count measures the PLAIN words — tags are directions, never counted
    // as spoken content (the backend strips them the same way).
    const wordCount = useMemo(() => {
        const plain = stripTags(draft)
        return plain.trim() ? plain.trim().split(/\s+/).length : 0
    }, [draft])
    // The editor reformats the script to one sentence per line, so the RAW draft
    // never equals `text` — comparing them directly made the no-op guard dead:
    // Re-voice stayed enabled on an untouched draft, and clicking it just closed
    // the editor while handleRevoice declined to act. That is the failure mode
    // recorded on 2026-07-29 ("a submit path must fail loudly"), one path over.
    // Both the button and the handler now read the SAME canonical comparison, so
    // they cannot drift again. The aired read (tags included) is the baseline,
    // so adding OR removing a tag counts as a change.
    const isUnchanged = useMemo(
        () =>
            canonicalizeEditedScript(draft) ===
            canonicalizeEditedScript(airedRead),
        [draft, airedRead]
    )
    const canApply = !isUnchanged
    const handleRevoice = () => {
        if (!canApply) return
        const next = canonicalizeEditedScript(draft)
        if (!next) return
        // The words go to the pipeline stripped; the tags ride along as the
        // exact payload the voice reads. The backend's word-immutability guard
        // (strip(tagged) == words) accepts this by construction.
        const plain = stripTags(next)
        const hasTags = plain !== next
        onRevoice(plain, hasTags ? next : undefined)
        setEditing(false)
    }
    const slot = editingGuidance
        ? Math.round(editingGuidance.targetDurationSeconds)
        : 0
    const estSec =
        editingGuidance && editingGuidance.wordsPerMinute > 0
            ? Math.round((wordCount / editingGuidance.wordsPerMinute) * 60)
            : 0
    const below = editingGuidance
        ? wordCount < editingGuidance.minWordCount
        : false
    const above = editingGuidance
        ? wordCount > editingGuidance.maxWordCount
        : false
    const cannotFit = editingGuidance
        ? isScriptTooLongToFit(wordCount, editingGuidance.maxWordCount)
        : false
    const counterTone = !editingGuidance
        ? 'text-muted-foreground'
        : cannotFit
          ? 'text-destructive'
          : below || above
            ? 'text-warning'
            : 'text-success'
    // Why Re-voice is unavailable, in the operator's terms. A greyed-out primary action with no
    // stated reason reads as a broken button; each of these is something they can act on.
    const revoiceBlockedReason = !draft.trim()
        ? 'The script is empty.'
        : !canApply
          ? 'Nothing has changed yet: edit the script, or apply a rewrite, first.'
          : cannotFit
            ? `Too long for the ${slot}s slot. Trim to about ${editingGuidance?.maxWordCount ?? 0} words.`
            : null
    const cancelEdit = () => {
        setDraft(airedRead)
        setEditing(false)
    }

    // Pronunciation: name the feature and exact historical dictionary version
    // explicitly; matching canonical words carry the detailed popover.
    const pronunciationStatus = useMemo(() => {
        if (!pronunciationMatch) return null
        if (pronunciationMatch.resolution === 'exact') {
            return {
                tone: 'muted' as const,
                text: `Pronunciation dictionary · ${pronunciationMatch.dictionaryName} · version ${pronunciationMatch.versionNumber}`,
            }
        }
        if (
            pronunciationMatch.resolution === 'missing-dictionary' ||
            pronunciationMatch.resolution === 'missing-version'
        ) {
            return {
                tone: 'warning' as const,
                text: 'Pronunciation dictionary: exact on-air version unavailable; cannot show what was said on air',
            }
        }
        return null
    }, [pronunciationMatch])

    // One mark per span, merging the dictionary substitution with the audio
    // pre-review's verdict, so a word is annotated exactly once in the script
    // and nothing needs a second summary row to repeat it. The matcher runs on
    // the PADDED read — tags replaced by same-length spaces — so offsets land
    // in the rendered text's coordinate space and tag labels can never match a
    // dictionary word.
    const readText = airedRead
    const scriptMarks = useMemo(
        () =>
            buildScriptMarks(
                padTags(readText),
                pronunciationMatch,
                suspectWords,
                numberMismatches,
                verifiedPronunciations,
                pendingPronunciation
            ).marks,
        [
            readText,
            pronunciationMatch,
            suspectWords,
            numberMismatches,
            verifiedPronunciations,
            pendingPronunciation,
        ]
    )

    // Recompute against the live draft (never the dictionary's current
    // rules) so the edit-mode preview stays exact as the operator types.
    const draftPronunciationMatch = useMemo(() => {
        if (!pronunciationContext) return null
        // Padded, not raw: the caret's tags shift every later offset, and the
        // marks below are placed in the padded draft's coordinate space.
        return findPronunciationMatches(
            padTags(draft),
            pronunciationContext.providerDictionaryId,
            pronunciationContext.providerVersionId,
            pronunciationContext.voiceModel,
            pronunciationContext.dictionaries
        )
    }, [draft, pronunciationContext])

    // Same padding trick as read mode: the mirror renders the tagged draft, so
    // markers must be computed in its coordinate space.
    const draftPronunciationMarkers = useMemo(
        () => buildDraftMarks(padTags(draft), draftPronunciationMatch),
        [draft, draftPronunciationMatch]
    )

    const draftPronunciationWarning =
        draftPronunciationMatch != null &&
        (draftPronunciationMatch.resolution === 'missing-dictionary' ||
            draftPronunciationMatch.resolution === 'missing-version')

    // Verdict counts for the script-header summary chips
    const verdictCounts = useMemo(
        () => (sentenceStatus ? countVerdicts(sentenceStatus) : null),
        [sentenceStatus]
    )

    // Jump-to-flagged-sentence: clicking a summary chip cycles through the
    // sentences of that verdict, scrolling each into view with a brief flash.
    const scriptRef = useRef<HTMLDivElement>(null)
    const jumpCursorRef = useRef<Partial<Record<SentenceVerdict, number>>>({})
    const flashTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
    const [flashIndex, setFlashIndex] = useState<number | null>(null)

    const jumpToVerdict = useCallback(
        (verdict: SentenceVerdict) => {
            if (!sentenceStatus) return
            const next = nextSentenceOfVerdict(
                sentenceStatus,
                verdict,
                jumpCursorRef.current[verdict] ?? -1
            )
            if (next === null) return
            jumpCursorRef.current[verdict] = next
            const smooth = !window.matchMedia(
                '(prefers-reduced-motion: reduce)'
            ).matches
            scriptRef.current
                ?.querySelector(`[data-sentence-index="${next}"]`)
                ?.scrollIntoView({
                    behavior: smooth ? 'smooth' : 'auto',
                    block: 'center',
                })
            setFlashIndex(next)
            if (flashTimerRef.current) clearTimeout(flashTimerRef.current)
            flashTimerRef.current = setTimeout(() => setFlashIndex(null), 1600)
        },
        [sentenceStatus]
    )

    useEffect(
        () => () => {
            if (flashTimerRef.current) clearTimeout(flashTimerRef.current)
        },
        []
    )

    const litWithFlash = useMemo<readonly number[] | undefined>(() => {
        if (flashIndex === null) return litSentenceIndexes
        return [...(litSentenceIndexes ?? []), flashIndex]
    }, [litSentenceIndexes, flashIndex])

    // Sentence hover via DOM event delegation on teleprompter lines
    const lastHoveredSentenceRef = useRef<number | null>(null)
    const handleScriptMouseOver = useCallback(
        (e: React.MouseEvent) => {
            if (!onSentenceHover) return
            const line = (e.target as HTMLElement).closest(
                '[data-sentence-index]'
            )
            const idx = line
                ? parseInt(line.getAttribute('data-sentence-index') || '', 10)
                : NaN
            const next = Number.isNaN(idx) ? null : idx
            if (next !== lastHoveredSentenceRef.current) {
                lastHoveredSentenceRef.current = next
                onSentenceHover(next)
            }
        },
        [onSentenceHover]
    )

    const handleScriptMouseOut = useCallback(() => {
        if (onSentenceHover && lastHoveredSentenceRef.current !== null) {
            lastHoveredSentenceRef.current = null
            onSentenceHover(null)
        }
    }, [onSentenceHover])

    return (
        <div className="flex flex-col gap-3">
            {/* The row's height must not depend on the mode: the Edit button (and the
                delivery-style/verdict chips) unmount in edit mode, so without a floor the
                row collapsed to the heading's text height and "Script" — and everything
                below it — jumped up by the button's 36px every time Edit was pressed.
                `min-h-9` matches the sm Button height, so the heading's y-offset is
                identical whether or not the button is mounted. */}
            <div className="flex min-h-9 items-center justify-between">
                <div className="flex items-center gap-2">
                    <h2 className="text-sm font-semibold">Script</h2>
                    {pronunciationStatus && (
                        <Tooltip>
                            <TooltipTrigger asChild>
                                <span
                                    role="img"
                                    tabIndex={0}
                                    aria-label={pronunciationStatus.text}
                                    className={cn(
                                        'inline-flex cursor-default items-center',
                                        pronunciationStatus.tone === 'warning'
                                            ? 'text-warning'
                                            : 'text-muted-foreground'
                                    )}
                                >
                                    {pronunciationStatus.tone === 'warning' ? (
                                        <ICON.warning
                                            className="size-3.5"
                                            aria-hidden
                                        />
                                    ) : (
                                        <BookOpenText
                                            className="size-3.5"
                                            aria-hidden
                                        />
                                    )}
                                </span>
                            </TooltipTrigger>
                            <TooltipContent side="top" className="max-w-xs">
                                {pronunciationStatus.text}
                                {/* The only place the hover gesture is named.
                                    It belongs on the dictionary icon: that is
                                    what an operator looks at when they wonder
                                    how the voice decides to say a word. */}
                                {canRespell && (
                                    <span className="mt-1 block text-muted-foreground">
                                        Rest the pointer on any word to change
                                        how it is said.
                                    </span>
                                )}
                            </TooltipContent>
                        </Tooltip>
                    )}
                    {/* Verification summary: state first, then action — the
                        flagged chip cycles through contradicted sentences. */}
                    {verdictCounts && !editing && (
                        <span
                            className={cn(
                                'ml-2 items-center gap-1.5',
                                verdictCounts.check > 0
                                    ? 'hidden sm:inline-flex'
                                    : 'inline-flex'
                            )}
                        >
                            {verdictCounts.check > 0 && (
                                <Tooltip>
                                    <TooltipTrigger asChild>
                                        <button
                                            type="button"
                                            onClick={() =>
                                                jumpToVerdict('check')
                                            }
                                            className="inline-flex items-center gap-1 rounded-full border border-destructive/30 bg-destructive/10 px-2 py-0.5 text-2xs font-medium text-destructive-on-tint transition-colors hover:bg-destructive/20"
                                        >
                                            <ICON.warning
                                                className="size-3"
                                                aria-hidden
                                            />
                                            {verdictCounts.check} to check
                                        </button>
                                    </TooltipTrigger>
                                    <TooltipContent side="top">
                                        Jump to the next contradicted claim
                                    </TooltipContent>
                                </Tooltip>
                            )}
                            <span className="text-2xs text-muted-foreground">
                                {verdictCounts.sourced} sourced
                            </span>
                            {verificationDegraded && (
                                <Tooltip>
                                    <TooltipTrigger asChild>
                                        <span
                                            tabIndex={0}
                                            className="text-2xs text-muted-foreground"
                                        >
                                            heuristic
                                        </span>
                                    </TooltipTrigger>
                                    <TooltipContent
                                        side="top"
                                        className="max-w-sm"
                                    >
                                        Sentence lineage could not be aligned
                                        with the stored attribution; sources are
                                        matched by road-name heuristic and
                                        contradiction checks are unavailable.
                                        Verify claims against the Sources panel.
                                    </TooltipContent>
                                </Tooltip>
                            )}
                        </span>
                    )}
                </div>
                <div className="flex items-center gap-2">
                    {canEdit && !editing && (
                        <Button variant="ghost" size="sm" onClick={startEdit}>
                            <ICON.edit data-icon="inline-start" />
                            Edit
                            {airedTagCount > 0 && (
                                <span
                                    aria-hidden
                                    className="ml-1 flex items-center gap-1 text-2xs text-muted-foreground"
                                >
                                    <AudioLines className="size-3" />
                                    <span className="tabular-nums">
                                        {airedTagCount}
                                    </span>
                                </span>
                            )}
                        </Button>
                    )}
                </div>
            </div>

            {editing ? (
                <div className="flex flex-col gap-3">
                    {/* ONE frame: the tools that change the script, the script,
                        and its measure. Three stacked bordered boxes read as
                        three unrelated components; a single bordered surface
                        with a rail above and a rail below reads as one editor,
                        and every control in it is visibly pointed at the words
                        between them. */}
                    {/* The frame carries the script's focus indication (the
                        textarea draws no ring of its own, so the border must).
                        Scoped to the textarea, not `focus-within`, or typing an
                        instruction in the rail would light the whole surface —
                        and `:focus`, not `:focus-visible`, because Chromium
                        withholds focus-visible from a programmatically focused
                        textarea, which is exactly how edit mode opens. */}
                    <div className="overflow-hidden rounded-md border border-input bg-background has-[textarea:focus]:border-ring/70">
                        <ScriptAssist
                            jobId={jobId}
                            draft={draft}
                            onApply={setDraft}
                            toneSupported={toneSupported}
                            toneModel={toneCapability.model}
                            trailing={
                                <>
                                    {toneSupported && (
                                        <TonePalette
                                            recent={recentTags}
                                            onInsert={insertTone}
                                        />
                                    )}
                                    {/* The one "how do I change this?" hint in
                                        edit mode — both levers and the manual
                                        tag, since they answer one question. */}
                                    <InfoHint label="How do I change this script?">
                                        <strong>Delivery</strong> keeps every
                                        fact and reshapes only the read.{' '}
                                        <strong>Content</strong> follows the
                                        operator&rsquo;s instruction to drop,
                                        reorder, reword and re-emphasise. Both
                                        return text only: read the diff, apply
                                        it, then press Re-voice.
                                        {toneSupported && (
                                            <span className="mt-1 block">
                                                <strong>Add tone</strong> drops
                                                a tag at the cursor by hand. A
                                                tag steers the read for about
                                                five words and is never spoken.
                                            </span>
                                        )}
                                    </InfoHint>
                                </>
                            }
                        />
                        <PronunciationScriptEditor
                            value={draft}
                            onChange={setDraft}
                            markers={draftPronunciationMarkers}
                            editorRef={textareaRef}
                            autoFocus
                        />
                        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 border-t border-border/60 bg-muted/20 px-2 py-1.5">
                            <p className={`text-xs ${counterTone}`}>
                                <span className="tabular-nums">
                                    {wordCount}
                                </span>{' '}
                                words
                                {editingGuidance && (
                                    <>
                                        {' · ~'}
                                        <span className="tabular-nums">
                                            {estSec}
                                        </span>
                                        s
                                        {below
                                            ? ` · short for ${slot}s, may not fill the slot (aim ${editingGuidance.minWordCount} to ${editingGuidance.maxWordCount})`
                                            : cannotFit
                                              ? ` · too long for ${slot}s. Trim to ~${editingGuidance.maxWordCount} words; cannot fit the slot`
                                              : above
                                                ? ` · long for ${slot}s, will be sped up to fit (aim ${editingGuidance.minWordCount} to ${editingGuidance.maxWordCount})`
                                                : ` · fits the ${slot}s slot (${editingGuidance.minWordCount} to ${editingGuidance.maxWordCount})`}
                                    </>
                                )}
                            </p>
                            {draftPronunciationWarning && (
                                <p className="flex items-center gap-1.5 text-2xs text-warning">
                                    <ICON.warning
                                        className="size-3 shrink-0"
                                        aria-hidden
                                    />
                                    Pronunciation dictionary: exact on-air
                                    version unavailable, so matches cannot be
                                    previewed.
                                </p>
                            )}
                        </div>
                    </div>

                    {/* No divider: the frame above closes the editing surface,
                        so a second hairline 12px below it drew nothing. */}
                    <div className="flex items-center gap-2">
                        <Tooltip>
                            <TooltipTrigger asChild>
                                {/* A span, so the tooltip still opens while the button is
                                    disabled — "why is this greyed out?" was unanswerable. */}
                                <span tabIndex={revoiceBlockedReason ? 0 : -1}>
                                    <Button
                                        size="sm"
                                        onClick={handleRevoice}
                                        disabled={
                                            isRevoicing ||
                                            Boolean(revoiceBlockedReason)
                                        }
                                    >
                                        {isRevoicing ? (
                                            <Spinner data-icon="inline-start" />
                                        ) : (
                                            <ICON.voice data-icon="inline-start" />
                                        )}
                                        {isRevoicing
                                            ? 'Re-voicing…'
                                            : 'Re-voice · ~20s'}
                                    </Button>
                                </span>
                            </TooltipTrigger>
                            <TooltipContent side="top" className="max-w-sm">
                                {revoiceBlockedReason ??
                                    'Re-synthesises the bulletin from this draft and replaces the audio on the job.'}
                            </TooltipContent>
                        </Tooltip>
                        <Tooltip>
                            <TooltipTrigger asChild>
                                <Button
                                    variant="outline"
                                    size="sm"
                                    onClick={runAudition}
                                    disabled={auditioning || !draft.trim()}
                                >
                                    {auditioning ? (
                                        <Spinner data-icon="inline-start" />
                                    ) : (
                                        <Headphones data-icon="inline-start" />
                                    )}
                                    {auditioning
                                        ? 'Auditioning…'
                                        : 'Audition · ~5s'}
                                </Button>
                            </TooltipTrigger>
                            <TooltipContent side="top">
                                Synthesises this draft on the bulletin's own
                                voice. Does not touch the job.
                            </TooltipContent>
                        </Tooltip>
                        {/* Cancel discards the draft, so it does not sit flush against the two
                            buttons that keep it. */}
                        <Button
                            variant="ghost"
                            size="sm"
                            onClick={cancelEdit}
                            className="ml-auto"
                        >
                            Cancel
                        </Button>
                    </div>

                    {/* Delivery on v3 is only steerable by trying it, so the loop has to be
                        cheap: this plays the draft verbatim without spending a re-voice. */}
                    {auditionUrl && (
                        <audio
                            key={auditionUrl}
                            ref={auditionAudioRef}
                            controls
                            preload="metadata"
                            src={auditionUrl}
                            className="h-8 w-full"
                            aria-label="Audition of the current draft"
                            onPlay={(event) => claimSolo(event.currentTarget)}
                        />
                    )}
                    {auditionError && (
                        <p className="text-2xs text-destructive">
                            {auditionError}
                        </p>
                    )}
                </div>
            ) : (
                <div
                    ref={scriptRef}
                    onMouseOver={handleScriptMouseOver}
                    onMouseOut={handleScriptMouseOut}
                >
                    <SentenceSourceLabels
                        bySentence={bySentence}
                        text={readText}
                        sentenceCount={sentences?.length ?? 0}
                    >
                        <ScriptRenderer
                            text={readText}
                            wordTimings={wordTimings}
                            onWordSeek={onWordSeek}
                            activeWordMs={activeWordMs ?? null}
                            activeSentenceWordRange={activeSentenceWordRange}
                            sentences={sentences}
                            sentenceStatus={sentenceStatus}
                            litSentenceIndexes={litWithFlash}
                            scriptMarks={scriptMarks}
                            markActions={markActions}
                            markDictionaryName={markDictionaryName}
                            roadPhraseSpans={roadPhraseSpans}
                            /* one-off: teleprompter reading size */
                            className="text-[1.0625rem] text-foreground"
                        />
                    </SentenceSourceLabels>
                </div>
            )}
        </div>
    )
}
