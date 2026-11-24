import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

type MovingBorderTone = 'primary' | 'warning' | 'destructive'

/**
 * Conic sweep per tone. Static literal class strings — Tailwind's JIT
 * cannot see runtime-built class names.
 */
const SWEEP_CLASS: Record<MovingBorderTone, string> = {
    primary:
        '[background:conic-gradient(from_0deg,transparent_55%,hsl(var(--primary))_80%,hsl(var(--primary)/0.35)_92%,transparent_100%)]',
    warning:
        '[background:conic-gradient(from_0deg,transparent_55%,hsl(var(--warning))_80%,hsl(var(--warning)/0.35)_92%,transparent_100%)]',
    destructive:
        '[background:conic-gradient(from_0deg,transparent_55%,hsl(var(--destructive))_80%,hsl(var(--destructive)/0.35)_92%,transparent_100%)]',
}

/**
 * Aceternity "Moving Border", CSS-only: their version animates a glow dot
 * along the border path with motion's getPointAtLength; the same read —
 * light traveling around the edge — comes from rotating an oversized
 * conic gradient behind a 1px-inset inner card. Reduced motion keeps the
 * static arc as a plain accent ring.
 */
export function MovingBorder({
    tone = 'primary',
    className,
    innerClassName,
    children,
}: {
    readonly tone?: MovingBorderTone
    readonly className?: string
    readonly innerClassName?: string
    readonly children: ReactNode
}) {
    return (
        <div
            className={cn(
                'relative overflow-hidden rounded-lg p-px',
                className
            )}
        >
            <div
                aria-hidden
                className={cn(
                    'absolute inset-[-100%] motion-safe:animate-[spin_3.5s_linear_infinite]',
                    SWEEP_CLASS[tone]
                )}
            />
            <div
                className={cn(
                    'relative rounded-[calc(0.5rem-1px)] bg-background',
                    innerClassName
                )}
            >
                {children}
            </div>
        </div>
    )
}
