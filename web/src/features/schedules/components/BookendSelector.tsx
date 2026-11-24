import type { FieldValues, Path, UseFormReturn } from 'react-hook-form'
import { useBookends } from '@/features/bookends/api/bookend-hooks'
import { EntitySelectorField } from './EntitySelectorField'

interface BookendSelectorProps<T extends FieldValues> {
    readonly form: UseFormReturn<T>
}

const NONE = 'none'

/**
 * Per-station bookend pair: pick a pair from the shared library, or leave it
 * unset. Pairs are authored in the Bookends tab; stations only choose one
 * here. Optional — a bulletin with no bookend opens directly on the first
 * read and ends on the last filler statement.
 *
 * The single/fallback pair for a station lives HERE; the rotation editor
 * below only adds round-robin/time-of-day policies on top of it.
 */
export function BookendSelector<T extends FieldValues>({
    form,
}: BookendSelectorProps<T>) {
    const { data, isLoading } = useBookends()
    const bookends = (data?.bookends ?? []).filter(
        (bookend) => bookend.status === 'active'
    )

    return (
        <EntitySelectorField
            form={form}
            name={'bookendId' as Path<T>}
            label="Bookend"
            isLoading={isLoading}
            loadingText="Loading bookends..."
            placeholder="No bookend"
            none={{
                value: NONE,
                label: 'No bookend',
            }}
            options={bookends.map((bookend) => ({
                id: bookend.id,
                label: bookend.name,
                secondaryLabel: bookendPairSummary(
                    bookend.introText,
                    bookend.outroText
                ),
            }))}
        />
    )
}

/** Compact shape summary so an operator can tell one pair from another. */
function bookendPairSummary(introText: string, outroText: string): string {
    const intro = introText.trim()
    const outro = outroText.trim()
    if (intro && outro) return 'intro + outro'
    if (intro) return 'intro only'
    if (outro) return 'outro only'
    return 'empty pair'
}
