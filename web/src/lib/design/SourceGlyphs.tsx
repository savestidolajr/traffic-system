import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
    TooltipProvider,
} from '@/components/ui/tooltip'
import { ICON, ICON_SIZE } from './icons'
import { cn } from '@/lib/utils'

interface SourceGlyphsProps {
    readonly traffic: boolean
    readonly weather: boolean
    readonly fuel: boolean
    readonly className?: string
}

/**
 * The bulletin's content sources as three micro glyphs — lit when included,
 * dimmed when off. Replaces the "traffic · weather · fuel" text run.
 */
export function SourceGlyphs({
    traffic,
    weather,
    fuel,
    className,
}: SourceGlyphsProps) {
    const items = [
        { Icon: ICON.traffic, on: traffic, label: 'Traffic' },
        { Icon: ICON.weather, on: weather, label: 'Weather' },
        { Icon: ICON.fuel, on: fuel, label: 'Fuel' },
    ] as const

    return (
        <TooltipProvider>
            <span className={cn('inline-flex items-center gap-1', className)}>
                {items.map(({ Icon, on, label }) => (
                    <Tooltip key={label}>
                        <TooltipTrigger asChild>
                            <span
                                tabIndex={0}
                                role="img"
                                aria-label={`${label} ${on ? 'included' : 'off'}`}
                                className="inline-flex cursor-default items-center"
                            >
                                <Icon
                                    className={cn(
                                        ICON_SIZE.micro,
                                        on
                                            ? 'text-foreground'
                                            : 'text-muted-foreground-subtle'
                                    )}
                                    aria-hidden
                                />
                            </span>
                        </TooltipTrigger>
                        <TooltipContent side="top">
                            {label} {on ? 'included' : 'not included'}
                        </TooltipContent>
                    </Tooltip>
                ))}
            </span>
        </TooltipProvider>
    )
}
