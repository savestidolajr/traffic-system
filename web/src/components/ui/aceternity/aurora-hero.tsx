import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

/**
 * Aceternity "Aurora Background", adapted for the workstation: a contained
 * hero band (not a 100vh marketing section), the palette shifted from
 * Aceternity's blues to the ARN purple family, and the drift wrapped in
 * `motion-safe` (reduced motion gets the same aurora, frozen).
 *
 * The aurora ramp below intentionally uses a fixed ARN brand palette rather
 * than theme tokens: it is the component's signature decorative treatment,
 * while the surrounding surfaces and foreground remain theme-driven. This
 * keeps the hero visually recognizable across light/dark themes.
 *
 * The gradient recipe is Aceternity's verbatim: two repeating linear
 * gradients stacked (a theme tone-mask plus the aurora ramp), blurred,
 * masked to bloom from the top-right. ONE deviation from upstream: the
 * moving layer animates `transform: translateX()` instead of upstream's
 * `background-position`. `background-position` is a main-thread-only
 * property — Chrome can NEVER composite it, so it forces a Style+Layout+
 * Paint pass on every animation frame for as long as the component is
 * mounted (measured: ~1.5s of main-thread work per 10s window under 4x CPU
 * throttle — real, continuous jank on a screen operators keep open all
 * day). `transform`/`opacity` are the only two CSS properties the
 * compositor can animate independent of the main thread (developer.chrome.com/
 * docs/lighthouse/performance/non-composited-animations). The moving layer
 * is oversized (130% width) so translating ±6% of ITS width never uncovers
 * an edge; a single 0%→50%→100% keyframe ping-pongs smoothly with no reset
 * seam (the gradient doesn't need to tile at an exact multiple).
 */
export function AuroraHero({
    className,
    contentClassName,
    children,
}: {
    readonly className?: string
    readonly contentClassName?: string
    readonly children: ReactNode
}) {
    return (
        <div
            className={cn(
                'relative overflow-hidden rounded-2xl border bg-card',
                className
            )}
        >
            <div
                aria-hidden
                className="absolute inset-0 overflow-hidden"
                style={
                    {
                        '--aurora':
                            'repeating-linear-gradient(100deg,#7c3aed 10%,#a78bfa 15%,#c4b5fd 20%,#ddd6fe 25%,#8b5cf6 30%)',
                        '--dark-gradient':
                            'repeating-linear-gradient(100deg,#000 0%,#000 7%,transparent 10%,transparent 12%,#000 16%)',
                        '--white-gradient':
                            'repeating-linear-gradient(100deg,#fff 0%,#fff 7%,transparent 10%,transparent 12%,#fff 16%)',
                    } as React.CSSProperties
                }
            >
                {/* Static base layer — never animates, no perf cost. */}
                <div
                    className={cn(
                        'pointer-events-none absolute -inset-[10px] opacity-40 blur-[10px] invert dark:invert-0',
                        '[background-image:var(--white-gradient),var(--aurora)] dark:[background-image:var(--dark-gradient),var(--aurora)]',
                        '[background-size:300%,200%] [background-position:50%_50%,50%_50%]',
                        '[mask-image:radial-gradient(ellipse_at_100%_0%,black_10%,transparent_70%)]'
                    )}
                />
                {/* Moving layer — oversized so a transform-based drift never
                    uncovers an edge; animates `transform` only (compositor
                    thread, zero main-thread cost per frame). */}
                <div
                    className={cn(
                        'pointer-events-none absolute -inset-y-[10px] -inset-x-[20%] opacity-40 blur-[10px] invert will-change-transform dark:invert-0 mix-blend-difference',
                        '[background-image:var(--white-gradient),var(--aurora)] dark:[background-image:var(--dark-gradient),var(--aurora)]',
                        '[background-size:200%,100%]',
                        'motion-safe:animate-aurora-drift',
                        '[mask-image:radial-gradient(ellipse_at_100%_0%,black_10%,transparent_70%)]'
                    )}
                />
            </div>
            <div className={cn('relative', contentClassName)}>{children}</div>
        </div>
    )
}
