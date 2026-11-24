import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { cn } from '@/lib/utils'
import { STRENGTH_OPTIONS, type Strength } from '../strength'

interface StrengthControlProps {
    readonly value: Strength
    readonly onChange: (value: Strength) => void
}

/**
 * The operator-facing replacement for the raw intensity slider. Three labelled
 * stops, each with a plain description of what changes. Neutral (0) is never
 * offered here - "no delivery style" is chosen in the station schedule.
 */
export function StrengthControl({ value, onChange }: StrengthControlProps) {
    return (
        <ToggleGroup
            type="single"
            value={String(value)}
            onValueChange={(next) => {
                if (next) onChange(Number(next) as Strength)
            }}
            className="grid grid-cols-1 items-stretch gap-2 sm:grid-cols-3"
        >
            {STRENGTH_OPTIONS.map((option) => (
                <ToggleGroupItem
                    key={option.value}
                    value={String(option.value)}
                    aria-label={option.label}
                    className={cn(
                        'h-auto flex-col items-start gap-1 whitespace-normal rounded-lg border border-border/60 bg-card p-3 text-left',
                        'data-[state=on]:border-primary data-[state=on]:bg-primary/5'
                    )}
                >
                    <span className="text-sm font-medium text-foreground">
                        {option.label}
                    </span>
                    <span className="text-xs font-normal leading-snug text-muted-foreground">
                        {option.description}
                    </span>
                </ToggleGroupItem>
            ))}
        </ToggleGroup>
    )
}
