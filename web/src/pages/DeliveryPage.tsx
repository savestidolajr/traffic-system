import { useEffect, useState, lazy, Suspense } from 'react'
import { useSearchParams } from 'react-router'
import { Drama } from 'lucide-react'
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
    useDeliveryStyles,
    useCreateDeliveryStyle,
    useDeleteDeliveryStyle,
    useArchiveDeliveryStyle,
    useUnarchiveDeliveryStyle,
} from '@/features/delivery/api/delivery-style-hooks'
import { Switch } from '@/components/ui/switch'
import { Label } from '@/components/ui/label'
import { useStyleAssignments } from '@/features/delivery/lib/assignments'
import { DeliveryStyleCard } from '@/features/delivery/components/DeliveryStyleCard'
import { DeliveryStyleTable } from '@/features/delivery/components/DeliveryStyleTable'
import { ViewToggle } from '@/components/ViewToggle'
import { useViewMode } from '@/hooks/use-view-mode'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'
import { inUseDeleteError } from '@/lib/library-delete-error'
import type { DeliveryStyle } from '@/features/delivery/types'

// Lazy: only ever renders after a user action (New/Edit), never on the
// initial gallery paint — keep it off the route chunk everyone downloads.
const DeliveryStyleStudio = lazy(() =>
    import('@/features/delivery/components/DeliveryStyleStudio').then((m) => ({
        default: m.DeliveryStyleStudio,
    }))
)

/**
 * Delivery styles — the shared library of how a station's bulletins are read,
 * a sibling of Voices. A gallery of styles, each opened in a focused Studio to
 * author, audition, and version. Styles are assigned per station in schedules.
 */
