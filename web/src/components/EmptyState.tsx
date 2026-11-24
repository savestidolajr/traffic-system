import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

interface EmptyStateProps {
    readonly children: ReactNode
    /**
     * `section` — a list/manager area with nothing in it: tall, centred,
     * room for an action button. `row` — a slot inside a card (no rules
     * yet, no markets assigned): one quiet line.
     */
    readonly variant?: 'section' | 'row'
    /** Optional action (button/link) rendered under the message. */
    readonly action?: ReactNode
    readonly className?: string
}

/**
 * The standard in-content empty state: dashed border, centred muted text.
 * For whole-page empties (nothing of an entity exists at all) use
 * `Placeholder`, which brings the hero treatment; this is the quiet
 * in-card / in-section tier.
 */
export function EmptyState({
    children,
    variant = 'section',
    action,
    className,
}: EmptyStateProps) {
    return (
        <div
            className={cn(
                'flex flex-col items-center justify-center rounded-lg border border-dashed border-border/60 text-center text-muted-foreground',
                variant === 'section'
                    ? 'gap-3 py-12 text-sm'
                    : 'gap-2 px-4 py-5 text-2xs',
                className
            )}
        >
            <div>{children}</div>
            {action}
        </div>
    )
}
