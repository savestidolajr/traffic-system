import type { ComponentType } from 'react'
import { cn } from '@/lib/utils'

type OpsCardHeaderProps = {
    readonly icon: ComponentType<{ className?: string }>
    readonly title: string
    readonly count?: number
    readonly countTone?: 'warning'
}

export function OpsCardHeader({
    icon: Icon,
    title,
    count,
    countTone,
}: OpsCardHeaderProps) {
    return (
        <span className="flex items-center gap-2 text-xs font-semibold">
            <Icon className="size-3.5 text-muted-foreground" aria-hidden />
            {title}
            {count !== undefined && (
                <span
                    className={cn(
                        'inline-flex h-4 min-w-4 items-center justify-center rounded-full px-1 font-mono text-3xs tabular-nums',
                        countTone === 'warning' && count > 0
                            ? 'bg-warning/20 text-warning'
                            : 'bg-muted text-muted-foreground'
                    )}
                >
                    {count}
                </span>
            )}
        </span>
    )
}
