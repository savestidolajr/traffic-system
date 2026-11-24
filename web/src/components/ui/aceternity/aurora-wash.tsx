import { cn } from '@/lib/utils'

interface AuroraWashProps {
    /** CSS background-image for the bloom (radial/linear gradient). */
    readonly gradient: string
    readonly className?: string
}

/**
 * Aceternity's Aurora Background at micro scale: a corner-masked gradient
 * bloom for a single data block, tinted by the data it sits behind (e.g.
 * the weather block tinted by actual conditions — state, not decoration).
 * CSS-only; the slow drift animates `transform` (compositor thread only —
 * see `aurora-hero.tsx` for why NOT `background-position`, upstream
 * Aceternity's approach) and is gated behind `motion-safe` (reduced motion
 * gets the same bloom, frozen).
 *
 * Parent must be `relative` (and usually `overflow-hidden rounded-*`).
 */
export function AuroraWash({ gradient, className }: AuroraWashProps) {
    return (
        <div
            aria-hidden
            className={cn(
                'pointer-events-none absolute inset-0 overflow-hidden rounded-[inherit]',
                className
            )}
        >
            <div
                className="absolute -inset-y-[12%] -inset-x-[25%] opacity-[0.16] blur-[18px] will-change-transform [mask-image:radial-gradient(ellipse_at_100%_0%,black_10%,transparent_70%)] motion-safe:animate-aurora-drift"
                style={{
                    backgroundImage: gradient,
                    backgroundSize: '150% 200%',
                }}
            />
        </div>
    )
}
