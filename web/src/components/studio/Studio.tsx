import { type ReactNode, useEffect, useRef, useState } from 'react'
import * as DialogPrimitive from '@radix-ui/react-dialog'
import { AlertTriangle, X } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { DotGridBackdrop } from '@/components/ui/aceternity/dot-grid-backdrop'
import {
    Tooltip,
    TooltipContent,
    TooltipTrigger,
} from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'
import {
    Dialog,
    DialogPortal,
    DialogOverlay,
    DialogClose,
    DialogTitle,
    DialogDescription,
    DIALOG_CLOSE_BUTTON_BASE_CLASS,
} from '@/components/ui/dialog'

const STUDIO_DIALOG_SIZE =
    'max-h-[94dvh] w-[min(1160px,96vw)] md:h-[min(780px,94dvh)]'
/** Compact variant for focused action modals (revise, confirm-with-options).
 *  `h-fit`, not `h-auto` — with `inset-0 m-auto` centering, a plain `auto`
 *  height plus `auto` margins is CSS's classic over-constrained case: the
 *  browser zeroes the margins and stretches the box to fill the viewport
 *  instead of shrink-wrapping. `h-fit` is a definite value, so it centers
 *  a content-sized box (capped by max-h) the way the default size's
 *  explicit height already does. */
const STUDIO_DIALOG_COMPACT = 'max-h-[90dvh] h-fit w-[min(560px,96vw)]'

// =============================================================================
// Studio — a centered command-center modal for multi-domain editors.
// Composition: StudioDialog > StudioBody > (StudioRail | StudioCanvas),
// then StudioFooter. Each step gets the full canvas; the rail carries live
// value summaries and validity; the footer carries live consequences.
// Entrance is the Aceternity animated-modal pop, as a CSS keyframe on a
// Radix Dialog (focus trap and ARIA for free, no motion dependency).
// =============================================================================

export interface StudioStep {
    readonly id: string
    readonly label: string
    readonly icon: LucideIcon
    /** Live one-line summary of the step's current value. */
    readonly summary?: ReactNode
    /** Step currently fails validation. */
    readonly invalid?: boolean
}

interface StudioDialogProps {
    readonly open: boolean
    readonly onOpenChange: (open: boolean) => void
    /** Bold lead-in naming the action, e.g. "Edit voice", "New station". */
    readonly title: string
    /** Plain-weight subject after the title (rendered as `: {subject}`) — the
     *  item's own name/identity, e.g. "ARN Hudson, Natural". Omitted in
     *  create flows where there's no subject yet. */
    readonly subject?: string
    readonly description?: string
    /** `default` = full multi-section editor; `compact` = focused action modal. */
    readonly size?: 'default' | 'compact'
    readonly children: ReactNode
}

export function StudioDialog({
    open,
    onOpenChange,
    title,
    subject,
    description,
    size = 'default',
    children,
}: StudioDialogProps) {
    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogPortal>
                <DialogOverlay />
                <DialogPrimitive.Content
                    className={cn(
                        `fixed inset-0 z-50 m-auto flex ${size === 'compact' ? STUDIO_DIALOG_COMPACT : STUDIO_DIALOG_SIZE} flex-col overflow-hidden rounded-2xl border bg-background shadow-2xl outline-none`,
                        'motion-safe:data-[state=open]:animate-studio-in motion-reduce:data-[state=open]:animate-in motion-reduce:data-[state=open]:fade-in-0',
                        'data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-95 data-[state=closed]:duration-150'
                    )}
                >
                    {/* Title bar: the close button lives in its own row,
                        never floating over step content — the industry-
                        standard "settings modal" chrome (macOS/Slack/VS
                        Code/Linear all sandwich sidebar+canvas between a
                        title bar and a footer, rather than pinning the
                        close affordance on top of whatever a page renders). */}
                    <div className="flex shrink-0 items-center justify-between gap-3 border-b bg-muted/30 px-5 py-3">
                        <DialogTitle className="truncate text-sm">
                            <span className="font-semibold">{title}</span>
                            {subject && (
                                <span className="font-normal text-muted-foreground">
                                    : {subject}
                                </span>
                            )}
                        </DialogTitle>
                        <DialogClose
                            className={DIALOG_CLOSE_BUTTON_BASE_CLASS}
                            aria-label="Close"
                        >
                            <X className="size-4" />
                        </DialogClose>
                    </div>
                    {description && (
                        <DialogDescription className="sr-only">
                            {description}
                        </DialogDescription>
                    )}
                    {children}
                </DialogPrimitive.Content>
            </DialogPortal>
        </Dialog>
    )
}

