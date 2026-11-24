import { useState } from 'react'
import { useWatch, type UseFormReturn } from 'react-hook-form'
import { toast } from 'sonner'
import { Loader2 } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { InfoHint } from '@/components/InfoHint'
import { Slider } from '@/components/ui/slider'
import { Switch } from '@/components/ui/switch'
import { MovingBorder } from '@/components/ui/aceternity/moving-border'
import { AudioWaveformPlayer } from '@/features/jobs/components/AudioWaveformPlayer'
import { StabilitySelector } from './StabilitySelector'
import { usePreviewVoice } from '../api/voice-profile-hooks'
import { ABSOLUTE_PACE_CEILING, MAX_PACE_BOOST_WPM } from '../form-schemas'
import type { ModelCapabilities } from '../lib/model-capabilities'
import type { VoiceProfileFormData } from '../form-schemas'
import type { VoicePreviewResponse } from '../profile-types'
import {
    isPreviewStale,
    previewFingerprint,
} from '../../delivery/lib/delivery-form'

interface PreviewState extends VoicePreviewResponse {
    /** Voice+settings+pace-boost fingerprint, to flag a stale preview after edits. */
    readonly signature: string
}

interface SettingSliderProps {
    readonly label: string
    readonly value: number
    readonly onChange: (value: number) => void
    readonly min?: number
    readonly max?: number
    readonly step?: number
    readonly display: (value: number) => string
    readonly hint: string
}

function SettingSlider({
    label,
    value,
    onChange,
    min = 0,
    max = 1,
    step = 0.01,
    display,
    hint,
}: SettingSliderProps) {
    return (
        <div className="flex flex-col gap-2">
            <div className="flex items-center justify-between">
                <span className="flex items-center gap-1.5 text-sm font-medium">
                    {label}
                    <InfoHint label={`What does ${label.toLowerCase()} do?`}>
                        {hint}
                    </InfoHint>
                </span>
                <span className="rounded bg-muted px-1.5 py-0.5 font-mono text-xs">
                    {display(value)}
                </span>
            </div>
            <Slider
                aria-label={label}
                value={[value]}
                min={min}
                max={max}
                step={step}
                onValueChange={(v) => onChange(v[0])}
            />
        </div>
    )
}

interface VoiceCharacterSectionProps {
    readonly form: UseFormReturn<VoiceProfileFormData>
    readonly caps: ModelCapabilities
    readonly model: string
    /** Measured WPM for this voice+settings, when known — drives the pace-boost math preview. */
    readonly calibratedWpm?: number
}

