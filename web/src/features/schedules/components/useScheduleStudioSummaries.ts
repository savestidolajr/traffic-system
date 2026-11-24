import { useMemo } from 'react'
import { useWatch, type UseFormReturn } from 'react-hook-form'
import { useVoiceProfiles } from '@/features/voices/api/voice-profile-hooks'
import { DEFAULT_COVERAGE } from '@/lib/api/schemas/shared'
import type { CoverageArea } from '@/lib/api/schemas/shared'
import type { MarketSchedule, ScheduleFormData } from '../types'
import { ScheduleFormSchema } from '../form-schemas'
import { STEP_FIELDS, type StepId } from '../lib/schedule-studio-helpers'
import { summariseContentSources } from '../lib/content-summary'

export function useScheduleStudioSummaries(
    form: UseFormReturn<ScheduleFormData>,
    schedule: MarketSchedule | null
) {
    const watchedValues = useWatch({ control: form.control })
    const [longitude] = useWatch({
        control: form.control,
        name: ['longitude'],
    })
    const values = useMemo(
        () =>
            ({
                ...watchedValues,
                longitude,
            }) as ScheduleFormData,
        [watchedValues, longitude]
    )
    const { data: voiceProfilesData } = useVoiceProfiles()
    const voiceName = voiceProfilesData?.profiles.find(
        (profile) => profile.id === values.voiceProfileId
    )?.name
    const { errors } = form.formState

    const eagerInvalidFields = useMemo(() => {
        const result = ScheduleFormSchema.safeParse(values)
        return result.success
            ? new Set<string>()
            : new Set(result.error.issues.map((issue) => String(issue.path[0])))
    }, [values])

    const stepInvalid = (id: StepId): boolean =>
        STEP_FIELDS[id].some(
            (field) => field in errors || eagerInvalidFields.has(field)
        )

    const coverageForSummary = {
        latitude: values.latitude ?? DEFAULT_COVERAGE.latitude,
        longitude: values.longitude ?? DEFAULT_COVERAGE.longitude,
        radiusKm: values.radiusKm ?? DEFAULT_COVERAGE.radiusKm,
        shapeType: values.shapeType,
        west: values.west,
        south: values.south,
        east: values.east,
        north: values.north,
        corridorPoints: values.corridorPoints,
        corridorRadiusMeters: values.corridorRadiusMeters,
        polygonPoints: values.polygonPoints,
    } satisfies CoverageArea

    const contentSummary = summariseContentSources(values.content)
    const voiceSummary = (() => {
        if (values.voiceRotationMode === 'roundRobin') {
            const count = values.voiceRotationIds?.length ?? 0
            return count > 0
                ? `${count} voice${count === 1 ? '' : 's'} · rotating`
                : 'Pick voices'
        }
        if (values.voiceRotationMode === 'timeOfDay') {
            const count = values.voiceWindows?.length ?? 0
            return count > 0
                ? `By time of day · ${count} window${count === 1 ? '' : 's'}`
                : 'Add windows'
        }
        return voiceName ?? 'Pick a voice'
    })()
    const styled = values.deliveryStyleId ? 'styled' : 'neutral'
    const dict = values.pronunciationDictionaryId ? 'dictionary' : 'no dict'
    const speechSummary = `${styled} · ${dict}`
    const timesSummary =
        values.scheduleMode === 'specific'
            ? `${values.specificTimes?.length ?? 0} time${values.specificTimes?.length === 1 ? '' : 's'}`
            : `every ${values.intervalMinutes ?? '-'} min`

    // Named in the rail so a station carrying date exceptions says so without the
    // operator opening the step — the whole point of the summary line.
    const exceptionCount = values.calendar?.assignments.length ?? 0
    const calendarSummary =
        exceptionCount === 0
            ? ''
            : ` \u00B7 ${exceptionCount} dated exception${exceptionCount === 1 ? '' : 's'}`

    return {
        values,
        contentSummary,
        voiceSummary,
        speechSummary,
        timesSummary,
        calendarSummary,
        coverageForSummary,
        stepInvalid,
        eagerInvalidFields,
        isEditing: !!schedule,
    }
}
