import type { Control, FieldPath, FieldValues } from 'react-hook-form'
import {
    FormControl,
    FormField,
    FormItem,
    FormLabel,
    FormMessage,
} from '@/components/ui/form'
import { Input } from '@/components/ui/input'
import type { InputHTMLAttributes } from 'react'

interface FormInputFieldProps<
    TFieldValues extends FieldValues = FieldValues,
    TName extends FieldPath<TFieldValues> = FieldPath<TFieldValues>,
> {
    readonly control: Control<TFieldValues>
    readonly name: TName
    readonly label: string
    readonly required?: boolean
    readonly placeholder?: string
    readonly type?: InputHTMLAttributes<HTMLInputElement>['type']
    readonly className?: string
}

export function FormInputField<
    TFieldValues extends FieldValues = FieldValues,
    TName extends FieldPath<TFieldValues> = FieldPath<TFieldValues>,
>({
    control,
    name,
    label,
    required,
    placeholder,
    type = 'text',
    className,
}: FormInputFieldProps<TFieldValues, TName>) {
    return (
        <FormField
            control={control}
            name={name}
            render={({ field }) => (
                <FormItem className={className}>
                    <FormLabel>
                        {label}
                        {required && (
                            <span className="text-destructive">*</span>
                        )}
                    </FormLabel>
                    <FormControl>
                        <Input
                            type={type}
                            placeholder={placeholder}
                            {...field}
                            onChange={(e) => {
                                if (type === 'number') {
                                    const val = e.target.valueAsNumber
                                    field.onChange(Number.isNaN(val) ? '' : val)
                                } else {
                                    field.onChange(e)
                                }
                            }}
                        />
                    </FormControl>
                    <FormMessage />
                </FormItem>
            )}
        />
    )
}
