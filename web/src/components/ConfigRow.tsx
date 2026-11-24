import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'
export function ConfigRow({
    label,
    children,
    className,
    labelWidth = 140,
}: {
    readonly label: string
    readonly children: ReactNode
    readonly className?: string
    /** Fixed label-column width in px; narrow it when the value regularly
     *  needs more room (default is suited to wide-value contexts). */
    readonly labelWidth?: number
}) {
    return (
        // A single element, not a row + <Separator/> pair. Radix's Separator
        // renders `<div role="none">`, and these rows are the direct children
        // of a <dl> — which may only contain dt/dd/div — so the decorative
        // divider was itself an axe `definition-list` violation. A border does
        // the same job with no extra node.
        <div
            className={cn(
                'grid items-baseline gap-3 border-b border-border py-3 last:border-b-0',
                className
            )}
            style={{
                gridTemplateColumns: `${labelWidth}px minmax(0,1fr)`,
            }}
        >
            <dt className="text-sm text-muted-foreground">{label}</dt>
            <dd className="min-w-0 text-sm font-medium text-foreground">
                {children}
            </dd>
        </div>
    )
}
