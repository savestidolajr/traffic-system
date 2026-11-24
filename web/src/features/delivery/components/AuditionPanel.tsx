import { useState } from 'react'
import { Check, Loader2, Sparkles, TriangleAlert } from 'lucide-react'
import { InfoHint } from '@/components/InfoHint'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { AudioWaveformPlayer } from '@/features/jobs/components/AudioWaveformPlayer'
import type { AuditionState } from '../lib/delivery-form'

interface AuditionPanelProps {
    readonly state: AuditionState
    readonly isStale: boolean
    readonly canGenerate: boolean
    readonly neutralLoading: boolean
    readonly onGenerate: () => void
    readonly onNeedNeutral: () => void
}

type AbMode = 'style' | 'neutral'

/**
 * The centre of the editor: the operator hears the style against the fixed
 * sample before saving, compares it to a Neutral read, and sees that the words
 * are unchanged. A fresh synth runs on each generate (nothing is cached).
 */
export function AuditionPanel({
    state,
    isStale,
    canGenerate,
    neutralLoading,
    onGenerate,
    onNeedNeutral,
}: AuditionPanelProps) {
    const [mode, setMode] = useState<AbMode>('style')
    const hasClip = state.status === 'ready'
    const announce = (() => {
        if (state.status === 'loading') return 'Creating audition.'
        if (isStale) return 'Audition out of date. Update the audition.'
        if (state.status === 'ready')
            return `Audition ready. Words unchanged. ${state.tagCount ?? 0} delivery cues. Estimated read ${Math.round(state.estimatedDurationSec ?? 0)} seconds.`
        return ''
    })()

    return (
        <section className="flex flex-col gap-3 rounded-lg border border-border/60 bg-muted/20 p-4">
            <div className="flex items-center gap-1.5">
                <h4 className="text-sm font-semibold">Audition</h4>
                <InfoHint label="How does the audition work?">
                    Hear this style against the fixed sample bulletin before
                    saving. A fresh preview is created each time.
                </InfoHint>
            </div>

            <p className="sr-only" role="status" aria-live="polite">
                {announce}
            </p>

            {state.status === 'idle' && (
                <Button
                    type="button"
                    variant="outline"
                    className="self-start"
                    disabled={!canGenerate}
                    onClick={onGenerate}
                >
                    <Sparkles className="size-4" />
                    Generate audition
                </Button>
            )}

            {state.status === 'loading' && (
                <div className="flex items-center gap-2 text-sm text-muted-foreground">
                    <Loader2 className="size-4 animate-spin" />
                    Creating audition… This can take a few seconds.
                </div>
            )}

            {state.status === 'error' && (
                <div className="flex flex-col gap-2">
                    <p className="text-sm text-destructive">
                        {state.error ??
                            'Could not create the audition. Try again.'}
                    </p>
                    <Button
                        type="button"
                        variant="outline"
                        className="self-start"
                        onClick={onGenerate}
                    >
                        Try again
                    </Button>
                </div>
            )}

            {hasClip && (
                <div className="flex flex-col gap-3">
                    {isStale && (
                        <div className="flex items-start gap-2 rounded-md border border-warning/30 bg-warning/10 p-2 text-xs text-warning">
                            <TriangleAlert className="mt-0.5 size-4 shrink-0" />
                            <span>
                                Audition out of date. Settings changed. Update
                                it to hear the current style.
                            </span>
                        </div>
                    )}

                    <ToggleGroup
                        type="single"
                        value={mode}
                        onValueChange={(next) => {
                            if (!next) return
                            const nextMode = next as AbMode
                            setMode(nextMode)
                            if (nextMode === 'neutral' && !state.neutralUrl) {
                                onNeedNeutral()
                            }
                        }}
                        className="justify-start gap-1"
                    >
                        <ToggleGroupItem
                            value="neutral"
                            className="h-8 rounded-md border border-border/60 px-3 text-xs data-[state=on]:border-primary data-[state=on]:bg-primary/10 data-[state=on]:text-primary-on-tint"
                        >
                            Neutral
                        </ToggleGroupItem>
                        <ToggleGroupItem
                            value="style"
                            className="h-8 rounded-md border border-border/60 px-3 text-xs data-[state=on]:border-primary data-[state=on]:bg-primary/10 data-[state=on]:text-primary-on-tint"
                        >
                            This style
                        </ToggleGroupItem>
                    </ToggleGroup>

                    {mode === 'neutral' && !state.neutralUrl ? (
                        <div className="flex items-center gap-2 text-sm text-muted-foreground">
                            <Loader2 className="size-4 animate-spin" />
                            {neutralLoading
                                ? 'Creating Neutral comparison…'
                                : 'Neutral comparison unavailable.'}
                        </div>
                    ) : (
                        <AudioWaveformPlayer
                            url={
                                mode === 'neutral'
                                    ? (state.neutralUrl as string)
                                    : (state.url as string)
                            }
                            duration={
                                mode === 'style'
                                    ? (state.estimatedDurationSec ?? undefined)
                                    : undefined
                            }
                        />
                    )}

                    <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
                        <span>
                            Delivery cues:{' '}
                            <span className="font-medium text-foreground">
                                {state.tagCount ?? 0}
                            </span>
                        </span>
                        <span>
                            Estimated read:{' '}
                            <span className="font-medium text-foreground">
                                ~{Math.round(state.estimatedDurationSec ?? 0)}s
                            </span>
                        </span>
                        <Badge variant="success" className="gap-1">
                            <Check className="size-3" />
                            Words unchanged
                        </Badge>
                    </div>

                    <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        className="self-start"
                        disabled={!canGenerate || state.status === 'loading'}
                        onClick={onGenerate}
                    >
                        {isStale ? 'Update audition' : 'Regenerate audition'}
                    </Button>
                </div>
            )}
        </section>
    )
}
