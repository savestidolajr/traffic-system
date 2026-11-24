import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'
import type { LucideIcon } from 'lucide-react'
import { InfoHint } from '@/components/InfoHint'

type HintProps =
    | {
          /**
           * The explanation that used to sit under the heading as a helper
           * paragraph. Rendered in a tooltip so the section stays scannable.
           */
          readonly hint: ReactNode
          /**
           * The hint trigger's accessible name, e.g. `What is a priority road?`.
           * Required alongside `hint`.
           */
          readonly hintLabel: string
      }
    | { readonly hint?: undefined; readonly hintLabel?: undefined }

type SectionHeaderProps = HintProps & {
    readonly icon?: LucideIcon
    readonly label: string
    readonly badge?: ReactNode
    readonly trailing?: ReactNode
    readonly className?: string
}

export function SectionHeader({
    icon: Icon,
    label,
    badge,
    hint,
    hintLabel,
    trailing,
    className,
}: SectionHeaderProps) {
    if (!Icon) {
        return (
            <p
                className={cn(
                    'flex items-center gap-1.5 text-sm font-semibold text-muted-foreground',
                    className
                )}
            >
                {label}
                {hint && <InfoHint label={hintLabel}>{hint}</InfoHint>}
            </p>
        )
    }

    return (
        <div className={cn('flex items-center justify-between', className)}>
            <div className="flex items-center gap-2.5">
                <Icon className="size-4 text-primary" />
                <p className="text-sm font-semibold">{label}</p>
                {badge}
                {hint && <InfoHint label={hintLabel}>{hint}</InfoHint>}
            </div>
            {trailing}
        </div>
    )
}
