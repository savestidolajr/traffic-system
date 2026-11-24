import { useEffect, useState, lazy, Suspense } from 'react'
import { useSearchParams } from 'react-router'
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
    useBookends,
    useCreateBookend,
    useDeleteBookend,
    useArchiveBookend,
    useUnarchiveBookend,
} from '@/features/bookends/api/bookend-hooks'
import { Switch } from '@/components/ui/switch'
import { Label } from '@/components/ui/label'
import { useBookendAssignments } from '@/features/bookends/lib/assignments'
import { BOOKEND_COPY, BOOKEND_ICON } from '@/features/bookends/lib/copy'
import { BookendCard } from '@/features/bookends/components/BookendCard'
import { BookendTable } from '@/features/bookends/components/BookendTable'
import { ViewToggle } from '@/components/ViewToggle'
import { useViewMode } from '@/hooks/use-view-mode'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'
import { inUseDeleteError } from '@/lib/library-delete-error'
import type { Bookend } from '@/features/bookends/types'

// Lazy: only ever renders after a user action (New/Edit), never on the
// initial gallery paint — keep it off the route chunk everyone downloads.
const BookendStudio = lazy(() =>
    import('@/features/bookends/components/BookendStudio').then((m) => ({
        default: m.BookendStudio,
    }))
)

/**
 * The shared bookend-pair library: a gallery of intro+outro pairs, each
 * opened in a focused Studio to author, preview, and version. A station
 * assigns one pair in schedules.
 *
 * Operator-writable: a pair is a station's own opening and closing lines, so
 * authoring it is operational work and every bookend route except DELETE
 * resolves to Operator in `RouteRoleMatrix`. Delete alone stays Admin —
 * archive is the reversible retire path — so it is the only affordance gated
 * here.
 */
