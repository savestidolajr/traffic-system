import {
    useCallback,
    useEffect,
    useRef,
    useState,
    type MouseEvent,
    type ReactNode,
} from 'react'
import { AlertTriangle } from 'lucide-react'
import { cn } from '@/lib/utils'
import { resolvePronunciationTarget } from '@/features/pronunciation/lib/pronunciation-target'
import { DIRECTION_CUE_PATTERN } from '../lib/verification/provenance'
import type { ScriptSentence } from '../lib/verification/provenance'
import type { ScriptRoadPhrase } from '../types'
import { ScriptWordMark, type ScriptMarkActions } from './ScriptWordMark'
import type { ScriptMark } from '../lib/script-marks'
import {
    ScriptRespellPopover,
    type RespellTarget,
} from './ScriptRespellPopover'

/**
 * How long the pointer must REST on a word before its respelling panel opens.
 * Long enough that reading across a line opens nothing; short enough that
 * stopping on a word you just heard feels immediate.
 */
const RESPELL_DWELL_MS = 400

/** Grace period to cross the gap from the word into the panel. */
const RESPELL_CLOSE_MS = 160

export interface WordTiming {
    readonly word: string
    readonly startMs: number
}

/** Verdict categories for sentence verification status. */
export type SentenceVerdict = 'sourced' | 'check' | 'unverified' | 'branding'

/** Verification state of a sentence, shown in the teleprompter gutter. */
export interface SentenceStatus {
    readonly kind: SentenceVerdict
    readonly note?: string
}

const isCue = (segment: string) =>
    segment.startsWith('[') && segment.endsWith(']')

type SegmentWordHandler = (
    word: string,
    charStart: number,
    wordIndex: number,
    segmentIndex: number,
    wordIndexInSegment: number
) => void

type SegmentCueHandler = (
    segment: string,
    charStart: number,
    segmentIndex: number
) => void

function walkSegments(
    segments: readonly string[],
    onWord: SegmentWordHandler,
    onCue: SegmentCueHandler
): void {
    let wordIndex = 0
    let charOffset = 0

    for (let segmentIndex = 0; segmentIndex < segments.length; segmentIndex++) {
        const segment = segments[segmentIndex]
        if (!segment) continue
        if (isCue(segment)) {
            onCue(segment, charOffset, segmentIndex)
            charOffset += segment.length
            continue
        }
        const words = segment.split(/(\s+)/)
        for (
            let wordIndexInSegment = 0;
            wordIndexInSegment < words.length;
            wordIndexInSegment++
        ) {
            const word = words[wordIndexInSegment]
            onWord(
                word,
                charOffset,
                wordIndex,
                segmentIndex,
                wordIndexInSegment
            )
            if (word.trim()) wordIndex++
            charOffset += word.length
        }
    }
}
interface ScriptRendererProps {
    text: string
    className?: string
    wordTimings?: WordTiming[]
    /** Seek audio to a timed word selected in the script. */
    readonly onWordSeek?: (startMs: number) => void
    /** When set, the word whose startMs is the latest ≤ this position gets
     *  a subtle primary-text style (karaoke playback sync). */
    readonly activeWordMs?: number | null
    /** Word-index range [start, end) of the current sentence. */
    readonly activeSentenceWordRange?: {
        readonly startWord: number
        readonly endWord: number
    }
    /** Sentence structure: when provided, renders teleprompter lines
     *  (one sentence per line with a verification gutter). */
    readonly sentences?: readonly ScriptSentence[]
    /** Per-sentence verification state, parallel to `sentences`. */
    readonly sentenceStatus?: readonly SentenceStatus[]
    /** Sentence indexes lit externally (incident hover in the index). */
    readonly litSentenceIndexes?: readonly number[]
    /** Marked spans: on-air pronunciation substitutions and audio-pre-review
     *  state, merged so a word is annotated exactly once. Read-only — never
     *  alters the visible characters. */
    readonly scriptMarks?: readonly ScriptMark[]
    /** Verdict actions offered in a mark's popover. Omit for read-only viewers. */
    readonly markActions?: ScriptMarkActions
    /** Dictionary attached to this bulletin; enables the respelling action. */
    readonly markDictionaryName?: string | null
    /** Server-resolved road phrase ranges in the rendered script coordinate space. */
    readonly roadPhraseSpans?: readonly ScriptRoadPhrase[] | null
}

const GUTTER_META: Record<
    SentenceVerdict,
    { readonly className: string; readonly title: string }
