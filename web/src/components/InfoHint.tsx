import type { ReactNode } from 'react'
import { ICON } from '@/lib/design/icons'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'

const HelpIcon = ICON.help

interface InfoHintProps {
    /** Accessible name for the trigger, e.g. `What is a priority road?`. */
    readonly label: string
    /** The explanation. Kept out of the default view on purpose. */
    readonly children: ReactNode
    readonly className?: string
}

/**
 * The one "explain this" affordance for the Bulletin-rules hub.
 *
 * A focusable `<button>`, never a `<span>`: Radix opens a tooltip on hover OR
 * focus, and a span takes neither, so the two call sites that shipped this as
 * a bare span were unreachable by keyboard on every tab.
 */
export function InfoHint({ label, children, className }: InfoHintProps) {
    return (
        <Tooltip>
            <TooltipTrigger asChild>
                <button
                    type="button"
                    aria-label={label}
                    className={cn(
                        'inline-flex size-5 shrink-0 items-center justify-center rounded-full text-muted-foreground transition-colors hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
                        className
                    )}
                >
                    <HelpIcon className="size-4" aria-hidden />
                </button>
            </TooltipTrigger>
            <TooltipContent side="bottom" className="max-w-xs">
                {children}
            </TooltipContent>
        </Tooltip>
    )
}
