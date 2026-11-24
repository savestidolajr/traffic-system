import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

interface RuleTabProps {
    readonly children: ReactNode
    readonly className?: string
}

/**
 * Shared scaffold for the Bulletin-rules hub tabs.
 */
export function RuleTab({ children, className }: RuleTabProps) {
    return (
        <div className={cn('flex flex-col gap-5', className)}>{children}</div>
    )
}
