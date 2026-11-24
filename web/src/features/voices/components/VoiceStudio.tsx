import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { toastAction } from '@/lib/toast-action'
import { Loader2, Tag } from 'lucide-react'
import { ICON, ICON_SIZE } from '@/lib/design/icons'
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
import { Form } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'
import { VoiceIdentitySection } from './VoiceIdentitySection'
import { VoiceCharacterSection } from './VoiceCharacterSection'
import { LiveEditConfirmationDialog } from '@/components/LiveEditConfirmationDialog'
import { VoiceVersionHistory } from './VoiceVersionHistory'
import { VoiceoverSection } from './VoiceoverSection'
import {
    useVoiceProfile,
    useCreateVoiceProfile,
    useUpdateVoiceProfile,
} from '../api/voice-profile-hooks'
import { useVoices } from '../api/voice-hooks'
import { sortVoicesByCategory } from '../lib/sort-voices'
import { useVoiceAssignments } from '../lib/assignments'
import { getModelCapabilities } from '../lib/model-capabilities'
import {
    currentVersion,
    summarizeVoiceSettings,
    wordTargetForDuration,
} from '../lib/voice-profile-utils'
import { DEFAULT_VOICE_SETTINGS } from '@/lib/api/schemas/shared'
import {
    VoiceProfileFormSchema,
    type VoiceProfileFormData,
} from '../form-schemas'
import type { ElevenLabsVoice } from '../types'
import type { VoiceProfile } from '../profile-types'
import { LiveAssignmentNotice } from '@/components/LiveAssignmentNotice'

type StepId = 'identity' | 'character' | 'voiceover' | 'history'
const DEFAULT_MODEL = 'eleven_multilingual_v2'
const SLOT_SECONDS = 30

interface VoiceStudioProps {
    readonly open: boolean
    readonly onOpenChange: (open: boolean) => void
    /** null → create a new variant; otherwise tune the given profile. */
    readonly profileId: string | null
}

export function VoiceStudio({
    open,
    onOpenChange,
    profileId,
}: VoiceStudioProps) {
    const isCreate = profileId === null
    const { data: profile, isLoading } = useVoiceProfile(profileId)
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
                title={isCreate ? 'New voice variant' : 'Edit voice'}
                subject={isCreate ? undefined : profile?.name}
                description="Tune a base voice into a named variant the stations can use."
            >
                {!isCreate && isLoading ? (
                    <div className="flex min-h-0 flex-1 items-center justify-center text-muted-foreground">
                        <Loader2 className="size-5 animate-spin" />
                    </div>
                ) : !isCreate && !profile ? (
                    <div className="flex min-h-0 flex-1 items-center justify-center text-sm text-muted-foreground">
                        Variant not found.
                    </div>
                ) : (
                    <VoiceStudioInner
                        key={profile?.id ?? 'new'}
                        profile={profile ?? null}
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
                    description="Unsaved edits to this voice variant will be lost."
                    confirmLabel="Discard"
                    destructive
                />
            )}
        </>
    )
}

interface VoiceStudioInnerProps {
    readonly profile: VoiceProfile | null
    readonly onClose: () => void
    /** Reports whether the editor currently holds unsaved edits (for the
     *  wrapper's close guard — this studio uses RHF, so dirtiness is the
     *  form's own `isDirty` against its seeded default values). */
    readonly onDirtyChange: (dirty: boolean) => void
}

