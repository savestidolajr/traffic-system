import { cn } from '@/lib/utils'
import { abbreviate, cronToText } from '@/features/schedules/lib/cron-format'

interface CronHumanReadableProps {
    expression: string | null
    abbreviated?: boolean
    className?: string
}

export function CronHumanReadable({
    expression,
    abbreviated = false,
    className,
}: CronHumanReadableProps) {
    if (!expression) return <span className={className}>-</span>

    const text = cronToText(expression)
    if (text === null) {
        return (
            <span
                className={cn('font-mono text-xs tabular-nums', className)}
                title={expression}
            >
                <code className="text-xs">{expression}</code>
            </span>
        )
    }

    return (
        <span
            className={cn('font-mono text-xs tabular-nums', className)}
            title={abbreviated ? text : undefined}
        >
            {abbreviated ? abbreviate(text) : text}
        </span>
    )
}
