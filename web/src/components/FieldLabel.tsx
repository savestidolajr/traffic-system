import type { ReactNode } from 'react'
import { Label } from '@/components/ui/label'
import { InfoHint } from '@/components/InfoHint'
import { cn } from '@/lib/utils'

type HintProps =
    | {
          /**
           * The explanation that used to sit under the control as a helper
           * paragraph. Rendered in a tooltip so the form stays scannable.
           */
          readonly hint: ReactNode
          /**
           * The hint trigger's accessible name, e.g. `What is a phoneme rule?`.
           * Required alongside `hint`: the label may be a node (a name plus a
           * muted count), so it cannot be reused as an accessible name.
           */
          readonly hintLabel: string
      }
    | { readonly hint?: undefined; readonly hintLabel?: undefined }

type FieldLabelProps = HintProps & {
    /** The label content. */
    readonly children: ReactNode
    /** Wires the label to a control's `id`. */
    readonly htmlFor?: string
    /** Needed when a Radix trigger points back at the label via `aria-labelledby`. */
    readonly id?: string
    /** Marks the field as required, mirroring FormLabel's required asterisk. */
    readonly required?: boolean
    /** Right-aligned controls that belong on the label row (counts, buttons). */
    readonly trailing?: ReactNode
    readonly className?: string
}

/**
 * A studio-form field label with its explanation behind a tooltip.
 *
 * Studio forms used to stack a `<p className="text-sm text-muted-foreground">`
 * under every control. Three or four of those push the content the operator
 * actually came to edit below the fold — the pronunciation studio's rule table
 * sat ~200px down a 650px viewport purely because of helper prose. The
 * explanation is still one hover/focus away via {@link InfoHint}; it just no
 * longer costs vertical space on every visit.
 *
 * Only pass `hint` when the sentence carries a fact the label does not. Copy
 * that merely restates the label (`Dictionary name` → "Use a name operators
 * recognise") belongs nowhere, not in a tooltip.
 */
export function FieldLabel({
    children,
    htmlFor,
    id,
    required,
    hint,
    hintLabel,
    trailing,
    className,
}: FieldLabelProps) {
    return (
        <div className={cn('flex min-h-5 items-center gap-1.5', className)}>
            <Label htmlFor={htmlFor} id={id}>
                {children}
                {required && <span className="text-destructive ml-0.5">*</span>}
            </Label>
            {hint && <InfoHint label={hintLabel}>{hint}</InfoHint>}
            {trailing && <div className="ml-auto">{trailing}</div>}
        </div>
    )
}