export function DeliveryPage() {
    useDocumentTitle('Delivery')
    // Mirrors the backend: DELETE /api/delivery-styles/{id} is the one Admin route.
    // Everything else — create, edit, duplicate, archive, restore, audition — resolves to
    // Operator in RouteRoleMatrix, so only Delete is gated here.
    const canDelete = useIsAdmin()
    const query = useDeliveryStyles()
    const { byStyleId } = useStyleAssignments()

    const [searchParams, setSearchParams] = useSearchParams()
    const openIdFromUrl = searchParams.get('open')
    const [studioOpen, setStudioOpen] = useState(() => !!openIdFromUrl)
    // null while the Studio is in create mode; a style id when editing one.
    const [activeStyleId, setActiveStyleId] = useState<string | null>(
        () => openIdFromUrl
    )
    const deleteDialog = useConfirmDialog()
    const [pendingDelete, setPendingDelete] = useState<DeliveryStyle | null>(
        null
    )
    const [search, setSearch] = useState('')
    const [showArchived, setShowArchived] = useState(false)
    const [view, setView] = useViewMode('delivery')

    const openCreate = () => {
        setActiveStyleId(null)
        setStudioOpen(true)
    }
    const openStyle = (id: string) => {
        setActiveStyleId(id)
        setStudioOpen(true)
    }

    // Deep-link from Cmd+K: `?open=<styleId>` opens the matching style once
    // the live gallery has loaded, then clears the param so back/refresh does
    // not re-trigger it.
    useEffect(() => {
        const openId = searchParams.get('open')
        if (!openId || !query.data) return
        const style = query.data.styles.find(
            (candidate) => candidate.id === openId
        )
        // Deep-link consumption only: clearing the URL param below prevents
        // this effect from reopening the same style after the state update.
        // eslint-disable-next-line react-hooks/set-state-in-effect
        if (style) openStyle(style.id)
        setSearchParams(
            (prev) => {
                const next = new URLSearchParams(prev)
                next.delete('open')
                return next
            },
            { replace: true }
        )
    }, [searchParams, setSearchParams, query.data])

    const create = useCreateDeliveryStyle()
    const deleteStyle = useDeleteDeliveryStyle()
    const archiveStyle = useArchiveDeliveryStyle()
    const unarchiveStyle = useUnarchiveDeliveryStyle()

    const handleArchive = (style: DeliveryStyle) => {
        void toastAction(
            () => archiveStyle.mutateAsync(style.id),
            'Style archived',
            'Could not archive the style'
        )
    }

    const handleUnarchive = (style: DeliveryStyle) => {
        void toastAction(
            () => unarchiveStyle.mutateAsync(style.id),
            'Style unarchived',
            'Could not unarchive the style'
        )
    }

    const handleDuplicate = (style: DeliveryStyle) => {
        const policy = style.versions.at(-1)?.policy
        if (!policy) return
        void toastAction(
            () =>
                create.mutateAsync({
                    name: `${style.name} (copy)`,
                    description: style.description,
                    policy: {
                        toneDescription: policy.toneDescription,
                        allowedTags: policy.allowedTags,
                        intensity: policy.intensity,
                    },
                    changeNote: `Duplicated from ${style.name} v${style.currentVersion}.`,
                }),
            'Style duplicated',
            'Could not duplicate the style'
        )
    }

    const handleDeleteClick = (style: DeliveryStyle) => {
        setPendingDelete(style)
        deleteDialog.open()
    }

    const handleConfirmDelete = async () => {
        if (!pendingDelete) return
        await toastAction(
            () => deleteStyle.mutateAsync(pendingDelete.id),
            'Style deleted',
            'Could not delete the style',
            // The backend refuses to delete an assigned style with a 409
            // `library.in_use` — surface the API's message verbatim so the
            // operator sees why the delete was refused.
            inUseDeleteError('style')
        )
        setPendingDelete(null)
    }

    const styles = query.data?.styles ?? []
    const filteredStyles = styles.filter((style) => {
        // Archived items are hidden unless explicitly shown — the same rule as
        // the schedule pickers (active only) — so an archived item stays
        // reachable through the toggle + Unarchive action.
        if (style.status === 'archived' && !showArchived) return false
        const policy = style.versions.at(-1)?.policy
        const haystack = [
            style.name,
            style.description ?? '',
            style.status,
            policy?.toneDescription ?? '',
            ...(policy?.allowedTags ?? []),
            ...(byStyleId.get(style.id) ?? []),
        ]
            .join(' ')
            .toLowerCase()
        return haystack.includes(search.trim().toLowerCase())
    })
    // One CTA per state: hide the persistent header action while the empty
    // hero owns the focal "New" button, so it isn't duplicated.
    const showHeaderAction =
        query.isLoading || query.isError || styles.length > 0

    return (
        <PageContainer>
            <Header
                title="Delivery"
                description="Reusable on-air read styles for station schedules"
            >
                {showHeaderAction && (
                    <Button onClick={openCreate} className="gap-2">
                        <ICON.add className="size-4" />
                        New style
                    </Button>
                )}
            </Header>

            <DataBoundary
                query={query}
                isEmpty={(d) => d.styles.length === 0}
                errorFallback="Could not load the delivery library."
                loading={<CardGridSkeleton />}
                empty={
                    <Placeholder
                        icon={Drama}
                        message="No delivery styles yet"
                        detail="Create a read style, hear it, then assign it in a schedule."
                        action={
                            <Button onClick={openCreate} className="gap-2">
                                <ICON.add className="size-4" />
                                New style
                            </Button>
                        }
                    />
                }
            >
                <div className="mb-4 flex items-center gap-3">
                    <Input
                        value={search}
                        onChange={(event) => setSearch(event.target.value)}
                        placeholder="Search delivery styles, cues, or stations..."
                        aria-label="Search delivery styles"
                        className="flex-1"
                    />
                    <div className="flex shrink-0 items-center gap-2">
                        <Switch
                            id="delivery-show-archived"
                            checked={showArchived}
                            onCheckedChange={setShowArchived}
                        />
                        <Label
                            htmlFor="delivery-show-archived"
                            className="cursor-pointer text-xs font-medium"
                        >
                            Show archived
                        </Label>
                    </div>
                    <ViewToggle value={view} onChange={setView} />
                </div>
                {filteredStyles.length === 0 ? (
                    <Placeholder
                        icon={Drama}
                        message="No matching delivery styles"
                        detail="Try a different style, cue, status, or station."
                    />
                ) : view === 'table' ? (
                    <DeliveryStyleTable
                        styles={filteredStyles}
                        assignmentsByStyleId={byStyleId}
                        onView={openStyle}
                        onEdit={openStyle}
                        onDuplicate={handleDuplicate}
                        onDelete={handleDeleteClick}
                        onArchive={handleArchive}
                        onUnarchive={handleUnarchive}
                        canDelete={canDelete}
                    />
                ) : (
                    <div className={CARD_GRID}>
                        {filteredStyles.map((style) => (
                            <DeliveryStyleCard
                                key={style.id}
                                style={style}
                                assignedStations={byStyleId.get(style.id) ?? []}
                                onView={openStyle}
                                onEdit={openStyle}
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
                    <DeliveryStyleStudio
                        open={studioOpen}
                        onOpenChange={setStudioOpen}
                        styleId={activeStyleId}
                    />
                </Suspense>
            )}
            <ConfirmDialog
                open={deleteDialog.isOpen}
                onOpenChange={deleteDialog.onOpenChange}
                onConfirm={handleConfirmDelete}
                title="Delete delivery style"
                description={`Stations still pointing at ${pendingDelete?.name ?? 'this delivery style'} will need a replacement style selected.`}
            />
        </PageContainer>
    )
}