> = {
    sourced: {
        className: 'bg-success/60',
        title: 'Matches source data',
    },
    check: {
        className: 'bg-destructive ring-2 ring-destructive/30',
        title: 'Claim differs from source data. Check before approving',
    },
    unverified: {
        className: 'bg-warning ring-2 ring-warning/30',
        title: 'No source found. Verify manually',
    },
    branding: {
        className: 'bg-muted-foreground/30',
        title: 'Station branding: no traffic source expected',
    },
}

/**
 * Renders broadcast scripts. With sentence structure available it renders as
 * a teleprompter: one sentence per line, a verification marker in the gutter,
 * whole-line hover targets (carrying `data-sentence-index` for delegation),
 * and karaoke line/word highlighting. Without sentences it falls back to
 * flowing text. Direction cues ([bracketed]) are styled as tags either way.
 */
export function ScriptRenderer({
    text,
    className,
    wordTimings,
    onWordSeek,
    activeWordMs,
    activeSentenceWordRange,
    sentences,
    sentenceStatus,
    litSentenceIndexes,
    scriptMarks,
    markActions,
    markDictionaryName,
    roadPhraseSpans,
}: ScriptRendererProps) {
    const segments = text.split(DIRECTION_CUE_PATTERN)
    // Any word can be taught, not just the ones a flag already reached — and by
    // the SAME gesture a marked word uses, so the script gains no second way to
    // do anything. Click stays a plain seek, which is what an operator does all
    // day while checking a read.
    const canRespell = Boolean(markActions && markDictionaryName)
    const [respell, setRespell] = useState<RespellTarget | null>(null)
    const dwellTimerRef = useRef<number | null>(null)
    const closeTimerRef = useRef<number | null>(null)

    const cancelDwell = useCallback(() => {
        window.clearTimeout(dwellTimerRef.current ?? undefined)
        dwellTimerRef.current = null
    }, [])

    const keepRespellOpen = useCallback(() => {
        window.clearTimeout(closeTimerRef.current ?? undefined)
        closeTimerRef.current = null
    }, [])

    const scheduleRespellClose = useCallback(() => {
        if (closeTimerRef.current !== null) return
        closeTimerRef.current = window.setTimeout(() => {
            closeTimerRef.current = null
            setRespell(null)
        }, RESPELL_CLOSE_MS)
    }, [])

    const closeRespell = useCallback(() => {
        cancelDwell()
        keepRespellOpen()
        setRespell(null)
    }, [cancelDwell, keepRespellOpen])

    useEffect(
        () => () => {
            window.clearTimeout(dwellTimerRef.current ?? undefined)
            window.clearTimeout(closeTimerRef.current ?? undefined)
        },
        []
    )

    const isActiveWordByMs = (timing: WordTiming | undefined): boolean =>
        activeWordMs != null &&
        timing != null &&
        activeWordMs === timing.startMs

    const renderWord = (
        word: string,
        key: string,
        charStart: number,
        timingWordIndex: number
    ): ReactNode => {
        const trimmed = word.trim()
        if (!trimmed) return <span key={key}>{word}</span>
        const timing = wordTimings?.[timingWordIndex]
        const mark = scriptMarks?.find(
            (candidate) =>
                candidate.start < charStart + word.length &&
                charStart < candidate.end
        )
        const roadTarget = roadPhraseSpans?.find(
            (candidate) =>
                candidate.start < charStart + word.length &&
                charStart < candidate.end
        )?.text
        const target = resolvePronunciationTarget(roadTarget ?? trimmed)
        const wordClassName = cn(
            isActiveWordByMs(timing) && 'text-primary',
            onWordSeek && timing && 'cursor-pointer',
            ((onWordSeek && timing) || canRespell) &&
                'rounded-sm hover:bg-primary/10'
        )

        if (mark) {
            return (
                <ScriptWordMark
                    key={key}
                    mark={mark}
                    className={wordClassName}
                    wordMs={timing?.startMs}
                    actions={markActions}
                    respellTarget={roadTarget}
                    dictionaryName={markDictionaryName}
                >
                    {word}
                </ScriptWordMark>
            )
        }

        return (
            <span
                key={key}
                data-word-ms={timing?.startMs}
                // The hover target for respelling. Marked words are left out on
                // purpose: their own popover already owns this gesture.
                data-script-word={canRespell ? target : undefined}
                className={wordClassName}
            >
                {word}
            </span>
        )
    }

    const renderCue = (segment: string, key: string): ReactNode => (
        <span
            key={key}
            /* one-off: text-[0.8em] keeps the cue relative to the teleprompter reading size */
            className="mr-1.5 inline-flex items-baseline rounded-sm border border-primary/10 bg-primary/5 px-1.5 py-0.5 font-sans text-[0.8em] not-italic tracking-wide text-primary-on-tint"
            title="Broadcast direction"
        >
            {segment}
        </span>
    )

    const handleWordClick = (event: MouseEvent<HTMLElement>) => {
        if (!onWordSeek || !(event.target instanceof Element)) return
        const word = event.target.closest<HTMLElement>('[data-word-ms]')
        if (!word || !event.currentTarget.contains(word)) return
        const startMs = Number(word.dataset.wordMs)
        if (Number.isFinite(startMs)) onWordSeek(startMs)
    }

    /**
     * Resting the pointer on a word opens its respelling panel — the same gesture
     * a marked word already answers to. Crossing words while reading only ever
     * restarts the dwell, so nothing opens until the pointer actually settles.
     */
    const handleWordHover = (event: MouseEvent<HTMLElement>) => {
        if (!canRespell || !(event.target instanceof Element)) return
        const container = event.currentTarget
        // The panel is portalled to the body but is still a REACT child of this
        // element, so its own pointer events bubble here. Without the DOM
        // containment check, moving into the panel reads as "left the script" and
        // closes it under the cursor.
        if (!container.contains(event.target)) return
        cancelDwell()
        const word = event.target.closest<HTMLElement>('[data-script-word]')
        const next = word
            ? resolvePronunciationTarget(word.dataset.scriptWord ?? '')
            : null
        if (!word || !next) {
            scheduleRespellClose()
            return
        }
        if (respell?.anchor === word) {
            keepRespellOpen()
            return
        }
        // A panel already open for a different word stays put until this one's
        // dwell elapses, so sliding along a line never flickers between panels.
        keepRespellOpen()
        dwellTimerRef.current = window.setTimeout(() => {
            dwellTimerRef.current = null
            setRespell({ text: next, anchor: word })
        }, RESPELL_DWELL_MS)
    }

    const handleScriptLeave = () => {
        cancelDwell()
        scheduleRespellClose()
    }

    // Keyed on the target: resting on a SECOND word must give a panel for that
    // word, and the panel seeds its editable target and respelling from props
    // once. Without the key those fields keep the first word's values while the
    // panel silently repositions — the operator then teaches the wrong word.
    const respellPopover =
        respell && markActions && markDictionaryName ? (
            <ScriptRespellPopover
                key={respell.text}
                target={respell}
                dictionaryName={markDictionaryName}
                onSave={markActions.onAddRule}
                onClose={closeRespell}
                onKeepOpen={keepRespellOpen}
                onScheduleClose={scheduleRespellClose}
            />
        ) : null

    // ── Teleprompter mode ────────────────────────────────────────────────
    if (sentences && sentences.length > 0) {
        // Build lines: walk segments/words in order, bucketing each word into
        // the sentence whose [startWord, endWord) range contains its index.
        // Cues attach to the line of the next word.
        const lines: { parts: ReactNode[] }[] = sentences.map(() => ({
            parts: [],
        }))
        const trailing: ReactNode[] = []
        let pendingCues: ReactNode[] = []
        let partKey = 0

        const lineForWordIndex = (idx: number): number =>
            sentences.findIndex((s) => idx >= s.startWord && idx < s.endWord)

        walkSegments(
            segments,
            (word, wordStart, idx) => {
                const trimmed = word.trim()
                const node = renderWord(word, `w${partKey++}`, wordStart, idx)
                if (!trimmed) {
                    // Whitespace goes wherever the previous word went
                    const line = lineForWordIndex(Math.max(idx - 1, 0))
                    if (line >= 0) {
                        lines[line].parts.push(node)
                    } else {
                        trailing.push(node)
                    }
                    return
                }
                const line = lineForWordIndex(idx)
                const bucket = line >= 0 ? lines[line].parts : trailing
                if (pendingCues.length > 0) {
                    bucket.push(...pendingCues)
                    pendingCues = []
                }
                bucket.push(node)
            },
            (segment) => {
                pendingCues.push(renderCue(segment, `c${partKey++}`))
            }
        )
        if (pendingCues.length > 0) trailing.push(...pendingCues)

        const activeLine =
            activeSentenceWordRange != null
                ? sentences.findIndex(
                      (s) =>
                          s.startWord === activeSentenceWordRange.startWord &&
                          s.endWord === activeSentenceWordRange.endWord
                  )
                : -1

        return (
            <div
                className={cn('flex flex-col font-serif', className)}
                onClick={handleWordClick}
                onMouseOver={handleWordHover}
                onMouseLeave={handleScriptLeave}
            >
                {lines.map((line, i) => {
                    if (line.parts.length === 0) return null
                    const statusEntry = sentenceStatus?.[i]
                    const gutterMeta = statusEntry
                        ? GUTTER_META[statusEntry.kind]
                        : null
                    const dotTitle =
                        statusEntry?.note ?? gutterMeta?.title ?? ''
                    return (
                        <div
                            key={i}
                            data-sentence-index={i}
                            className={cn(
                                '-mx-2 flex items-baseline gap-3 rounded-md px-2 py-1.5 transition-colors',
                                i === activeLine
                                    ? 'bg-primary/[0.07]'
                                    : litSentenceIndexes?.includes(i)
                                      ? 'bg-primary/[0.05] ring-1 ring-inset ring-primary/20'
                                      : 'hover:bg-primary/[0.06] hover:ring-1 hover:ring-inset hover:ring-primary/15'
                            )}
                        >
                            {gutterMeta && (
                                <span
                                    className={cn(
                                        'mt-0.5 size-2 shrink-0 self-start translate-y-[0.45em] rounded-full',
                                        gutterMeta.className
                                    )}
                                    title={dotTitle}
                                    aria-label={dotTitle}
                                    role="img"
                                />
                            )}
                            <span className="min-w-0 flex-1 leading-relaxed">
                                {line.parts}
                                {/* A named contradiction is decision-critical:
                                    print it, never hide it in a tooltip. */}
                                {statusEntry?.kind === 'check' &&
                                    statusEntry.note && (
                                        <span className="mt-1 flex items-start gap-1.5 font-sans text-xs leading-snug text-destructive">
                                            <AlertTriangle
                                                className="mt-0.5 size-3 shrink-0"
                                                aria-hidden
                                            />
                                            {statusEntry.note}
                                        </span>
                                    )}
                            </span>
                        </div>
                    )
                })}
                {trailing.length > 0 && (
                    <div className="-mx-2 flex items-baseline gap-3 px-2 py-1.5">
                        <span className="size-2 shrink-0" aria-hidden />
                        <span className="min-w-0 flex-1 leading-relaxed">
                            {trailing}
                        </span>
                    </div>
                )}
                {respellPopover}
            </div>
        )
    }

    // ── Flowing-text fallback ────────────────────────────────────────────
    const isWordInActiveSentence = (idx: number): boolean =>
        activeSentenceWordRange != null &&
        idx >= activeSentenceWordRange.startWord &&
        idx < activeSentenceWordRange.endWord

    const hasWordMetadata =
        wordTimings != null || (scriptMarks?.length ?? 0) > 0 || canRespell
    const flowingNodes: ReactNode[] = []
    const nodesBySegment = new Map<number, ReactNode[]>()

    walkSegments(
        segments,
        (
            word,
            wordStart,
            currentWordIndex,
            segmentIndex,
            wordIndexInSegment
        ) => {
            let segmentNodes = nodesBySegment.get(segmentIndex)
            if (!segmentNodes) {
                segmentNodes = []
                nodesBySegment.set(segmentIndex, segmentNodes)
                flowingNodes.push(
                    hasWordMetadata ? (
                        segmentNodes
                    ) : (
                        <span key={segmentIndex}>{segmentNodes}</span>
                    )
                )
            }

            const node = hasWordMetadata
                ? renderWord(
                      word,
                      `${segmentIndex}-${wordIndexInSegment}`,
                      wordStart,
                      currentWordIndex
                  )
                : word
            if (
                hasWordMetadata &&
                word.trim() &&
                isWordInActiveSentence(currentWordIndex)
            ) {
                segmentNodes.push(
                    <span
                        key={`s${segmentIndex}-${wordIndexInSegment}`}
                        className="bg-primary/[0.04]"
                    >
                        {node}
                    </span>
                )
            } else {
                segmentNodes.push(node)
            }
        },
        (segment, _charStart, segmentIndex) => {
            flowingNodes.push(renderCue(segment, `f${segmentIndex}`))
        }
    )

    return (
        <>
            <p
                className={cn('whitespace-pre-wrap font-serif', className)}
                onClick={handleWordClick}
                onMouseOver={handleWordHover}
                onMouseLeave={handleScriptLeave}
            >
                {flowingNodes}
            </p>
            {respellPopover}
        </>
    )
}
