import type * as React from 'react'
import type { UseFormReturn, FieldValues, Path } from 'react-hook-form'
import { useVoiceProfiles } from '@/features/voices/api/voice-profile-hooks'
import { EntitySelectorField } from './EntitySelectorField'

interface VoiceProfileSelectorProps<T extends FieldValues> {
    readonly form: UseFormReturn<T>
    readonly name?: Path<T>
    readonly label?: string
    /** Marks the field as required, mirroring FormLabel's required asterisk. */
    readonly required?: boolean
    /** Overrides the default hint. Load-bearing: the time-of-day voice editor
     *  passes "Used for any time not covered by a window below." here. */
    readonly description?: React.ReactNode
}

/**
 * Per-station voice selection: pick a tuned Voice Profile from the shared library.
 * Voices are created and tuned in the Voices tab; stations only choose one here.
 */
export function VoiceProfileSelector<T extends FieldValues>({
    form,
    name = 'voiceProfileId' as Path<T>,
    label = 'Voice',
    required,
    description,
}: VoiceProfileSelectorProps<T>) {
    const { data, isLoading } = useVoiceProfiles()
    const profiles = (data?.profiles ?? []).filter((p) => p.status === 'active')

    return (
        <EntitySelectorField
            form={form}
            name={name}
            label={label}
            required={required}
            description={description}
            hintLabel="What is a voice profile?"
            hint="Tuned voice from the shared library. Create and adjust voices in the Voices tab."
            isLoading={isLoading}
            loadingText="Loading voices..."
            placeholder="Select a voice"
            options={profiles.map((profile) => ({
                id: profile.id,
                label: profile.name,
                secondaryLabel: profile.baseVoiceName,
            }))}
        />
    )
}
