import { useLayoutEffect, useRef, type ReactNode, type RefObject } from 'react'
import { Textarea } from '@/components/ui/textarea'
import { ScriptWordMark } from '../ScriptWordMark'
import type { ScriptMark } from '../../lib/script-marks'
import { formatScriptForEditing } from './script-editor-text'

/** One v3 audio tag — same shape the backend strips and the mirror highlights. */
const TAG = /(\[[^[\]]{0,80}\])/g

/**
 * Marks inside the mirror must not change the text's advance width: the
 * `<mark>` is inline, so its own `px-0.5` would push every later glyph 4px
 * right of the caret it is meant to sit under. The negative inline margin
 * gives the padding back, so the tint still breathes and the geometry does
 * not move.
 */
const MARK_CLASS = 'pointer-events-auto -mx-0.5'

interface PronunciationScriptEditorProps {
    readonly value: string
    readonly onChange: (value: string) => void
    readonly markers?: readonly ScriptMark[]
    readonly autoFocus?: boolean
    /**
     * The editable textarea, for callers that insert text at the caret (e.g.
     * the tone palette). The textarea's own ref still wins for sizing.
     */
    readonly editorRef?: RefObject<HTMLTextAreaElement | null>
}

/**
 * Paints the whole draft — newlines included — as one run of inline nodes:
 * tags as chips, marked words as hoverable marks, everything else as plain
 * text. One flow, so `white-space: pre-wrap` breaks it exactly where the
 * textarea behind it breaks, and nothing can accumulate a per-line offset.
 *
 * Every node is metric-neutral: colours and backgrounds only, never padding,
 * font size or margin, because a single pixel of extra advance width drags the
 * caret out from under the glyph it precedes.
 *
 * `markers` arrive ordered and non-overlapping (`buildScriptMarks`), so a
 * single forward index walks them alongside the text.
 */
function renderMirror(
    value: string,
    markers: readonly ScriptMark[]
): ReactNode[] {
    const nodes: ReactNode[] = []
    let cursor = 0
    let next = 0
    // Tag / plain-text runs. Markers are word spans on real words, so they
    // never overlap a tag run — tag labels are padded to spaces for matching.
    for (const part of value.split(TAG)) {
        if (!part) continue
        const start = cursor
        const end = start + part.length
        cursor = end

        if (part.startsWith('[') && part.endsWith(']')) {
            nodes.push(
                <span
                    key={`tag-${start}`}
                    aria-hidden
                    className="rounded-sm bg-primary/15 text-primary-on-tint"
                >
                    {part}
                </span>
            )
            continue
        }

        let at = start
        while (next < markers.length && markers[next].end <= start) next += 1
        while (next < markers.length && markers[next].start < end) {
            const marker = markers[next]
            const from = Math.max(marker.start, at)
            const to = Math.min(marker.end, end)
            if (from > at) {
                nodes.push(
                    <span key={`t-${at}`} aria-hidden>
                        {part.slice(at - start, from - start)}
                    </span>
                )
            }
            nodes.push(
                <ScriptWordMark
                    key={`m-${marker.start}`}
                    mark={marker}
                    className={MARK_CLASS}
                >
                    {part.slice(from - start, to - start)}
                </ScriptWordMark>
            )
            at = to
            // A marker can only span one run, so it is spent here.
            next += 1
        }
        if (at < end) {
            nodes.push(
                <span key={`t-${at}`} aria-hidden>
                    {part.slice(at - start)}
                </span>
            )
        }
    }
    return nodes
}

/**
 * Sizes the box to its text. The reset to `auto` is load-bearing: `scrollHeight`
 * never reports less than the current height, so without it the editor could
 * only ever grow.
 */
function fitToContent(textarea: HTMLTextAreaElement): void {
    textarea.style.height = 'auto'
    textarea.style.height = `${textarea.scrollHeight}px`
}

/**
 * A native textarea remains responsible for selection, typing, undo, and IME.
 * A pointer-transparent mirror sitting exactly on top of it paints tags as
 * chips and pronunciation markers as hoverable words, without changing the
 * editable text. Both layers share font, size, leading and padding, so the
 * caret always lands under the glyph it precedes.
 *
 * Deliberately frameless: the caller owns one border around the script and the
 * rails that act on it, so this draws none of its own — and because the mirror
 * is pinned to the textarea's border box, both layers must agree about that
 * border being absent.
 */
export function PronunciationScriptEditor({
    value,
    onChange,
    markers = [],
    autoFocus = false,
    editorRef,
}: PronunciationScriptEditorProps) {
    const textareaRef = useRef<HTMLTextAreaElement>(null)

    useLayoutEffect(() => {
        const textarea = textareaRef.current
        if (!textarea) return
        fitToContent(textarea)
    }, [value])

    // The height also has to be refit when the WIDTH changes: a narrower column
    // rewraps the same words onto more lines. Sizing on `value` alone left the
    // tail of the script clipped behind the frame's bottom edge whenever the
    // surrounding layout reflowed after the first measure — measured at 224px
    // of box for 276px of text once the evidence rail mounted beside it.
    useLayoutEffect(() => {
        const textarea = textareaRef.current
        if (!textarea) return
        // Width only: refitting on our own height change would loop.
        let lastWidth = textarea.clientWidth
        const observer = new ResizeObserver((entries) => {
            const width = entries[0]?.contentRect.width
            if (width === undefined || width === lastWidth) return
            lastWidth = width
            fitToContent(textarea)
        })
        observer.observe(textarea)
        return () => observer.disconnect()
    }, [])

    return (
        <div className="relative">
            <Textarea
                ref={(element) => {
                    textareaRef.current = element
                    if (editorRef) editorRef.current = element
                }}
                value={value}
                onChange={(event) => {
                    const next = event.target.value
                    const formatted = formatScriptForEditing(next)
                    const hasNewSentenceInLine =
                        formatted.split('\n').length > next.split('\n').length
                    onChange(hasNewSentenceInLine ? formatted : next)
                }}
                className="min-h-52 resize-none overflow-hidden rounded-none border-0 bg-transparent px-3 py-2 font-serif text-base leading-relaxed text-transparent caret-foreground selection:bg-primary/25 focus-visible:ring-0 focus-visible:ring-offset-0"
                aria-label="Edit script"
                autoFocus={autoFocus}
            />
            {/* `wrap-break-word` is not cosmetic: a textarea breaks an
                over-long token mid-word by UA rule, and a plain div does not,
                so without it one unbroken URL would desynchronise every line
                after it. */}
            <div
                data-script-mirror
                className="pointer-events-none absolute inset-0 overflow-hidden whitespace-pre-wrap wrap-break-word px-3 py-2 font-serif text-base leading-relaxed text-foreground"
            >
                {renderMirror(value, markers)}
            </div>
        </div>
    )
}
