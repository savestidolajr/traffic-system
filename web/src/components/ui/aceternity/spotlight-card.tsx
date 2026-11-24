import { useRef, type ReactNode, type MouseEvent } from 'react'
import { cn } from '@/lib/utils'

interface SpotlightCardProps {
    readonly children: ReactNode
    readonly className?: string
    /** Spotlight radius in px. */
    readonly radius?: number
}

/**
 * Aceternity-style spotlight card: a radial glow that follows the cursor,
 * plus a border highlight near the pointer. Implemented with CSS custom
 * properties written straight to the DOM node (no re-renders, no
 * framer-motion) so the effect costs nothing while idle.
 *
 * `className` styles the card's SKIN — padding, background, border, `h-full`,
 * `overflow`, transitions. It does NOT lay out `children`: it lands on the
 * outer element, whose only in-flow child is the `relative h-full` wrapper
 * below, so a `flex flex-col gap-*` here spaces exactly one item and the gap
 * renders as **zero**. Twelve callsites had declared an inert gap that way.
 * Children needing layout get their own wrapper div — see `MetricCard.tsx` or
 * any entity card (`DeliveryStyleCard.tsx`).
 */
export function SpotlightCard({
    children,
    className,
    radius = 320,
}: SpotlightCardProps) {
    const ref = useRef<HTMLDivElement>(null)

    const handleMouseMove = (e: MouseEvent<HTMLDivElement>) => {
        const el = ref.current
        if (!el) return
        const rect = el.getBoundingClientRect()
        el.style.setProperty('--spot-x', `${e.clientX - rect.left}px`)
        el.style.setProperty('--spot-y', `${e.clientY - rect.top}px`)
    }

    return (
        <div
            ref={ref}
            onMouseMove={handleMouseMove}
            className={cn(
                'group/spotlight relative overflow-hidden rounded-xl border bg-card text-card-foreground',
                'transition-colors duration-200 hover:border-primary/40',
                className
            )}
            style={{ '--spot-r': `${radius}px` } as React.CSSProperties}
        >
            {/* Cursor-following glow */}
            <div
                aria-hidden
                className="pointer-events-none absolute inset-0 opacity-0 transition-opacity duration-300 group-hover/spotlight:opacity-100"
                style={{
                    background:
                        'radial-gradient(var(--spot-r) circle at var(--spot-x, 50%) var(--spot-y, 50%), hsl(var(--primary) / 0.1), transparent 70%)',
                }}
            />
            {/* Top edge light, the tonal layering cue for dark surfaces */}
            <div
                aria-hidden
                className="pointer-events-none absolute inset-x-0 top-0 h-px bg-linear-to-r from-transparent via-primary/30 to-transparent opacity-0 transition-opacity duration-300 group-hover/spotlight:opacity-100"
            />
            <div className="relative h-full">{children}</div>
        </div>
    )
}
