import { useCallback, useEffect, useRef, useState } from 'react'
import { toast } from 'sonner'
import {
    Loader2,
    Sparkles,
    SlidersHorizontal,
    History,
    Radio,
} from 'lucide-react'
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
import { cn } from '@/lib/utils'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'
import {
    useDeliveryStyle,
    useCreateDeliveryStyle,
    useUpdateDeliveryStyle,
    usePreviewDeliveryStyle,
} from '../api/delivery-style-hooks'
import { useStyleAssignments } from '../lib/assignments'
import {
    IDLE_AUDITION,
    isPreviewStale,
    policyHash,
    presetForm,
    previewFingerprint,
    styleForm,
    versionForm,
    type AuditionState,
    type DeliveryStyleFormValue,
} from '../lib/delivery-form'
import { PRESETS, type DeliveryStylePreset, type PresetId } from '../presets'
import { strengthLabel } from '../strength'
import type { DeliveryStyle, DeliveryStyleVersion } from '../types'
import { StrengthControl } from './StrengthControl'
import { PermittedCueSelector } from './PermittedCueSelector'
import { AuditionPanel } from './AuditionPanel'
import { LiveEditConfirmationDialog } from '@/components/LiveEditConfirmationDialog'
import { VersionHistory } from './VersionHistory'
import { LiveAssignmentNotice } from '@/components/LiveAssignmentNotice'

type StepId = 'style' | 'delivery' | 'history'
type StudioMode = 'create' | 'edit' | 'restore'

const DEFAULT_PRESET: PresetId = 'standard'

/** Backend ConfigValidationLimits.DeliveryStyleNameMax — enforced here so the save never 400s. */
const MAX_STYLE_NAME_LENGTH = 120

/** Backend ConfigValidationLimits.DeliveryStyleDescriptionMax — enforced here so the save never 400s. */
const MAX_STYLE_DESCRIPTION_LENGTH = 1000
interface StyleSectionProps {
    readonly mode: StudioMode
    readonly selectedPreset: PresetId | null
    readonly restoreSource: number | null
    readonly isAssigned: boolean
    readonly assignedStations: readonly string[]
    readonly value: DeliveryStyleFormValue
    readonly onPatch: (next: Partial<DeliveryStyleFormValue>) => void
    readonly onApplyPreset: (preset: DeliveryStylePreset) => void
}

function StyleSection({
    mode,
    selectedPreset,
    restoreSource,
    isAssigned,
    assignedStations,
    value,
    onPatch,
    onApplyPreset,
}: StyleSectionProps) {
    return (
        <>
            {mode === 'create' && (
                <div className="flex flex-col gap-2">
                    <FieldLabel
                        hintLabel="What are the presets?"
                        hint="A station-safe starting point. Nothing is saved until you audition and save."
                    >
                        Start from
                    </FieldLabel>
                    <div className="flex flex-wrap gap-2">
                        {PRESETS.map((preset) => (
                            <button
                                key={preset.id}
                                type="button"
                                onClick={() => onApplyPreset(preset)}
                                aria-pressed={selectedPreset === preset.id}
                                title={preset.usage}
                                className={cn(
                                    'rounded-full border px-3 py-1 text-xs font-medium transition-colors',
                                    selectedPreset === preset.id
                                        ? 'border-primary/40 bg-primary/10 text-primary-on-tint'
                                        : 'border-border bg-background/60 text-muted-foreground hover:bg-muted/60'
                                )}
                            >
                                {preset.name}
                            </button>
                        ))}
                    </div>
                </div>
            )}

            {mode === 'restore' && (
                <div className="rounded-lg border border-border/60 bg-muted/30 p-3 text-sm">
                    <p className="font-medium">
                        Restore v{restoreSource} as a new version
                    </p>
                    <p className="text-muted-foreground">
                        Copies the old settings into a new current version,
                        keeping version history intact.
                    </p>
                </div>
            )}

            {mode === 'edit' && isAssigned && (
                <LiveAssignmentNotice stations={assignedStations} />
            )}

            <div className="flex flex-col gap-2">
                <Label htmlFor="ds-name">
                    Style name
                    <span className="text-destructive ml-0.5">*</span>
                </Label>
                <Input
                    id="ds-name"
                    maxLength={MAX_STYLE_NAME_LENGTH}
                    value={value.name}
                    placeholder="for example, 2GB Drive, Peak urgency"
                    onChange={(e) => onPatch({ name: e.target.value })}
                />
            </div>

            <div className="flex flex-col gap-2">
                <FieldLabel
                    htmlFor="ds-tone"
                    required
                    hintLabel="What goes in the tone description?"
                    hint="Describe the delivery only. Do not write script words here."
                >
                    How should this sound on air?
                </FieldLabel>
                <Textarea
                    id="ds-tone"
                    value={value.toneDescription}
                    placeholder="for example, Read with controlled urgency."
                    onChange={(e) =>
                        onPatch({ toneDescription: e.target.value })
                    }
                />
            </div>

            <div className="flex flex-col gap-2">
                <FieldLabel
                    htmlFor="ds-summary"
                    required
                    hintLabel="Where is the summary shown?"
                    hint="Shown on the style card so operators recognise when to use it."
                >
                    Summary
                </FieldLabel>
                <Input
                    id="ds-summary"
                    maxLength={MAX_STYLE_DESCRIPTION_LENGTH}
                    value={value.description}
                    placeholder="for example, Controlled urgent read for peak traffic windows."
                    onChange={(e) => onPatch({ description: e.target.value })}
                />
            </div>
        </>
    )
}

