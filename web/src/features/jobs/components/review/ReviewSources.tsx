import {
    Sun,
    CloudSun,
    CloudRain,
    CloudLightning,
    CloudFog,
    CloudSnow,
    Wind,
    Droplets,
    Info,
    Layers,
    type LucideIcon,
} from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import type { Job } from '../../types'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { AuroraWash } from '@/components/ui/aceternity/aurora-wash'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import {
    weatherTone,
    WEATHER_TONE_GRADIENT,
    type WeatherTone,
} from '../../lib/weather-tone'
import { cn } from '@/lib/utils'
import { WEATHER_TINT } from '@/lib/design/icons'

const WEATHER_GLYPH: Record<WeatherTone, LucideIcon> = {
    sun: Sun,
    cloud: CloudSun,
    rain: CloudRain,
    storm: CloudLightning,
    fog: CloudFog,
    snow: CloudSnow,
}

function SectionLabel({
    children,
    tone = 'muted',
    hint,
}: {
    readonly children: React.ReactNode
    readonly tone?: 'muted' | 'success'
    readonly hint?: string
}) {
    return (
        <span className="inline-flex items-center gap-1">
            <span
                className={cn(
                    'text-2xs font-medium uppercase tracking-wider',
                    tone === 'success'
                        ? 'text-success'
                        : 'text-muted-foreground'
                )}
            >
                {children}
            </span>
            {hint && (
                <Tooltip>
                    <TooltipTrigger asChild>
                        <Info
                            tabIndex={0}
                            aria-label={hint}
                            className="size-3 cursor-default text-muted-foreground"
                        />
                    </TooltipTrigger>
                    <TooltipContent side="top" className="max-w-xs">
                        {hint}
                    </TooltipContent>
                </Tooltip>
            )}
        </span>
    )
}

/** Tiny stat chip: one supporting fact, one icon. */
function StatChip({
    icon: Icon,
    children,
    className,
}: {
    readonly icon: LucideIcon
    readonly children: React.ReactNode
    readonly className?: string
}) {
    return (
        <span
            className={cn(
                'inline-flex min-w-0 items-center gap-1 rounded-full border border-border/60 bg-muted/30 px-2 py-0.5 text-2xs text-muted-foreground',
                className
            )}
        >
            <Icon className="size-3 shrink-0" aria-hidden />
            <span className="truncate">{children}</span>
        </span>
    )
}

/** A selected-but-unavailable source: an honest empty slot, not silence. */
function MissingSlot({
    icon: Icon,
    children,
}: {
    readonly icon: LucideIcon
    readonly children: React.ReactNode
}) {
    return (
        <p className="flex items-start gap-1.5 rounded-md border border-dashed border-border/70 px-2.5 py-2 text-xs leading-snug text-muted-foreground">
            <Icon className="mt-0.5 size-3.5 shrink-0" aria-hidden />
            <span className="min-w-0">{children}</span>
        </p>
    )
}

/**
 * What the bulletin drew on besides traffic (WS1): the weather and fuel
 * snapshots persisted on the job, and the priority roads it reported as
 * flowing well. This panel exists so the operator can verify every
 * non-traffic claim in the script against the data that produced it —
 * the same role the incident index plays for corridors. Each source leads
 * with the fact the script would quote (temperature, cheapest price) and
 * keeps the supporting evidence quiet underneath.
 */
