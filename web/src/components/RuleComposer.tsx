import type { ReactNode } from 'react'
import type { LucideIcon } from 'lucide-react'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { GlowBorder } from '@/components/ui/aceternity/glow-border'
import { Button } from '@/components/ui/button'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'

export interface RuleComposerAction {
    readonly label: string
    readonly icon?: LucideIcon
    readonly onClick: () => void
    /** Disabled while input is invalid or a request is in flight. */
    readonly disabled: boolean
    /** Shows the GlowBorder "ready" treatment when the input is valid. */
    readonly ready: boolean
    readonly submitting: boolean
    /** Tooltip shown on hover when the button is disabled. */
    readonly tooltip?: string
}

interface RuleComposerProps {
    /**
     * Header text. OMIT for a plain create form: the toolbar's Add button that
     * opened this card already said "Add a priority road", and repeating it
     * 60px below is a whole wasted row saying nothing new. Pass a title only
     * when it tells the operator something the button did not — that they are
     * editing an existing row, or promoting a note.
     */
    readonly title?: string
    /** Renders a Cancel ghost button beside the submit action. */
    readonly onCancel?: () => void
    /** Primary action, rendered inline at the end of the field row. */
    readonly submit: RuleComposerAction
    /** The fields — sibling controls laid out as one row before the action. */
    readonly children: ReactNode
    readonly className?: string
}

/**
 * Shared authoring shell for the Bulletin-rules hub composers. Owns the card
 * surface, the title/cancel header, and a single field row that ends with the
 * GlowBorder-when-ready primary action. Each tab passes its controls as
 * sibling children; the value input should carry `flex-1` so the action sits
 * hard against the right edge.
 *
 * This card is NOT permanently mounted. It opens from the tab's `RuleToolbar`
 * Add button (or by editing a row) and dismisses on save/cancel, so the resting
 * state of every tab is toolbar + list rather than a form you scroll past to
 * read what already exists. The "what is this kind of rule?" explanation
 * therefore lives on the toolbar, not here — it has to stay reachable while
 * the composer is closed.
 */
export function RuleComposer({
    title,
    onCancel,
    submit,
    children,
    className,
}: RuleComposerProps) {
    const SubmitIcon = submit.icon
    const buttonInner = (
        <Button onClick={submit.onClick} disabled={submit.disabled}>
            {SubmitIcon && <SubmitIcon />}
            {submit.submitting ? 'Saving…' : submit.label}
        </Button>
    )

    const button =
        submit.disabled && submit.tooltip ? (
            <Tooltip>
                <TooltipTrigger asChild>
                    <span className="inline-flex">{buttonInner}</span>
                </TooltipTrigger>
                <TooltipContent side="bottom">{submit.tooltip}</TooltipContent>
            </Tooltip>
        ) : (
            buttonInner
        )

    return (
        <SpotlightCard className={cn('p-4', className)}>
            <div className="flex flex-col gap-4">
                {title && (
                    <span className="text-sm font-semibold">{title}</span>
                )}

                <div className="flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-end">
                    {children}
                    {onCancel && (
                        <Button variant="ghost" onClick={onCancel}>
                            Cancel
                        </Button>
                    )}
                    {submit.ready && !submit.submitting ? (
                        <GlowBorder innerClassName="bg-transparent">
                            {button}
                        </GlowBorder>
                    ) : (
                        button
                    )}
                </div>
            </div>
        </SpotlightCard>
    )
}
