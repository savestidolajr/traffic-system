import { hasAudio, type Job, type SourceFetchDiagnostic } from '../../types'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import {
    AlertTriangle,
    CheckCircle2,
    Radio,
    Mic2,
    Sliders,
    Route,
    CloudSun,
    Fuel,
} from 'lucide-react'
import { cn } from '@/lib/utils'
import { formatDuration } from '@/lib/formatting'
import { usePronunciationDictionaries } from '@/features/pronunciation/api/pronunciation-hooks'
import { summarizeVoiceSettings } from '@/features/voices/lib/voice-profile-utils'
import { voiceModelLabel } from '@/features/voices/lib/model-labels'
import { prettify } from '@/lib/text'
import { resolveProviderLabel } from './provider-label'
import { resolvePronunciationDisplay } from './pronunciation-display'
import { sourceDisplayName } from '../../lib/source-display-names'
import {
    SOURCE_OUTCOME_LABEL,
    formatStaleAge,
    visibleSourceDiagnostics,
} from '../../lib/source-diagnostics'

const BULLETIN_TYPE_LABELS: Record<string, string> = {
    traffic: 'Traffic',
    TRAFFIC_ONLY: 'Traffic only',
    TRAFFIC_WEATHER: 'Traffic and weather',
}

interface ConfigurationTabProps {
    readonly job: Job
}

function SourceDiagnosticPill({
    diagnostic,
}: {
    readonly diagnostic: SourceFetchDiagnostic
}) {
    const isFailed = diagnostic.outcome === 'Failed'
    const isStale = diagnostic.outcome === 'ServedStale'
    const problem = isFailed || isStale
    const label = sourceDisplayName(diagnostic.sourceId)

    return (
        <div
            className={cn(
                'flex flex-col gap-0.5 rounded-lg border p-2.5 transition-colors',
                isFailed
                    ? 'border-destructive/40 bg-destructive/10 text-destructive'
                    : isStale
                      ? 'border-warning/40 bg-warning/10 text-warning'
                      : 'border-border/60 bg-muted/20 text-foreground'
            )}
        >
            <div className="flex items-center justify-between gap-2">
                <span className="font-semibold text-xs">{label}</span>
                <div className="flex items-center gap-1">
                    {problem ? (
                        <AlertTriangle
                            className="size-3 shrink-0"
                            aria-hidden
                        />
                    ) : (
                        <CheckCircle2
                            className="size-3 shrink-0 text-success"
                            aria-hidden
                        />
                    )}
                    <span className="text-3xs font-medium uppercase tracking-wider">
                        {SOURCE_OUTCOME_LABEL[diagnostic.outcome]}
                    </span>
                </div>
            </div>
            {(diagnostic.detail || (isStale && diagnostic.staleSeconds)) && (
                <div className="text-3xs text-muted-foreground flex flex-col gap-0.5 mt-0.5">
                    {isStale && (
                        <span>{formatStaleAge(diagnostic.staleSeconds)}</span>
                    )}
                    {diagnostic.detail && (
                        <span className="truncate">{diagnostic.detail}</span>
                    )}
                </div>
            )}
        </div>
    )
}

