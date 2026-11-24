import { lazy, Suspense, useState } from 'react'
import { BookOpen } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Header, PageContainer } from '@/components/layout'
import { DataBoundary } from '@/components/DataBoundary'
import { Placeholder } from '@/components/Placeholder'
import { CardGridSkeleton } from '@/components/LoadingSkeleton'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'
import { useDocumentTitle } from '@/hooks/use-document-title'
import { useIsAdmin } from '@/hooks/use-current-user'
import { toastAction } from '@/lib/toast-action'
import { CARD_GRID } from '@/lib/layout'
import {
    useArchivePlaybook,
    useCreatePlaybook,
    useDeletePlaybook,
    usePlaybooks,
    useUnarchivePlaybook,
} from '@/features/playbooks/api/playbook-hooks'
import { PlaybookCard } from '@/features/playbooks/components/PlaybookCard'
import type { BulletinPlaybook } from '@/features/playbooks/types'

const PlaybookStudio = lazy(() =>
    import('@/features/playbooks/components/studio/Studio').then((module) => ({
        default: module.Studio,
    }))
)

/** The operator-facing library of station diffs over the shipped writer playbook. */
export function PlaybooksPage() {
    useDocumentTitle('Playbooks')
    const query = usePlaybooks()
    const canDelete = useIsAdmin()
    const create = useCreatePlaybook()
    const archive = useArchivePlaybook()
    const unarchive = useUnarchivePlaybook()
    const remove = useDeletePlaybook()
    const deleteDialog = useConfirmDialog()
    const [pendingDelete, setPendingDelete] = useState<BulletinPlaybook | null>(
        null
    )
    const [activeId, setActiveId] = useState<string | null>(null)
    const [studioOpen, setStudioOpen] = useState(false)
    const [search, setSearch] = useState('')
    const [showArchived, setShowArchived] = useState(false)

    const openCreate = () => {
        setActiveId(null)
        setStudioOpen(true)
    }
    const openEdit = (id: string) => {
        setActiveId(id)
        setStudioOpen(true)
    }
    const handleArchive = (playbook: BulletinPlaybook) => {
        void toastAction(
            () => archive.mutateAsync(playbook.id),
            'Playbook archived',
            'Could not archive the playbook'
        )
    }
    const handleUnarchive = (playbook: BulletinPlaybook) => {
        void toastAction(
            () => unarchive.mutateAsync(playbook.id),
            'Playbook unarchived',
            'Could not unarchive the playbook'
        )
    }
    const handleDuplicate = (playbook: BulletinPlaybook) => {
        void toastAction(
            () =>
                create.mutateAsync({
                    name: `${playbook.name} (copy)`,
                    description: playbook.description,
                    role: playbook.role,
                    overlay: playbook.currentOverlay,
                    changeNote: `Duplicated from ${playbook.name} v${playbook.currentVersion}.`,
                }),
            'Playbook duplicated',
            'Could not duplicate the playbook'
        )
    }
    const requestDelete = (playbook: BulletinPlaybook) => {
        setPendingDelete(playbook)
        deleteDialog.open()
    }
    const confirmDelete = async () => {
        if (!pendingDelete) return
        await toastAction(
            () => remove.mutateAsync(pendingDelete.id),
            'Playbook deleted',
            'Could not delete the playbook'
        )
        setPendingDelete(null)
    }

    const playbooks = query.data?.playbooks ?? []
    const filtered = playbooks.filter((playbook) => {
        if (playbook.status === 'archived' && !showArchived) return false
        const needle = search.trim().toLowerCase()
        return (
            !needle ||
            [
                playbook.name,
                playbook.description ?? '',
                playbook.role,
                ...playbook.assignedStations,
            ]
                .join(' ')
                .toLowerCase()
                .includes(needle)
        )
    })
    const showHeaderAction =
        query.isLoading || query.isError || playbooks.length > 0

    return (
        <PageContainer>
            <Header
                title="Playbooks"
                description="Station-specific edits over the shipped bulletin writer instructions"
            >
                {showHeaderAction && (
                    <Button onClick={openCreate} className="gap-2">
                        <ICON.add className="size-4" /> New playbook
                    </Button>
                )}
            </Header>
            <DataBoundary
                query={query}
                isEmpty={(data) => data.playbooks.length === 0}
                errorFallback="Could not load the playbook library."
                loading={<CardGridSkeleton />}
                empty={
                    <Placeholder
                        icon={BookOpen}
                        message="No bulletin playbooks yet"
                        detail="Create a playbook to give one station its own writer instructions, without copying the shipped prompt."
                        action={
                            <Button onClick={openCreate} className="gap-2">
                                <ICON.add className="size-4" /> New playbook
                            </Button>
                        }
                    />
                }
            >
                <div className="mb-4 flex items-center gap-3">
                    <Input
                        value={search}
                        onChange={(event) => setSearch(event.target.value)}
                        placeholder="Search playbooks, roles, or stations..."
                        aria-label="Search playbooks"
                        className="flex-1"
                    />
                    <div className="flex shrink-0 items-center gap-2">
                        <Switch
                            id="playbooks-show-archived"
                            checked={showArchived}
                            onCheckedChange={setShowArchived}
                        />
                        <Label
                            htmlFor="playbooks-show-archived"
                            className="cursor-pointer text-xs font-medium"
                        >
                            Show archived
                        </Label>
                    </div>
                </div>
                {filtered.length === 0 ? (
                    <Placeholder
                        icon={BookOpen}
                        message="No matching playbooks"
                        detail="Try a different name, role, station, or status."
                    />
                ) : (
                    <div className={CARD_GRID}>
                        {filtered.map((playbook) => (
                            <PlaybookCard
                                key={playbook.id}
                                playbook={playbook}
                                onView={openEdit}
                                onEdit={openEdit}
                                onDuplicate={handleDuplicate}
                                onDelete={requestDelete}
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
                    <PlaybookStudio
                        open={studioOpen}
                        onOpenChange={setStudioOpen}
                        playbookId={activeId}
                    />
                </Suspense>
            )}
            <ConfirmDialog
                open={deleteDialog.isOpen}
                onOpenChange={deleteDialog.onOpenChange}
                onConfirm={confirmDelete}
                title="Delete bulletin playbook"
                description={`Delete ${pendingDelete?.name ?? 'this playbook'}? Stations using it will fall back to the shipped defaults.`}
            />
        </PageContainer>
    )
}
