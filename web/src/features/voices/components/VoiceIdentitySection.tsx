import type { UseFormReturn } from 'react-hook-form'
import { ICON } from '@/lib/design/icons'
import { InfoHint } from '@/components/InfoHint'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import {
    Select,
    SelectTrigger,
    SelectValue,
    SelectContent,
    SelectItem,
} from '@/components/ui/select'
import {
    FormField,
    FormItem,
    FormLabel,
    FormControl,
    FormMessage,
} from '@/components/ui/form'
import {
    getModelDisplayName,
    getModelShortLabel,
} from '../lib/model-capabilities'
import type { VoiceProfileFormData } from '../form-schemas'
import type { ElevenLabsVoice } from '../types'

interface VoiceIdentitySectionProps {
    readonly form: UseFormReturn<VoiceProfileFormData>
    readonly isEdit: boolean
    readonly baseVoiceName: string | null
    readonly model: string
    readonly sortedVoices: readonly ElevenLabsVoice[]
    readonly voicesLoading: boolean
    readonly onPickVoice: (voiceId: string) => void
}

export function VoiceIdentitySection({
    form,
    isEdit,
    baseVoiceName,
    model,
    sortedVoices,
    voicesLoading,
    onPickVoice,
}: VoiceIdentitySectionProps) {
    const voiceId = form.watch('voiceId')

    return (
        <div className="flex flex-col gap-5">
            <FormField
                control={form.control}
                name="name"
                render={({ field }) => (
                    <FormItem>
                        <FormLabel required>Variant name</FormLabel>
                        <FormControl>
                            <Input
                                placeholder="for example, Tom, Drive AM"
                                {...field}
                            />
                        </FormControl>
                        <FormMessage />
                    </FormItem>
                )}
            />

            {isEdit ? (
                <FormItem>
                    <div className="flex items-center gap-1.5">
                        <FormLabel>Base voice</FormLabel>
                        <InfoHint label="Can I change the base voice?">
                            Base voice is fixed. Create a new variant to use a
                            different voice.
                        </InfoHint>
                    </div>
                    <div className="flex h-10 items-center gap-2 rounded-md border bg-muted/30 px-3 text-sm">
                        <ICON.voice className="size-4 text-primary" />
                        <span className="font-medium">{baseVoiceName}</span>
                        <span className="rounded bg-muted px-1.5 py-0.5 text-xs text-muted-foreground">
                            {getModelDisplayName(model)}
                        </span>
                    </div>
                </FormItem>
            ) : (
                <FormItem>
                    <FormLabel required>Base voice</FormLabel>
                    <Select
                        value={voiceId || undefined}
                        onValueChange={onPickVoice}
                        disabled={voicesLoading}
                    >
                        <FormControl>
                            <SelectTrigger aria-label="Base voice">
                                <SelectValue
                                    placeholder={
                                        voicesLoading
                                            ? 'Loading voices…'
                                            : 'Select a base voice'
                                    }
                                />
                            </SelectTrigger>
                        </FormControl>
                        <SelectContent>
                            {sortedVoices.map((v) => {
                                const modelId =
                                    v.resolvedModelId || v.modelIds?.[0] || ''
                                const modelLabel = modelId
                                    ? getModelShortLabel(modelId)
                                    : ''
                                return (
                                    <SelectItem
                                        key={v.voiceId}
                                        value={v.voiceId}
                                    >
                                        <span className="truncate">
                                            {v.name}
                                            {v.labels?.accent
                                                ? ` · ${v.labels.accent}`
                                                : ''}
                                        </span>
                                        {modelLabel && (
                                            <span className="ml-auto shrink-0 rounded bg-primary/10 px-1.5 py-px text-3xs font-semibold uppercase tracking-wide text-primary-on-tint">
                                                {modelLabel}
                                            </span>
                                        )}
                                    </SelectItem>
                                )
                            })}
                        </SelectContent>
                    </Select>
                </FormItem>
            )}

            <FormField
                control={form.control}
                name="description"
                render={({ field }) => (
                    <FormItem>
                        <FormLabel>Description</FormLabel>
                        <FormControl>
                            <Textarea
                                rows={2}
                                className="max-h-24 resize-none"
                                placeholder="When this variant should be used."
                                {...field}
                                value={field.value ?? ''}
                            />
                        </FormControl>
                        <FormMessage />
                    </FormItem>
                )}
            />
        </div>
    )
}