export function VoiceCharacterSection({
    form,
    caps,
    model,
    calibratedWpm,
}: VoiceCharacterSectionProps) {
    const settings =
        useWatch({ control: form.control, name: 'voiceSettings' }) ??
        form.getValues('voiceSettings')
    const voiceId = useWatch({ control: form.control, name: 'voiceId' }) ?? ''
    const paceBoostWpm =
        useWatch({ control: form.control, name: 'paceBoostWpm' }) ??
        form.getValues('paceBoostWpm')

    const preview = usePreviewVoice()
    const [previewState, setPreviewState] = useState<PreviewState | null>(null)
    const liveSignature = previewFingerprint({
        voiceId,
        model,
        settings,
        paceBoostWpm,
    })
    const previewStale =
        previewState !== null &&
        isPreviewStale(liveSignature, previewState.signature)

    const setSetting = (
        key: keyof typeof settings,
        value: number | boolean
    ) => {
        form.setValue(`voiceSettings.${key}`, value, {
            shouldDirty: true,
            shouldValidate: true,
        })
    }

    const runPreview = () => {
        if (!voiceId) {
            toast.error('Pick a base voice first')
            return
        }
        preview.mutate(
            { baseVoiceId: voiceId, model, settings, paceBoostWpm },
            {
                onSuccess: (result) =>
                    setPreviewState({
                        ...result,
                        signature: previewFingerprint({
                            voiceId,
                            model,
                            settings,
                            paceBoostWpm,
                        }),
                    }),
                onError: () => toast.error('Preview failed. Try again'),
            }
        )
    }

    // The boost is realised by compressing the render, and that compression is capped
    // (SpeechDurationOptions.AbsolutePaceBoostSpeedUpCeiling). Past the cap a bigger number buys
    // nothing, so show what the voice will actually READ AT rather than base + the raw request —
    // otherwise the panel promises a pace the pipeline then silently declines to deliver.
    const paceCeilingWpm =
        calibratedWpm && calibratedWpm > 0
            ? calibratedWpm * ABSOLUTE_PACE_CEILING
            : null
    const effectiveWpm =
        calibratedWpm && calibratedWpm > 0 && paceCeilingWpm !== null
            ? Math.min(calibratedWpm + paceBoostWpm, paceCeilingWpm)
            : null
    const paceSaturated =
        effectiveWpm !== null &&
        !!calibratedWpm &&
        calibratedWpm + paceBoostWpm > effectiveWpm + 0.5

    return (
        <div className="flex flex-col gap-6">
            <div className="flex flex-col gap-5 rounded-xl border bg-card/40 p-4">
                <div className="flex items-center gap-2">
                    <ICON.ai className="size-4 text-primary" />
                    <h3 className="text-sm font-semibold">Voice character</h3>
                </div>

                <StabilitySelector
                    value={settings.stability}
                    onChange={(v) => setSetting('stability', v)}
                    discrete={caps.discreteStability}
                />

                <div className="grid gap-5 sm:grid-cols-2">
                    <SettingSlider
                        label="Similarity"
                        value={settings.similarityBoost}
                        onChange={(v) => setSetting('similarityBoost', v)}
                        display={(v) => `${Math.round(v * 100)}%`}
                        hint="Higher hugs the source voice; too high amplifies artefacts."
                    />
                    {caps.supportsStyle && (
                        <SettingSlider
                            label="Style"
                            value={settings.style}
                            onChange={(v) => setSetting('style', v)}
                            display={(v) => `${Math.round(v * 100)}%`}
                            hint="Exaggerates the voice's characteristics; can reduce stability."
                        />
                    )}
                    {caps.supportsSpeed && (
                        <SettingSlider
                            label="Speed"
                            value={settings.speed}
                            onChange={(v) => setSetting('speed', v)}
                            min={0.7}
                            max={1.2}
                            display={(v) => `${v.toFixed(2)}×`}
                            hint="Initial pace. Final duration is fit automatically."
                        />
                    )}
                </div>

                {caps.supportsSpeakerBoost && (
                    <div className="flex items-center justify-between rounded-lg border p-3">
                        <div className="flex items-center gap-1.5">
                            <p className="text-sm font-medium">Speaker boost</p>
                            <InfoHint label="What does speaker boost do?">
                                Enhances clarity and likeness to the source.
                            </InfoHint>
                        </div>
                        <Switch
                            checked={settings.useSpeakerBoost}
                            onCheckedChange={(v) =>
                                setSetting('useSpeakerBoost', v)
                            }
                        />
                    </div>
                )}
            </div>

            <div className="flex flex-col gap-3 rounded-xl border bg-card/40 p-4">
                <div className="flex items-center gap-2">
                    <ICON.speed className="size-4 text-primary" />
                    <h3 className="text-sm font-semibold">Delivery pace</h3>
                </div>
                <SettingSlider
                    label="Pace boost"
                    value={paceBoostWpm}
                    onChange={(v) =>
                        form.setValue('paceBoostWpm', v, {
                            shouldDirty: true,
                            shouldValidate: true,
                        })
                    }
                    min={0}
                    max={MAX_PACE_BOOST_WPM}
                    step={1}
                    display={(v) => `+${v} wpm`}
                    hint="Sizes the script for a faster read, then compresses it to fit the slot. A punchier delivery, independent of Speed above. Works on every model including v3 (which ignores Speed)."
                />
                {paceBoostWpm > 0 && effectiveWpm !== null && (
                    <p className="text-xs text-muted-foreground">
                        {Math.round(calibratedWpm!)} wpm base →{' '}
                        {Math.round(effectiveWpm)} wpm script, compressed to fit
                        the slot.
                    </p>
                )}
                {paceSaturated && (
                    <p className="text-xs text-warning">
                        This voice tops out at +
                        {Math.round(paceCeilingWpm! - calibratedWpm!)} wpm.
                        Anything above that reads the same.
                    </p>
                )}
            </div>

            <div className="flex flex-col gap-3 rounded-xl border bg-card/40 p-4">
                <div className="flex items-center justify-between gap-3">
                    <div>
                        <p className="text-sm font-semibold">Preview</p>
                        <p className="text-xs text-muted-foreground">
                            Hear these settings on a standard bulletin.
                        </p>
                    </div>
                    <MovingBorder tone="primary" className="shrink-0">
                        <button
                            type="button"
                            onClick={runPreview}
                            disabled={preview.isPending || !voiceId}
                            className="flex items-center gap-2 rounded-[calc(0.5rem-1px)] px-4 py-2 text-sm font-medium disabled:opacity-50"
                        >
                            {preview.isPending ? (
                                <Loader2 className="size-4 animate-spin" />
                            ) : (
                                <ICON.play className="size-4" />
                            )}
                            {previewState ? 'Re-preview' : 'Generate preview'}
                        </button>
                    </MovingBorder>
                </div>
                {previewState && (
                    <div className="flex flex-col gap-2">
                        <AudioWaveformPlayer
                            url={previewState.url}
                            duration={previewState.estimatedDurationSec}
                        />
                        <p className="text-xs text-muted-foreground">
                            Estimated {previewState.wordsPerMinute} wpm ·{' '}
                            {previewState.estimatedDurationSec}s
                            {previewState.effectivePaceBoostWpm > 0 && (
                                <span className="ml-1">
                                    · pace-boosted +
                                    {previewState.effectivePaceBoostWpm} wpm
                                </span>
                            )}
                            {previewStale && (
                                <span className="ml-1 text-warning">
                                    · settings changed, preview again
                                </span>
                            )}
                        </p>
                    </div>
                )}
            </div>
        </div>
    )
}
