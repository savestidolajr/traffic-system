import * as React from 'react'
import * as LabelPrimitive from '@radix-ui/react-label'
import { Slot } from '@radix-ui/react-slot'
import {
    Controller,
    FormProvider,
    type ControllerProps,
    type FieldPath,
    type FieldValues,
} from 'react-hook-form'

import { cn } from '@/lib/utils'
import { Label } from '@/components/ui/label'
import { FormFieldContext, FormItemContext, useFormField } from './form-context'

const Form = FormProvider

const FormField = <
    TFieldValues extends FieldValues = FieldValues,
    TName extends FieldPath<TFieldValues> = FieldPath<TFieldValues>,
>({
    ...props
}: ControllerProps<TFieldValues, TName>) => {
    return (
        <FormFieldContext.Provider value={{ name: props.name }}>
            <Controller {...props} />
        </FormFieldContext.Provider>
    )
}

function FormItem({ className, ref, ...props }: React.ComponentProps<'div'>) {
    const id = React.useId()

    return (
        <FormItemContext.Provider value={{ id }}>
            <div ref={ref} className={cn('space-y-2', className)} {...props} />
        </FormItemContext.Provider>
    )
}

function FormLabel({
    className,
    required,
    ref,
    ...props
}: React.ComponentPropsWithRef<typeof LabelPrimitive.Root> & {
    required?: boolean
}) {
    const { error, formItemId } = useFormField()

    return (
        <Label
            ref={ref}
            // `block` is load-bearing: a Radix Label is `display:inline`, so its
            // box is the font em box (16.7px for 14px/leading-none text), not the
            // 14px line box. Inside `FormItem`'s `space-y-2` that ate 5px of the
            // 8px step — measured 3px to the control here versus a true 8px
            // wherever a label happens to sit in a flex wrapper
            // (`EntitySelectorField`). One relationship, two rendered gaps.
            className={cn('block', error && 'text-destructive', className)}
            htmlFor={formItemId}
            {...props}
        >
            {props.children}
            {required && <span className="text-destructive ml-0.5">*</span>}
        </Label>
    )
}

function FormControl({
    ref,
    ...props
}: React.ComponentPropsWithRef<typeof Slot>) {
    const { error, formItemId, formDescriptionId, formMessageId } =
        useFormField()

    return (
        <Slot
            ref={ref}
            id={formItemId}
            aria-describedby={
                !error
                    ? `${formDescriptionId}`
                    : `${formDescriptionId} ${formMessageId}`
            }
            aria-invalid={!!error}
            {...props}
        />
    )
}

function FormDescription({
    className,
    ref,
    ...props
}: React.ComponentProps<'p'>) {
    const { formDescriptionId } = useFormField()

    return (
        <p
            ref={ref}
            id={formDescriptionId}
            className={cn('text-sm text-muted-foreground', className)}
            {...props}
        />
    )
}

function FormMessage({
    className,
    children,
    ref,
    ...props
}: React.ComponentProps<'p'>) {
    const { error, formMessageId } = useFormField()
    const body = error ? String(error?.message ?? '') : children

    if (!body) {
        return null
    }

    return (
        <p
            ref={ref}
            id={formMessageId}
            className={cn('text-sm font-medium text-destructive', className)}
            {...props}
        >
            {body}
        </p>
    )
}

export {
    Form,
    FormItem,
    FormLabel,
    FormControl,
    FormDescription,
    FormMessage,
    FormField,
}
