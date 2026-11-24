import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { toast } from 'sonner'
import { Loader2, BookText, Radio } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import {
    StudioDialog,
    StudioBody,
    StudioRail,
    StudioCanvas,
    StudioErrorSummary,
    StudioFooter,
    type StudioStep,
} from '@/components/studio/Studio'
import { useStudioStepGate } from '@/components/studio/use-studio-step-gate'
import { StudioSubmitButton } from '@/components/studio/StudioSubmitButton'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Label } from '@/components/ui/label'
import { FieldLabel } from '@/components/FieldLabel'
import { InfoHint } from '@/components/InfoHint'
import { cn } from '@/lib/utils'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'
import { useDebounce } from '@/hooks/use-debounce'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import {
    useBookend,
    useCreateBookend,
    useUpdateBookend,
    useBookendPreview,
} from '../api/bookend-hooks'
import { useBookendAssignments } from '../lib/assignments'
import { BOOKEND_COPY } from '../lib/copy'
import { BUILT_IN_TOKENS, extractTokensAcross } from '../lib/tokens'
import type { Bookend } from '../types'
import { BookendVersionHistory } from './VersionHistory'
import { LiveEditConfirmationDialog } from '@/components/LiveEditConfirmationDialog'
import { LiveAssignmentNotice } from '@/components/LiveAssignmentNotice'

/** Backend BookendTemplate.MaxTextLength — enforced here so the save never 400s. */
const MAX_BOOKEND_TEXT_LENGTH = 500

/** Backend ConfigValidationLimits.BookendNameMax — enforced here so the save never 400s. */
const MAX_BOOKEND_NAME_LENGTH = 120

/** Backend ConfigValidationLimits.BookendDescriptionMax — enforced here so the save never 400s. */
const MAX_BOOKEND_DESCRIPTION_LENGTH = 1000

/** Backend BookendTemplate.Validate failure for an empty pair. */
const BOTH_BLANK_MESSAGE = 'A bookend needs an intro, an outro, or both.'

type StepId = 'text' | 'history'
type StudioMode = 'create' | 'edit'

/** Editor working value — both lines of the pair. */
interface BookendFormValue {
    name: string
    description: string
    introText: string
    outroText: string
    changeNote: string
}

function emptyForm(): BookendFormValue {
    return {
        name: '',
        description: '',
        introText: '',
        outroText: '',
        changeNote: '',
    }
}

/** Seed the editor from a bookend's current template (for Edit). */
function bookendForm(bookend: Bookend): BookendFormValue {
    return {
        name: bookend.name,
        description: bookend.description ?? '',
        introText: bookend.introText,
        outroText: bookend.outroText,
        changeNote: '',
    }
}

/** A representative market the author previews against (first schedule, else the Sydney default). */
interface RepresentativeStation {
    readonly marketId: string
    readonly station: string
}

function useRepresentativeStation(): RepresentativeStation {
    const { data } = useSchedules({ refreshInterval: 0 })
    const schedule = data?.schedules[0]
    return {
        marketId: schedule?.marketId ?? 'sydney',
        station: schedule?.station ?? schedule?.name ?? 'the station',
    }
}

// ---------------------------------------------------------------------------
// Token insight: live placeholder detection + debounced sample resolution
// ---------------------------------------------------------------------------

const BUILT_IN_SET = new Set(
    BUILT_IN_TOKENS.map((token) => token.toLowerCase())
)

interface BookendTokenInsightProps {
    /** The live (non-debounced) lines — token chips update keystroke-fast. */
    readonly tokens: readonly string[]
    readonly introText: string
    readonly outroText: string
}

/** The last preview that completed for the current input key. */
interface ResolvedPreview {
    readonly resolvedIntro: string
    readonly resolvedOutro: string
    readonly unresolvedTokens: readonly string[]
    readonly values: Record<string, string>
}

/**
 * The text step's token affordance: lists every {token} the author has typed
 * across BOTH lines, flags custom ones (which need a per-station value in the
 * schedule studio), and shows a debounced sample resolution of both lines
 * against a representative market so the author sees exactly what a station
 * will hear before saving.
 *
 * Pending/error/ready are DERIVED from a "last completed input key" rather
 * than stored: state only ever changes inside the async preview callbacks, so
 * stale results keep rendering (stale-while-revalidate) while a newer input
 * is in flight.
 */
