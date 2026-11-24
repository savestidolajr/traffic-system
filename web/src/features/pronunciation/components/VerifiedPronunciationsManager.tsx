import { useMemo, useState } from 'react'
import { toast } from 'sonner'
import { EmptyState } from '@/components/EmptyState'
import { ScopedActivityHistory } from '@/features/activity/components/ScopedActivityHistory'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { ComposerField } from '@/components/ComposerField'
import { RuleRow } from '@/components/RuleRow'
import { RuleTab } from '@/components/RuleTab'
import { RuleComposer } from '@/components/RuleComposer'
import { RuleToolbar } from '@/components/RuleToolbar'
import {
    ScopeGroupedList,
    type ScopeGroup,
} from '@/components/ScopeGroupedList'
import { ICON } from '@/lib/design/icons'
import {
    useAllVerifiedPronunciations,
    useSetVerifiedPronunciations,
    pronunciationKey,
    VERIFIED_PRONUNCIATIONS_MAX,
    VERIFIED_PRONUNCIATION_MAX_LENGTH,
    VERIFIED_PRONUNCIATION_NOTE_MAX_LENGTH,
    type VerifiedPronunciationEntry,
} from '../api/verified-pronunciation-hooks'

const AddIcon = ICON.add
const EditIcon = ICON.edit
const ResetIcon = ICON.reset

interface MarketOption {
    readonly marketId: string
    readonly label: string
}

interface VerifiedRow {
    readonly marketId: string
    readonly entry: VerifiedPronunciationEntry
}

/**
 * Operators normally record verdicts inline in the review cockpit, not here —
 * this tab exists so a whole market's accumulated verdicts can be reviewed and
 * pruned in one place, and so a wrong verdict recorded weeks ago is findable by
 * someone who wasn't the one who recorded it. Three affordances per row: the
 * toggle suspends a verdict (the word flags again but its provenance survives),
 * edit corrects the word or its note, and delete drops it entirely.
 *
 * Editing only the note keeps the original verifier and date, because identity is
 * the normalised word and the backend carries provenance forward on a matching key.
 * Changing the word itself to something that normalises differently is a NEW verdict
 * as far as provenance goes, and the backend re-stamps it to whoever made the edit —
 * which is honest: nobody listened to the new spelling.
 */
