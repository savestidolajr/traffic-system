import { useEffect, useState, lazy, Suspense } from 'react'
import { useSearchParams } from 'react-router'
import { BookText } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { toastAction } from '@/lib/toast-action'
import { Header, PageContainer } from '@/components/layout'
import { CARD_GRID } from '@/lib/layout'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { CardGridSkeleton } from '@/components/LoadingSkeleton'
import { DataBoundary } from '@/components/DataBoundary'
import { Placeholder } from '@/components/Placeholder'
import { useDocumentTitle } from '@/hooks/use-document-title'
import { useIsAdmin } from '@/hooks/use-current-user'
import {
    usePronunciationDictionaries,
    useCreatePronunciationDictionary,
    useDeletePronunciationDictionary,
    useArchivePronunciationDictionary,
    useUnarchivePronunciationDictionary,
} from '@/features/pronunciation/api/pronunciation-hooks'
import { useDictionaryAssignments } from '@/features/pronunciation/lib/assignments'
import {
    groupDictionariesByScope,
    sortPlatformWideFirst,
} from '@/features/pronunciation/lib/scope-grouping'
import { PronunciationDictionaryCard } from '@/features/pronunciation/components/PronunciationDictionaryCard'
import { PronunciationDictionaryTable } from '@/features/pronunciation/components/PronunciationDictionaryTable'
import { ViewToggle } from '@/components/ViewToggle'
import { useViewMode } from '@/hooks/use-view-mode'
import { Switch } from '@/components/ui/switch'
import { Label } from '@/components/ui/label'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'
import { inUseDeleteError } from '@/lib/library-delete-error'
import type { Dictionary } from '@/features/pronunciation/types'

// Lazy: only ever renders after a user action (New/Edit), never on the
// initial gallery paint — keep it off the route chunk everyone downloads.
const PronunciationDictionaryStudio = lazy(() =>
    import('@/features/pronunciation/components/PronunciationDictionaryStudio').then(
        (m) => ({ default: m.PronunciationDictionaryStudio })
    )
)

/**
 * Pronunciation dictionaries — the shared library of TTS pronunciation fixes,
 * a sibling of Delivery. A gallery of dictionaries, each opened in a focused
 * Studio to author rules, preview, and version.
 */
