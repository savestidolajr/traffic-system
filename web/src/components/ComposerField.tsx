import type { ReactNode } from 'react'
import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'

interface ComposerFieldProps {
    readonly label: string
    /** Wires the label to a control's `id` for a11y (native inputs only — Select/pill controls skip this). */
    readonly htmlFor?: string
    /** Tailwind width utility for the field's column (e.g. `sm:w-44`, `flex-1`). Omit for natural content sizing (e.g. a pill/select control). */
    readonly width?: string
    readonly children: ReactNode
}

/**
 * One labelled field in a {@link RuleComposer} row: a small muted label above
 * a control. This is the ONE label style for the compact composer-bar family
 * (bulletin rules + editorial guidance) — deliberately smaller/quieter than
 * Studio dialog labels, which are a different, spacious editing context.
 * Extracting this stops the two contexts from drifting into each other.
 */
export function ComposerField({
    label,
    htmlFor,
    width,
    children,
}: ComposerFieldProps) {
    /*
     * A composer row is `flex-wrap` (see RuleComposer), so each column has to
     * declare how it behaves when the row runs out of space. Fixed columns
     * (`sm:w-44`, `sm:w-60`) must NOT shrink — left to the flex default they
     * compressed until the selected market read `Global · all…` at 768px.
     * The one flexible column carries the field the operator actually types
     * into, so it gets a floor instead: below `min-w-56` it wrapped to its own
     * line rather than collapsing to a 60px sliver.
     */
    const flexible = width?.includes('flex-1')
    return (
        <div
            className={cn(
                'flex flex-col gap-1.5',
                flexible ? 'min-w-56' : 'shrink-0',
                width
            )}
        >
            {/* No `text-xs` override. The rules-hub composers rendered 12px
                labels while every studio form rendered 14px — one role, two
                sizes. The project already resolved this once (2026-06-24,
                GuidanceComposer) and it came back through this shared field;
                the size now comes from the Label primitive so it cannot. */}
            <Label htmlFor={htmlFor} className="text-muted-foreground">
                {label}
            </Label>
            {children}
        </div>
    )
}