export function ReviewSources({ job }: { readonly job: Job }) {
    const { content, weather, fuel, goodRuns } = job
    const weatherMissing = content?.weather === true && !weather
    const fuelMissing = content?.fuel === true && !fuel

    const hasAnything =
        weather || fuel || goodRuns.length > 0 || weatherMissing || fuelMissing
    if (!hasAnything) return null

    const tone = weather ? weatherTone(weather.conditions) : 'cloud'
    const Glyph = WEATHER_GLYPH[tone]

    return (
        <SpotlightCard className="flex flex-col p-4">
            <span className="flex items-center gap-1.5 pb-1 text-xs font-semibold">
                <Layers
                    className="size-3.5 text-muted-foreground"
                    aria-hidden
                />
                Sources
            </span>

            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
                {weather && (
                    <section className="relative overflow-hidden rounded-lg border border-border/40 p-3">
                        {/* Bloom tinted by the actual conditions */}
                        <AuroraWash gradient={WEATHER_TONE_GRADIENT[tone]} />
                        <div className="relative flex flex-col gap-2">
                            <SectionLabel>Weather</SectionLabel>
                            <div className="flex items-center gap-2.5">
                                <Glyph
                                    className={cn(
                                        'size-8 shrink-0',
                                        WEATHER_TINT[tone]
                                    )}
                                    aria-hidden
                                />
                                <span className="shrink-0 text-3xl font-semibold tabular-nums leading-none tracking-tight">
                                    {Math.round(weather.temperatureC)}°
                                </span>
                                <span className="min-w-0 flex-1 text-sm leading-snug text-foreground/90">
                                    {weather.conditions}
                                </span>
                            </div>
                            {(weather.windDescription ||
                                weather.precipitationProbabilityPercent >
                                    0) && (
                                <div className="flex flex-wrap items-center gap-1.5">
                                    {weather.windDescription && (
                                        <StatChip icon={Wind}>
                                            {weather.windDescription}
                                        </StatChip>
                                    )}
                                    {weather.precipitationProbabilityPercent >
                                        0 && (
                                        <StatChip
                                            icon={Droplets}
                                            className={cn(
                                                weather.precipitationProbabilityPercent >=
                                                    40 && WEATHER_TINT.rain
                                            )}
                                        >
                                            {
                                                weather.precipitationProbabilityPercent
                                            }
                                            % rain
                                        </StatChip>
                                    )}
                                </div>
                            )}
                            {weather.alerts.map((alert) => (
                                <p
                                    key={alert}
                                    className="flex items-start gap-1.5 rounded-md border border-warning/25 bg-warning/10 px-2 py-1.5 text-xs leading-snug text-warning"
                                >
                                    <ICON.warning
                                        className="mt-0.5 size-3 shrink-0"
                                        aria-hidden
                                    />
                                    <span className="min-w-0">{alert}</span>
                                </p>
                            ))}
                        </div>
                    </section>
                )}
                {weatherMissing && (
                    <section className="rounded-lg border border-border/40 p-3">
                        <MissingSlot icon={CloudSun}>
                            Weather was selected but unavailable.
                        </MissingSlot>
                    </section>
                )}

                {fuel && fuel.summaries.length > 0 && (
                    <section className="flex flex-col gap-2.5 rounded-lg border border-border/40 p-3">
                        <SectionLabel hint="Cheapest station → market average.">
                            Fuel prices
                        </SectionLabel>
                        {fuel.summaries.map((s) => {
                            const spreadCents = Math.round(
                                (s.averagePrice - s.minPrice) * 100
                            )
                            return (
                                <div
                                    key={s.fuelType}
                                    className="flex flex-col gap-1"
                                >
                                    <div className="flex items-baseline justify-between gap-2">
                                        <span className="min-w-0 truncate text-sm font-medium">
                                            {s.fuelType}
                                        </span>
                                        <span className="shrink-0 text-2xs text-muted-foreground">
                                            {s.stationCount} stations
                                        </span>
                                    </div>
                                    {/* Cheapest → average dumbbell: the script
                                        quotes the left end */}
                                    <Tooltip>
                                        <TooltipTrigger asChild>
                                            <div
                                                tabIndex={0}
                                                className="flex cursor-default items-center gap-2"
                                            >
                                                <span className="shrink-0 font-mono text-sm font-semibold tabular-nums text-success">
                                                    ${s.minPrice.toFixed(2)}
                                                </span>
                                                <span className="relative h-px min-w-6 flex-1 bg-linear-to-r from-success/70 to-muted-foreground/40">
                                                    <span className="absolute left-0 top-1/2 size-1.5 -translate-y-1/2 rounded-full bg-success" />
                                                    <span className="absolute right-0 top-1/2 size-1.5 -translate-y-1/2 rounded-full bg-muted-foreground/70" />
                                                </span>
                                                <span className="shrink-0 font-mono text-xs tabular-nums text-muted-foreground">
                                                    ${s.averagePrice.toFixed(2)}
                                                </span>
                                            </div>
                                        </TooltipTrigger>
                                        <TooltipContent side="top">
                                            {spreadCents}¢ between the cheapest
                                            station and the market average
                                        </TooltipContent>
                                    </Tooltip>
                                </div>
                            )
                        })}
                    </section>
                )}
                {fuelMissing && (
                    <section className="rounded-lg border border-border/40 p-3">
                        <MissingSlot icon={ICON.fuel}>
                            Fuel prices were selected but unavailable.
                        </MissingSlot>
                    </section>
                )}

                {goodRuns.length > 0 && (
                    <section className="flex flex-col gap-1.5 rounded-lg border border-border/40 p-3 sm:col-span-2 lg:col-span-1">
                        <SectionLabel
                            tone="success"
                            hint="Priority roads with no incidents in the fresh fetch."
                        >
                            Flowing well
                        </SectionLabel>
                        <div className="flex flex-wrap gap-1.5">
                            {goodRuns.map((road) => (
                                <span
                                    key={road}
                                    className="inline-flex min-w-0 items-center gap-1 rounded-full border border-success/25 bg-success/10 px-2 py-0.5 text-xs text-success"
                                >
                                    <ICON.success
                                        className="size-3 shrink-0"
                                        aria-hidden
                                    />
                                    <span className="truncate">{road}</span>
                                </span>
                            ))}
                        </div>
                    </section>
                )}
            </div>
        </SpotlightCard>
    )
}
