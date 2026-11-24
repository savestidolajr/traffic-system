import type * as React from 'react'
import type { FieldValues, Path, UseFormReturn } from 'react-hook-form'
import {
    FormControl,
    FormDescription,
    FormField,
    FormItem,
    FormLabel,
    FormMessage,
} from '@/components/ui/form'
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from '@/components/ui/select'
import { InfoHint } from '@/components/InfoHint'
import { Loader2 } from 'lucide-react'

export interface EntitySelectorOption {
    readonly id: string
    readonly label: React.ReactNode
    readonly secondaryLabel?: React.ReactNode
}

type HintProps =
    | {
          /** The explanation, shown in a tooltip beside the label. */
          readonly hint: React.ReactNode
          /** The hint trigger's accessible name, e.g. `What is a voice profile?`. */
          readonly hintLabel: string
      }
    | { readonly hint?: undefined; readonly hintLabel?: undefined }

type EntitySelectorFieldProps<T extends FieldValues> = HintProps & {
    readonly form: UseFormReturn<T>
    readonly name: Path<T>
    readonly label: React.ReactNode
    /** Marks the field as required, mirroring FormLabel's required asterisk. */
    readonly required?: boolean
    /**
     * A helper line under the control. Reserve this for a fact that must be
     * read without hovering (the time-of-day fallback note); everything
     * explanatory belongs in `hint`.
     */
    readonly description?: React.ReactNode
    readonly isLoading: boolean
    readonly loadingText: string
    readonly placeholder: string
    readonly options: readonly EntitySelectorOption[]
    readonly none?: {
        readonly value: string
        readonly label: React.ReactNode
    }
    readonly itemClassName?: string
    /** A right-aligned control on the label row. Prefer `hint` for explanations. */
    readonly labelAdornment?: React.ReactNode
}

/** Shared FormField/Select shape for station-level library selectors. */
export function EntitySelectorField<T extends FieldValues>({
    form,
    name,
    label,
    required,
    description,
    hint,
    hintLabel,
    isLoading,
    loadingText,
    placeholder,
    options,
    none,
    itemClassName,
    labelAdornment,
}: EntitySelectorFieldProps<T>) {
    return (
        <FormField
            control={form.control}
            name={name}
            render={({ field }) => {
                const current = none
                    ? (field.value as string) || none.value
                    : ((field.value as string) ?? '')
                return (
                    <FormItem className={itemClassName}>
                        <div className="flex items-center justify-between gap-2">
                            <div className="flex items-center gap-1.5">
                                <FormLabel required={required}>
                                    {label}
                                </FormLabel>
                                {hint && (
                                    <InfoHint label={hintLabel}>
                                        {hint}
                                    </InfoHint>
                                )}
                            </div>
                            {labelAdornment}
                        </div>
                        {isLoading ? (
                            <div className="flex h-10 items-center gap-2 px-3 text-sm text-muted-foreground">
                                <Loader2 className="size-4 animate-spin" />
                                {loadingText}
                            </div>
                        ) : (
                            <Select
                                value={current}
                                onValueChange={(value) =>
                                    field.onChange(
                                        none && value === none.value
                                            ? ''
                                            : value
                                    )
                                }
                            >
                                <FormControl>
                                    <SelectTrigger
                                        aria-label={
                                            typeof label === 'string'
                                                ? label
                                                : placeholder
                                        }
                                    >
                                        <SelectValue
                                            placeholder={placeholder}
                                        />
                                    </SelectTrigger>
                                </FormControl>
                                <SelectContent>
                                    {none && (
                                        <SelectItem value={none.value}>
                                            <span className="text-muted-foreground">
                                                {none.label}
                                            </span>
                                        </SelectItem>
                                    )}
                                    {options.map((option) => (
                                        <SelectItem
                                            key={option.id}
                                            value={option.id}
                                        >
                                            {option.secondaryLabel ? (
                                                <span className="flex items-center gap-2">
                                                    {option.label}
                                                    <span className="text-xs text-muted-foreground">
                                                        {option.secondaryLabel}
                                                    </span>
                                                </span>
                                            ) : (
                                                option.label
                                            )}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                        )}
                        <FormDescription>{description}</FormDescription>
                        <FormMessage />
                    </FormItem>
                )
            }}
        />
    )
}
