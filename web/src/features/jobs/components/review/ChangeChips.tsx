import {
    X,
    Volume2,
    MinusCircle,
    PlusCircle,
    Route,
    Waves,
    BookText,
    Mic2,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type {
    ReviewChange,
    ReviewChangeKind,
} from '../../hooks/use-bulletin-review'

const KIND_ICON: Record<ReviewChangeKind, LucideIcon> = {
    pronunciation: Volume2,
    'incident-removed': MinusCircle,
    'incident-added': PlusCircle,
    'corridors-curated': Route,
    'flow-curated': Waves,
    voice: Mic2,
}

interface ChangeChipsProps {
    readonly changes: readonly ReviewChange[]
    readonly onRemove: (id: string) => void
    /** Optional callback for pronunciation feedback: extracts the target word and navigates to the pronunciation studio. */
    readonly onAddToDictionary?: (word: string) => void
}

/**
 * The queued-changes list: every source, pronunciation and voice change waiting
 * for the next apply. Display + remove only — the action lives in the Revise
 * dialog, the single decision surface.
 */
export function ChangeChips({
    changes,
    onRemove,
    onAddToDictionary,
}: ChangeChipsProps) {
    if (changes.length === 0) return null
    return (
        <ul className="flex flex-col gap-1.5">
            {changes.map((change) => {
                const Icon = KIND_ICON[change.kind]
                const isPronunciation =
                    change.kind === 'pronunciation' &&
                    change.label.startsWith('Pronounce ')
                const targetWord = isPronunciation
                    ? change.label.slice('Pronounce '.length)
                    : ''
                return (
                    <li
                        key={change.id}
                        className="flex items-center gap-2 rounded-md border bg-background/60 px-2.5 py-1.5 text-sm"
                    >
                        <Icon className="size-4 shrink-0 text-muted-foreground" />
                        <span className="min-w-0 flex-1 truncate">
                            {change.label}
                            {change.detail && (
                                <span className="text-muted-foreground">
                                    {' '}
                                    &middot; {change.detail}
                                </span>
                            )}
                        </span>
                        {/* Add-to-dictionary affordance for pronunciation feedback */}
                        {isPronunciation && onAddToDictionary && (
                            <button
                                type="button"
                                onClick={() => onAddToDictionary(targetWord)}
                                className="shrink-0 rounded p-0.5 text-muted-foreground hover:text-primary"
                                title="Add to pronunciation dictionary"
                                aria-label={`Add "${targetWord}" to pronunciation dictionary`}
                            >
                                <BookText className="size-3.5" />
                            </button>
                        )}
                        {change.removable && (
                            <button
                                type="button"
                                onClick={() => onRemove(change.id)}
                                className="shrink-0 rounded p-0.5 text-muted-foreground hover:bg-accent hover:text-foreground"
                                aria-label="Remove change"
                            >
                                <X className="size-3.5" />
                            </button>
                        )}
                    </li>
                )
            })}
        </ul>
    )
}