export function PronunciationPage() {
    useDocumentTitle('Pronunciation')
    // Mirrors the backend: DELETE /api/pronunciation-dictionaries/{id} is the one Admin route.
    // Everything else — create, edit, duplicate, archive, restore, preview — resolves to
    // Operator in RouteRoleMatrix, so only Delete is gated here.
    const canDelete = useIsAdmin()
    const query = usePronunciationDictionaries()
    const { byDictionaryId, stationCount } = useDictionaryAssignments()

    const [searchParams, setSearchParams] = useSearchParams()
    const openIdFromUrl = searchParams.get('open')
    const [studioOpen, setStudioOpen] = useState(() => !!openIdFromUrl)
    const [activeDictionaryId, setActiveDictionaryId] = useState<string | null>(
        () => openIdFromUrl
    )
    const [suggestWord, setSuggestWord] = useState<string | null>(null)
    const [search, setSearch] = useState('')
    const [showArchived, setShowArchived] = useState(false)
    const [view, setView] = useViewMode('pronunciation')
    const deleteDialog = useConfirmDialog()
    const [pendingDelete, setPendingDelete] = useState<Dictionary | null>(null)

    // Read ?suggest=<word> from URL for deep-linking from review cockpit
    const suggestFromUrl = searchParams.get('suggest')

    const openCreate = () => {
        setActiveDictionaryId(null)
        setSuggestWord(suggestFromUrl)
        setStudioOpen(true)
    }

    const openDictionary = (id: string) => {
        setActiveDictionaryId(id)
        setSuggestWord(null)
        setStudioOpen(true)
    }

    // Deep-link from Cmd+K: `?open=<dictionaryId>` opens the matching
    // dictionary once the live gallery has loaded, then clears the param so
    // back/refresh does not re-trigger it.
    useEffect(() => {
        const openId = searchParams.get('open')
        if (!openId || !query.data) return
        const dictionary = query.data.items.find(
            (candidate) => candidate.id === openId
        )
        // Deep-link consumption only: clearing the URL param below prevents
        // this effect from reopening the same dictionary after the state update.
        // eslint-disable-next-line react-hooks/set-state-in-effect
        if (dictionary) openDictionary(dictionary.id)
        setSearchParams(
            (prev) => {
                const next = new URLSearchParams(prev)
                next.delete('open')
                return next
            },
            { replace: true }
        )
    }, [searchParams, setSearchParams, query.data])

    const create = useCreatePronunciationDictionary()
    const deleteDictionary = useDeletePronunciationDictionary()
    const archiveDictionary = useArchivePronunciationDictionary()
    const unarchiveDictionary = useUnarchivePronunciationDictionary()

    const handleArchive = (dict: Dictionary) => {
        void toastAction(
            () => archiveDictionary.mutateAsync(dict.id),
            'Dictionary archived',
            'Could not archive the dictionary'
        )
    }

    const handleUnarchive = (dict: Dictionary) => {
        void toastAction(
            () => unarchiveDictionary.mutateAsync(dict.id),
            'Dictionary unarchived',
            'Could not unarchive the dictionary'
        )
    }

    const handleDuplicate = (dict: Dictionary) => {
        void toastAction(
            () =>
                create.mutateAsync({
                    name: `${dict.name} (copy)`,
                    description: dict.description,
                    rules: dict.rules,
                    changeNote: null,
                }),
            'Dictionary duplicated',
            'Failed to duplicate dictionary'
        )
    }

    const handleDeleteClick = (dict: Dictionary) => {
        setPendingDelete(dict)
        deleteDialog.open()
    }

    const handleConfirmDelete = async () => {
        if (!pendingDelete) return
        await toastAction(
            () => deleteDictionary.mutateAsync(pendingDelete.id),
            'Dictionary deleted',
            'Failed to delete dictionary',
            // The backend refuses to delete an assigned dictionary with a 409
            // `library.in_use` — surface the API's message verbatim so the
            // operator sees why the delete was refused.
            inUseDeleteError('dictionary')
        )
        setPendingDelete(null)
    }

    const dictionaries = query.data?.items ?? []
    const filteredDictionaries = dictionaries.filter((dict) => {
        // Archived items are hidden unless explicitly shown — the same rule as
        // the schedule pickers (active only) — so an archived item stays
        // reachable through the toggle + Unarchive action.
        if (dict.status === 'archived' && !showArchived) return false
        const haystack = [
            dict.name,
            dict.description ?? '',
            dict.status,
            ...dict.assignedStations,
            ...dict.rules.flatMap((rule) => [
                rule.stringToReplace,
                rule.replacement,
                rule.alphabet ?? '',
            ]),
            ...(byDictionaryId.get(dict.id) ?? []),
        ]
            .join(' ')
            .toLowerCase()
        return haystack.includes(search.trim().toLowerCase())
    })
    const showHeaderAction =
        query.isLoading || query.isError || dictionaries.length > 0

    return (
        <PageContainer>
            <Header
                title="Pronunciation"
                description="How TTS pronounces road names, suburbs, and other words"
            >
                {showHeaderAction && (
                    <Button onClick={openCreate}>
                        <ICON.add />
                        New dictionary
                    </Button>
                )}
            </Header>

            <DataBoundary
                query={query}
                isEmpty={(d) => d.items.length === 0}
                errorFallback="Could not load the pronunciation library."
                loading={<CardGridSkeleton />}
                empty={
                    <Placeholder
                        icon={BookText}
                        message="No pronunciation dictionaries yet"
                        detail="Alias and phoneme rules fix place names ElevenLabs mispronounces."
                        action={
                            <Button onClick={openCreate} className="gap-2">
                                <ICON.add className="size-4" />
                                New dictionary
                            </Button>
                        }
                    />
                }
            >
                <div className="mb-4 flex items-center gap-3">
                    <Input
                        value={search}
                        onChange={(event) => setSearch(event.target.value)}
                        placeholder="Search dictionaries, words, or stations..."
                        aria-label="Search pronunciation dictionaries"
                        className="flex-1"
                    />
                    <div className="flex shrink-0 items-center gap-2">
                        <Switch
                            id="pronunciation-show-archived"
                            checked={showArchived}
                            onCheckedChange={setShowArchived}
                        />
                        <Label
                            htmlFor="pronunciation-show-archived"
                            className="cursor-pointer text-xs font-medium"
                        >
                            Show archived
                        </Label>
                    </div>
                    <ViewToggle value={view} onChange={setView} />
                </div>
                {filteredDictionaries.length === 0 ? (
                    <Placeholder
                        icon={BookText}
                        message="No matching dictionaries"
                        detail="Try another word, status, or station."
                    />
                ) : view === 'table' ? (
                    <PronunciationDictionaryTable
                        dictionaries={sortPlatformWideFirst(
                            filteredDictionaries
                        )}
                        assignmentsByDictionaryId={byDictionaryId}
                        stationCount={stationCount}
                        onView={openDictionary}
                        onEdit={openDictionary}
                        onDuplicate={handleDuplicate}
                        onDelete={handleDeleteClick}
                        onArchive={handleArchive}
                        onUnarchive={handleUnarchive}
                        canDelete={canDelete}
                    />
                ) : (
                    <div className="flex flex-col gap-6">
                        {groupDictionariesByScope(filteredDictionaries).map(
                            (group) => (
                                <section key={group.key} className="space-y-3">
                                    <h2 className="text-sm font-semibold">
                                        {group.label}
                                        <span className="ml-2 text-xs font-normal text-muted-foreground">
                                            ({group.items.length})
                                        </span>
                                    </h2>
                                    <div className={CARD_GRID}>
                                        {group.items.map((dict) => (
                                            <PronunciationDictionaryCard
                                                key={dict.id}
                                                dict={dict}
                                                assignedStations={
                                                    byDictionaryId.get(
                                                        dict.id
                                                    ) ?? []
                                                }
                                                stationCount={stationCount}
                                                onView={openDictionary}
                                                onEdit={openDictionary}
                                                onDuplicate={handleDuplicate}
                                                onDelete={handleDeleteClick}
                                                onArchive={handleArchive}
                                                onUnarchive={handleUnarchive}
                                                canDelete={canDelete}
                                            />
                                        ))}
                                    </div>
                                </section>
                            )
                        )}
                    </div>
                )}
            </DataBoundary>

            {studioOpen && (
                <Suspense fallback={null}>
                    <PronunciationDictionaryStudio
                        open={studioOpen}
                        onOpenChange={setStudioOpen}
                        dictionaryId={activeDictionaryId}
                        suggestWord={suggestWord}
                    />
                </Suspense>
            )}
            <ConfirmDialog
                open={deleteDialog.isOpen}
                onOpenChange={deleteDialog.onOpenChange}
                onConfirm={handleConfirmDelete}
                title="Delete pronunciation dictionary"
                description={`Stations still pointing at ${pendingDelete?.name ?? 'this dictionary'} will need a replacement selected.`}
            />
        </PageContainer>
    )
}