export function StudioBody({ children }: { readonly children: ReactNode }) {
    return (
        <div className="flex min-h-0 flex-1 flex-col md:flex-row">
            {children}
        </div>
    )
}

interface StudioRailProps {
    /** Accessible label for the step nav ("{title} steps") — no longer
     *  rendered visually now that the dialog's title bar shows the title. */
    readonly title: string
    readonly steps: readonly StudioStep[]
    readonly activeId: string
    readonly onSelect: (id: string) => void
}

/**
 * The step rail: free navigation (an editor, not a wizard), each step
 * carrying its live value summary and validity dot. Polished horizontal
 * labelled rail on mobile (hidden native scrollbar, edge fades, 44px
 * hit areas), vertical rail on desktop.
 */
export function StudioRail({
    title,
    steps,
    activeId,
    onSelect,
}: StudioRailProps) {
    const railRef = useRef<HTMLDivElement>(null)
    const [canScrollLeft, setCanScrollLeft] = useState(false)
    const [canScrollRight, setCanScrollRight] = useState(false)

    // Track horizontal overflow for edge-fade cues
    useEffect(() => {
        const rail = railRef.current
        if (!rail) return
        const update = () => {
            setCanScrollLeft(rail.scrollLeft > 2)
            setCanScrollRight(
                rail.scrollLeft < rail.scrollWidth - rail.clientWidth - 2
            )
        }
        update()
        rail.addEventListener('scroll', update, { passive: true })
        const ro = new ResizeObserver(update)
        ro.observe(rail)
        return () => {
            rail.removeEventListener('scroll', update)
            ro.disconnect()
        }
    }, [])

    // Auto-scroll active step into view on mobile
    useEffect(() => {
        const rail = railRef.current
        if (!rail) return
        const btn = rail.querySelector('[aria-current="step"]')
        if (btn && typeof btn.scrollIntoView === 'function') {
            btn.scrollIntoView({
                behavior: 'smooth',
                block: 'nearest',
                inline: 'center',
            })
        }
    }, [activeId])

    return (
        <nav
            ref={railRef}
            aria-label={`${title} steps`}
            className={cn(
                'relative flex w-full shrink-0 flex-row gap-1 border-b bg-muted/20 p-2',
                // Mobile: horizontal scroll, hidden native scrollbar
                'overflow-x-auto overflow-y-hidden [-ms-overflow-style:none] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden',
                // Desktop: vertical rail, no scroll
                'md:w-[230px] md:flex-col md:overflow-visible md:border-b-0 md:border-r md:p-3'
            )}
        >
            <DotGridBackdrop />
            {/* Edge fades — only visible on mobile when content overflows */}
            {canScrollLeft && (
                <div
                    aria-hidden
                    className="pointer-events-none absolute bottom-0 left-0 top-0 z-10 w-5 bg-linear-to-r from-muted/60 to-transparent md:hidden"
                />
            )}
            {canScrollRight && (
                <div
                    aria-hidden
                    className="pointer-events-none absolute bottom-0 right-0 top-0 z-10 w-5 bg-linear-to-l from-muted/60 to-transparent md:hidden"
                />
            )}
            {steps.map((step) => {
                const Icon = step.icon
                const active = step.id === activeId
                return (
                    <button
                        key={step.id}
                        type="button"
                        onClick={() => onSelect(step.id)}
                        aria-current={active ? 'step' : undefined}
                        aria-label={step.label}
                        className={cn(
                            'relative flex shrink-0 items-center gap-1.5 rounded-lg px-2 py-1.5 text-left transition-colors',
                            'min-h-11',
                            active ? 'bg-primary/10' : 'hover:bg-muted/60',
                            'md:w-full md:gap-2.5 md:px-2.5 md:py-2'
                        )}
                    >
                        <span
                            className={cn(
                                'flex size-8 shrink-0 items-center justify-center rounded-md border transition-colors',
                                active
                                    ? 'border-primary/40 bg-primary/10 text-primary-on-tint'
                                    : 'border-border bg-background/60 text-muted-foreground'
                            )}
                        >
                            <Icon className="size-4" aria-hidden />
                        </span>
                        {/* Mobile: compact single-line label */}
                        <span
                            className={cn(
                                'min-w-0 shrink truncate text-sm font-medium leading-tight md:hidden',
                                active
                                    ? 'text-foreground'
                                    : 'text-muted-foreground'
                            )}
                        >
                            {step.label}
                        </span>
                        {/* Desktop: full label + summary + validation dot */}
                        <span className="hidden min-w-0 flex-1 flex-col md:flex">
                            <span
                                className={cn(
                                    'flex items-center gap-1.5 text-sm font-medium leading-tight',
                                    active
                                        ? 'text-foreground'
                                        : 'text-muted-foreground'
                                )}
                            >
                                {step.label}
                                {step.invalid && (
                                    <Tooltip>
                                        <TooltipTrigger asChild>
                                            <span
                                                className="size-1.5 rounded-full bg-warning"
                                                aria-label="Step needs attention"
                                                role="img"
                                            />
                                        </TooltipTrigger>
                                        <TooltipContent side="bottom">
                                            <p>Needs attention</p>
                                        </TooltipContent>
                                    </Tooltip>
                                )}
                            </span>
                            {step.summary && (
                                <span className="truncate text-2xs text-muted-foreground">
                                    {step.summary}
                                </span>
                            )}
                        </span>
                    </button>
                )
            })}
        </nav>
    )
}