export function ConfigurationTab({ job }: ConfigurationTabProps) {
    const provider = job.input.voice.provider
    const model = job.input.voice.model
    const bulletinType = job.input.bulletinType
    const audio = hasAudio(job) ? job.audio : null
    const sourceDiagnostics = visibleSourceDiagnostics(job.sourceDiagnostics)

    // Preserve the exact historical provider dictionary version used by the job. Both layers are
    // shown separately: provenance must stay exact, and they are edited by different people.
    const { data: dicts } = usePronunciationDictionaries()
    const pronunciation = resolvePronunciationDisplay(
        job.pronunciationDictionaryId,
        job.pronunciationVersionId,
        model,
        dicts?.items ?? []
    )
    const platformPronunciation = resolvePronunciationDisplay(
        job.globalPronunciationDictionaryId,
        job.globalPronunciationVersionId,
        model,
        dicts?.items ?? []
    )
    const airedWithPlatformLayer = job.globalPronunciationDictionaryId != null

    return (
        <div className="flex flex-col gap-4">
            {/* 1. Broadcast Spec & Output Vitals */}
            <SpotlightCard className="p-4 flex flex-col gap-3">
                <div className="flex items-center justify-between border-b border-border/40 pb-2.5">
                    <div className="flex items-center gap-2">
                        <div className="flex size-6 items-center justify-center rounded-md bg-primary/10 text-primary border border-primary/20">
                            <Radio className="size-3.5" aria-hidden />
                        </div>
                        <div className="flex flex-col">
                            <span className="font-bold text-sm text-foreground">
                                {job.input.branding.stationTag}
                            </span>
                            <span className="text-3xs text-muted-foreground uppercase tracking-wide font-medium">
                                {BULLETIN_TYPE_LABELS[bulletinType] ??
                                    prettify(bulletinType)}
                            </span>
                        </div>
                    </div>
                    <div className="flex items-center gap-1.5">
                        {job.content?.traffic && (
                            <span className="inline-flex items-center gap-1 rounded-md border border-border/50 bg-muted/30 px-2 py-0.5 text-3xs font-medium text-muted-foreground">
                                <Route className="size-2.5" /> Traffic
                            </span>
                        )}
                        {job.content?.weather && (
                            <span className="inline-flex items-center gap-1 rounded-md border border-border/50 bg-muted/30 px-2 py-0.5 text-3xs font-medium text-muted-foreground">
                                <CloudSun className="size-2.5" /> Weather
                            </span>
                        )}
                        {job.content?.fuel && (
                            <span className="inline-flex items-center gap-1 rounded-md border border-border/50 bg-muted/30 px-2 py-0.5 text-3xs font-medium text-muted-foreground">
                                <Fuel className="size-2.5" /> Fuel
                            </span>
                        )}
                    </div>
                </div>

                <div className="grid grid-cols-2 gap-2.5">
                    <div className="flex flex-col gap-0.5 rounded-lg border border-border/40 bg-muted/20 p-2.5">
                        <span className="text-3xs uppercase tracking-wider font-semibold text-muted-foreground">
                            Target Duration
                        </span>
                        <div className="flex items-baseline gap-1.5">
                            <span className="font-mono text-base font-bold text-foreground">
                                {formatDuration(job.targetDurationSec)}
                            </span>
                            {audio && (
                                <span className="font-mono text-3xs text-muted-foreground">
                                    (produced{' '}
                                    {formatDuration(
                                        Math.round(audio.measuredDurationSec)
                                    )}
                                    )
                                </span>
                            )}
                        </div>
                    </div>

                    <div className="flex flex-col gap-0.5 rounded-lg border border-border/40 bg-muted/20 p-2.5">
                        <span className="text-3xs uppercase tracking-wider font-semibold text-muted-foreground">
                            Word Band
                        </span>
                        <span className="font-mono text-base font-bold text-foreground">
                            {job.editingGuidance
                                ? `${job.editingGuidance.minWordCount}–${job.editingGuidance.maxWordCount} w`
                                : '–'}
                        </span>
                    </div>
                </div>
            </SpotlightCard>

            {/* 2. Voice & Synthesis Profile */}
            <SpotlightCard className="p-4 flex flex-col gap-3">
                <div className="flex items-center gap-2 border-b border-border/40 pb-2.5">
                    <div className="flex size-6 items-center justify-center rounded-md bg-secondary/20 text-secondary-foreground border border-secondary/30">
                        <Mic2 className="size-3.5" aria-hidden />
                    </div>
                    <div className="flex flex-col">
                        <span className="font-semibold text-sm text-foreground">
                            {job.input.voice.voiceName}
                        </span>
                        <span className="text-3xs text-muted-foreground font-medium">
                            {resolveProviderLabel(provider, model)} ·{' '}
                            {voiceModelLabel(model)}
                        </span>
                    </div>
                </div>

                <div className="flex flex-col gap-2 text-xs">
                    <div className="flex items-center justify-between gap-2 rounded-md bg-muted/20 px-2.5 py-1.5 border border-border/30">
                        <span className="text-muted-foreground text-3xs font-semibold uppercase tracking-wider">
                            Pronunciation
                        </span>
                        <span
                            className="font-medium text-foreground truncate max-w-[200px]"
                            title={pronunciation.title}
                        >
                            {pronunciation.label}
                        </span>
                    </div>

                    {airedWithPlatformLayer && (
                        <div className="flex items-center justify-between gap-2 rounded-md bg-muted/20 px-2.5 py-1.5 border border-border/30">
                            <span className="text-muted-foreground text-3xs font-semibold uppercase tracking-wider">
                                Platform Dict
                            </span>
                            <span
                                className="font-medium text-foreground truncate max-w-[200px]"
                                title={platformPronunciation.title}
                            >
                                {platformPronunciation.label}
                            </span>
                        </div>
                    )}

                    {job.input.voice.settings && (
                        <div className="flex items-center justify-between gap-2 rounded-md bg-muted/20 px-2.5 py-1.5 border border-border/30">
                            <span className="text-muted-foreground text-3xs font-semibold uppercase tracking-wider flex items-center gap-1">
                                <Sliders className="size-2.5" /> Acoustics
                            </span>
                            <span className="font-mono text-3xs text-foreground/90 font-medium">
                                {summarizeVoiceSettings(
                                    job.input.voice.settings
                                )}
                            </span>
                        </div>
                    )}
                </div>
            </SpotlightCard>

            {/* 3. Ingestion & Data Sources Health */}
            {sourceDiagnostics.length > 0 && (
                <SpotlightCard className="p-4 flex flex-col gap-3">
                    <div className="flex items-center justify-between border-b border-border/40 pb-2.5">
                        <span className="text-xs font-semibold tracking-wide text-foreground uppercase">
                            Data Sources Health
                        </span>
                        <span className="text-3xs font-mono text-muted-foreground">
                            {
                                sourceDiagnostics.filter(
                                    (d) => d.outcome === 'Ok'
                                ).length
                            }
                            /{sourceDiagnostics.length} Live
                        </span>
                    </div>

                    <div className="flex flex-col gap-2">
                        {sourceDiagnostics.map((diagnostic) => (
                            <SourceDiagnosticPill
                                key={diagnostic.sourceId}
                                diagnostic={diagnostic}
                            />
                        ))}
                    </div>
                </SpotlightCard>
            )}
        </div>
    )
}
