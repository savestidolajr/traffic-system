import { useEffect, useState, lazy, Suspense } from 'react'
import { useSearchParams } from 'react-router'
import { toastAction } from '@/lib/toast-action'
import { AudioLines } from 'lucide-react'
import { ICON } from '@/lib/design/icons'
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
    useVoiceProfiles,
    useCreateVoiceProfile,
    useDeleteVoiceProfile,
    useArchiveVoiceProfile,
    useUnarchiveVoiceProfile,
} from '@/features/voices/api/voice-profile-hooks'
import { useVoiceAssignments } from '@/features/voices/lib/assignments'
import { currentVersion } from '@/features/voices/lib/voice-profile-utils'
import { VoiceVariantCard } from '@/features/voices/components/VoiceVariantCard'
import { VoiceProfileTable } from '@/features/voices/components/VoiceProfileTable'
import { ViewToggle } from '@/components/ViewToggle'
import { useViewMode } from '@/hooks/use-view-mode'
import { Switch } from '@/components/ui/switch'
import { Label } from '@/components/ui/label'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'
import { inUseDeleteError } from '@/lib/library-delete-error'
import type { VoiceProfile } from '@/features/voices/profile-types'

// Lazy: only ever renders after a user action (New/Edit), never on the
// initial gallery paint — keep it off the route chunk everyone downloads.
const VoiceStudio = lazy(() =>
    import('@/features/voices/components/VoiceStudio').then((m) => ({
        default: m.VoiceStudio,
    }))
)

