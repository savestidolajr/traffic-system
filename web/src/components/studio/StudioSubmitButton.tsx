import { Button } from '@/components/ui/button'
import { GlowBorder } from '@/components/ui/aceternity/glow-border'
import { Spinner } from '@/components/ui/spinner'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'

interface StudioSubmitButtonProps {
    readonly label: string
    readonly isSubmitting: boolean
    readonly isValid: boolean
    /** Why the button is disabled — shown as a tooltip so the operator is
     *  never left guessing (e.g. "No changes yet"). */
    readonly disabledReason?: string
    readonly className?: string
}

export function StudioSubmitButton({
    label,
    isSubmitting,
    isValid,
    disabledReason,
    className,
}: StudioSubmitButtonProps) {
    if (isSubmitting) {
        return (
            <Button type="submit" disabled className={cn('gap-2', className)}>
                <Spinner className="size-4" />
                {label}
            </Button>
        )
    }
    const btn = (
        <Button type="submit" disabled={!isValid} className={className}>
            {label}
        </Button>
    )
    if (isValid) return <GlowBorder>{btn}</GlowBorder>
    if (!disabledReason) return btn
    return (
        <Tooltip>
            <TooltipTrigger asChild>
                {/* Disabled buttons swallow pointer events; the span keeps the
                    tooltip reachable by hover and keyboard. */}
                <span tabIndex={0} className="inline-flex rounded-md">
                    {btn}
                </span>
            </TooltipTrigger>
            <TooltipContent side="top">
                <p>{disabledReason}</p>
            </TooltipContent>
        </Tooltip>
    )
}
