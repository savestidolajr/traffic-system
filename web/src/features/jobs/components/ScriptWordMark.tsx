import { useEffect, useRef, useState, type ReactNode } from 'react'
import { ICON } from '@/lib/design/icons'
import { Button } from '@/components/ui/button'
import { InfoHint } from '@/components/InfoHint'
import { Popover, PopoverAnchor, PopoverContent } from '@/components/ui/popover'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'
import type { ScriptMark } from '../lib/script-marks'
import { RespellingField } from './RespellingField'

/**
 * Why a word is marked. Kept OUT of the default view on purpose — the popover
 * is a place to act, not to read. An operator who already knows what amber
 * means should see two controls, not two paragraphs explaining them.
 */
const STATE_HINT: Record<string, string> = {
    suspect:
        "The transcript of the aired audio didn't contain this word. Listen before approving.",
    verified:
        'Someone listened and confirmed this is correct, so it no longer counts as a word to check.',
    figure: "The transcript couldn't confirm this figure aired as written. Listen before approving. There is nothing to teach the voice: figures are only checked against the transcript.",
    dictionary:
        'The written script stays unchanged. The voice reads this word differently on air.',
    pending:
        'You changed this word during this review. The script stays as written; re-voice for listeners to hear it.',
}

const HOVER_CLOSE_DELAY_MS = 140

/** Verdict actions. Omitted on the edit surface and for read-only viewers. */
export interface ScriptMarkActions {
    readonly onVerify: (word: string) => void
    readonly onUnverify: (word: string) => void
    readonly onAddRule: (word: string, replacement: string) => Promise<void>
}

interface ScriptWordMarkProps {
    readonly mark: ScriptMark
    readonly children: ReactNode
    readonly className?: string
    readonly wordMs?: number
    /** Absent = informational popover only. */
    readonly actions?: ScriptMarkActions
    /** Enables the "teach the dictionary" action; null when no dictionary is attached. */
    readonly dictionaryName?: string | null
    /** Whole known road phrase to use when this mark covers one road token. */
    readonly respellTarget?: string
}

/** Amber for anything needing an ear, quiet grey once resolved, primary for a plain rule. */
function markClassName(mark: ScriptMark): string {
    const kind = mark.review?.kind
    if (kind === 'suspect' || kind === 'figure') {
        return 'bg-warning/15 decoration-warning/70 ring-warning/30 hover:bg-warning/25'
    }
    if (kind === 'verified') {
        return 'bg-muted/50 decoration-muted-foreground/50 ring-border/60 hover:bg-muted/80'
    }
    return 'bg-primary/15 decoration-primary/70 ring-primary/30 hover:bg-primary/25'
}

/**
 * The accessible name carries the facts a sighted operator gets from the
 * highlight colour PLUS the substitution, so a screen-reader user never has to
 * open the popover to learn what the voice actually said.
 */
function describe(mark: ScriptMark): string {
    const dictionary = mark.dictionary
        ? `${mark.text} is read on air as ${mark.dictionary.replacement}. From ${mark.dictionary.dictionaryName}, version ${mark.dictionary.versionNumber}.`
        : null
    const suffix = dictionary ? ` ${dictionary}` : ''
    switch (mark.review?.kind) {
        case 'suspect':
            return `${mark.text}: verify by ear.${suffix}`
        case 'verified':
            return `${mark.text}: verified as correct.${suffix}`
        case 'figure':
            return `${mark.text}: aired figure not confirmed.`
        default:
            // A rule saved in this review has no aired substitution to report —
            // the audio still says the old thing until the bulletin is re-voiced.
            if (!dictionary)
                return `${mark.text}: queued to be read as ${mark.pendingReplacement}, from the next re-voice.`
            return `Pronunciation dictionary: ${dictionary}`
    }
}

