import { useCallback, useEffect, useRef, useState } from 'react'
import { toast } from 'sonner'
import { Loader2, List, History, TriangleAlert } from 'lucide-react'
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
import { FieldLabel } from '@/components/FieldLabel'
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from '@/components/ui/select'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'
import {
    usePronunciationDictionary,
    useCreatePronunciationDictionary,
    useUpdatePronunciationDictionary,
} from '../api/pronunciation-hooks'
import { useDictionaryAssignments } from '../lib/assignments'
import {
    dictionaryForm,
    versionForm,
    ruleSetProblem,
    DEFAULT_FORM,
    type PronunciationFormValue,
} from '../lib/pronunciation-form'
import type { Dictionary, DictionaryVersion, DictionaryScope } from '../types'
import { PronunciationRuleTable } from './PronunciationRuleTable'
import { VersionHistory } from './VersionHistory'
import { LiveEditConfirmationDialog } from '@/components/LiveEditConfirmationDialog'
import { LiveAssignmentNotice } from '@/components/LiveAssignmentNotice'

type StepId = 'rules' | 'history'
type StudioMode = 'create' | 'edit' | 'restore'

/** Backend ConfigValidationLimits.PronunciationDictionaryNameMax — enforced here so the save never 400s. */
const MAX_DICTIONARY_NAME_LENGTH = 120

/** Backend ConfigValidationLimits.PronunciationDictionaryDescriptionMax — enforced here so the save never 400s. */
const MAX_DICTIONARY_DESCRIPTION_LENGTH = 1000

interface PronunciationDictionaryStudioProps {
    readonly open: boolean
    readonly onOpenChange: (open: boolean) => void
    readonly dictionaryId: string | null
    /** Optional word to pre-fill as an alias rule (from the ?suggest= query param). */
    readonly suggestWord?: string | null
}

/**
 * Pronunciation dictionary editor — the Delivery Studio pattern.
 * Two steps: rules (define word→pronunciation mappings) and version history.
 * Auditioning happens per row inside the rules table, in the voice picked
 * there — a whole-dictionary preview step told the operator less and cost a
 * synth per press.
 */
export function PronunciationDictionaryStudio({
    open,
    onOpenChange,
    dictionaryId,
    suggestWord,
}: PronunciationDictionaryStudioProps) {
    const isCreate = dictionaryId === null
    const { data: dict, isLoading } = usePronunciationDictionary(dictionaryId)
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
                title={isCreate ? 'New dictionary' : 'Edit dictionary'}
                subject={isCreate ? undefined : dict?.name}
                description="Manage how ElevenLabs TTS pronounces place names, road names, and other words."
            >
                {!isCreate && isLoading ? (
                    <div className="flex min-h-0 flex-1 items-center justify-center text-muted-foreground">
                        <Loader2 className="size-5 animate-spin" />
                    </div>
                ) : !isCreate && !dict ? (
                    <div className="flex min-h-0 flex-1 items-center justify-center text-sm text-muted-foreground">
                        Dictionary not found.
                    </div>
                ) : (
                    <PronunciationDictionaryStudioInner
                        key={dict?.id ?? 'new'}
                        dict={dict ?? null}
                        // The inner component's close contract is arity-zero;
                        // a close request means "close now, guard applies".
                        onClose={() => requestClose(false)}
                        onDirtyChange={reportDirty}
                        suggestWord={suggestWord ?? null}
                    />
                )}
            </StudioDialog>
            {discardConfirm.isOpen && (
                <ConfirmDialog
                    open={discardConfirm.isOpen}
                    onOpenChange={discardConfirm.onOpenChange}
                    onConfirm={() => onOpenChange(false)}
                    title="Discard unsaved changes?"
                    description="Unsaved edits to this dictionary will be lost."
                    confirmLabel="Discard"
                    destructive
                />
            )}
        </>
    )
}

interface PronunciationDictionaryStudioInnerProps {
    readonly dict: Dictionary | null
    readonly onClose: () => void
    /** Reports whether the editor currently holds unsaved edits (for the
     *  wrapper's close guard — this studio keeps its working value in local
     *  state, not RHF, so the wrapper cannot read dirtiness itself). */
    readonly onDirtyChange: (dirty: boolean) => void
    readonly suggestWord: string | null
}

