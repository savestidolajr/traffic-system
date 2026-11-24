import type { FieldValues, Path, UseFormReturn } from 'react-hook-form'
import { EntitySelectorField } from '@/features/schedules/components/EntitySelectorField'
import { usePlaybooks } from '../api/playbook-hooks'

interface PlaybookSelectorProps<T extends FieldValues> {
    readonly form: UseFormReturn<T>
}

/** Selects the station's writer instructions without copying the shipped prompt into a schedule. */
export function PlaybookSelector<T extends FieldValues>({
    form,
}: PlaybookSelectorProps<T>) {
    const { data, isLoading } = usePlaybooks()
    const playbooks = (data?.playbooks ?? []).filter(
        (playbook) => playbook.status === 'active'
    )

    return (
        <EntitySelectorField
            form={form}
            name={'playbookId' as Path<T>}
            label="Bulletin playbook"
            none={{ value: 'none', label: 'Shipped defaults' }}
            isLoading={isLoading}
            loadingText="Loading bulletin playbooks..."
            placeholder="Shipped defaults"
            hintLabel="What is a bulletin playbook?"
            hint="A playbook stores only this station's edits over the shipped writer instructions. Leaving it on shipped defaults keeps the station on future prompt improvements. Manage playbooks in the Playbooks library."
            options={playbooks.map((playbook) => ({
                id: playbook.id,
                label: playbook.name,
                secondaryLabel: `v${playbook.currentVersion}`,
            }))}
        />
    )
}