function VoiceStudioInner({
    profile,
    onClose,
    onDirtyChange,
}: VoiceStudioInnerProps) {
    const isEdit = profile !== null
    const editingVersion = profile ? currentVersion(profile) : null
    const [step, setStep] = useState<StepId>('identity')
    const [pickedVoice, setPickedVoice] = useState<ElevenLabsVoice | null>(null)
    const [confirmOpen, setConfirmOpen] = useState(false)
    const [pendingEdit, setPendingEdit] = useState<VoiceProfileFormData | null>(
        null
    )

    const form = useForm<VoiceProfileFormData>({
        resolver: zodResolver(VoiceProfileFormSchema),
        mode: 'onChange',
        defaultValues: profile
            ? {
                  name: profile.name,
                  description: profile.description ?? '',
                  voiceId: profile.baseVoiceId,
                  voiceName: profile.baseVoiceName,
                  voiceSettings: editingVersion!.settings,
                  changeNote: '',
                  paceBoostWpm: editingVersion!.paceBoostWpm,
              }
            : {
                  name: '',
                  description: '',
                  voiceId: '',
                  voiceName: null,
                  voiceSettings: DEFAULT_VOICE_SETTINGS,
                  changeNote: '',
                  paceBoostWpm: 0,
              },
    })

    // RHF reports dirtiness against the seeded defaults; report it upward so
    // the wrapper's close guard can prompt before discarding typed work.
    useEffect(() => {
        onDirtyChange(form.formState.isDirty)
    }, [form.formState.isDirty, onDirtyChange])

    const { data: voicesData, isLoading: voicesLoading } = useVoices()
    const sortedVoices = useMemo(
        () => sortVoicesByCategory(voicesData?.voices ?? []),
        [voicesData]
    )

    const model =
        editingVersion?.model ?? pickedVoice?.resolvedModelId ?? DEFAULT_MODEL
    const caps = useMemo(() => getModelCapabilities([model]), [model])

    const createProfile = useCreateVoiceProfile()
    const updateProfile = useUpdateVoiceProfile()
    const isSubmitting = createProfile.isPending || updateProfile.isPending

    const name = useWatch({ control: form.control, name: 'name' })
    const voiceId = useWatch({ control: form.control, name: 'voiceId' })
    const settings =
        useWatch({ control: form.control, name: 'voiceSettings' }) ??
        form.getValues('voiceSettings')

    // Derived from the schedules list — the wire DTO's `assignedStations` is
    // never populated, so the live-edit warning and footer count would
    // otherwise claim every profile is unassigned (see `lib/assignments.ts`).
    const { byProfileId } = useVoiceAssignments()
    const assignedStations = profile ? (byProfileId.get(profile.id) ?? []) : []
    const isAssigned = assignedStations.length > 0

    const handlePickVoice = (id: string) => {
        const voice = sortedVoices.find((v) => v.voiceId === id) ?? null
        form.setValue('voiceId', id, {
            shouldDirty: true,
            shouldValidate: true,
        })
        form.setValue('voiceName', voice?.name ?? null, { shouldDirty: true })
        form.setValue('voiceSettings', DEFAULT_VOICE_SETTINGS, {
            shouldDirty: true,
        })
        setPickedVoice(voice)
    }

    const saveEdit = async (data: VoiceProfileFormData) => {
        if (!profile) return
        const description = data.description?.trim() ? data.description : null
        const changeNote = data.changeNote?.trim() ? data.changeNote : null
        const result = await toastAction(
            () =>
                updateProfile.mutateAsync({
                    id: profile.id,
                    body: {
                        name: data.name,
                        description,
                        model,
                        settings: data.voiceSettings,
                        changeNote,
                        paceBoostWpm: data.paceBoostWpm,
                    },
                }),
            'Variant saved',
            'Could not save the variant'
        )
        if (result) {
            setPendingEdit(null)
            setConfirmOpen(false)
            // Re-baseline RHF's dirty flag against what was just saved, so
            // closing the studio no longer prompts over a persisted version.
            form.reset(form.getValues())
            setStep('history')
        }
    }

    const onSubmit = async (data: VoiceProfileFormData) => {
        gate.onSubmitAttempt()
        if (isEdit && profile) {
            if (isAssigned) {
                setPendingEdit(data)
                setConfirmOpen(true)
                return
            }
            await saveEdit(data)
            return
        }
        const description = data.description?.trim() ? data.description : null
        const changeNote = data.changeNote?.trim() ? data.changeNote : null
        const result = await toastAction(
            () =>
                createProfile.mutateAsync({
                    name: data.name,
                    description,
                    baseVoiceId: data.voiceId,
                    baseVoiceName: data.voiceName ?? pickedVoice?.name ?? '',
                    model,
                    settings: data.voiceSettings,
                    changeNote,
                    paceBoostWpm: data.paceBoostWpm,
                }),
            'Variant created',
            'Could not create the variant'
        )
        if (result) {
            // Re-baseline first so the close guard (which runs synchronously
            // in the same tick) sees a clean form instead of prompting over a
            // successful create.
            form.reset(form.getValues())
            onDirtyChange(false)
            onClose()
        }
    }

    const identityInvalid = !name?.trim() || !voiceId
    // The voiceover booth needs a saved profile id to render against, so it only appears once
    // the variant exists.
    const voiceoverStep: StudioStep[] = profile
        ? [
              {
                  id: 'voiceover',
                  label: 'Voiceover',
                  icon: ICON.audio,
                  summary: 'Read any copy · download',
              },
          ]
        : []
    const steps: StudioStep[] = [
        {
            id: 'identity',
            label: 'Basics',
            icon: Tag,
            summary: name?.trim()
                ? `${name}${profile ? ` · ${profile.baseVoiceName}` : ''}`
                : 'Name & base voice',
            invalid: identityInvalid,
        },
        {
            id: 'character',
            label: 'Voice',
            icon: ICON.tune,
            summary: summarizeVoiceSettings(settings),
        },
        ...voiceoverStep,
        ...(profile
            ? [
                  {
                      id: 'history',
                      label: 'History',
                      icon: ICON.history,
                      summary: `v${profile.currentVersion} · ${profile.versions.length} versions`,
                  } satisfies StudioStep,
              ]
            : []),
    ]

    // Validation indicators stay hidden until the Basics step is visited or a
    // save is attempted — a fresh studio does not scold before the operator
    // has looked at anything.
    const gate = useStudioStepGate(steps, step)

    const calibration = editingVersion?.calibration

    return (
        <Form {...form}>
            <form
                onSubmit={form.handleSubmit(onSubmit)}
                className="flex min-h-0 flex-1 flex-col"
            >
                <StudioBody>
                    <StudioRail
                        title={isEdit ? 'Edit voice' : 'New voice variant'}
                        steps={gate.steps}
                        activeId={step}
                        onSelect={(id) => {
                            gate.onSelectStep(id)
                            setStep(id as StepId)
                        }}
                    />
                    <StudioCanvas stepKey={step}>
                        {step === 'identity' && (
                            <>
                                {isEdit && isAssigned && (
                                    <LiveAssignmentNotice
                                        stations={assignedStations}
                                        className="mb-5"
                                    />
                                )}
                                <VoiceIdentitySection
                                    form={form}
                                    isEdit={isEdit}
                                    baseVoiceName={
                                        profile?.baseVoiceName ?? null
                                    }
                                    model={model}
                                    sortedVoices={sortedVoices}
                                    voicesLoading={voicesLoading}
                                    onPickVoice={handlePickVoice}
                                />
                            </>
                        )}
                        {step === 'character' && (
                            <VoiceCharacterSection
                                form={form}
                                caps={caps}
                                model={model}
                                calibratedWpm={calibration?.wordsPerMinute}
                            />
                        )}
                        {step === 'voiceover' && profile && (
                            <VoiceoverSection
                                profileId={profile.id}
                                baseVoiceName={profile.baseVoiceName}
                                model={editingVersion?.model ?? model}
                                settingsSummary={summarizeVoiceSettings(
                                    editingVersion?.settings ?? settings
                                )}
                                paceBoostWpm={editingVersion?.paceBoostWpm ?? 0}
                                hasUnsavedTuning={form.formState.isDirty}
                            />
                        )}
                        {step === 'history' && profile && (
                            <VoiceVersionHistory
                                profile={profile}
                                onRestored={() => {
                                    /* query invalidates → refresh */
                                }}
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
                            {summarizeVoiceSettings(settings)}
                        </span>
                        {calibration && (
                            <span className="border-l border-border/60 pl-3 tabular-nums">
                                {Math.round(calibration.wordsPerMinute)} wpm · ~
                                {wordTargetForDuration(
                                    calibration.wordsPerMinute,
                                    SLOT_SECONDS
                                )}{' '}
                                words/{SLOT_SECONDS}s
                            </span>
                        )}
                        {isEdit && (
                            <span className="inline-flex items-center gap-1 border-l border-border/60 pl-3">
                                <ICON.broadcast className={ICON_SIZE.inline} />
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
                            disabled={isSubmitting}
                        >
                            Cancel
                        </Button>
                        <StudioSubmitButton
                            label={
                                isEdit ? 'Save new version' : 'Create variant'
                            }
                            isSubmitting={isSubmitting}
                            isValid={form.formState.isValid}
                            disabledReason="Add a name and pick a base voice"
                        />
                    </div>
                </StudioFooter>
                <LiveEditConfirmationDialog
                    open={confirmOpen}
                    onOpenChange={setConfirmOpen}
                    entityName={profile?.name ?? ''}
                    noun="voice"
                    stations={assignedStations}
                    nextVersion={(profile?.currentVersion ?? 0) + 1}
                    changeNote={pendingEdit?.changeNote?.trim() ?? ''}
                    isSaving={isSubmitting}
                    onConfirm={() => {
                        if (pendingEdit) void saveEdit(pendingEdit)
                    }}
                    guidance="Voice changes affect the on-air sound. They do not change the bulletin words."
                />
            </form>
        </Form>
    )
}
