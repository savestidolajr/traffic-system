/**
 * Aceternity-style dot-grid backdrop, radially masked so it fades toward
 * the edges. Mounted once in the app shell behind every page. Pure CSS.
 */
export function DotGridBackdrop() {
    return (
        <div
            aria-hidden
            className="pointer-events-none absolute inset-0 [background-image:radial-gradient(hsl(var(--muted-foreground)/0.13)_1px,transparent_1px)] [background-size:22px_22px] [mask-image:radial-gradient(ellipse_70%_55%_at_50%_25%,black,transparent)]"
        />
    )
}
