import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

interface GlowBorderProps {
    readonly children: ReactNode
    readonly className?: string
    /** Inner surface classes (background, radius is derived). */
    readonly innerClassName?: string
    /** Border thickness in px. */
    readonly inset?: number
    /** Conic gradient stops; defaults to the urgency sweep. */
    readonly gradient?: string
}

const DEFAULT_GRADIENT =
    'conic-gradient(from 0deg, transparent 0%, hsl(var(--destructive) / 0.9) 12%, transparent 28%, transparent 50%, hsl(var(--primary) / 0.8) 62%, transparent 78%)'

/**
 * Aceternity-style moving border: a slowly rotating conic gradient clipped
 * to a 1-2px frame around the content. Pure CSS (an oversized spinning
 * layer under an inset surface), gated behind motion-safe; reduced-motion
 * users get a static gradient frame instead.
 */
export function GlowBorder({
    children,
    className,
    innerClassName,
    inset = 1.5,
    gradient = DEFAULT_GRADIENT,
}: GlowBorderProps) {
    return (
        <div className={cn('relative overflow-hidden rounded-lg', className)}>
            {/* Spinning gradient frame: oversized square so corners stay covered */}
            <div
                aria-hidden
                className="absolute left-1/2 top-1/2 aspect-square w-[200%] -translate-x-1/2 -translate-y-1/2 motion-safe:animate-[spin_5s_linear_infinite]"
                style={{ background: gradient }}
            />
            <div
                className={cn(
                    'relative rounded-[calc(0.5rem-1px)] bg-card',
                    innerClassName
                )}
                style={{ margin: `${inset}px` }}
            >
                {children}
            </div>
        </div>
    )
}