interface DeliverySectionProps {
    readonly value: DeliveryStyleFormValue
    readonly audition: AuditionState
    readonly isStale: boolean
    readonly canGenerate: boolean
    readonly neutralLoading: boolean
    readonly onPatch: (next: Partial<DeliveryStyleFormValue>) => void
    readonly onGenerate: () => void
    readonly onNeedNeutral: () => void
}

function DeliverySection({
    value,
    audition,
    isStale,
    canGenerate,
    neutralLoading,
    onPatch,
    onGenerate,
    onNeedNeutral,
}: DeliverySectionProps) {
    return (
        <>
            <div className="flex flex-col gap-2">
                <FieldLabel
                    hintLabel="What does strength control?"
                    hint="Controls how often the voice may use emphasis, pauses, and delivery cues. It does not change the script words."
                >
                    Strength
                </FieldLabel>
                <StrengthControl
                    value={value.intensity}
                    onChange={(intensity) => onPatch({ intensity })}
                />
            </div>

            <div className="flex flex-col gap-2">
                <FieldLabel
                    required
                    hintLabel="What are permitted delivery cues?"
                    hint="The delivery moves this style is allowed to use. The script words stay locked."
                >
                    Permitted delivery cues
                </FieldLabel>
                <PermittedCueSelector
                    value={value.allowedTags}
                    onChange={(allowedTags) => onPatch({ allowedTags })}
                />
            </div>

            <AuditionPanel
                state={audition}
                isStale={isStale}
                canGenerate={canGenerate}
                neutralLoading={neutralLoading}
                onGenerate={onGenerate}
                onNeedNeutral={onNeedNeutral}
            />
        </>
    )
}

interface DeliveryStyleStudioProps {
    readonly open: boolean
    readonly onOpenChange: (open: boolean) => void
    /** null → create a new style; otherwise edit the given style. */
    readonly styleId: string | null
}

/**
 * Delivery style editor — the Voices Studio pattern. A station-safe library of
 * how a station's bulletins are read: the operator describes the read in plain
 * language, picks permitted cues, and hears it before saving. Raw audio tags
 * are never exposed.
 */
export function DeliveryStyleStudio({
    open,
    onOpenChange,
    styleId,
}: DeliveryStyleStudioProps) {
    const isCreate = styleId === null
    const { data: style, isLoading } = useDeliveryStyle(styleId)
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
                title={isCreate ? 'New delivery style' : 'Edit style'}
                subject={isCreate ? undefined : style?.name}
                description="Describe how a station's bulletins are read, then hear it before saving."
            >
                {!isCreate && isLoading ? (
                    <div className="flex min-h-0 flex-1 items-center justify-center text-muted-foreground">
                        <Loader2 className="size-5 animate-spin" />
                    </div>
                ) : !isCreate && !style ? (
                    <div className="flex min-h-0 flex-1 items-center justify-center text-sm text-muted-foreground">
                        Style not found.
                    </div>
                ) : (
                    <DeliveryStyleStudioInner
                        key={style?.id ?? 'new'}
                        style={style ?? null}
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
                    description="Unsaved edits to this style will be lost."
                    confirmLabel="Discard"
                    destructive
                />
            )}
        </>
    )
}

interface DeliveryStyleStudioInnerProps {
    readonly style: DeliveryStyle | null
    readonly onClose: () => void
    /** Reports whether the editor currently holds unsaved edits (for the
     *  wrapper's close guard — this studio keeps its working value in local
     *  state, not RHF, so the wrapper cannot read dirtiness itself). */
    readonly onDirtyChange: (dirty: boolean) => void
}