function BookendTokenInsight({
    tokens,
    introText,
    outroText,
}: BookendTokenInsightProps) {
    const { marketId, station } = useRepresentativeStation()
    const debouncedIntro = useDebounce(introText, 400)
    const debouncedOutro = useDebounce(outroText, 400)
    const preview = useBookendPreview()
    // The mutation object is recreated every render; keep a stable handle for
    // the effect so previews only fire on text/market changes. Updated in an
    // effect (declared before the preview effect below) so it is always fresh
    // when a preview fires — writing it during render trips react-hooks/refs.
    const mutateRef = useRef(preview.mutateAsync)
    useEffect(() => {
        mutateRef.current = preview.mutateAsync
    })
    const runRef = useRef(0)
    const [doneKey, setDoneKey] = useState<string | null>(null)
    const [result, setResult] = useState<ResolvedPreview | null>(null)
    const [failed, setFailed] = useState(false)

    const inputKey = `${marketId}\u0000${station}\u0000${debouncedIntro.trim()}\u0000${debouncedOutro.trim()}`

    useEffect(() => {
        const intro = debouncedIntro.trim()
        const outro = debouncedOutro.trim()
        if (
            (!intro && !outro) ||
            extractTokensAcross([intro, outro]).length === 0
        ) {
            return
        }
        const key = `${marketId}\u0000${station}\u0000${intro}\u0000${outro}`
        const run = ++runRef.current
        void mutateRef
            .current({
                introText: intro || undefined,
                outroText: outro || undefined,
                marketId,
                station,
            })
            .then((res) => {
                if (run !== runRef.current) return
                setResult({
                    resolvedIntro: res.resolvedIntro,
                    resolvedOutro: res.resolvedOutro,
                    unresolvedTokens: res.unresolvedTokens,
                    values: res.values,
                })
                setFailed(false)
                setDoneKey(key)
            })
            .catch(() => {
                if (run !== runRef.current) return
                setFailed(true)
                setDoneKey(key)
            })
    }, [debouncedIntro, debouncedOutro, marketId, station])

    if (tokens.length === 0) return null

    const pending = doneKey !== inputKey
    const usedValues =
        result && !pending && !failed
            ? Object.entries(result.values).filter(([key]) =>
                  tokens.some(
                      (token) => token.toLowerCase() === key.toLowerCase()
                  )
              )
            : []

    const builtInList = BUILT_IN_TOKENS.map((t) => `{${t}}`).join(', ')

    return (
        <div className="flex flex-col gap-3 rounded-lg border border-border/60 bg-muted/20 p-4">
            <div className="flex flex-col gap-1.5">
                <div className="flex items-center gap-1.5">
                    <Label className="text-xs">Tokens in this bookend</Label>
                    <InfoHint label="What do custom tokens need?">
                        Custom tokens need a per-station value the operator
                        supplies in the schedule studio. Built-ins (
                        {builtInList}) are filled from the schedule
                        automatically.
                    </InfoHint>
                </div>
                <div className="flex flex-wrap gap-1.5">
                    {tokens.map((token) => {
                        const isBuiltIn = BUILT_IN_SET.has(token.toLowerCase())
                        return (
                            <span
                                key={token}
                                title={
                                    isBuiltIn
                                        ? 'Filled automatically from the schedule'
                                        : 'Needs a per-station value in the schedule studio'
                                }
                                className={cn(
                                    'rounded-full border px-2 py-px font-mono text-2xs font-medium',
                                    isBuiltIn
                                        ? 'border-border bg-background/60 text-muted-foreground'
                                        : 'border-warning/40 bg-warning/10 text-warning'
                                )}
                            >
                                {'{'}
                                {token}
                                {'}'}
                            </span>
                        )
                    })}
                </div>
            </div>

            <div className="flex flex-col gap-1.5">
                <div className="flex items-center justify-between gap-2">
                    <Label className="text-xs">Sample resolution</Label>
                    <span className="text-2xs text-muted-foreground">
                        against {marketId}
                        {station && station !== 'the station'
                            ? ` / ${station}`
                            : ''}
                    </span>
                </div>
                {pending && (
                    <p className="text-sm text-muted-foreground">Resolving…</p>
                )}
                {!pending && failed && (
                    <p className="text-sm text-destructive">
                        Could not resolve this bookend right now.
                    </p>
                )}
                {!pending && !failed && result && (
                    <div className="flex flex-col gap-1.5">
                        {(result.resolvedIntro || result.resolvedOutro) && (
                            <div className="flex flex-col gap-1.5">
                                {result.resolvedIntro && (
                                    <div>
                                        <p className="text-2xs font-medium uppercase tracking-wide text-muted-foreground">
                                            Intro
                                        </p>
                                        <p className="rounded-md border border-border/60 bg-background/60 px-3 py-2 text-sm">
                                            {result.resolvedIntro}
                                        </p>
                                    </div>
                                )}
                                {result.resolvedOutro && (
                                    <div>
                                        <p className="text-2xs font-medium uppercase tracking-wide text-muted-foreground">
                                            Outro
                                        </p>
                                        <p className="rounded-md border border-border/60 bg-background/60 px-3 py-2 text-sm">
                                            {result.resolvedOutro}
                                        </p>
                                    </div>
                                )}
                            </div>
                        )}
                        {result.unresolvedTokens.length > 0 && (
                            <p className="text-xs text-warning">
                                Could not fill:{' '}
                                {result.unresolvedTokens
                                    .map((t) => `{${t}}`)
                                    .join(', ')}
                                ; these would be dropped on air.
                            </p>
                        )}
                        {usedValues.length > 0 && (
                            <p className="text-2xs text-muted-foreground">
                                {usedValues
                                    .map(
                                        ([key, value]) => `{${key}} → ${value}`
                                    )
                                    .join('  ·  ')}
                            </p>
                        )}
                    </div>
                )}
                {!pending && !failed && !result && (
                    <p className="text-sm text-muted-foreground">
                        Add {'{token}'} placeholders to see what a station will
                        hear.
                    </p>
                )}
            </div>
        </div>
    )
}