/** The step's working area. Re-keys per step for a quick slide-in. */
export function StudioCanvas({
    stepKey,
    children,
}: {
    readonly stepKey: string
    readonly children: ReactNode
}) {
    return (
        <div className="min-w-0 flex-1 overflow-y-auto p-5">
            <div
                key={stepKey}
                className="flex flex-col gap-6 animate-in fade-in-0 slide-in-from-right-2 duration-200 motion-reduce:animate-none"
            >
                {children}
            </div>
        </div>
    )
}

interface StudioErrorSummaryProps {
    readonly steps: readonly StudioStep[]
    readonly onSelect: (id: string) => void
}

/**
 * Compact cross-step error summary for the submit area: validation problems
 * live inside their steps, so when the operator is on another step the only
 * signal is the rail dot. This row names every invalid step next to the
 * submit button and jumps straight to it. Renders nothing when all is well.
 */
export function StudioErrorSummary({
    steps,
    onSelect,
}: StudioErrorSummaryProps) {
    const invalidSteps = steps.filter((step) => step.invalid)
    if (invalidSteps.length === 0) return null
    return (
        <div
            role="alert"
            className="flex flex-wrap items-center gap-x-2 gap-y-1.5 border-t bg-warning/5 px-5 py-2"
        >
            <span className="inline-flex items-center gap-1.5 text-xs font-medium text-warning">
                <AlertTriangle className="size-3.5 shrink-0" aria-hidden />
                Fix issues in:
            </span>
            {invalidSteps.map((step) => (
                <button
                    key={step.id}
                    type="button"
                    onClick={() => onSelect(step.id)}
                    className="inline-flex items-center gap-1 rounded-full border border-warning/40 bg-warning/10 px-2 py-0.5 text-xs font-medium text-warning transition-colors hover:bg-warning/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                >
                    {step.label}
                </button>
            ))}
        </div>
    )
}

/** Consequences + actions: one horizontal line, always exactly full. */
export function StudioFooter({ children }: { readonly children: ReactNode }) {
    return (
        <div className="flex flex-wrap items-center gap-x-5 gap-y-2 border-t bg-muted/30 px-5 py-3">
            {children}
        </div>
    )
}