export function VerifiedPronunciationsManager({
    markets,
    station,
}: {
    readonly markets: readonly MarketOption[]
    readonly station: string
}) {
    const marketIds = useMemo(() => markets.map((m) => m.marketId), [markets])
    const { data: inventory, isLoading } =
        useAllVerifiedPronunciations(marketIds)
    const save = useSetVerifiedPronunciations()

    const [composerOpen, setComposerOpen] = useState(false)
    // The verdict being edited. Non-null puts the shared composer in edit mode against
    // that row's market; null means the composer records a new verdict.
    const [editing, setEditing] = useState<VerifiedRow | null>(null)
    const composing = composerOpen || editing !== null
    const scope = editing
        ? editing.marketId
        : station || (markets[0]?.marketId ?? '')
    const [word, setWord] = useState('')
    const [note, setNote] = useState('')

    const [search, setSearch] = useState('')

    const trimmed = word.trim()
    const trimmedNote = note.trim()
    // The note is half of what there is to edit here, so an unchanged check that looked
    // at the word alone would lock the Save button on a note-only correction.
    const unchanged =
        editing !== null &&
        trimmed === editing.entry.word &&
        trimmedNote === (editing.entry.note ?? '')
    const valid =
        trimmed.length > 0 &&
        trimmed.length <= VERIFIED_PRONUNCIATION_MAX_LENGTH &&
        pronunciationKey(trimmed).length > 0 &&
        !unchanged &&
        scope !== ''
    const submitting = save.isPending

    const wordsFor = (
        marketId: string
    ): readonly VerifiedPronunciationEntry[] =>
        inventory?.find((m) => m.marketId === marketId)?.words ?? []

    const labelFor = (marketId: string) =>
        markets.find((m) => m.marketId === marketId)?.label ?? marketId

    const active = search.trim() !== ''

    const groups = useMemo<ScopeGroup<VerifiedRow>[]>(() => {
        const q = search.trim().toLowerCase()
        return (inventory ?? [])
            .filter((m) => m.marketId === station)
            .map((m) => ({
                key: m.marketId,
                label: labelFor(m.marketId),
                items: m.words
                    .filter(
                        (w) =>
                            !q ||
                            w.word.toLowerCase().includes(q) ||
                            (w.note?.toLowerCase().includes(q) ?? false)
                    )
                    .map((w) => ({ marketId: m.marketId, entry: w })),
            }))
            .filter((g) => g.items.length > 0)
            .sort((a, b) => a.label.localeCompare(b.label))
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [inventory, markets, search, station])

    const handleAdd = async () => {
        if (!valid || submitting) return
        const current = wordsFor(scope)
        if (current.length >= VERIFIED_PRONUNCIATIONS_MAX) {
            toast.error(
                `Maximum ${VERIFIED_PRONUNCIATIONS_MAX} verified pronunciations per market`
            )
            return
        }
        const key = pronunciationKey(trimmed)
        if (current.some((w) => pronunciationKey(w.word) === key)) {
            toast.error(`"${trimmed}" is already verified for this market`)
            return
        }
        try {
            await save.mutateAsync({
                marketId: scope,
                words: [
                    ...current,
                    {
                        word: trimmed,
                        note: note.trim() || null,
                        enabled: true,
                        verifiedBy: null,
                        verifiedAt: null,
                    },
                ],
            })
            setWord('')
            setNote('')
            setComposerOpen(false)
            toast.success(`"${trimmed}" won't be flagged again`)
        } catch (err: unknown) {
            toast.error('Failed to add verified pronunciation', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const startEdit = (item: VerifiedRow) => {
        setEditing(item)
        setWord(item.entry.word)
        setNote(item.entry.note ?? '')
        setComposerOpen(false)
    }

    const closeComposer = () => {
        setEditing(null)
        setComposerOpen(false)
        setWord('')
        setNote('')
    }

    /**
     * Correct an existing verdict's word and/or note in place, keeping its position and
     * its enabled state. Identity is the normalised word, so the collision check excludes
     * the row being edited — otherwise a pure note edit would report itself as a duplicate.
     */
    const handleSaveEdit = async () => {
        if (!editing || !valid || submitting) return
        const originalKey = pronunciationKey(editing.entry.word)
        const current = wordsFor(editing.marketId)
        const key = pronunciationKey(trimmed)
        if (
            current.some(
                (w) =>
                    pronunciationKey(w.word) !== originalKey &&
                    pronunciationKey(w.word) === key
            )
        ) {
            toast.error(`"${trimmed}" is already verified for this market`)
            return
        }
        try {
            await save.mutateAsync({
                marketId: editing.marketId,
                words: current.map((w) =>
                    pronunciationKey(w.word) === originalKey
                        ? { ...w, word: trimmed, note: trimmedNote || null }
                        : w
                ),
            })
            closeComposer()
            toast.success('Verified pronunciation updated')
        } catch (err: unknown) {
            toast.error('Failed to update verified pronunciation', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const handleDelete = async (item: VerifiedRow) => {
        const key = pronunciationKey(item.entry.word)
        try {
            await save.mutateAsync({
                marketId: item.marketId,
                words: wordsFor(item.marketId).filter(
                    (w) => pronunciationKey(w.word) !== key
                ),
            })
            if (pronunciationKey(editing?.entry.word ?? '') === key)
                closeComposer()
            toast.success(`"${item.entry.word}" will be flagged again`)
        } catch (err: unknown) {
            toast.error('Failed to remove verified pronunciation', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const handleToggleEnabled = async (item: VerifiedRow) => {
        const key = pronunciationKey(item.entry.word)
        try {
            await save.mutateAsync({
                marketId: item.marketId,
                words: wordsFor(item.marketId).map((w) =>
                    pronunciationKey(w.word) === key
                        ? { ...w, enabled: !w.enabled }
                        : w
                ),
            })
        } catch (err: unknown) {
            toast.error('Failed to update verified pronunciation', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const secondaryFor = (entry: VerifiedPronunciationEntry) => {
        const provenance = [
            entry.verifiedBy,
            entry.verifiedAt
                ? new Date(entry.verifiedAt).toLocaleDateString()
                : null,
        ]
            .filter(Boolean)
            .join(' · ')
        return [entry.note, provenance].filter(Boolean).join(': ') || undefined
    }

    return (
        <RuleTab>
            <RuleToolbar
                add={{
                    what: 'a verified word',
                    onClick: () => {
                        // Pressing Add mid-edit abandons it and starts a blank create.
                        const wasEditing = editing !== null
                        setEditing(null)
                        setWord('')
                        setNote('')
                        setComposerOpen((v) => wasEditing || !v)
                    },
                    active: composing,
                    hint: (
                        <>
                            Stops the audio review flagging a word an operator
                            has already listened to and confirmed is correct.
                        </>
                    ),
                }}
                search={search}
                onSearchChange={setSearch}
                placeholder="Search words…"
            />

            {composing && (
                <RuleComposer
                    title={editing ? 'Edit a verified word' : undefined}
                    onCancel={closeComposer}
                    submit={{
                        label: editing ? 'Save changes' : 'Add',
                        icon: editing ? EditIcon : AddIcon,
                        onClick: editing ? handleSaveEdit : handleAdd,
                        disabled: !valid || submitting,
                        ready: valid,
                        submitting,
                        tooltip: unchanged
                            ? 'Change the word or its note to save.'
                            : undefined,
                    }}
                >
                    <ComposerField
                        label="Word"
                        htmlFor="verified-word"
                        width="sm:w-60"
                    >
                        <Input
                            id="verified-word"
                            maxLength={VERIFIED_PRONUNCIATION_MAX_LENGTH}
                            placeholder="for example, Keilor"
                            value={word}
                            onChange={(e) => setWord(e.target.value)}
                            onKeyDown={(e) => {
                                if (e.key !== 'Enter') return
                                if (editing) handleSaveEdit()
                                else handleAdd()
                            }}
                        />
                    </ComposerField>
                    <ComposerField
                        label="Note (optional)"
                        htmlFor="verified-note"
                        width="flex-1"
                    >
                        <Input
                            id="verified-note"
                            maxLength={VERIFIED_PRONUNCIATION_NOTE_MAX_LENGTH}
                            placeholder="for example, said KEE-ler, transcript always writes Keeler"
                            value={note}
                            onChange={(e) => setNote(e.target.value)}
                            onKeyDown={(e) => {
                                if (e.key !== 'Enter') return
                                if (editing) handleSaveEdit()
                                else handleAdd()
                            }}
                        />
                    </ComposerField>
                </RuleComposer>
            )}

            {Boolean(station) && (
                <ScopedActivityHistory
                    key={station}
                    resource="verified-pronunciations"
                    entityId={station}
                    label={`History for ${labelFor(station)}`}
                />
            )}

            <ScopeGroupedList<VerifiedRow>
                groups={groups}
                isLoading={isLoading}
                empty={
                    <EmptyState
                        action={
                            active && (
                                <Button
                                    variant="outline"
                                    size="sm"
                                    onClick={() => {
                                        setSearch('')
                                    }}
                                >
                                    <ResetIcon />
                                    Clear filters
                                </Button>
                            )
                        }
                    >
                        {active
                            ? 'No words match these filters.'
                            : 'Nothing verified yet. Add one above, or from bulletin review.'}
                    </EmptyState>
                }
                rowKey={(item) =>
                    `${item.marketId}:${pronunciationKey(item.entry.word)}`
                }
                renderRow={(item) => (
                    <RuleRow
                        enabled={item.entry.enabled}
                        onToggle={() => handleToggleEnabled(item)}
                        label={item.entry.word}
                        secondary={secondaryFor(item.entry)}
                        onEdit={() => startEdit(item)}
                        editLabel={`Edit ${item.entry.word}`}
                        onDelete={() => handleDelete(item)}
                        deleteLabel={`Delete ${item.entry.word}`}
                        itemLabel="verified pronunciation"
                        disabled={submitting}
                    />
                )}
            />
        </RuleTab>
    )
}
