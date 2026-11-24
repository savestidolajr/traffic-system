import { Sparkles, AudioLines, ShieldCheck } from 'lucide-react'
import { GlowBorder } from '@/components/ui/aceternity/glow-border'
import { Slider } from '@/components/ui/slider'
import { InfoHint } from '@/components/InfoHint'
import { cn } from '@/lib/utils'

interface StabilitySelectorProps {
    readonly value: number
    readonly onChange: (value: number) => void
    /** v3 buckets stability into 3 modes; v2-family is a continuous dial. */
    readonly discrete: boolean
}

const PRESETS = [
    {
        value: 0,
        label: 'Creative',
        icon: Sparkles,
        desc: 'Expressive and variable: energy and emotion, less predictable.',
    },
    {
        value: 0.5,
        label: 'Natural',
        icon: AudioLines,
        desc: 'Balanced delivery. Recommended for most reads.',
    },
    {
        value: 1,
        label: 'Robust',
        icon: ShieldCheck,
        desc: 'Most stable and consistent: best for straight news.',
    },
] as const

export function StabilitySelector({
    value,
    onChange,
    discrete,
}: StabilitySelectorProps) {
    if (!discrete) {
        return (
            <div className="flex flex-col gap-2">
                <div className="flex items-center justify-between">
                    <span className="flex items-center gap-1.5 text-sm font-medium">
                        Stability
                        <InfoHint label="What does stability do?">
                            Higher is steadier; lower is more emotive.
                        </InfoHint>
                    </span>
                    <span className="rounded bg-muted px-1.5 py-0.5 font-mono text-xs">
                        {Math.round(value * 100)}%
                    </span>
                </div>
                <Slider
                    aria-label="Stability"
                    value={[value]}
                    min={0}
                    max={1}
                    step={0.01}
                    onValueChange={(v) => onChange(v[0])}
                />
            </div>
        )
    }

    return (
        <div className="flex flex-col gap-2">
            <span className="text-sm font-medium">Stability</span>
            <div className="grid gap-2 sm:grid-cols-3">
                {PRESETS.map((preset) => {
                    const active = Math.abs(value - preset.value) < 0.01
                    const Icon = preset.icon
                    const inner = (
                        <button
                            type="button"
                            onClick={() => onChange(preset.value)}
                            aria-pressed={active}
                            className={cn(
                                'flex h-full w-full flex-col gap-1 rounded-lg p-3 text-left transition-colors',
                                active
                                    ? 'bg-card'
                                    : 'border bg-card hover:border-primary/40'
                            )}
                        >
                            <span className="flex items-center gap-2 text-sm font-medium">
                                <Icon
                                    className={cn(
                                        'size-4',
                                        active
                                            ? 'text-primary'
                                            : 'text-muted-foreground'
                                    )}
                                />
                                {preset.label}
                            </span>
                            <span className="text-xs leading-snug text-muted-foreground">
                                {preset.desc}
                            </span>
                        </button>
                    )
                    return active ? (
                        <GlowBorder key={preset.value} className="h-full">
                            {inner}
                        </GlowBorder>
                    ) : (
                        <div key={preset.value} className="h-full">
                            {inner}
                        </div>
                    )
                })}
            </div>
        </div>
    )
}
