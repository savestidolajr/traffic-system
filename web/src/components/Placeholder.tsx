import { Card, CardContent } from './ui/card'
import { type LucideIcon } from 'lucide-react'
import { cn } from '@/lib/utils'
import type { ReactNode } from 'react'
import { Spotlight } from './ui/aceternity/spotlight'
import { DotGridBackdrop } from './ui/aceternity/dot-grid-backdrop'

interface PlaceholderProps {
    readonly icon: LucideIcon
    readonly message: string
    readonly detail?: string
    readonly action?: ReactNode
    readonly variant?: 'empty' | 'loading' | 'error'
    readonly className?: string
}

/**
 * The standard empty / loading / error state. The `empty` variant is an
 * Aceternity "empty hero": a dot-grid backdrop + soft spotlight sweep behind a
 * glowing icon medallion (mirrors the 404 page), so a page with nothing in it
 * still reads as intentional. `loading` / `error` stay lean for inline use.
 */
export function Placeholder({
    icon: Icon,
    message,
    detail,
    action,
    variant = 'empty',
    className,
}: PlaceholderProps) {
    const isEmpty = variant === 'empty'

    return (
        <Card
            className={cn('relative overflow-hidden border-dashed', className)}
        >
            {isEmpty && (
                <>
                    <DotGridBackdrop />
                    <Spotlight />
                </>
            )}
            <CardContent
                className={cn(
                    'relative flex flex-col items-center justify-center text-center',
                    isEmpty ? 'py-16' : 'py-12'
                )}
            >
                {isEmpty ? (
                    <div className="mb-4 flex size-14 items-center justify-center rounded-2xl border border-primary/25 bg-primary/10 text-primary-on-tint shadow-glow">
                        <Icon className="size-7" aria-hidden />
                    </div>
                ) : (
                    <Icon
                        className={cn(
                            'mb-4 size-8 text-muted-foreground-subtle',
                            variant === 'loading' && 'animate-spin'
                        )}
                        aria-hidden
                    />
                )}
                <p
                    className={cn(
                        'mb-1 font-medium',
                        isEmpty ? 'text-base' : 'text-sm'
                    )}
                >
                    {message}
                </p>
                {detail && (
                    <p className="mb-4 max-w-sm text-xs text-muted-foreground">
                        {detail}
                    </p>
                )}
                {action && <div className="mt-2">{action}</div>}
            </CardContent>
        </Card>
    )
}