function PronunciationDictionaryStudioInner({
    dict,
    onClose,
    onDirtyChange,
    suggestWord,
}: PronunciationDictionaryStudioInnerProps) {
    const isEdit = dict !== null
    const { byDictionaryId, stationCount } = useDictionaryAssignments()
    const assignedStations = dict ? (byDictionaryId.get(dict.id) ?? []) : []

    const [step, setStep] = useState<StepId>('rules')
    const [pendingScopeChange, setPendingScopeChange] = useState(false)
    const [confirmOpen, setConfirmOpen] = useState(false)
    const [restoreSource, setRestoreSource] = useState<number | null>(null)
    const [value, setValue] = useState<PronunciationFormValue>(() => {
        if (dict) return dictionaryForm(dict)
        // If suggestWord is provided, pre-fill the first alias rule
        if (suggestWord) {
            return {
                ...DEFAULT_FORM,
                rules: [
                    {
                        type: 'alias',
                        stringToReplace: suggestWord,
                        replacement: '',
                        alphabet: null,
                        caseSensitive: false,
                        wordBoundaries: true,
                    },
                ],
            }
        }
        return DEFAULT_FORM
    })
    // Last state that is safely persisted (the seed, or the content after a
    // successful save). Divergence means unsaved edits exist. Held in state
    // (not a ref) so a save re-seed re-renders with a clean dirty fingerprint
    // instead of reading a ref during render.
    const [savedValue, setSavedValue] = useState(value)
    // Rule objects are flat and built through the same constructors, so plain
    // JSON comparison is deterministic (a typed change note counts as an edit).
    const isDirty = JSON.stringify(value) !== JSON.stringify(savedValue)
    useEffect(() => {
        onDirtyChange(isDirty)
    }, [isDirty, onDirtyChange])

    const create = useCreatePronunciationDictionary()
    const update = useUpdatePronunciationDictionary()

    const mode: StudioMode = !isEdit
        ? 'create'
        : restoreSource !== null
          ? 'restore'
          : 'edit'
    const isAssigned = assignedStations.length > 0
    const saving = create.isPending || update.isPending

    // Reset suggestWord once it's been applied
    const [suggestApplied] = useState(!!suggestWord)

    const rulesInvalid = value.rules.length === 0
    // The two rejections the backend makes on the rule set (duplicate word, missing
    // respelling), resolved before the request so the operator is told which word to fix.
    const ruleProblem = ruleSetProblem(value.rules)
    const stepInvalid =
        !value.name.trim() || rulesInvalid || ruleProblem !== null
    const canSave = !stepInvalid && !saving

    const blockHelper = (() => {
        if (!value.name.trim()) return 'Add a name before saving.'
        if (rulesInvalid) return 'Add at least one pronunciation rule.'
        if (ruleProblem) return ruleProblem
        return ''
    })()

    const patch = (next: Partial<PronunciationFormValue>) =>
        setValue((v) => ({ ...v, ...next }))

    const doSave = async () => {
        // _uid is editor-only row identity (stable React keys); the wire and
        // the stored dictionary must not carry it.
        const rules = value.rules
            .filter((r) => r.stringToReplace.trim().length > 0)
            .map((r) => ({
                type: r.type,
                stringToReplace: r.stringToReplace.trim(),
                replacement: r.replacement.trim(),
                alphabet: r.alphabet,
                caseSensitive: r.caseSensitive,
                wordBoundaries: r.wordBoundaries,
            }))
        const name = value.name.trim()
        try {
            if (mode === 'create') {
                await create.mutateAsync({
                    name,
                    description: value.description.trim() || null,
                    rules,
                    changeNote: null,
                    scope: value.scope,
                })
                toast.success('Dictionary created', {
                    description:
                        value.scope === 'Global'
                            ? `${name} now applies to every station.`
                            : `${name} is ready to assign in station schedules.`,
                })
                // The create is saved — closing now discards nothing, so the
                // close guard must not prompt.
                setSavedValue(value)
                onDirtyChange(false)
                onClose()
                return
            }
            if (!dict) return
            const saved = await update.mutateAsync({
                id: dict.id,
                body: {
                    name,
                    description: value.description.trim() || null,
                    rules,
                    changeNote:
                        mode === 'restore'
                            ? `Restored from version ${restoreSource}.`
                            : null,
                    expectedEtag: dict.etag,
                    scope: value.scope,
                },
            })
            const v = saved.currentVersion
            if (mode === 'restore') {
                toast.success('Version restored', {
                    description: `v${restoreSource} was saved as v${v}.`,
                })
            } else if (isAssigned) {
                toast.success('Live version saved', {
                    description: `${name} now uses v${v}. Assigned stations will use it from the next bulletin.`,
                })
            } else {
                toast.success('New version saved', {
                    description: `${name} now uses v${v}.`,
                })
            }
            setRestoreSource(null)
            setValue((cur) => ({ ...cur, changeNote: '' }))
            // v{currentVersion} now reflects `value` — the studio stays open on
            // the history step, but closing it no longer loses anything.
            setSavedValue({ ...value, changeNote: '' })
            setStep('history')
        } catch (err: unknown) {
            // The backend rejects a rule set with an actionable message (which word has two
            // rules, which one is unfinished, which version is too large). Swallowing it left
            // "try again" in front of a request that will never succeed unchanged.
            toast.error('Could not save dictionary', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        } finally {
            setConfirmOpen(false)
        }
    }

    const scopeChanged = dict !== null && dict.scope !== value.scope

    const handleSubmit = () => {
        gate.onSubmitAttempt()
        if (!canSave) return
        // Changing reach changes who hears it, so it is confirmed rather than saved silently.
        if (scopeChanged) {
            setPendingScopeChange(true)
            return
        }
        // Saving a NEW version of an assigned dictionary goes live on the next
        // bulletin for its stations, so it is confirmed rather than saved
        // silently (the same gate the delivery/voice/bookend studios use).
        if (isEdit && isAssigned) {
            setConfirmOpen(true)
            return
        }
        void doSave()
    }

    const onRestore = (version: DictionaryVersion) => {
        if (!dict) return
        setValue(versionForm(dict, version))
        setRestoreSource(version.version)
        setStep('rules')
    }

    const saveLabel =
        mode === 'create'
            ? 'Save dictionary'
            : mode === 'restore'
              ? 'Restore as new version'
              : 'Save new version'

    const ruleCount = value.rules.filter(
        (r) => r.stringToReplace.trim().length > 0
    ).length

    const steps: StudioStep[] = [
        {
            id: 'rules',
            label: 'Rules',
            icon: List,
            summary: `${ruleCount} rule${ruleCount === 1 ? '' : 's'}`,
            invalid: stepInvalid,
        },
        ...(dict
            ? [
                  {
                      id: 'history',
                      label: 'History',
                      icon: History,
                      summary: `v${dict.currentVersion} · ${dict.versions.length} version${dict.versions.length === 1 ? '' : 's'}`,
                  } satisfies StudioStep,
              ]
            : []),
    ]

    // Validation indicators stay hidden until the Rules step is visited or a
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
                    title={isEdit ? 'Edit dictionary' : 'New dictionary'}
                    steps={gate.steps}
                    activeId={step}
                    onSelect={(id) => {
                        gate.onSelectStep(id)
                        setStep(id as StepId)
                    }}
                />
                <StudioCanvas stepKey={step}>
                    {step === 'rules' && (
                        <>
                            {mode === 'restore' && (
                                <div className="rounded-lg border border-border/60 bg-muted/30 p-3 text-sm">
                                    Restoring v{restoreSource} copies its rules
                                    into a new current version. History is kept.
                                </div>
                            )}

                            {mode === 'edit' && isAssigned && (
                                <LiveAssignmentNotice
                                    stations={assignedStations}
                                />
                            )}
                            {suggestApplied && (
                                <div className="rounded-lg border border-primary/30 bg-primary/10 p-3 text-sm text-primary-on-tint">
                                    Suggested word pre-filled from review.
                                </div>
                            )}

                            <div className="flex flex-col gap-2">
                                <FieldLabel htmlFor="pd-name" required>
                                    Dictionary name
                                </FieldLabel>
                                <Input
                                    id="pd-name"
                                    maxLength={MAX_DICTIONARY_NAME_LENGTH}
                                    value={value.name}
                                    placeholder="for example, Sydney place names, Station 2GB"
                                    onChange={(e) =>
                                        patch({ name: e.target.value })
                                    }
                                />
                            </div>

                            <div className="flex flex-col gap-2">
                                <FieldLabel
                                    htmlFor="pd-desc"
                                    hintLabel="Where is the description shown?"
                                    hint="Shown on the dictionary card so operators know when to use it."
                                >
                                    Description
                                </FieldLabel>
                                <Input
                                    id="pd-desc"
                                    maxLength={
                                        MAX_DICTIONARY_DESCRIPTION_LENGTH
                                    }
                                    value={value.description}
                                    placeholder="for example, TTS fixes for local road and suburb names"
                                    onChange={(e) =>
                                        patch({
                                            description: e.target.value,
                                        })
                                    }
                                />
                            </div>

                            <div className="flex flex-col gap-2">
                                <FieldLabel
                                    id="pd-scope-label"
                                    htmlFor="pd-scope"
                                    hintLabel="How does dictionary scope work?"
                                    hint="A platform-wide dictionary applies to every station, underneath whatever dictionary that station carries. A station rule for the same word wins. Only one dictionary can be platform-wide."
                                >
                                    Applies to
                                </FieldLabel>
                                <Select
                                    value={value.scope}
                                    onValueChange={(next) =>
                                        patch({
                                            scope: next as DictionaryScope,
                                        })
                                    }
                                >
                                    <SelectTrigger
                                        id="pd-scope"
                                        aria-labelledby="pd-scope-label"
                                    >
                                        <SelectValue />
                                    </SelectTrigger>
                                    <SelectContent>
                                        <SelectItem value="Station">
                                            Stations that choose it
                                        </SelectItem>
                                        <SelectItem value="Global">
                                            Every station, platform-wide
                                        </SelectItem>
                                    </SelectContent>
                                </Select>
                            </div>

                            {ruleProblem && (
                                <div
                                    role="alert"
                                    className="flex items-start gap-2 rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive"
                                >
                                    <TriangleAlert className="mt-0.5 size-4 shrink-0" />
                                    <p>{ruleProblem}</p>
                                </div>
                            )}

                            <PronunciationRuleTable
                                rules={value.rules}
                                onChange={(rules) => patch({ rules })}
                                dictionaryId={dict?.id ?? null}
                                assignedStations={dict?.assignedStations ?? []}
                            />
                        </>
                    )}

                    {step === 'history' && dict && (
                        <VersionHistory
                            versions={dict.versions}
                            currentVersion={dict.currentVersion}
                            onRestore={onRestore}
                        />
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
                        {ruleCount} rule{ruleCount === 1 ? '' : 's'}
                    </span>
                    {isEdit && value.scope === 'Global' && (
                        <span className="inline-flex items-center gap-1 border-l border-border/60 pl-3">
                            Every station ({stationCount})
                        </span>
                    )}
                    {isEdit && value.scope !== 'Global' && (
                        <span className="inline-flex items-center gap-1 border-l border-border/60 pl-3">
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

            <ConfirmDialog
                open={pendingScopeChange}
                onOpenChange={setPendingScopeChange}
                title={
                    value.scope === 'Global'
                        ? 'Make this apply to every station?'
                        : 'Stop this applying to every station?'
                }
                description={
                    value.scope === 'Global'
                        ? `Applies to all ${stationCount} stations. A station's own rule for the same word still wins.`
                        : `Only the ${assignedStations.length} stations that choose it in their schedule will use it.`
                }
                confirmLabel="Save"
                onConfirm={() => {
                    setPendingScopeChange(false)
                    void doSave()
                }}
            />

            <LiveEditConfirmationDialog
                open={confirmOpen}
                onOpenChange={setConfirmOpen}
                entityName={value.name.trim()}
                noun="dictionary"
                stations={assignedStations}
                nextVersion={(dict?.currentVersion ?? 0) + 1}
                changeNote={value.changeNote.trim()}
                isSaving={saving}
                onConfirm={() => void doSave()}
                guidance="Pronunciation rules change how words are said. They do not change the bulletin words."
            />
        </form>
    )
}
