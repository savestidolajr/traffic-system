import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import type { LucideIcon } from 'lucide-react'

export function MetricCard({
    label,
    icon: Icon,
    children,
    className,
}: {
    readonly label: string
    readonly icon?: LucideIcon
    readonly children: ReactNode
    readonly className?: string
}) {
    return (
        <SpotlightCard className={cn('p-3 text-center', className)}>
            <div className="flex flex-col items-center gap-1">
                <span className="text-xs font-semibold uppercase tracking-widest text-muted-foreground">
                    {label}
                </span>
                <span className="flex items-center justify-center gap-1.5 font-mono text-xs">
                    {Icon && <Icon className="size-3 text-muted-foreground" />}
                    {children}
                </span>
            </div>
        </SpotlightCard>
    )
}
