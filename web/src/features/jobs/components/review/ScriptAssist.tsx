import { useMemo, useState, type ReactNode } from 'react'
import { AlertTriangle, Lock, Sparkles } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { EntityDiffView } from '@/components/EntityDiffView'
import { diffEntities } from '@/lib/entity-diff'
import { retoneScript, rewriteScript } from '../../api/job-hooks'
import { parseRewriteRefusal } from '../../lib/rewrite-refusal'
import { formatScriptForEditing } from './script-editor-text'

/**
 * Which lever the operator is pulling. Two LLM passes with the same shape sat side by side as two
 * unlabelled text boxes and two buttons both reading "Rewrite…", distinguishable only by a tooltip;
 * the operator had to read prose to find out which box changed the words and which changed the
 * facts. One box with an explicit mode makes the choice the first thing on screen.
 */
export type AssistMode = 'delivery' | 'content'

interface ModeSpec {
    readonly label: string
    readonly placeholder: string
    readonly ariaLabel: string
}

const MODES: Record<AssistMode, ModeSpec> = {
    delivery: {
        label: 'Delivery',
        placeholder: 'for example, read this more calmly',
        ariaLabel: 'Describe the read you want',
    },
    content: {
        label: 'Content',
        placeholder: 'for example, drop the weather and lead with the closure',
        ariaLabel: 'Instruction for rewriting the script content',
    },
}

interface AssistPreview {
    readonly mode: AssistMode
    readonly script: string
    readonly wordsBefore: number
    readonly wordsAfter: number
    readonly preservedLines: number
    readonly tags: readonly string[]
}

interface ScriptAssistProps {
    readonly jobId: string | undefined
    /** The live editor draft — both passes rewrite what the operator currently sees. */
    readonly draft: string
    /** Hand an accepted rewrite back into the editor. Never voices; the operator still presses Re-voice. */
    readonly onApply: (script: string) => void
    /**
     * Whether the voice can act on delivery tags. A non-v3 voice SPEAKS a tag, so the delivery mode
     * is withheld entirely rather than shown disabled — the fail-closed rule the tone palette uses.
     */
    readonly toneSupported: boolean
    /**
     * The resolved voice model, named by the "cannot use delivery tones" warning when
     * `toneSupported` is false — the model IS the reason the levers are withheld.
     */
    readonly toneModel?: string
    /**
     * The caller's own script tools (e.g. the tone palette and the one help
     * hint), placed at the end of the same rail. Everything that edits the
     * draft belongs on one line above it, not in a second stacked panel.
     */
    readonly trailing?: ReactNode
}

/**
 * The "ask for a change" rail: the top edge of the script editor's own frame,
 * not a panel above it. Pick the lever, describe the change, read the diff,
 * apply it or throw it away.
 *
 * It reads as a toolbar because that is what it is — a tool pointed at the
 * text directly below it. As its own bordered, headed card it looked bolted
 * on, and an operator had to cross a border to connect the instruction with
 * the script it rewrites.
 *
 * Both passes are previews. The content pass always was; the delivery pass used to overwrite the
 * draft in place with no undo and no way to see what moved in an 80-word read. Same category of
 * action, so the same safety model — and the diff is the actual product, because "spot what changed
 * by re-reading it" is not a review.
 *
 * Nothing here reaches air. Apply lands the text in the editor below, where the operator reads it
 * and presses Re-voice, and the safety reviewer sees the changed words on submit.
 */
