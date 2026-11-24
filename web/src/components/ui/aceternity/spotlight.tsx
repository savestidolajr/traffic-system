import { cn } from '@/lib/utils'

/**
 * Aceternity-style spotlight: a soft diagonal light sweep for sparse
 * surfaces (404s, empty heroes). Pure CSS — a blurred ellipse, rotated
 * and faded in; reduced-motion users get the static beam.
 */
export function Spotlight({ className }: { readonly className?: string }) {
    return (
        <div
            aria-hidden
            className={cn(
                'pointer-events-none absolute -top-40 left-1/2 h-[150%] w-[200%] -translate-x-1/2 select-none',
                className
            )}
        >
            <div className="absolute left-1/4 top-0 h-[55%] w-[45%] -rotate-[20deg] rounded-full bg-primary/30 blur-[110px] motion-safe:animate-in motion-safe:fade-in-0 motion-safe:slide-in-from-left-10 motion-safe:duration-1000" />
            <div className="absolute left-[45%] top-[10%] h-[40%] w-[30%] -rotate-[20deg] rounded-full bg-primary/20 blur-[90px] motion-safe:animate-in motion-safe:fade-in-0 motion-safe:slide-in-from-left-6 motion-safe:duration-1000" />
            <div className="absolute left-[35%] top-[5%] h-[30%] w-[15%] -rotate-[20deg] rounded-full bg-foreground/10 blur-[80px] motion-safe:animate-in motion-safe:fade-in-0 motion-safe:duration-1000" />
        </div>
    )
}