// ---------------------------------------------------------------------------
// Steps
// ---------------------------------------------------------------------------

interface TextSectionProps {
    readonly mode: StudioMode
    readonly isAssigned: boolean
    readonly assignedStations: readonly string[]
    readonly value: BookendFormValue
    readonly onPatch: (next: Partial<BookendFormValue>) => void
}

function TextSection({
    mode,
    isAssigned,
    assignedStations,
    value,
    onPatch,
}: TextSectionProps) {
    const copy = BOOKEND_COPY
    const tokens = useMemo(
        () => extractTokensAcross([value.introText, value.outroText]),
        [value.introText, value.outroText]
    )
    const introOverLimit = value.introText.length > MAX_BOOKEND_TEXT_LENGTH
    const outroOverLimit = value.outroText.length > MAX_BOOKEND_TEXT_LENGTH

    return (
        <>
            {mode === 'edit' && isAssigned && (
                <LiveAssignmentNotice stations={assignedStations} />
            )}

            <div className="flex flex-col gap-2">
                <FieldLabel htmlFor="bookend-name" required>
                    {copy.singularTitle} name
                </FieldLabel>
                <Input
                    id="bookend-name"
                    maxLength={MAX_BOOKEND_NAME_LENGTH}
                    value={value.name}
                    placeholder="for example, Morning drive pair"
                    onChange={(e) => onPatch({ name: e.target.value })}
                />
            </div>

            <div className="flex flex-col gap-2">
                <FieldLabel
                    htmlFor="bookend-intro"
                    hintLabel="What is the opening read?"
                    hint="The opening read before the bulletin's first line. Leave blank to open straight on the first read."
                    trailing={
                        <span
                            className={cn(
                                'text-2xs tabular-nums',
                                introOverLimit
                                    ? 'text-destructive'
                                    : 'text-muted-foreground'
                            )}
                        >
                            {value.introText.length}/{MAX_BOOKEND_TEXT_LENGTH}
                        </span>
                    }
                >
                    Intro
                </FieldLabel>
                <Textarea
                    id="bookend-intro"
                    value={value.introText}
                    placeholder={copy.placeholder}
                    onChange={(e) => onPatch({ introText: e.target.value })}
                    className="min-h-32"
                />
                {introOverLimit && (
                    <p className="text-xs text-destructive">
                        Bookend text is limited to {MAX_BOOKEND_TEXT_LENGTH}{' '}
                        characters.
                    </p>
                )}
            </div>

            <div className="flex flex-col gap-2">
                <FieldLabel
                    htmlFor="bookend-outro"
                    hintLabel="What is the closing read?"
                    hint="The closing read after the bulletin's last line. Leave blank to end on the final read."
                    trailing={
                        <span
                            className={cn(
                                'text-2xs tabular-nums',
                                outroOverLimit
                                    ? 'text-destructive'
                                    : 'text-muted-foreground'
                            )}
                        >
                            {value.outroText.length}/{MAX_BOOKEND_TEXT_LENGTH}
                        </span>
                    }
                >
                    Outro
                </FieldLabel>
                <Textarea
                    id="bookend-outro"
                    value={value.outroText}
                    placeholder="for example, Thanks for joining us on {station}, safe travels."
                    onChange={(e) => onPatch({ outroText: e.target.value })}
                    className="min-h-32"
                />
                {outroOverLimit && (
                    <p className="text-xs text-destructive">
                        Bookend text is limited to {MAX_BOOKEND_TEXT_LENGTH}{' '}
                        characters.
                    </p>
                )}
            </div>

            <BookendTokenInsight
                tokens={tokens}
                introText={value.introText}
                outroText={value.outroText}
            />

            <div className="flex flex-col gap-2">
                <FieldLabel
                    htmlFor="bookend-summary"
                    hintLabel="Where is the summary shown?"
                    hint={`Shown on the ${copy.singular} card so operators recognise when to use it.`}
                >
                    Summary
                </FieldLabel>
                <Input
                    id="bookend-summary"
                    maxLength={MAX_BOOKEND_DESCRIPTION_LENGTH}
                    value={value.description}
                    placeholder="for example, Warm pair used for the morning drive bulletins."
                    onChange={(e) => onPatch({ description: e.target.value })}
                />
            </div>

            {mode === 'edit' && (
                <div className="flex flex-col gap-2">
                    <FieldLabel
                        htmlFor="bookend-change-note"
                        hintLabel="Where is the change note recorded?"
                        hint="Recorded against the new version in the history."
                    >
                        Change note (optional)
                    </FieldLabel>
                    <Input
                        id="bookend-change-note"
                        value={value.changeNote}
                        placeholder="for example, Dropped the weather hook"
                        onChange={(e) =>
                            onPatch({ changeNote: e.target.value })
                        }
                    />
                </div>
            )}
        </>
    )
}