/**
 * One marked word in the script, and the only place its state is shown.
 *
 * The script already anchors every signal an operator needs about a word: the
 * dictionary substitution applied on air, whether the audio pre-review could
 * hear it, and the standing verdict if someone has already checked. Rendering
 * any of that a second time in a summary row meant the same word appeared twice
 * with two different affordances, so all of it lives here now.
 *
 * Clicking still seeks (inherited from the script's word-seek handler); hover or
 * focus opens the popover that explains the state and carries the verdicts.
 */
export function ScriptWordMark({
    mark,
    children,
    className,
    wordMs,
    actions,
    dictionaryName,
    respellTarget,
}: ScriptWordMarkProps) {
    const [open, setOpen] = useState(false)
    const closeTimerRef = useRef<number | null>(null)
    const markRef = useRef<HTMLElement>(null)
    // Opening the popover moves focus off the word, firing blur while the
    // pointer is still on it. Closing on that blur would put every action out
    // of reach, so a blur only closes once the pointer has genuinely left.
    const pointerInsideRef = useRef(false)

    const kind = mark.review?.kind
    const verdict = mark.review?.verdict ?? null
    const isFigure = kind === 'figure'
    // A word marked only by a rule the operator saved during this review: no
    // aired substitution yet, and never on the check list.
    const isPendingOnly =
        kind === undefined &&
        mark.dictionary === undefined &&
        mark.pendingReplacement !== undefined
    const hintKey = kind ?? (isPendingOnly ? 'pending' : 'dictionary')
    // Only a word the pre-review actually flagged has a verdict to give. A word
    // marked purely because a dictionary rule rewrites it was never on the check
    // list, so "Sounded fine" there would create a verdict about nothing.
    const canVerify =
        actions !== undefined && (kind === 'suspect' || kind === 'verified')
    const canRespell =
        actions !== undefined && !isFigure && Boolean(dictionaryName)
    // Nothing to do here — an unconfirmed figure, the edit surface, or a
    // read-only viewer. A popover would then be an empty box whose only content
    // is a "?" hiding the one thing worth saying, so say it directly instead.
    const hasActions = canVerify || canRespell

    const cancelClose = () => {
        window.clearTimeout(closeTimerRef.current ?? undefined)
        closeTimerRef.current = null
    }

    const openPopover = () => {
        cancelClose()
        setOpen(true)
    }

    const scheduleClose = () => {
        cancelClose()
        closeTimerRef.current = window.setTimeout(
            () => setOpen(false),
            HOVER_CLOSE_DELAY_MS
        )
    }

    const handlePointerEnter = () => {
        pointerInsideRef.current = true
        openPopover()
    }

    const handlePointerLeave = () => {
        pointerInsideRef.current = false
        scheduleClose()
    }

    useEffect(
        () => () => window.clearTimeout(closeTimerRef.current ?? undefined),
        []
    )

    const markClasses = cn(
        'rounded-sm px-0.5 text-inherit underline decoration-2 underline-offset-[3px] ring-1 ring-inset transition-[background-color,box-shadow] focus-visible:outline-none focus-visible:ring-2',
        markClassName(mark),
        className
    )

    if (!hasActions) {
        return (
            <Tooltip>
                <TooltipTrigger asChild>
                    <mark
                        contentEditable={false}
                        data-pronunciation-word
                        data-word-ms={wordMs}
                        tabIndex={0}
                        // Still a button: clicking it seeks the audio via the
                        // script's word handler, and it must be keyboard-reachable
                        // for the tooltip to be readable at all.
                        role="button"
                        aria-label={describe(mark)}
                        className={markClasses}
                    >
                        {children}
                    </mark>
                </TooltipTrigger>
                <TooltipContent side="top" className="max-w-xs">
                    {STATE_HINT[hintKey]}
                    {mark.dictionary
                        ? ` Said as "${mark.dictionary.replacement}" (${mark.dictionary.dictionaryName} v${mark.dictionary.versionNumber}).`
                        : ''}
                </TooltipContent>
            </Tooltip>
        )
    }

    return (
        <Popover open={open} onOpenChange={setOpen}>
            <PopoverAnchor asChild>
                <mark
                    ref={markRef}
                    contentEditable={false}
                    data-pronunciation-word
                    data-word-ms={wordMs}
                    tabIndex={0}
                    role="button"
                    aria-label={describe(mark)}
                    onMouseEnter={handlePointerEnter}
                    onMouseLeave={handlePointerLeave}
                    onFocus={openPopover}
                    onBlur={() => {
                        if (!pointerInsideRef.current) scheduleClose()
                    }}
                    onClick={openPopover}
                    onKeyDown={(event) => {
                        if (event.key === 'Enter' || event.key === ' ') {
                            event.preventDefault()
                            openPopover()
                        }
                    }}
                    className={markClasses}
                >
                    {children}
                </mark>
            </PopoverAnchor>
            <PopoverContent
                align="start"
                sideOffset={6}
                className="w-64"
                onMouseEnter={handlePointerEnter}
                onMouseLeave={handlePointerLeave}
                onOpenAutoFocus={(event) => event.preventDefault()}
                onCloseAutoFocus={(event) => event.preventDefault()}
                // The marked word is the ANCHOR, which sits outside the content,
                // so Radix would treat clicking it (to seek) as a dismissal and
                // the actions would vanish under the cursor. Exempt it only.
                onInteractOutside={(event) => {
                    if (markRef.current?.contains(event.target as Node))
                        event.preventDefault()
                }}
            >
                <div className="flex flex-col gap-2">
                    <div className="flex items-center gap-1.5">
                        <span className="min-w-0 flex-1 truncate text-sm font-semibold">
                            {mark.text}
                        </span>
                        <InfoHint label={`Why is ${mark.text} marked?`}>
                            {STATE_HINT[hintKey]}
                        </InfoHint>
                    </div>

                    {mark.pendingReplacement ? (
                        <p className="text-2xs font-medium text-warning">
                            Queued: will say “{mark.pendingReplacement}”.
                            Re-voice to apply
                        </p>
                    ) : (
                        mark.dictionary && (
                            <p className="truncate text-2xs text-muted-foreground">
                                Said as{' '}
                                <span className="font-medium text-primary">
                                    {mark.dictionary.replacement}
                                </span>{' '}
                                · {mark.dictionary.dictionaryName} v
                                {mark.dictionary.versionNumber}
                            </p>
                        )
                    )}

                    {verdict && (
                        <p className="text-2xs text-muted-foreground">
                            {verdict.note ? `${verdict.note} · ` : ''}
                            {verdict.verifiedBy ?? 'Unknown'}
                            {verdict.verifiedAt
                                ? ` · ${new Date(verdict.verifiedAt).toLocaleDateString()}`
                                : ''}
                        </p>
                    )}

                    {canVerify && actions && (
                        <Button
                            variant="outline"
                            size="sm"
                            className="justify-start"
                            onClick={() => {
                                if (kind === 'verified')
                                    actions.onUnverify(mark.text)
                                else actions.onVerify(mark.text)
                                setOpen(false)
                            }}
                        >
                            {kind === 'verified' ? (
                                <>
                                    <ICON.reset />
                                    Needs another listen
                                </>
                            ) : (
                                <>
                                    <ICON.success />
                                    Sounded fine
                                </>
                            )}
                        </Button>
                    )}

                    {actions && !isFigure && dictionaryName && (
                        <RespellingField
                            word={respellTarget ?? mark.text}
                            dictionaryName={dictionaryName}
                            hasExistingRule={mark.dictionary !== undefined}
                            note={
                                kind
                                    ? 'It may not stop the flag on its own.'
                                    : undefined
                            }
                            onSave={async (replacement) => {
                                await actions.onAddRule(
                                    respellTarget ?? mark.text,
                                    replacement
                                )
                                setOpen(false)
                            }}
                        />
                    )}
                </div>
            </PopoverContent>
        </Popover>
    )
}
