import type { UseFormReturn } from 'react-hook-form'
import { Separator } from '@/components/ui/separator'
import { InfoHint } from '@/components/InfoHint'
import { DeliveryStyleSelector } from '../DeliveryStyleSelector'
import { PronunciationDictionarySelector } from '../PronunciationDictionarySelector'
import { BookendSelector } from '../BookendSelector'
import { BookendRotationEditor } from '../BookendRotationEditor'
import { BookendTokensEditor } from '../BookendTokensEditor'
import { BookendResolvedPreview } from '../BookendResolvedPreview'
import type { ScheduleFormData } from '../../types'

interface SpeechSectionProps {
    readonly form: UseFormReturn<ScheduleFormData>
}

/**
 * How the bulletin is written and performed — the production chain after the
 * voice is chosen: the delivery (eleven_v3 performance), the pronunciation
 * dictionary, and the intro/outro bookends wrapped around every bulletin.
 * Grouped here so the Voice step stays focused on who reads and when.
 */
export function SpeechSection({ form }: SpeechSectionProps) {
    return (
        <div className="space-y-5">
            <DeliveryStyleSelector form={form} />
            <Separator />
            <PronunciationDictionarySelector form={form} />
            <Separator />
            <div className="space-y-4">
                <div>
                    <p className="flex items-center gap-1.5 text-sm font-medium leading-none">
                        Bookends
                        <InfoHint label="How do bookends air?">
                            The intro and outro pair wrapped around every
                            bulletin. On air: intro, then the reads, then filler
                            up to the target, then the outro.
                        </InfoHint>
                    </p>
                </div>
                <BookendSelector form={form} />
                <BookendRotationEditor form={form} />
                <BookendTokensEditor form={form} />
                {/* The unresolved-token warnings render for everyone; the
                    server-resolved "what the station will hear" lines are
                    withheld for Operators inside the component (the preview
                    endpoint is Admin in RouteRoleMatrix). */}
                <BookendResolvedPreview form={form} />
            </div>
        </div>
    )
}
