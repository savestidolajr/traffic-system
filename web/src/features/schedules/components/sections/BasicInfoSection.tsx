import type { UseFormReturn } from 'react-hook-form'
import { Radio } from 'lucide-react'
import { FormInputField } from '@/components/FormInputField'
import {
    Select,
    SelectContent,
    SelectGroup,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from '@/components/ui/select'
import {
    FormControl,
    FormField,
    FormItem,
    FormLabel,
    FormMessage,
} from '@/components/ui/form'
import type { ScheduleFormData } from '../../types'

interface BasicInfoSectionProps {
    readonly form: UseFormReturn<ScheduleFormData>
}

export function BasicInfoSection({ form }: BasicInfoSectionProps) {
    return (
        <div className="flex flex-col gap-4">
            <div className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
                <Radio className="size-4" />
                Basic information
            </div>
            <div className="grid gap-4 sm:grid-cols-2">
                <FormInputField
                    control={form.control}
                    name="marketId"
                    label="Market ID"
                    required
                    placeholder="for example, SYDNEY_METRO"
                />
                <FormInputField
                    control={form.control}
                    name="name"
                    label="Friendly name"
                    required
                    placeholder="for example, Sydney Morning Drive"
                />
            </div>

            <div className="grid gap-4 sm:grid-cols-2">
                <FormInputField
                    control={form.control}
                    name="station"
                    label="Station name"
                    required
                    placeholder="for example, KIIS 106.5"
                />
                <FormField
                    control={form.control}
                    name="timezone"
                    render={({ field }) => (
                        <FormItem>
                            <FormLabel>
                                Timezone
                                <span className="text-destructive">*</span>
                            </FormLabel>
                            <Select
                                value={field.value}
                                onValueChange={field.onChange}
                            >
                                <FormControl>
                                    <SelectTrigger aria-label="Timezone">
                                        <SelectValue placeholder="Select timezone" />
                                    </SelectTrigger>
                                </FormControl>
                                <SelectContent>
                                    <SelectGroup>
                                        <SelectItem value="Australia/Sydney">
                                            Sydney (AEST/AEDT)
                                        </SelectItem>
                                        <SelectItem value="Australia/Melbourne">
                                            Melbourne (AEST/AEDT)
                                        </SelectItem>
                                        <SelectItem value="Australia/Brisbane">
                                            Brisbane (AEST)
                                        </SelectItem>
                                        <SelectItem value="Australia/Adelaide">
                                            Adelaide (ACST/ACDT)
                                        </SelectItem>
                                        <SelectItem value="Australia/Hobart">
                                            Hobart (Tasmania AEST/AEDT)
                                        </SelectItem>
                                        <SelectItem value="Australia/Perth">
                                            Perth (AWST)
                                        </SelectItem>
                                        <SelectItem value="UTC">UTC</SelectItem>
                                    </SelectGroup>
                                </SelectContent>
                            </Select>
                            <FormMessage />
                        </FormItem>
                    )}
                />
            </div>

            <div className="grid gap-4 sm:grid-cols-2">
                <FormInputField
                    control={form.control}
                    name="targetDurationSeconds"
                    label="Target duration (seconds)"
                    required
                    type="number"
                    placeholder="for example, 30"
                />
            </div>
        </div>
    )
}