export function VoicesPage() {
    useDocumentTitle('Voices')
    // Mirrors the backend: DELETE /api/voice-profiles/{id} is the one Admin route.
    // Everything else — create, tune, duplicate, archive, restore, preview — resolves
    // to Operator in RouteRoleMatrix, so only Delete is gated here.
    const canDelete = useIsAdmin()
    const query = useVoiceProfiles()
    // Derived from schedules — the wire DTO's `assignedStations` is always []
    // (see `lib/assignments.ts`), so the gallery would otherwise report every
    // profile Unassigned while stations air with five of them.
    const { byProfileId } = useVoiceAssignments()
    const [searchParams, setSearchParams] = useSearchParams()
    const openIdFromUrl = searchParams.get('open')
    const [workshopOpen, setWorkshopOpen] = useState(() => !!openIdFromUrl)
    // null while the workshop is in create mode; a profile id when tuning one.
    const [activeProfileId, setActiveProfileId] = useState<string | null>(
        () => openIdFromUrl
    )
    const deleteDialog = useConfirmDialog()
    const [pendingDelete, setPendingDelete] = useState<VoiceProfile | null>(
        null
    )
    const [search, setSearch] = useState('')
    const [showArchived, setShowArchived] = useState(false)
    const [view, setView] = useViewMode('voices')

    const openCreate = () => {
        setActiveProfileId(null)
        setWorkshopOpen(true)
    }
    const openProfile = (id: string) => {
        setActiveProfileId(id)
        setWorkshopOpen(true)
    }

    // Deep-link from Cmd+K: `?open=<profileId>` opens the matching profile
    // once the live gallery has loaded, then clears the param so back/refresh
    // does not re-trigger it.
    useEffect(() => {
        const openId = searchParams.get('open')
        if (!openId || !query.data) return
        const profile = query.data.profiles.find(
            (candidate) => candidate.id === openId
        )
        // Deep-link consumption only: clearing the URL param below prevents
        // this effect from reopening the same profile after the state update.
        // eslint-disable-next-line react-hooks/set-state-in-effect
        if (profile) openProfile(profile.id)
        setSearchParams(
            (prev) => {
                const next = new URLSearchParams(prev)
                next.delete('open')
                return next
            },
            { replace: true }
        )
    }, [searchParams, setSearchParams, query.data])

    const createProfile = useCreateVoiceProfile()
    const deleteProfile = useDeleteVoiceProfile()
    const archiveProfile = useArchiveVoiceProfile()
    const unarchiveProfile = useUnarchiveVoiceProfile()

    const handleArchive = (profile: VoiceProfile) => {
        void toastAction(
            () => archiveProfile.mutateAsync(profile.id),
            'Variant archived',
            'Could not archive the variant'
        )
    }

    const handleUnarchive = (profile: VoiceProfile) => {
        void toastAction(
            () => unarchiveProfile.mutateAsync(profile.id),
            'Variant unarchived',
            'Could not unarchive the variant'
        )
    }
    const handleDuplicate = (profile: VoiceProfile) => {
        const version = currentVersion(profile)
        void toastAction(
            () =>
                createProfile.mutateAsync({
                    name: `${profile.name} (copy)`,
                    description: profile.description,
                    baseVoiceId: profile.baseVoiceId,
                    baseVoiceName: profile.baseVoiceName,
                    model: version.model,
                    settings: version.settings,
                    paceBoostWpm: version.paceBoostWpm,
                    changeNote: `Duplicated from ${profile.name} v${profile.currentVersion}.`,
                }),
            'Variant duplicated',
            'Could not duplicate the variant'
        )
    }

    const handleDeleteClick = (profile: VoiceProfile) => {
        setPendingDelete(profile)
        deleteDialog.open()
    }

    const handleConfirmDelete = async () => {
        if (!pendingDelete) return
        await toastAction(
            () => deleteProfile.mutateAsync(pendingDelete.id),
            'Variant deleted',
            'Could not delete the variant',
            // The backend refuses to delete an assigned variant with a 409
            // `library.in_use` — surface the API's message verbatim so the
            // operator sees why the delete was refused.
            inUseDeleteError('voice variant')
        )
        setPendingDelete(null)
    }

    const profiles = query.data?.profiles ?? []
    const filteredProfiles = profiles.filter((profile) => {
        // Archived items are hidden unless explicitly shown — the same rule as
        // the schedule pickers (active only) — so an archived item stays
        // reachable through the toggle + Unarchive action.
        if (profile.status === 'archived' && !showArchived) return false
        const haystack = [
            profile.name,
            profile.baseVoiceName,
            profile.status,
            ...(byProfileId.get(profile.id) ?? []),
        ]
            .join(' ')
            .toLowerCase()
        return haystack.includes(search.trim().toLowerCase())
    })
    // One CTA per state: hide the header action while the empty hero owns the
    // focal "New" button, so it isn't duplicated.
    const showHeaderAction =
        query.isLoading || query.isError || profiles.length > 0

    return (
        <PageContainer>
            <Header
                title="Voices"
                description="Named, tuned variants of a base voice"
            >
                {showHeaderAction && (
                    <Button onClick={openCreate} className="gap-2">
                        <ICON.add className="size-4" />
                        New variant
                    </Button>
                )}
            </Header>

            <DataBoundary
                query={query}
                isEmpty={(d) => d.profiles.length === 0}
                errorFallback="Could not load the voice library."
                loading={<CardGridSkeleton />}
                empty={
                    <Placeholder
                        icon={AudioLines}
                        message="No voice variants yet"
                        detail="Create a tuned variant of a base voice for stations to read with."
                        action={
                            <Button onClick={openCreate} className="gap-2">
                                <ICON.add className="size-4" />
                                New variant
                            </Button>
                        }
                    />
                }
            >
                <div className="mb-4 flex items-center gap-3">
                    <Input
                        value={search}
                        onChange={(event) => setSearch(event.target.value)}
                        placeholder="Search voice variants, base voices, or stations..."
                        aria-label="Search voice variants"
                        className="flex-1"
                    />
                    <div className="flex shrink-0 items-center gap-2">
                        <Switch
                            id="voices-show-archived"
                            checked={showArchived}
                            onCheckedChange={setShowArchived}
                        />
                        <Label
                            htmlFor="voices-show-archived"
                            className="cursor-pointer text-xs font-medium"
                        >
                            Show archived
                        </Label>
                    </div>
                    <ViewToggle value={view} onChange={setView} />
                </div>
                {filteredProfiles.length === 0 ? (
                    <Placeholder
                        icon={AudioLines}
                        message="No matching voice variants"
                        detail="Try a different variant, base voice, status, or station."
                    />
                ) : view === 'table' ? (
                    <VoiceProfileTable
                        profiles={filteredProfiles}
                        assignmentsByProfileId={byProfileId}
                        onView={openProfile}
                        onEdit={openProfile}
                        onDuplicate={handleDuplicate}
                        onDelete={handleDeleteClick}
                        onArchive={handleArchive}
                        onUnarchive={handleUnarchive}
                        canDelete={canDelete}
                    />
                ) : (
                    <div className={CARD_GRID}>
                        {filteredProfiles.map((profile) => (
                            <VoiceVariantCard
                                key={profile.id}
                                profile={profile}
                                assignedStations={
                                    byProfileId.get(profile.id) ?? []
                                }
                                onView={openProfile}
                                onEdit={openProfile}
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

            {workshopOpen && (
                <Suspense fallback={null}>
                    <VoiceStudio
                        open={workshopOpen}
                        onOpenChange={setWorkshopOpen}
                        profileId={activeProfileId}
                    />
                </Suspense>
            )}
            <ConfirmDialog
                open={deleteDialog.isOpen}
                onOpenChange={deleteDialog.onOpenChange}
                onConfirm={handleConfirmDelete}
                title="Delete voice variant"
                description={`Stations still pointing at ${pendingDelete?.name ?? 'this voice variant'} will need a replacement voice selected.`}
            />
        </PageContainer>
    )
}
