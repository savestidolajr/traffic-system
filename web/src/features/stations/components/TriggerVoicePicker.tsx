import { useCallback, useEffect } from 'react'
import type {
    UseFormReturn,
    FieldValues,
    Path,
    PathValue,
} from 'react-hook-form'
import { Link } from 'react-router'
import { Loader2 } from 'lucide-react'
import { useVoiceProfiles } from '@/features/voices/api/voice-profile-hooks'
import { currentVersion } from '@/features/voices/lib/voice-profile-utils'
import {
    FormControl,
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
import { ROUTE_VOICES } from '@/lib/routes'
import type { VoiceProfile } from '@/features/voices/profile-types'

/**
 * Trigger-studio voice step: choose a tuned Voice Profile from the shared
 * library — the only voices configured in the app. Selecting (or prefilling)
 * a profile resolves its current version into the concrete voiceId / name /
 * model / settings the broadcast request carries, so operators never reach a
 * raw ElevenLabs voice here.
 */
export function TriggerVoicePicker<T extends FieldValues>({
    form,
}: {
    readonly form: UseFormReturn<T>
}) {
    const { data, isLoading } = useVoiceProfiles()
    const profiles = (data?.profiles ?? []).filter((p) => p.status === 'active')

    const set = useCallback(
        <K extends Path<T>>(name: K, value: PathValue<T, K>) =>
            form.setValue(name, value, {
                shouldValidate: true,
                shouldDirty: true,
            }),
        [form]
    )

    const applyProfile = useCallback(
        (profile: VoiceProfile) => {
            const version = currentVersion(profile)
            set(
                'voiceProfileId' as Path<T>,
                profile.id as PathValue<T, Path<T>>
            )
            set(
                'voiceId' as Path<T>,
                profile.baseVoiceId as PathValue<T, Path<T>>
            )
            set('voiceName' as Path<T>, profile.name as PathValue<T, Path<T>>)
            set('voiceModel' as Path<T>, version.model as PathValue<T, Path<T>>)
            set(
                'voiceSettings' as Path<T>,
                version.settings as PathValue<T, Path<T>>
            )
        },
        [set]
    )

    const selectedId = form.watch('voiceProfileId' as Path<T>) as string
    const resolvedVoiceId = form.watch('voiceId' as Path<T>) as string

    // Prefill resolution: the station's assigned profile id is known
    // synchronously, but its concrete voice params only arrive once the
    // profile library loads. Resolve once, and never clobber a live choice.
    useEffect(() => {
        if (!selectedId || resolvedVoiceId) return
        const profile = profiles.find((p) => p.id === selectedId)
        if (profile) applyProfile(profile)
    }, [profiles, selectedId, resolvedVoiceId, applyProfile])

    return (
        <FormField
            control={form.control}
            name={'voiceProfileId' as Path<T>}
            render={({ field }) => (
                <FormItem className="max-w-md">
                    <div className="flex items-center gap-1.5">
                        <FormLabel>Voice</FormLabel>
                        <InfoHint label="What is a voice profile?">
                            Tuned voice from the shared library. Create and
                            adjust voices in the Voices tab.
                        </InfoHint>
                    </div>
                    {isLoading ? (
                        <div className="flex h-10 items-center gap-2 px-3 text-sm text-muted-foreground">
                            <Loader2 className="size-4 animate-spin" />
                            Loading voices...
                        </div>
                    ) : profiles.length === 0 ? (
                        <p className="rounded-lg border border-border bg-muted/30 px-3 py-2 text-sm text-muted-foreground">
                            No voices configured yet. Create one in the{' '}
                            <Link
                                to={ROUTE_VOICES}
                                className="font-medium text-foreground underline-offset-4 hover:underline"
                            >
                                Voices tab
                            </Link>{' '}
                            before triggering a bulletin.
                        </p>
                    ) : (
                        <Select
                            value={(field.value as string) ?? ''}
                            onValueChange={(id) => {
                                const profile = profiles.find(
                                    (p) => p.id === id
                                )
                                if (profile) applyProfile(profile)
                            }}
                        >
                            <FormControl>
                                <SelectTrigger aria-label="Voice">
                                    <SelectValue placeholder="Select a voice" />
                                </SelectTrigger>
                            </FormControl>
                            <SelectContent>
                                {profiles.map((profile) => (
                                    <SelectItem
                                        key={profile.id}
                                        value={profile.id}
                                    >
                                        <span className="flex items-center gap-2">
                                            {profile.name}
                                            <span className="text-xs text-muted-foreground">
                                                {profile.baseVoiceName}
                                            </span>
                                        </span>
                                    </SelectItem>
                                ))}
                            </SelectContent>
                        </Select>
                    )}
                    <FormMessage />
                </FormItem>
            )}
        />
    )
}