export function BookendGalleryPage() {
    const copy = BOOKEND_COPY
    useDocumentTitle(copy.title)
    // Mirrors the backend: DELETE /api/bookends/{id} is the one Admin route.
    const canDelete = useIsAdmin()
    const query = useBookends()
    const { byBookendId } = useBookendAssignments()

    const [searchParams, setSearchParams] = useSearchParams()
    const openIdFromUrl = searchParams.get('open')
    const [studioOpen, setStudioOpen] = useState(() => !!openIdFromUrl)
    // null while the Studio is in create mode; a bookend id when editing one.
    const [activeBookendId, setActiveBookendId] = useState<string | null>(
        () => openIdFromUrl
    )
    const deleteDialog = useConfirmDialog()
    const [pendingDelete, setPendingDelete] = useState<Bookend | null>(null)
    const [search, setSearch] = useState('')
    const [showArchived, setShowArchived] = useState(false)
    const [view, setView] = useViewMode('bookends')

    const openCreate = () => {
        setActiveBookendId(null)
        setStudioOpen(true)
    }
    const openBookend = (id: string) => {
        setActiveBookendId(id)
        setStudioOpen(true)
    }

    // Deep-link from Cmd+K: `?open=<bookendId>` opens the matching bookend once
    // the live gallery has loaded, then clears the param so back/refresh does
    // not re-trigger it.
    useEffect(() => {
        const openId = searchParams.get('open')
        if (!openId || !query.data) return
        const bookend = query.data.bookends.find(
            (candidate) => candidate.id === openId
        )
        // Deep-link consumption only: clearing the URL param below prevents
        // this effect from reopening the same bookend after the state update.
        // eslint-disable-next-line react-hooks/set-state-in-effect
        if (bookend) openBookend(bookend.id)
        setSearchParams(
            (prev) => {
                const next = new URLSearchParams(prev)
                next.delete('open')
                return next
            },
            { replace: true }
        )
    }, [searchParams, setSearchParams, query.data])

    const create = useCreateBookend()
    const deleteBookend = useDeleteBookend()
    const archiveBookend = useArchiveBookend()
    const unarchiveBookend = useUnarchiveBookend()

    const handleArchive = (bookend: Bookend) => {
        void toastAction(
            () => archiveBookend.mutateAsync(bookend.id),
            `${copy.singular} archived`,
            `Could not archive the ${copy.singular}`
        )
    }

    const handleUnarchive = (bookend: Bookend) => {
        void toastAction(
            () => unarchiveBookend.mutateAsync(bookend.id),
            `${copy.singular} unarchived`,
            `Could not unarchive the ${copy.singular}`
        )
    }

    const handleDuplicate = (bookend: Bookend) => {
        void toastAction(
            () =>
                create.mutateAsync({
                    name: `${bookend.name} (copy)`,
                    description: bookend.description,
                    introText: bookend.introText,
                    outroText: bookend.outroText,
                    changeNote: `Duplicated from ${bookend.name} v${bookend.currentVersion}.`,
                }),
            `${copy.singular} duplicated`,
            `Could not duplicate the ${copy.singular}`
        )
    }

    const handleDeleteClick = (bookend: Bookend) => {
        setPendingDelete(bookend)
        deleteDialog.open()
    }

    const handleConfirmDelete = async () => {
        if (!pendingDelete) return
        await toastAction(
            () => deleteBookend.mutateAsync(pendingDelete.id),
            `${copy.singular} deleted`,
            `Could not delete the ${copy.singular}`,
            // The backend refuses to delete an assigned bookend with a 409
            // `library.in_use` — surface the API's message verbatim so the
            // operator sees why the delete was refused.
            inUseDeleteError(copy.singular)
        )
        setPendingDelete(null)
    }

    const bookends = query.data?.bookends ?? []
    const filteredBookends = bookends.filter((bookend) => {
        // Archived items are hidden unless explicitly shown — the same rule as
        // the schedule pickers (active only) — so an archived item stays
        // reachable through the toggle + Unarchive action.
        if (bookend.status === 'archived' && !showArchived) return false
        const haystack = [
            bookend.name,
            bookend.description ?? '',
            bookend.status,
            bookend.introText,
            bookend.outroText,
            ...bookend.tokens,
            ...(byBookendId.get(bookend.id) ?? []),
        ]
            .join(' ')
            .toLowerCase()
        return haystack.includes(search.trim().toLowerCase())
    })
    // One CTA per state: hide the persistent header action while the empty
    // hero owns the focal "New" button, so it isn't duplicated.
    const showHeaderAction =
        query.isLoading || query.isError || bookends.length > 0

    return (
        <PageContainer>
            <Header title={copy.title} description={copy.description}>
                {showHeaderAction && (
                    <Button onClick={openCreate} className="gap-2">
                        <ICON.add className="size-4" />
                        {copy.createLabel}
                    </Button>
                )}
            </Header>

            <DataBoundary
                query={query}
                isEmpty={(d) => d.bookends.length === 0}
                errorFallback={`Could not load the ${copy.title.toLowerCase()} library.`}
                loading={<CardGridSkeleton />}
                empty={
                    <Placeholder
                        icon={BOOKEND_ICON}
                        message={copy.emptyMessage}
                        detail={copy.emptyDetail}
                        action={
                            <Button onClick={openCreate} className="gap-2">
                                <ICON.add className="size-4" />
                                {copy.createLabel}
                            </Button>
                        }
                    />
                }
            >
                <div className="mb-4 flex items-center gap-3">
                    <Input
                        value={search}
                        onChange={(event) => setSearch(event.target.value)}
                        placeholder={copy.searchPlaceholder}
                        aria-label={`Search ${copy.title.toLowerCase()}`}
                        className="flex-1"
                    />
                    <div className="flex shrink-0 items-center gap-2">
                        <Switch
                            id="bookends-show-archived"
                            checked={showArchived}
                            onCheckedChange={setShowArchived}
                        />
                        <Label
                            htmlFor="bookends-show-archived"
                            className="cursor-pointer text-xs font-medium"
                        >
                            Show archived
                        </Label>
                    </div>
                    <ViewToggle value={view} onChange={setView} />
                </div>
                {filteredBookends.length === 0 ? (
                    <Placeholder
                        icon={BOOKEND_ICON}
                        message={`No matching ${copy.plural}`}
                        detail="Try a different name, token, status, or station."
                    />
                ) : view === 'table' ? (
                    <BookendTable
                        bookends={filteredBookends}
                        assignmentsByBookendId={byBookendId}
                        onView={openBookend}
                        onEdit={openBookend}
                        onDuplicate={handleDuplicate}
                        onDelete={handleDeleteClick}
                        onArchive={handleArchive}
                        onUnarchive={handleUnarchive}
                        canDelete={canDelete}
                    />
                ) : (
                    <div className={CARD_GRID}>
                        {filteredBookends.map((bookend) => (
                            <BookendCard
                                key={bookend.id}
                                bookend={bookend}
                                assignedStations={
                                    byBookendId.get(bookend.id) ?? []
                                }
                                onView={openBookend}
                                onEdit={openBookend}
                                onDuplicate={handleDuplicate}
                                onDelete={handleDeleteClick}
                                onArchive={handleArchive}
                                onUnarchive={handleUnarchive}
                                canDelete={canDelete}
                            />
                        ))}
                    </div>
                )}
            </DataBoundary>

            {studioOpen && (
                <Suspense fallback={null}>
                    <BookendStudio
                        open={studioOpen}
                        onOpenChange={setStudioOpen}
                        bookendId={activeBookendId}
                    />
                </Suspense>
            )}
            <ConfirmDialog
                open={deleteDialog.isOpen}
                onOpenChange={deleteDialog.onOpenChange}
                onConfirm={handleConfirmDelete}
                title={`Delete ${copy.singular}`}
                description={`Any station still pointing at ${pendingDelete?.name ?? `this ${copy.singular}`} will need a replacement ${copy.singular} selected.`}
            />
        </PageContainer>
    )
}