// ---------------------------------------------------------------------------
// Studio
// ---------------------------------------------------------------------------

interface BookendStudioProps {
    readonly open: boolean
    readonly onOpenChange: (open: boolean) => void
    /** null → create a new bookend; otherwise edit the given one. */
    readonly bookendId: string | null
}

/**
 * Bookend editor — the DeliveryStyleStudio pattern, minus the audio side: the
 * author writes both lines of the pair (with live token detection + sample
 * resolution), previews what a station will hear, and saves a new version.
 *
 * Operators author here too: every bookend route except DELETE resolves to
 * Operator in `RouteRoleMatrix`, so there is no read-only variant of this
 * studio — the gallery gates Delete alone.
 */
export function BookendStudio({
    open,
    onOpenChange,
    bookendId,
}: BookendStudioProps) {
    const isCreate = bookendId === null
    const { data: bookend, isLoading } = useBookend(bookendId)
    const copy = BOOKEND_COPY
    const discardConfirm = useConfirmDialog()
    const dirtyRef = useRef(false)
    const reportDirty = useCallback((dirty: boolean) => {
        dirtyRef.current = dirty
    }, [])

    // Any close attempt — the Cancel button, the title-bar X, ESC, or a
    // backdrop click — confirms first when the editor holds unsaved edits
    // (the ScheduleStudio pattern), so typed work is never silently
    // discarded.
    const requestClose = (next: boolean) => {
        if (!next && dirtyRef.current) {
            discardConfirm.open()
            return
        }
        onOpenChange(next)
    }

    return (
        <>
            <StudioDialog
                open={open}
                onOpenChange={requestClose}
                title={
                    isCreate ? `New ${copy.singular}` : `Edit ${copy.singular}`
                }
                subject={isCreate ? undefined : bookend?.name}
                description={`Author the ${copy.singular} pair that wraps every bulletin read.`}
            >
                {!isCreate && isLoading ? (
                    <div className="flex min-h-0 flex-1 items-center justify-center text-muted-foreground">
                        <Loader2 className="size-5 animate-spin" />
                    </div>
                ) : !isCreate && !bookend ? (
                    <div className="flex min-h-0 flex-1 items-center justify-center text-sm text-muted-foreground">
                        {copy.title} not found.
                    </div>
                ) : (
                    <BookendStudioInner
                        key={bookend?.id ?? 'new'}
                        bookend={bookend ?? null}
                        // The inner component's close contract is arity-zero;
                        // a close request means "close now, guard applies".
                        onClose={() => requestClose(false)}
                        onDirtyChange={reportDirty}
                    />
                )}
            </StudioDialog>
            {discardConfirm.isOpen && (
                <ConfirmDialog
                    open={discardConfirm.isOpen}
                    onOpenChange={discardConfirm.onOpenChange}
                    onConfirm={() => onOpenChange(false)}
                    title="Discard unsaved changes?"
                    description={`Unsaved edits to this ${copy.singular} will be lost.`}
                    confirmLabel="Discard"
                    destructive
                />
            )}
        </>
    )
}

