import type { UseFormReturn, FieldValues, Path } from 'react-hook-form'
import { usePronunciationDictionaries } from '@/features/pronunciation/api/pronunciation-hooks'
import { EntitySelectorField } from './EntitySelectorField'

interface PronunciationDictionarySelectorProps<T extends FieldValues> {
    readonly form: UseFormReturn<T>
}

const NONE = 'none'

/**
 * Per-station pronunciation dictionary: pick from the shared library, or
 * leave it unset. Dictionaries are authored in the Pronunciation tab;
 * stations only choose one here. Optional — no custom pronunciation rules.
 *
 * A platform-wide dictionary is deliberately not offered: it already applies to
 * every station underneath whatever that station carries, and the backend
 * rejects a schedule that names one.
 */
export function PronunciationDictionarySelector<T extends FieldValues>({
    form,
}: PronunciationDictionarySelectorProps<T>) {
    const { data, isLoading } = usePronunciationDictionaries()
    const dictionaries = (data?.items ?? []).filter(
        (dictionary) =>
            dictionary.status === 'active' && dictionary.scope !== 'Global'
    )

    return (
        <EntitySelectorField
            form={form}
            name={'pronunciationDictionaryId' as Path<T>}
            label="Pronunciation dictionary"
            hintLabel="What is a pronunciation dictionary?"
            hint="Custom TTS pronunciation rules for place names and road names. Manage dictionaries in the Pronunciation tab."
            isLoading={isLoading}
            loadingText="Loading pronunciation dictionaries..."
            placeholder="None (no pronunciation rules)"
            none={{
                value: NONE,
                label: 'None (no pronunciation rules)',
            }}
            options={dictionaries.map((dictionary) => ({
                id: dictionary.id,
                label: dictionary.name,
            }))}
        />
    )
}
