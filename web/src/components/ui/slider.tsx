import * as React from 'react'
import * as SliderPrimitive from '@radix-ui/react-slider'

import { cn } from '@/lib/utils'

/**
 * The Thumb carries `role="slider"`, and a slider takes NO name from its
 * contents — so `aria-label`/`aria-labelledby` must land on the Thumb, not the
 * Root, and one of them must be present or the control is anonymous to
 * assistive tech (axe `aria-input-field-name`).
 *
 * Required in the type for the same reason `SelectTrigger` is: three sliders
 * shipped and only one was named. A convention loses; a compile error does not.
 */
type SliderProps = Omit<
    React.ComponentPropsWithRef<typeof SliderPrimitive.Root>,
    'aria-label' | 'aria-labelledby'
> &
    (
        | { 'aria-label': string; 'aria-labelledby'?: never }
        | { 'aria-labelledby': string; 'aria-label'?: never }
    )

function Slider({
    className,
    ref,
    'aria-label': ariaLabel,
    'aria-labelledby': ariaLabelledby,
    ...props
}: SliderProps) {
    return (
        <SliderPrimitive.Root
            ref={ref}
            className={cn(
                'relative flex w-full touch-none select-none items-center',
                className
            )}
            {...props}
        >
            <SliderPrimitive.Track className="relative h-2 w-full grow overflow-hidden rounded-full bg-accent">
                <SliderPrimitive.Range className="absolute h-full bg-primary" />
            </SliderPrimitive.Track>
            <SliderPrimitive.Thumb
                className="block h-5 w-5 rounded-full border-2 border-primary bg-card shadow-md shadow-black/25 ring-offset-background transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:pointer-events-none disabled:opacity-50"
                aria-label={ariaLabel}
                aria-labelledby={ariaLabelledby}
            />
        </SliderPrimitive.Root>
    )
}

export { Slider }