export function ScriptAssist({
    jobId,
    draft,
    onApply,
    toneSupported,
    toneModel,
    trailing,
}: ScriptAssistProps) {
    // Content is the mode that works on every voice, so it is the fallback when delivery is withheld.
    const [mode, setMode] = useState<AssistMode>(
        toneSupported ? 'delivery' : 'content'
    )
    const active = toneSupported ? mode : 'content'
    const spec = MODES[active]

    // One box per mode: switching levers must not silently discard the sentence already typed.
    const [asks, setAsks] = useState<Record<AssistMode, string>>({
        delivery: '',
        content: '',
    })
    const ask = asks[active]
    const setAsk = (value: string) =>
        setAsks((prev) => ({ ...prev, [active]: value }))

    const [running, setRunning] = useState(false)
    const [error, setError] = useState<string | null>(null)
    const [preview, setPreview] = useState<AssistPreview | null>(null)

    const refusal = useMemo(
        () => (error ? parseRewriteRefusal(error) : null),
        [error]
    )

    // Unchanged sentences match and disappear; changed ones render as removed/added pairs. The
    // shared entity-diff engine, fed one-sentence-per-line arrays.
    const diff = useMemo(() => {
        if (!preview) return []
        const before = formatScriptForEditing(draft).split('\n').filter(Boolean)
        const after = formatScriptForEditing(preview.script)
            .split('\n')
            .filter(Boolean)
        return diffEntities({ Sentences: before }, { Sentences: after })
    }, [draft, preview])

    const run = async () => {
        const instruction = ask.trim()
        const current = draft.trim()
        if (!instruction || !current || !jobId || running) return
        setRunning(true)
        setError(null)
        setPreview(null)
        try {
            if (active === 'delivery') {
                const res = await retoneScript({
                    jobId,
                    text: current,
                    intent: instruction,
                })
                if (!res.script) throw new Error('No rewrite came back.')
                setPreview({
                    mode: 'delivery',
                    script: res.script,
                    wordsBefore: res.wordsBefore,
                    wordsAfter: res.wordsAfter,
                    preservedLines: res.preservedLines,
                    tags: res.tags,
                })
            } else {
                const res = await rewriteScript({
                    jobId,
                    text: current,
                    instruction,
                })
                if (!res.script) throw new Error('No rewrite came back.')
                setPreview({
                    mode: 'content',
                    script: res.script,
                    wordsBefore: res.wordsBefore,
                    wordsAfter: res.wordsAfter,
                    preservedLines: res.preservedLines,
                    tags: [],
                })
            }
        } catch (err) {
            // A safety refusal or transport error leaves the instruction in the box so the operator
            // can edit and retry rather than retyping from scratch.
            setError(
                err instanceof Error
                    ? err.message
                    : 'Could not rewrite that script.'
            )
        } finally {
            setRunning(false)
        }
    }

    const applyPreview = () => {
        if (!preview) return
        onApply(formatScriptForEditing(preview.script))
        setPreview(null)
        setAsk('')
    }

    const canRun = Boolean(ask.trim()) && Boolean(draft.trim()) && !running

    return (
        <div
            role="group"
            aria-label="Change this script"
            className="flex flex-col gap-2 border-b border-border/60 bg-muted/20 p-2"
        >
            {/* One rail, read left to right: which lever, what to change, go —
                then the caller's manual tools. Every control is h-8 so the row
                has a single baseline. */}
            <div className="flex flex-wrap items-center gap-2">
                {toneSupported ? (
                    <ToggleGroup
                        type="single"
                        value={active}
                        onValueChange={(next) => {
                            if (next) setMode(next as AssistMode)
                        }}
                        aria-label="What to change"
                        className="shrink-0 gap-1"
                    >
                        {(['delivery', 'content'] as const).map((key) => (
                            <ToggleGroupItem
                                key={key}
                                value={key}
                                className="h-8 rounded-md border border-border/60 px-2.5 text-2xs data-[state=on]:border-primary data-[state=on]:bg-primary/10 data-[state=on]:text-primary-on-tint"
                            >
                                {MODES[key].label}
                            </ToggleGroupItem>
                        ))}
                    </ToggleGroup>
                ) : (
                    /* Why the Delivery lever is missing, in the space the toggle
                       would occupy — as an icon, not a sentence: it explains an
                       absence, so it must not out-shout the control that is here. */
                    <Tooltip>
                        <TooltipTrigger asChild>
                            <span
                                tabIndex={0}
                                role="img"
                                aria-label={`This voice (${toneModel || 'unknown'}) cannot use delivery tones, so only the content lever is offered.`}
                                className="inline-flex size-8 shrink-0 cursor-default items-center justify-center text-warning"
                            >
                                <AlertTriangle
                                    className="size-3.5"
                                    aria-hidden
                                />
                            </span>
                        </TooltipTrigger>
                        <TooltipContent side="top" className="max-w-sm">
                            This voice ({toneModel || 'unknown'}) cannot use
                            delivery tones: it reads a tag out loud, so
                            “[excitedly]” would air as the word “excitedly”.
                            Shape the read with sentence length, full stops and
                            CAPITALS instead; those work on every voice.
                            {draft.includes('[') &&
                                ' Any tag left in this text is removed before it airs.'}
                        </TooltipContent>
                    </Tooltip>
                )}
                {/* `min-w-50` rather than `min-w-0`: the field keeps a usable
                    width and the rail wraps to a second line instead, which is
                    what a narrow viewport needs from a one-line instruction. */}
                <Input
                    value={ask}
                    onChange={(e) => setAsk(e.target.value)}
                    onKeyDown={(e) => {
                        if (e.key === 'Enter' && canRun) {
                            e.preventDefault()
                            void run()
                        }
                    }}
                    placeholder={spec.placeholder}
                    aria-label={spec.ariaLabel}
                    className="h-8 min-w-50 flex-1 text-xs"
                />
                <Button
                    variant="outline"
                    size="sm"
                    onClick={run}
                    disabled={!canRun}
                    className="h-8 shrink-0"
                >
                    {running ? (
                        <Spinner data-icon="inline-start" />
                    ) : (
                        <Sparkles data-icon="inline-start" />
                    )}
                    {running ? 'Rewriting…' : 'Rewrite'}
                </Button>
                {trailing}
            </div>

            {refusal && (
                <div
                    role="alert"
                    className="flex flex-col gap-1.5 rounded-md border border-destructive/40 bg-destructive/5 p-2"
                >
                    <p className="flex items-center gap-1.5 text-xs font-medium text-destructive">
                        <AlertTriangle
                            className="size-3.5 shrink-0"
                            aria-hidden
                        />
                        {refusal.headline}
                    </p>
                    {refusal.claims.length > 0 && (
                        <ul className="flex flex-col gap-1.5">
                            {refusal.claims.map((claim, i) => (
                                <li
                                    key={`${claim.quote ?? ''}-${i}`}
                                    className="text-2xs text-muted-foreground"
                                >
                                    {claim.quote && (
                                        <span className="text-foreground">
                                            &ldquo;{claim.quote}&rdquo;{' '}
                                        </span>
                                    )}
                                    {claim.scope && (
                                        <span className="mr-1 rounded bg-muted/60 px-1 font-mono text-[0.625rem] uppercase">
                                            {claim.scope}
                                        </span>
                                    )}
                                    {claim.reason}
                                </li>
                            ))}
                        </ul>
                    )}
                    {refusal.detail && (
                        <p className="text-2xs text-muted-foreground">
                            {refusal.detail}
                        </p>
                    )}
                    {refusal.remedy && (
                        <p className="text-2xs text-muted-foreground">
                            {refusal.remedy}
                        </p>
                    )}
                </div>
            )}

            {preview && (
                <div className="flex flex-col gap-2 rounded-md border border-border/60 bg-background/60 p-2">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                        <span className="text-2xs text-muted-foreground">
                            Preview · {MODES[preview.mode].label} ·{' '}
                            <span className="tabular-nums">
                                {preview.wordsBefore}
                            </span>{' '}
                            →{' '}
                            <span className="tabular-nums">
                                {preview.wordsAfter}
                            </span>{' '}
                            words
                            {preview.tags.length > 0 &&
                                ` · ${preview.tags.join(' ')}`}
                        </span>
                        <div className="ml-auto flex items-center gap-1">
                            <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => setPreview(null)}
                            >
                                Discard
                            </Button>
                            <Button
                                variant="outline"
                                size="sm"
                                onClick={applyPreview}
                            >
                                Apply to editor
                            </Button>
                        </div>
                    </div>
                    {preview.preservedLines > 0 && (
                        <p className="flex items-start gap-1.5 text-2xs text-muted-foreground">
                            <Lock
                                className="mt-0.5 size-3 shrink-0"
                                aria-hidden
                            />
                            {preview.preservedLines} station{' '}
                            {preview.preservedLines === 1 ? 'line' : 'lines'}{' '}
                            (bookend or filler) kept exactly as written. Those
                            are ARN&rsquo;s own copy and are never rewritten.
                        </p>
                    )}
                    {/* A full-bulletin rewrite can diff a dozen sentences; cap it so Apply and the
                        editor below stay reachable without a long scroll. */}
                    <div className="max-h-64 overflow-y-auto">
                        <EntityDiffView entries={diff} />
                    </div>
                </div>
            )}
        </div>
    )
}
