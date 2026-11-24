import * as React from 'react'
import { cn } from '@/lib/utils'

function Input({
    className,
    type,
    ref,
    ...props
}: React.ComponentProps<'input'>) {
    return (
        <input
            type={type}
            className={cn(
                // h-10, not h-9: `SelectTrigger` and the default `Button` are
                // both 40px, so an h-9 input sat 4px short of every control
                // beside it. In a `sm:items-end` composer row that pushed the
                // input's ComposerField label 4px BELOW its neighbours' labels
                // (measured 214 vs 210 on all four Bulletin-rules composers),
                // and in every filter bar the search box under-ran the filter
                // selects. One height for the three box controls.
                'flex h-10 w-full rounded-md border border-input bg-background px-3 py-1 text-sm text-foreground ring-offset-background file:border-0 file:bg-transparent file:text-sm file:font-medium file:text-foreground placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-0 disabled:cursor-not-allowed disabled:opacity-50 aria-[invalid=true]:border-destructive aria-[invalid=true]:ring-destructive/20 motion-safe:transition-colors motion-safe:duration-200 pointer-coarse:min-h-11',
                className
            )}
            ref={ref}
            {...props}
        />
    )
}

export { Input }
