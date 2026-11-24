import { useState } from 'react'
import { toast } from 'sonner'
import { Loader2 } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { InfoHint } from '@/components/InfoHint'
import { Textarea } from '@/components/ui/textarea'
import { Button } from '@/components/ui/button'
import { AudioWaveformPlayer } from '@/features/jobs/components/AudioWaveformPlayer'
import { resolveAudioSrc, ApiError } from '@/lib/api/client'
import { downloadUrl } from '@/lib/transfer'
import { useVoiceover, VOICEOVER_SCRIPT_MAX } from '../api/voice-profile-hooks'
import type { VoiceoverResponse } from '../profile-types'
import { isPreviewStale } from '../../delivery/lib/delivery-form'

interface VoiceoverSectionProps {
    readonly profileId: string
    readonly baseVoiceName: string
    readonly model: string
    /** One-line rendering of the settings this read will use (the footer's summary). */
    readonly settingsSummary: string
    /** The current version's bulletin pace boost, if any — deliberately NOT applied here. */
    readonly paceBoostWpm: number
    /** The studio holds unsaved tuning edits, which this read will NOT use. */
    readonly hasUnsavedTuning: boolean
}

/**
 * The voiceover booth: type any copy — an advertisement, a promo, a station ident — hear it in
 * this voice, and download the file.
 *
 * The read comes off the profile's SAVED current version, resolved server-side from the profile
 * id, so what the operator downloads is exactly what a station hears. Two deliberate omissions,
 * both surfaced below when they apply: unsaved slider edits in the studio are not used (there is
 * no version to render them from yet), and the version's bulletin pace boost is not applied
 * (it is slot-fitting compression, and an ad has no slot).
 */
export function VoiceoverSection({
    profileId,
    baseVoiceName,
    model,
    settingsSummary,
    paceBoostWpm,
    hasUnsavedTuning,
}: VoiceoverSectionProps) {
    const voiceover = useVoiceover()
    const [script, setScript] = useState('')
    const [render, setRender] = useState<VoiceoverResponse | null>(null)
    /** The script that produced the current render — used to flag staleness. */
    const [renderedScript, setRenderedScript] = useState('')

    const trimmed = script.trim()
    const words = trimmed ? trimmed.split(/\s+/).length : 0
    const stale = render !== null && isPreviewStale(trimmed, renderedScript)

    const runVoiceover = () => {
        if (!trimmed) {
            toast.error('Type the copy you want read first')
            return
        }
        voiceover.mutate(
            { profileId, script: trimmed },
            {
                onSuccess: (result) => {
                    setRender(result)
                    setRenderedScript(trimmed)
                },
                onError: (error) =>
                    toast.error(
                        error instanceof ApiError
                            ? error.message
                            : 'Could not record the voiceover. Try again'
                    ),
            }
        )
    }

    return (
        <div className="rounded-lg border p-4">
            <div className="flex items-center gap-2">
                <ICON.audio className="size-4 text-primary" />
                <p className="text-sm font-semibold">Record a voiceover</p>
                <InfoHint label="What is this for?">
                    Anything that is not a traffic bulletin. An advertisement, a
                    promo, a station ident. The read uses{' '}
                    <span className="font-medium text-foreground">
                        {baseVoiceName || 'this voice'}
                    </span>{' '}
                    exactly as it is configured now, and nothing here touches a
                    bulletin or a station.
                </InfoHint>
            </div>

            <p className="mt-1 font-mono text-xs text-muted-foreground">
                {model} · {settingsSummary}
            </p>

            <Textarea
                rows={8}
                className="mt-3"
                placeholder={
                    'Paste or type the copy to be read.\n\nFor example, This weekend only at Barry’s Betta Electrical, 20 per cent off every fridge in the store.'
                }
                maxLength={VOICEOVER_SCRIPT_MAX}
                value={script}
                onChange={(e) => setScript(e.target.value)}
            />

            <div className="mt-2 flex flex-wrap items-center justify-between gap-3">
                <span className="text-xs tabular-nums text-muted-foreground">
                    {words} word{words === 1 ? '' : 's'} ·{' '}
                    {script.length.toLocaleString()}/
                    {VOICEOVER_SCRIPT_MAX.toLocaleString()} characters
                </span>
                <Button
                    type="button"
                    variant="secondary"
                    onClick={runVoiceover}
                    disabled={voiceover.isPending || !trimmed}
                    className="gap-2"
                >
                    {voiceover.isPending ? (
                        <Loader2 className="size-4 animate-spin" />
                    ) : (
                        <ICON.voice className="size-4" />
                    )}
                    {voiceover.isPending
                        ? 'Recording…'
                        : render
                          ? 'Record again'
                          : 'Record'}
                </Button>
            </div>

            {hasUnsavedTuning && (
                <p className="mt-2 text-xs text-warning">
                    This records the saved version of the voice. Save your
                    tuning changes first to hear them.
                </p>
            )}

            {render && (
                <div className="mt-4 flex flex-col gap-2 border-t border-border/60 pt-4">
                    <AudioWaveformPlayer
                        url={render.url}
                        duration={render.durationSec}
                    />
                    <div className="flex flex-wrap items-center justify-between gap-2">
                        <span className="text-xs tabular-nums text-muted-foreground">
                            {render.durationSec.toFixed(1)}s ·{' '}
                            {render.wordCount} words
                            {paceBoostWpm > 0 &&
                                ' · natural pace (the bulletin pace boost is not applied)'}
                        </span>
                        {/* Same-origin download: the API streams these bytes back as an
                            attachment, so the browser saves the file instead of opening a
                            media viewer — which is what a cross-origin blob URL would do. */}
                        <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            onClick={() =>
                                downloadUrl(
                                    resolveAudioSrc(render.url),
                                    render.fileName
                                )
                            }
                        >
                            <ICON.download className="size-4" />
                            Download WAV
                        </Button>
                    </div>
                    {stale && (
                        <p className="text-xs text-warning">
                            The copy has changed since this recording. Record
                            again to hear the new version.
                        </p>
                    )}
                </div>
            )}
        </div>
    )
}
