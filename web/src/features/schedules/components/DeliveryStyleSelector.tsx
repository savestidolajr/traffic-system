import type { UseFormReturn, FieldValues, Path } from 'react-hook-form'
import { useDeliveryStyles } from '@/features/delivery/api/delivery-style-hooks'
import { EntitySelectorField } from './EntitySelectorField'

interface DeliveryStyleSelectorProps<T extends FieldValues> {
    readonly form: UseFormReturn<T>
}

const NONE = 'none'

/**
 * Per-station delivery style: pick a tone from the shared delivery library, or
 * leave it neutral. Styles are authored in the Delivery tab; stations only
 * choose one here. Optional — neutral reads exactly as today.
 */
export function DeliveryStyleSelector<T extends FieldValues>({
    form,
}: DeliveryStyleSelectorProps<T>) {
    const { data, isLoading } = useDeliveryStyles()
    const styles = (data?.styles ?? []).filter(
        (style) => style.status === 'active'
    )

    return (
        <EntitySelectorField
            form={form}
            name={'deliveryStyleId' as Path<T>}
            label="Delivery style"
            itemClassName="mt-6"
            isLoading={isLoading}
            loadingText="Loading delivery styles..."
            placeholder="Neutral (no tagging)"
            none={{ value: NONE, label: 'Neutral (no tagging)' }}
            hintLabel="What is a delivery style?"
            hint="Tells the voice how to read a bulletin: pace, emphasis, tone, and small delivery cues. It never changes the bulletin words. Neutral means the bulletin reads exactly as it does today. Manage tones in the Delivery tab."
            options={styles.map((style) => ({
                id: style.id,
                label: style.name,
            }))}
        />
    )
}
