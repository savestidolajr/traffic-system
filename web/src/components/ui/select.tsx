import * as React from 'react'
import * as SelectPrimitive from '@radix-ui/react-select'
import { Check, ChevronDown, ChevronUp } from 'lucide-react'
import { cn } from '@/lib/utils'

const Select = SelectPrimitive.Root

const SelectGroup = SelectPrimitive.Group

const SelectValue = SelectPrimitive.Value

/**
 * `role="combobox"` does NOT take its accessible name from its contents, so the
 * visible selected value ("Global · all markets") names nothing — and a
 * `<label for>` cannot target a `<button>` either. Every trigger therefore needs
 * an explicit `aria-label`, or an `aria-labelledby` pointing at visible text.
 *
 * That is enforced in the type rather than documented: Lighthouse scored 29 of
 * these unnamed (`button-name`, WCAG 4.1.2 A) across the filter rails and studio
 * forms on 2026-07-27, which is exactly the kind of defect a convention loses to.
 */
type SelectTriggerProps = Omit<
    React.ComponentPropsWithRef<typeof SelectPrimitive.Trigger>,
    'aria-label' | 'aria-labelledby'
> &
    (
        | { 'aria-label': string; 'aria-labelledby'?: never }
        | { 'aria-labelledby': string; 'aria-label'?: never }
    )

function SelectTrigger({
    className,
    children,
    ref,
    ...props
}: SelectTriggerProps) {
    return (
        <SelectPrimitive.Trigger
            ref={ref}
            className={cn(
                'flex h-10 w-full items-center justify-between rounded-md border border-input bg-background px-3 py-2 text-sm text-foreground ring-offset-background placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 [&>span]:line-clamp-1 aria-[invalid=true]:border-destructive aria-[invalid=true]:ring-destructive/20 motion-safe:transition-colors motion-safe:duration-200 pointer-coarse:min-h-11',
                'text-left',
                className
            )}
            {...props}
        >
            {children}
            <SelectPrimitive.Icon asChild>
                <ChevronDown className="h-4 w-4 opacity-50 dark:opacity-100" />
            </SelectPrimitive.Icon>
        </SelectPrimitive.Trigger>
    )
}

function SelectScrollUpButton({
    className,
    ref,
    ...props
}: React.ComponentPropsWithRef<typeof SelectPrimitive.ScrollUpButton>) {
    return (
        <SelectPrimitive.ScrollUpButton
            ref={ref}
            className={cn(
                'flex cursor-default items-center justify-center py-1',
                className
            )}
            {...props}
        >
            <ChevronUp className="h-4 w-4" />
        </SelectPrimitive.ScrollUpButton>
    )
}

function SelectScrollDownButton({
    className,
    ref,
    ...props
}: React.ComponentPropsWithRef<typeof SelectPrimitive.ScrollDownButton>) {
    return (
        <SelectPrimitive.ScrollDownButton
            ref={ref}
            className={cn(
                'flex cursor-default items-center justify-center py-1',
                className
            )}
            {...props}
        >
            <ChevronDown className="h-4 w-4" />
        </SelectPrimitive.ScrollDownButton>
    )
}

function SelectContent({
    className,
    children,
    position = 'popper',
    ref,
    ...props
}: React.ComponentPropsWithRef<typeof SelectPrimitive.Content>) {
    return (
        <SelectPrimitive.Portal>
            <SelectPrimitive.Content
                ref={ref}
                className={cn(
                    'relative z-50 max-h-96 min-w-32 overflow-hidden rounded-md border bg-popover text-popover-foreground shadow-md data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[side=bottom]:slide-in-from-top-2 data-[side=left]:slide-in-from-right-2 data-[side=right]:slide-in-from-left-2 data-[side=top]:slide-in-from-bottom-2',
                    position === 'popper' &&
                        'data-[side=bottom]:translate-y-1 data-[side=left]:-translate-x-1 data-[side=right]:translate-x-1 data-[side=top]:-translate-y-1',
                    className
                )}
                position={position}
                {...props}
            >
                <SelectScrollUpButton />
                <SelectPrimitive.Viewport
                    className={cn(
                        'p-1',
                        position === 'popper' &&
                            'h-[var(--radix-select-trigger-height)] w-full min-w-[var(--radix-select-trigger-width)]'
                    )}
                >
                    {children}
                </SelectPrimitive.Viewport>
                <SelectScrollDownButton />
            </SelectPrimitive.Content>
        </SelectPrimitive.Portal>
    )
}

function SelectLabel({
    className,
    ref,
    ...props
}: React.ComponentPropsWithRef<typeof SelectPrimitive.Label>) {
    return (
        <SelectPrimitive.Label
            ref={ref}
            className={cn('py-1.5 pl-8 pr-2 text-sm font-semibold', className)}
            {...props}
        />
    )
}

/**
 * `description` renders a one-line explainer under the label, and deliberately
 * sits OUTSIDE `ItemText`. Radix clones `ItemText`'s children into the
 * trigger's `SelectValue`, so a description nested in there renders the whole
 * two-line stack inside the fixed-height (`h-10`) trigger and spills out of it
 * — the editorial-guidance "Type" regression. Anything that must not appear in
 * the trigger belongs in this slot, not in `children`.
 */
type SelectItemProps = React.ComponentPropsWithRef<
    typeof SelectPrimitive.Item
> & { description?: React.ReactNode }

function SelectItem({
    className,
    children,
    description,
    ref,
    ...props
}: SelectItemProps) {
    return (
        <SelectPrimitive.Item
            ref={ref}
            className={cn(
                'relative flex w-full cursor-default select-none items-center rounded-sm py-1.5 pl-8 pr-2 text-sm outline-none focus:bg-accent focus:text-accent-foreground data-[disabled]:pointer-events-none data-[disabled]:opacity-50 pointer-coarse:min-h-11',
                className
            )}
            {...props}
        >
            <span className="absolute left-2 flex h-3.5 w-3.5 items-center justify-center">
                <SelectPrimitive.ItemIndicator>
                    <Check className="h-4 w-4" />
                </SelectPrimitive.ItemIndicator>
            </span>

            {description ? (
                <span className="flex min-w-0 flex-col">
                    <SelectPrimitive.ItemText>
                        {children}
                    </SelectPrimitive.ItemText>
                    <span className="text-xs text-muted-foreground-subtle">
                        {description}
                    </span>
                </span>
            ) : (
                <SelectPrimitive.ItemText>{children}</SelectPrimitive.ItemText>
            )}
        </SelectPrimitive.Item>
    )
}

function SelectSeparator({
    className,
    ref,
    ...props
}: React.ComponentPropsWithRef<typeof SelectPrimitive.Separator>) {
    return (
        <SelectPrimitive.Separator
            ref={ref}
            className={cn('-mx-1 my-1 h-px bg-muted', className)}
            {...props}
        />
    )
}

export {
    Select,
    SelectGroup,
    SelectValue,
    SelectTrigger,
    SelectContent,
    SelectLabel,
    SelectItem,
    SelectSeparator,
    SelectScrollUpButton,
    SelectScrollDownButton,
}
