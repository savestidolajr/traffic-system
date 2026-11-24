import type { ComponentPropsWithoutRef } from 'react'
import { cn } from '@/lib/utils'

/**
 * Keeps list controls reachable while desktop operators scan long records.
 * Mobile remains in normal flow so the rail never consumes scarce viewport
 * height or competes with the sticky application header.
 */
export function StickyFilterRail({
    className,
    ...props
}: ComponentPropsWithoutRef<'div'>) {
    return (
        <div
            data-slot="sticky-filter-rail"
            className={cn(
                'md:sticky md:top-2 md:z-20 md:rounded-lg md:border md:border-border/60 md:bg-background/95 md:p-2 md:shadow-sm md:backdrop-blur md:supports-[backdrop-filter]:bg-background/85',
                className
            )}
            {...props}
        />
    )
}