interface BookendStudioInnerProps {
    readonly bookend: Bookend | null
    readonly onClose: () => void
    /** Reports whether the editor currently holds unsaved edits (for the
     *  wrapper's close guard — this studio keeps its working value in local
     *  state, so the wrapper cannot read dirtiness itself). */
    readonly onDirtyChange: (dirty: boolean) => void
}

function BookendStudioInner({
    bookend,
    onClose,
    onDirtyChange,
}: BookendStudioInnerProps) {
    const isEdit = bookend !== null
    const copy = BOOKEND_COPY
    const { byBookendId } = useBookendAssignments()
    const assignedStations = bookend ? (byBookendId.get(bookend.id) ?? []) : []

    const [step, setStep] = useState<StepId>('text')
    const [value, setValue] = useState<BookendFormValue>(() =>
        bookend ? bookendForm(bookend) : emptyForm()
    )
    // Last state that is safely persisted (the seed, or the content after a
    // successful save). Divergence means unsaved edits exist.
    const [savedValue, setSavedValue] = useState(value)
    // The form is flat (name, description, both lines, change note), so plain
    // JSON comparison is deterministic.
    const isDirty = JSON.stringify(value) !== JSON.stringify(savedValue)
    useEffect(() => {
        onDirtyChange(isDirty)
    }, [isDirty, onDirtyChange])
    const [confirmOpen, setConfirmOpen] = useState(false)

    const create = useCreateBookend()
    const update = useUpdateBookend()

    const isAssigned = assignedStations.length > 0
    const saving = create.isPending || update.isPending
    const nextVersion = (bookend?.currentVersion ?? 0) + 1

    const textStepInvalid =
        !value.name.trim() ||
        (!value.introText.trim() && !value.outroText.trim()) ||
        value.introText.length > MAX_BOOKEND_TEXT_LENGTH ||
        value.outroText.length > MAX_BOOKEND_TEXT_LENGTH
    const canSave = !textStepInvalid && !saving

    const blockHelper = (() => {
        if (!value.name.trim()) return 'Give this bookend a name before saving.'
        if (!value.introText.trim() && !value.outroText.trim())
            return BOTH_BLANK_MESSAGE
        if (value.introText.length > MAX_BOOKEND_TEXT_LENGTH)
            return `Bookend text is limited to ${MAX_BOOKEND_TEXT_LENGTH} characters.`
        if (value.outroText.length > MAX_BOOKEND_TEXT_LENGTH)
            return `Bookend text is limited to ${MAX_BOOKEND_TEXT_LENGTH} characters.`
        return ''
    })()

    const patch = (next: Partial<BookendFormValue>) =>
        setValue((v) => ({ ...v, ...next }))

    const doSave = async () => {
        const name = value.name.trim()
        try {
            if (!isEdit) {
                await create.mutateAsync({
                    name,
                    description: value.description.trim() || null,
                    introText: value.introText.trim(),
                    outroText: value.outroText.trim(),
                    changeNote: null,
                })
                toast.success(`${copy.singularTitle} created`, {
                    description: `${name} is ready to assign in station schedules.`,
                })
                // The create is saved — closing now discards nothing, so the
                // close guard must not prompt.
                setSavedValue(value)
                onDirtyChange(false)
                onClose()
                return
            }
            const saved = await update.mutateAsync({
                id: bookend.id,
                body: {
                    name,
                    description: value.description.trim() || null,
                    introText: value.introText.trim(),
                    outroText: value.outroText.trim(),
                    changeNote: value.changeNote.trim() || null,
                },
            })
            const v = saved.currentVersion
            if (isAssigned) {
                toast.success('Live version saved', {
                    description: `${name} now uses v${v}. Assigned stations will use it from the next bulletin.`,
                })
            } else {
                toast.success('New version saved', {
                    description: `${name} now uses v${v}.`,
                })
            }
            setValue((cur) => ({ ...cur, changeNote: '' }))
            // v{currentVersion} now reflects `value` — the studio stays open on
            // the history step, but closing it no longer loses anything.
            setSavedValue({ ...value, changeNote: '' })
            setStep('history')
        } catch (err: unknown) {
            // Keep the server's reason (validation text, version-size wall, revision conflict):
            // "try again" in front of a deterministic rejection sends the operator in circles.
            toast.error(`Could not save ${copy.singular}`, {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        } finally {
            setConfirmOpen(false)
        }
    }

    const handleSubmit = () => {
        gate.onSubmitAttempt()
        if (!canSave) return
        if (isEdit && isAssigned) {
            setConfirmOpen(true)
            return
        }
        void doSave()
    }

    const saveLabel = isEdit ? 'Save new version' : `Save ${copy.singular}`

    const tokenCount = extractTokensAcross([
        value.introText,
        value.outroText,
    ]).length
    const steps: StudioStep[] = [
        {
            id: 'text',
            label: 'Text',
            icon: BookText,
            summary: value.name.trim() || 'Name & text',
            invalid: textStepInvalid,
        },
        ...(bookend
            ? [
                  {
                      id: 'history',
                      label: 'History',
                      icon: ICON.history,
                      summary: `v${bookend.currentVersion} · ${bookend.versions.length} version${bookend.versions.length === 1 ? '' : 's'}`,
                  } satisfies StudioStep,
              ]
            : []),
    ]

    // Validation indicators stay hidden until the Text step is visited or a
    // save is attempted — a fresh studio does not scold before the operator
    // has looked at anything.
    const gate = useStudioStepGate(steps, step)

    return (
        <form
            onSubmit={(e) => {
                e.preventDefault()
                handleSubmit()
            }}
            className="flex min-h-0 flex-1 flex-col"
        >
            <StudioBody>
                <StudioRail
                    title={
                        isEdit
                            ? `Edit ${copy.singular}`
                            : `New ${copy.singular}`
                    }
                    steps={gate.steps}
                    activeId={step}
                    onSelect={(id) => {
                        gate.onSelectStep(id)
                        setStep(id as StepId)
                    }}
                />
                <StudioCanvas stepKey={step}>
                    {step === 'text' && (
                        <TextSection
                            mode={isEdit ? 'edit' : 'create'}
                            isAssigned={isAssigned}
                            assignedStations={assignedStations}
                            value={value}
                            onPatch={patch}
                        />
                    )}

                    {step === 'history' && bookend && (
                        <BookendVersionHistory bookend={bookend} />
                    )}
                </StudioCanvas>
            </StudioBody>

            <StudioErrorSummary
                steps={gate.steps}
                onSelect={(id) => setStep(id as StepId)}
            />

            <StudioFooter>
                <div className="flex min-w-0 flex-1 flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
                    <span className="font-mono">
                        {tokenCount} token{tokenCount === 1 ? '' : 's'}
                    </span>
                    {isEdit && (
                        <span className="inline-flex items-center gap-1 border-l border-border/60 pl-3">
                            <Radio className="size-3" />
                            {assignedStations.length} station
                            {assignedStations.length === 1 ? '' : 's'}
                        </span>
                    )}
                </div>
                <div className="flex items-center gap-2">
                    <Button
                        type="button"
                        variant="ghost"
                        onClick={onClose}
                        disabled={saving}
                    >
                        Cancel
                    </Button>
                    <StudioSubmitButton
                        label={saveLabel}
                        isSubmitting={saving}
                        isValid={canSave}
                        disabledReason={blockHelper}
                    />
                </div>
            </StudioFooter>

            <LiveEditConfirmationDialog
                open={confirmOpen}
                onOpenChange={setConfirmOpen}
                entityName={value.name.trim()}
                noun={copy.singular}
                stations={assignedStations}
                nextVersion={nextVersion}
                changeNote={value.changeNote.trim()}
                isSaving={saving}
                onConfirm={() => void doSave()}
                guidance="Bookends wrap the bulletin read and never change the bulletin words."
            />
        </form>
    )
}
