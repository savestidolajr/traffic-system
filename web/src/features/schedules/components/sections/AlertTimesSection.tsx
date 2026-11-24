import type { UseFormReturn } from 'react-hook-form'
import { Input } from '@/components/ui/input'
import {
    FormControl,
    FormDescription,
    FormField,
    FormItem,
    FormLabel,
    FormMessage,
} from '@/components/ui/form'
import { Bell } from 'lucide-react'
import type { ScheduleFormData } from '../../types'

interface AlertTimesSectionProps {
    readonly form: UseFormReturn<ScheduleFormData>
}

export function AlertTimesSection({ form }: AlertTimesSectionProps) {
    return (
        <div className="flex flex-col gap-4">
            <div className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
                <Bell className="size-4" />
                Alert times
            </div>
            <div className="grid gap-4 sm:grid-cols-2">
                <FormField
                    control={form.control}
                    name="notifyBeforePlayoutMinutes"
                    render={({ field }) => (
                        <FormItem>
                            <FormLabel>Notify before playout (mins)</FormLabel>
                            <FormControl>
                                <Input
                                    type="number"
                                    min={1}
                                    max={120}
                                    step={1}
                                    {...field}
                                    onChange={(e) =>
                                        field.onChange(
                                            parseInt(e.target.value, 10) || 0
                                        )
                                    }
                                />
                            </FormControl>
                            <FormMessage />
                        </FormItem>
                    )}
                />
                <FormField
                    control={form.control}
                    name="escalationBeforePlayoutMinutes"
                    render={({ field }) => (
                        <FormItem>
                            <FormLabel>
                                Escalation before playout (mins)
                            </FormLabel>
                            <FormControl>
                                <Input
                                    type="number"
                                    min={1}
                                    max={120}
                                    step={1}
                                    {...field}
                                    onChange={(e) =>
                                        field.onChange(
                                            parseInt(e.target.value, 10) || 0
                                        )
                                    }
                                />
                            </FormControl>
                            <FormDescription>
                                Must be less than the notification time above
                            </FormDescription>
                            <FormMessage />
                        </FormItem>
                    )}
                />
            </div>
        </div>
    )
}