function DeliveryStyleStudioInner({
    style,
    onClose,
    onDirtyChange,
}: DeliveryStyleStudioInnerProps) {
    const isEdit = style !== null
    const { byStyleId } = useStyleAssignments()
    const assignedStations = style ? (byStyleId.get(style.id) ?? []) : []

    const [step, setStep] = useState<StepId>('style')
    const [restoreSource, setRestoreSource] = useState<number | null>(null)
    const [selectedPreset, setSelectedPreset] = useState<PresetId | null>(
        isEdit ? null : DEFAULT_PRESET
    )
    const [value, setValue] = useState<DeliveryStyleFormValue>(() =>
        style ? styleForm(style) : presetForm(PRESETS[0])
    )
    // Last state that is safely persisted (the seed, or the content after a
    // successful save). Divergence means unsaved edits exist. Held in state
    // (not a ref) so a save re-seed re-renders with a clean dirty fingerprint
    // instead of reading a ref during render.
    const [savedValue, setSavedValue] = useState(value)
    const isDirty = previewFingerprint(value) !== previewFingerprint(savedValue)
    useEffect(() => {
        onDirtyChange(isDirty)
    }, [isDirty, onDirtyChange])
    const [audition, setAudition] = useState<AuditionState>(IDLE_AUDITION)
    const [neutralLoading, setNeutralLoading] = useState(false)
    const [confirmOpen, setConfirmOpen] = useState(false)

    const create = useCreateDeliveryStyle()
    const update = useUpdateDeliveryStyle()
    const preview = usePreviewDeliveryStyle()

    const mode: StudioMode = !isEdit
        ? 'create'
        : restoreSource !== null
          ? 'restore'
          : 'edit'
    const isVersionedEdit = mode === 'edit' || mode === 'restore'
    const isAssigned = assignedStations.length > 0
    const saving = create.isPending || update.isPending
    const nextVersion = (style?.currentVersion ?? 0) + 1

    const currentHash = policyHash(value)
    const isStale =
        audition.status === 'ready' &&
        isPreviewStale(currentHash, audition.auditionedHash)

    const styleStepInvalid =
        !value.name.trim() ||
        !value.toneDescription.trim() ||
        !value.description.trim()
    // A preview is NOT a save gate (the pronunciation studio made the same
    // decision): the audition tells the operator what the style sounds like,
    // it does not vouch for the policy. Blocking save on the provider's
    // availability wedged every style behind ElevenLabs.
    const deliveryStepInvalid = value.allowedTags.length === 0
    const canSave = !styleStepInvalid && !deliveryStepInvalid && !saving

    const blockHelper = (() => {
        if (value.allowedTags.length === 0)
            return 'Select at least one delivery cue.'
        if (styleStepInvalid) return 'Complete the Style step before saving.'
        return ''
    })()

    const patch = (next: Partial<DeliveryStyleFormValue>) =>
        setValue((v) => ({ ...v, ...next }))

    const applyPreset = (preset: DeliveryStylePreset) => {
        setSelectedPreset(preset.id)
        // Keep any name the operator already typed; reseed the rest.
        setValue((v) => ({ ...presetForm(preset), name: v.name }))
        setAudition(IDLE_AUDITION)
    }

    const generate = async () => {
        setAudition((a) => ({ ...a, status: 'loading', error: null }))
        try {
            const res = await preview.mutateAsync({
                policy: {
                    toneDescription: value.toneDescription.trim(),
                    allowedTags: value.allowedTags,
                    intensity: value.intensity,
                },
            })
            setAudition((a) => ({
                status: 'ready',
                url: res.url,
                estimatedDurationSec: res.estimatedDurationSec,
                tagCount: res.tagCount,
                error: null,
                auditionedHash: policyHash(value),
                neutralUrl: a.neutralUrl,
            }))
        } catch (err: unknown) {
            // Surface the provider's reason (invalid voice, bad key, provider
            // down) — the panel shows it verbatim, and "failed" with no way
            // past the loop sent operators in circles.
            setAudition((a) => ({
                ...a,
                status: 'error',
                error:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Could not create the audition.',
            }))
        }
    }

    const needNeutral = async () => {
        if (neutralLoading || audition.neutralUrl) return
        setNeutralLoading(true)
        try {
            const res = await preview.mutateAsync({
                policy: {
                    toneDescription: value.toneDescription.trim(),
                    allowedTags: [],
                    intensity: value.intensity,
                },
            })
            setAudition((a) => ({ ...a, neutralUrl: res.url }))
        } catch {
            // Leave neutralUrl null; the panel shows "unavailable".
        } finally {
            setNeutralLoading(false)
        }
    }

    const doSave = async () => {
        const policy = {
            toneDescription: value.toneDescription.trim(),
            allowedTags: value.allowedTags,
            intensity: value.intensity,
        }
        const name = value.name.trim()
        try {
            if (mode === 'create') {
                await create.mutateAsync({
                    name,
                    description: value.description.trim() || null,
                    policy,
                    changeNote: null,
                })
                toast.success('Style created', {
                    description: `${name} is ready to assign in station schedules.`,
                })
                // The create is saved — closing now discards nothing, so the
                // close guard must not prompt.
                setSavedValue(value)
                onDirtyChange(false)
                onClose()
                return
            }
            if (!style) return
            const saved = await update.mutateAsync({
                id: style.id,
                body: {
                    name,
                    description: value.description.trim() || null,
                    policy,
                    changeNote:
                        mode === 'restore'
                            ? `Restored from version ${restoreSource}.`
                            : null,
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
            setAudition(IDLE_AUDITION)
            setStep('history')
        } catch (err: unknown) {
            // Keep the server's reason (validation text, version-size wall, revision conflict):
            // "try again" in front of a deterministic rejection sends the operator in circles.
            toast.error('Could not save style', {
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
        if (isVersionedEdit && isAssigned) {
            setConfirmOpen(true)
            return
        }
        void doSave()
    }

    const onRestore = (version: DeliveryStyleVersion) => {
        if (!style) return
        setValue(versionForm(style, version))
        setRestoreSource(version.version)
        setAudition(IDLE_AUDITION)
        setStep('style')
    }

    const saveLabel =
        mode === 'create'
            ? 'Save style'
            : mode === 'restore'
              ? 'Restore as new version'
              : 'Save new version'

    const cueCount = value.allowedTags.length
    const steps: StudioStep[] = [
        {
            id: 'style',
            label: 'Style',
            icon: Sparkles,
            summary: value.name.trim() || 'Name & tone',
            invalid: styleStepInvalid,
        },
        {
            id: 'delivery',
            label: 'Delivery',
            icon: SlidersHorizontal,
            summary: `${strengthLabel(value.intensity)} · ${cueCount} cue${cueCount === 1 ? '' : 's'}`,
            invalid: deliveryStepInvalid,
        },
        ...(style
            ? [
                  {
                      id: 'history',
                      label: 'History',
                      icon: History,
                      summary: `v${style.currentVersion} · ${style.versions.length} version${style.versions.length === 1 ? '' : 's'}`,
                  } satisfies StudioStep,
              ]
            : []),
    ]

    // Validation indicators stay hidden until a step is visited or a save is
    // attempted — a fresh studio does not scold before the operator has
    // looked at anything.
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
                    title={isEdit ? 'Edit style' : 'New style'}
                    steps={gate.steps}
                    activeId={step}
                    onSelect={(id) => {
                        gate.onSelectStep(id)
                        setStep(id as StepId)
                    }}
                />
                <StudioCanvas stepKey={step}>
                    {step === 'style' && (
                        <StyleSection
                            mode={mode}
                            selectedPreset={selectedPreset}
                            restoreSource={restoreSource}
                            isAssigned={isAssigned}
                            assignedStations={assignedStations}
                            value={value}
                            onPatch={patch}
                            onApplyPreset={applyPreset}
                        />
                    )}

                    {step === 'delivery' && (
                        <DeliverySection
                            value={value}
                            audition={audition}
                            isStale={isStale}
                            canGenerate={
                                Boolean(value.toneDescription.trim()) &&
                                value.allowedTags.length > 0 &&
                                !saving
                            }
                            neutralLoading={neutralLoading}
                            onPatch={patch}
                            onGenerate={() => void generate()}
                            onNeedNeutral={() => void needNeutral()}
                        />
                    )}

                    {step === 'history' && style && (
                        <VersionHistory
                            versions={style.versions}
                            currentVersion={style.currentVersion}
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
                        {strengthLabel(value.intensity)} · {cueCount} cue
                        {cueCount === 1 ? '' : 's'}
                    </span>
                    {audition.status === 'ready' &&
                        audition.estimatedDurationSec != null && (
                            <span className="border-l border-border/60 pl-3 tabular-nums">
                                ~{Math.round(audition.estimatedDurationSec)}s
                                {isStale ? ' · out of date' : ''}
                            </span>
                        )}
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
                noun="style"
                stations={assignedStations}
                nextVersion={nextVersion}
                changeNote={value.changeNote.trim()}
                isSaving={saving}
                onConfirm={() => void doSave()}
                guidance="Delivery styles guide the read only. They never change the bulletin words."
            />
        </form>
    )
}
