import { useMemo, useState } from 'react'
import {
    AlertTriangle,
    BookOpen,
    Check,
    Eye,
    Loader2,
    Lock,
    Plus,
} from 'lucide-react'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import {
    StudioBody,
    StudioCanvas,
    StudioDialog,
    StudioFooter,
    StudioRail,
    type StudioStep,
} from '@/components/studio/Studio'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Separator } from '@/components/ui/separator'
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from '@/components/ui/select'
import { ApiError } from '@/lib/api/client'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import { Textarea } from '@/components/ui/textarea'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'
import {
    useCreatePlaybook,
    useDefaultPlaybook,
    usePlaybook,
    usePreviewPlaybook,
    useRestorePlaybookVersion,
    useUpdatePlaybook,
} from '../../api/playbook-hooks'
import {
    PLAYBOOK_SECTION_LABELS,
    PLAYBOOK_SECTIONS,
    type BulletinPlaybook,
    type PlaybookSection,
} from '../../types'
import {
    PRESETS,
    SectionBlock,
    buildBlocks,
    buildOverlay,
    isReadOnlySection,
    lockedSectionReason,
    type EditableBlock,
    type Preset,
} from '../writer-prompt/shared'

interface PlaybookStudioProps {
    readonly open: boolean
    readonly onOpenChange: (open: boolean) => void
    readonly playbookId: string | null
    /** Create-mode prefill: playbook name (Writer prompt tab passes the station). */
    readonly initialName?: string
    /** Preferred preview market; falls back to the first schedule. */
    readonly initialMarket?: string
    /** Called with the saved playbook so callers can assign it to a schedule. */
    readonly onSaved?: (playbook: BulletinPlaybook) => void
}
const STEPS: readonly StudioStep[] = [
    { id: 'instructions', label: 'Instructions', icon: BookOpen },
]

const ROLE_OPTIONS: readonly {
    readonly value: string
    readonly label: string
}[] = [
    { value: 'compose2', label: 'Compose 2' },
    { value: 'writer', label: 'Writer' },
]

export function Studio({
    open,
    onOpenChange,
    playbookId,
    initialName,
    initialMarket,
    onSaved,
}: PlaybookStudioProps) {
    const isCreate = playbookId === null
    const playbookQuery = usePlaybook(playbookId)
    const [role, setRole] = useState('compose2')
    const defaultRole = isCreate ? role : (playbookQuery.data?.role ?? 'writer')
    const defaultQuery = useDefaultPlaybook(defaultRole, open)
    const schedulesQuery = useSchedules({
        enabled: open,
        refreshInterval: 60_000,
    })
    const create = useCreatePlaybook()
    const update = useUpdatePlaybook()
    const preview = usePreviewPlaybook()
    const restore = useRestorePlaybookVersion()
    const discard = useConfirmDialog()
    const presetConfirm = useConfirmDialog()
    const [name, setName] = useState('')
    const [description, setDescription] = useState('')
    const [blocks, setBlocks] = useState<EditableBlock[]>([])
    const [newSection, setNewSection] = useState<PlaybookSection>('role')
    const [newBody, setNewBody] = useState('')
    const [selectedMarket, setSelectedMarket] = useState('')
    const [pendingPreset, setPendingPreset] = useState<Preset | null>(null)
    const [dirty, setDirty] = useState(false)

    const defaults = defaultQuery.data?.blocks ?? []
    const playbook = playbookQuery.data
    const markets = schedulesQuery.data?.schedules ?? []
    // Derived, never synced: the user's explicit pick wins when it still exists,
    // then the caller's preferred market, then the first schedule.
    const marketExists = (id: string | undefined): id is string =>
        id !== undefined &&
        id !== '' &&
        markets.some((schedule) => schedule.marketId === id)
    const effectiveMarket = marketExists(selectedMarket)
        ? selectedMarket
        : marketExists(initialMarket)
          ? initialMarket
          : (markets[0]?.marketId ?? '')

    const hydrationKey =
        !open || (isCreate ? defaultQuery.isLoading : playbookQuery.isLoading)
            ? null
            : `${playbook?.id ?? 'new'}:${defaultRole}:${defaults.length}`
    const [hydratedFor, setHydratedFor] = useState<string | null>(null)
    if (hydrationKey !== null && hydrationKey !== hydratedFor) {
        setHydratedFor(hydrationKey)
        setName(playbook?.name ?? initialName ?? '')
        setDescription(playbook?.description ?? '')
        setRole(playbook?.role ?? role)
        setBlocks(buildBlocks(defaults, playbook))
        setDirty(false)
    }

    const grouped = useMemo(
        () =>
            PLAYBOOK_SECTIONS.map((section) => ({
                section,
                blocks: blocks.filter((block) => block.section === section),
            })),
        [blocks]
    )
    const applyPreset = (preset: Preset) => {
        setBlocks((current) =>
            current
                .filter((block) => !block.isAdded)
                .map((block) => ({
                    ...block,
                    body: preset.rewrites[block.id] ?? block.originalBody,
                    enabled: true,
                }))
        )
        setDirty(true)
    }
    const choosePreset = (value: string) => {
        const preset = PRESETS.find((item) => item.value === value)
        if (!preset) return
        const hasCustomisations = blocks.some(
            (block) =>
                block.isAdded ||
                (!block.locked &&
                    (block.body !== block.originalBody || !block.enabled))
        )
        if (hasCustomisations) {
            setPendingPreset(preset)
            presetConfirm.open()
        } else {
            applyPreset(preset)
        }
    }

    const patchBlock = (id: string, patch: Partial<EditableBlock>) => {
        setBlocks((current) =>
            current.map((block) =>
                block.id === id ? { ...block, ...patch } : block
            )
        )
        setDirty(true)
    }

    const addBlock = () => {
        if (isReadOnlySection(newSection)) return
        const body = newBody.trim()
        if (!body) return
        const sectionBlocks = blocks.filter(
            (block) => block.section === newSection
        )
        const block: EditableBlock = {
            id: `station.${newSection}.${Date.now()}`,
            section: newSection,
            body,
            order: sectionBlocks.length,
            locked: false,
            enabled: true,
            originalBody: '',
            isAdded: true,
        }
        setBlocks((current) => [...current, block])
        setNewBody('')
        setDirty(true)
    }

    const removeBlock = (id: string) => {
        setBlocks((current) => current.filter((block) => block.id !== id))
        setDirty(true)
    }

    const close = () => {
        if (dirty) {
            discard.open()
            return
        }
        onOpenChange(false)
    }

    const save = async () => {
        const trimmedName = name.trim()
        if (!trimmedName) {
            toast.error('A playbook name is required')
            return
        }
        const overlay = buildOverlay(blocks)
        try {
            const saved = isCreate
                ? await create.mutateAsync({
                      name: trimmedName,
                      description: description.trim() || null,
                      role,
                      overlay,
                      changeNote: null,
                  })
                : await update.mutateAsync({
                      id: playbookId,
                      body: {
                          name: trimmedName,
                          description: description.trim() || null,
                          overlay,
                          changeNote: null,
                      },
                  })
            onSaved?.(saved)
            toast.success(isCreate ? 'Playbook created' : 'Playbook updated')
            setDirty(false)
            onOpenChange(false)
        } catch (error) {
            const body =
                error instanceof ApiError &&
                typeof error.body === 'object' &&
                error.body !== null
                    ? (error.body as { Refused?: unknown })
                    : null
            const refused = Array.isArray(body?.Refused)
                ? body.Refused.filter(
                      (value): value is string => typeof value === 'string'
                  )
                : []
            toast.error(
                isCreate
                    ? 'Could not create the playbook'
                    : 'Could not update the playbook',
                refused.length > 0
                    ? { description: refused.join(' • ') }
                    : undefined
            )
        }
    }
    const previewDraft = async () => {
        if (!effectiveMarket) {
            toast.error('Choose a market before previewing')
            return
        }
        try {
            await preview.mutateAsync({
                role: defaultRole,
                market: effectiveMarket,
                overlay: buildOverlay(blocks),
            })
        } catch {
            // The pane renders the query error state, including a 404 empty state.
        }
    }
    const handleRestore = async (version: number) => {
        if (!playbook) return
        try {
            await restore.mutateAsync({ id: playbook.id, version })
            toast.success(`Restored playbook v${version}`)
        } catch {
            toast.error('Could not restore that version')
        }
    }

    const loading =
        defaultQuery.isLoading || (!isCreate && playbookQuery.isLoading)
    const unavailable =
        !loading &&
        !defaultQuery.isError &&
        !defaultQuery.data?.defaultAvailable
    const loadError = defaultQuery.isError || playbookQuery.isError
    const saving = create.isPending || update.isPending

    return (
        <StudioDialog
            open={open}
            onOpenChange={(next) => (next ? onOpenChange(true) : close())}
            title={
                isCreate
                    ? 'New bulletin playbook'
                    : `Edit ${playbook?.name ?? 'playbook'}`
            }
            subject="Playbook"
            description="Edit only the station-specific instructions. The shipped defaults remain the source of truth."
        >
            <StudioBody>
                <StudioRail
                    title="Playbook"
                    steps={STEPS}
                    activeId="instructions"
                    onSelect={() => undefined}
                />
                <StudioCanvas stepKey="instructions">
                    <div className="space-y-6">
                        <div className="grid gap-4 md:grid-cols-3">
                            <div className="space-y-2">
                                <Label htmlFor="playbook-name">Name</Label>
                                <Input
                                    id="playbook-name"
                                    value={name}
                                    maxLength={120}
                                    onChange={(event) => {
                                        setName(event.target.value)
                                        setDirty(true)
                                    }}
                                    placeholder="Morning writer"
                                />
                            </div>
                            <div className="space-y-2">
                                <Label htmlFor="playbook-description">
                                    Description
                                </Label>
                                <Input
                                    id="playbook-description"
                                    value={description}
                                    maxLength={1000}
                                    onChange={(event) => {
                                        setDescription(event.target.value)
                                        setDirty(true)
                                    }}
                                    placeholder="Station-specific writer direction"
                                />
                            </div>
                            <div className="space-y-2">
                                <Label htmlFor="playbook-role">Role</Label>
                                <Select
                                    value={role}
                                    disabled={!isCreate}
                                    onValueChange={(value) => {
                                        setRole(value)
                                        setDirty(true)
                                    }}
                                >
                                    <SelectTrigger
                                        id="playbook-role"
                                        aria-label="Playbook role"
                                    >
                                        <SelectValue />
                                    </SelectTrigger>
                                    <SelectContent>
                                        {ROLE_OPTIONS.map((option) => (
                                            <SelectItem
                                                key={option.value}
                                                value={option.value}
                                            >
                                                {option.label}
                                                {option.value === 'writer' && (
                                                    <span className="ml-2 text-xs text-muted-foreground">
                                                        not in use
                                                    </span>
                                                )}
                                            </SelectItem>
                                        ))}
                                    </SelectContent>
                                </Select>
                            </div>
                        </div>
                        <Separator />
                        {loading ? (
                            <div className="flex items-center gap-2 py-10 text-sm text-muted-foreground">
                                <Loader2 className="size-4 animate-spin" />
                                Loading shipped {defaultRole} instructions...
                            </div>
                        ) : loadError ? (
                            <div className="rounded-xl border border-destructive/40 bg-destructive/5 p-6 text-sm text-destructive">
                                Could not load the {defaultRole} playbook. Close
                                and try again.
                            </div>
                        ) : unavailable ? (
                            <div className="rounded-xl border border-dashed border-border/70 bg-muted/20 p-6 text-sm text-muted-foreground">
                                <p className="font-medium text-foreground">
                                    The shipped {defaultRole} playbook is
                                    unavailable
                                </p>
                                <p className="mt-1">
                                    No default blocks were published for the{' '}
                                    {defaultRole} role, so there is nothing to
                                    edit yet. You can still save a named
                                    playbook and add your own block below.
                                </p>
                            </div>
                        ) : (
                            <div className="space-y-7">
                                <div className="flex flex-wrap items-center justify-between gap-2">
                                    <div>
                                        <h2 className="text-sm font-semibold">
                                            Shipped {defaultRole} instructions
                                        </h2>
                                        <p className="text-xs text-muted-foreground">
                                            Only changed blocks are sent as a
                                            PlaybookOverlay.
                                        </p>
                                    </div>
                                    <div className="flex items-center gap-3 text-xs text-muted-foreground">
                                        <span className="inline-flex items-center gap-1">
                                            <Check className="size-3.5" />{' '}
                                            Default
                                        </span>
                                        <span>Edited</span>
                                        <span>Off</span>
                                    </div>
                                </div>
                                <div className="flex flex-wrap items-end justify-between gap-3 rounded-lg border border-border/60 bg-muted/10 p-3">
                                    <div className="space-y-1">
                                        <Label htmlFor="playbook-preset">
                                            Start from
                                        </Label>
                                        <Select onValueChange={choosePreset}>
                                            <SelectTrigger
                                                id="playbook-preset"
                                                className="w-52"
                                                aria-label="Start from preset"
                                            >
                                                <SelectValue placeholder="Choose a preset" />
                                            </SelectTrigger>
                                            <SelectContent>
                                                {PRESETS.map((preset) => (
                                                    <SelectItem
                                                        key={preset.value}
                                                        value={preset.value}
                                                    >
                                                        {preset.label}
                                                    </SelectItem>
                                                ))}
                                            </SelectContent>
                                        </Select>
                                    </div>
                                    <div className="flex flex-wrap items-end gap-2">
                                        <div className="space-y-1">
                                            <Label htmlFor="playbook-preview-market">
                                                Market
                                            </Label>
                                            <Select
                                                value={effectiveMarket}
                                                onValueChange={
                                                    setSelectedMarket
                                                }
                                            >
                                                <SelectTrigger
                                                    id="playbook-preview-market"
                                                    className="w-48"
                                                    aria-label="Preview market"
                                                >
                                                    <SelectValue placeholder="Choose a market" />
                                                </SelectTrigger>
                                                <SelectContent>
                                                    {markets.map((market) => (
                                                        <SelectItem
                                                            key={
                                                                market.marketId
                                                            }
                                                            value={
                                                                market.marketId
                                                            }
                                                        >
                                                            {market.name ??
                                                                market.marketId}
                                                        </SelectItem>
                                                    ))}
                                                </SelectContent>
                                            </Select>
                                        </div>
                                        <Button
                                            type="button"
                                            variant="outline"
                                            className="gap-2"
                                            disabled={
                                                preview.isPending ||
                                                !effectiveMarket ||
                                                loading
                                            }
                                            onClick={() => void previewDraft()}
                                        >
                                            {preview.isPending ? (
                                                <Loader2 className="size-4 animate-spin" />
                                            ) : (
                                                <Eye className="size-4" />
                                            )}
                                            Preview with recent data
                                        </Button>
                                    </div>
                                </div>
                                {grouped.map(
                                    ({ section, blocks: sectionBlocks }) => (
                                        <section
                                            key={section}
                                            className="space-y-3"
                                            aria-labelledby={`section-${section}`}
                                        >
                                            <div className="flex items-start justify-between gap-3">
                                                <div>
                                                    <h3
                                                        id={`section-${section}`}
                                                        className="text-sm font-semibold"
                                                    >
                                                        {isReadOnlySection(
                                                            section
                                                        ) && (
                                                            <Lock
                                                                className="mr-1.5 inline size-3.5 text-muted-foreground"
                                                                aria-hidden
                                                            />
                                                        )}
                                                        {
                                                            PLAYBOOK_SECTION_LABELS[
                                                                section
                                                            ]
                                                        }
                                                    </h3>
                                                    {isReadOnlySection(
                                                        section
                                                    ) && (
                                                        <p className="mt-1 text-xs text-muted-foreground">
                                                            {lockedSectionReason(
                                                                section
                                                            )}
                                                        </p>
                                                    )}
                                                </div>
                                                <span className="text-xs text-muted-foreground">
                                                    {sectionBlocks.length} block
                                                    {sectionBlocks.length === 1
                                                        ? ''
                                                        : 's'}
                                                </span>
                                            </div>
                                            {sectionBlocks.length === 0 ? (
                                                <p className="rounded-lg border border-dashed border-border/60 p-3 text-xs text-muted-foreground">
                                                    No blocks in this section.
                                                </p>
                                            ) : (
                                                sectionBlocks.map((block) => (
                                                    <SectionBlock
                                                        key={block.id}
                                                        block={block}
                                                        onChange={(patch) =>
                                                            patchBlock(
                                                                block.id,
                                                                patch
                                                            )
                                                        }
                                                        onRemove={() =>
                                                            removeBlock(
                                                                block.id
                                                            )
                                                        }
                                                    />
                                                ))
                                            )}
                                        </section>
                                    )
                                )}
                            </div>
                        )}
                        {(preview.isPending ||
                            preview.data ||
                            preview.error) && (
                            <section
                                className="space-y-3 rounded-xl border border-border/70 bg-card/40 p-4"
                                aria-labelledby="playbook-preview-heading"
                            >
                                <div className="flex items-start justify-between gap-3">
                                    <div>
                                        <h2
                                            id="playbook-preview-heading"
                                            className="flex items-center gap-2 text-sm font-semibold"
                                        >
                                            <Eye
                                                className="size-4 text-primary"
                                                aria-hidden
                                            />
                                            Preview
                                        </h2>
                                        <p className="mt-1 text-xs text-muted-foreground">
                                            A dry run against the most recent
                                            bulletin for this market.
                                        </p>
                                    </div>
                                    {preview.data?.predictedSeconds != null && (
                                        <Badge variant="info">
                                            {preview.data.predictedSeconds.toFixed(
                                                1
                                            )}
                                            s predicted
                                        </Badge>
                                    )}
                                </div>
                                {preview.isPending ? (
                                    <div className="flex items-center gap-2 rounded-lg bg-muted/30 p-4 text-sm text-muted-foreground">
                                        <Loader2 className="size-4 animate-spin" />
                                        Writing a test bulletin with your rules
                                        - about 30 seconds
                                    </div>
                                ) : preview.error instanceof ApiError &&
                                  preview.error.status === 404 ? (
                                    <div className="rounded-lg border border-dashed border-border/70 bg-muted/20 p-4 text-sm text-muted-foreground">
                                        There is no recent bulletin for this
                                        market to preview yet. Run a bulletin,
                                        then try again.
                                    </div>
                                ) : preview.error ? (
                                    <div className="flex items-center gap-2 rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
                                        <AlertTriangle
                                            className="size-4"
                                            aria-hidden
                                        />
                                        Could not generate the preview. Try
                                        again.
                                    </div>
                                ) : preview.data ? (
                                    <>
                                        {preview.data.refused.length > 0 && (
                                            <div className="flex flex-wrap items-center gap-2">
                                                <span className="text-xs font-medium text-warning">
                                                    Refused
                                                </span>
                                                {preview.data.refused.map(
                                                    (item) => (
                                                        <Badge
                                                            key={item}
                                                            variant="warning"
                                                        >
                                                            {item}
                                                        </Badge>
                                                    )
                                                )}
                                            </div>
                                        )}
                                        {preview.data.notes.length > 0 && (
                                            <ul className="list-disc space-y-1 pl-5 text-xs text-muted-foreground">
                                                {preview.data.notes.map(
                                                    (note) => (
                                                        <li key={note}>
                                                            {note}
                                                        </li>
                                                    )
                                                )}
                                            </ul>
                                        )}
                                        <div className="grid gap-3 md:grid-cols-2">
                                            {preview.data.baselineScript !==
                                                null && (
                                                <div className="space-y-1">
                                                    <h3 className="text-xs font-semibold text-muted-foreground">
                                                        Baseline
                                                    </h3>
                                                    <p className="rounded-lg border border-border/60 bg-muted/20 p-3 text-sm leading-relaxed text-muted-foreground">
                                                        {
                                                            preview.data
                                                                .baselineScript
                                                        }
                                                    </p>
                                                </div>
                                            )}
                                            <div className="space-y-1">
                                                <h3 className="text-xs font-semibold text-muted-foreground">
                                                    Preview with your rules
                                                </h3>
                                                <p className="rounded-lg border border-primary/30 bg-primary/[0.03] p-3 text-sm leading-relaxed text-foreground">
                                                    {preview.data.script}
                                                </p>
                                            </div>
                                        </div>
                                    </>
                                ) : null}
                            </section>
                        )}
                        <Separator />
                        <section
                            className="space-y-3"
                            aria-labelledby="add-playbook-block"
                        >
                            <div>
                                <h2
                                    id="add-playbook-block"
                                    className="text-sm font-semibold"
                                >
                                    Add your own block
                                </h2>
                                <p className="text-xs text-muted-foreground">
                                    Added blocks are included in the overlay and
                                    survive shipped prompt updates.
                                </p>
                            </div>
                            <div className="grid gap-3 md:grid-cols-[180px_1fr_auto] md:items-end">
                                <div className="space-y-2">
                                    <Label htmlFor="new-playbook-section">
                                        Section
                                    </Label>
                                    <Select
                                        value={newSection}
                                        onValueChange={(value) =>
                                            setNewSection(
                                                value as PlaybookSection
                                            )
                                        }
                                    >
                                        <SelectTrigger
                                            id="new-playbook-section"
                                            aria-label="Section"
                                        >
                                            <SelectValue />
                                        </SelectTrigger>
                                        <SelectContent>
                                            {PLAYBOOK_SECTIONS.filter(
                                                (section) =>
                                                    !isReadOnlySection(section)
                                            ).map((section) => (
                                                <SelectItem
                                                    key={section}
                                                    value={section}
                                                >
                                                    {
                                                        PLAYBOOK_SECTION_LABELS[
                                                            section
                                                        ]
                                                    }
                                                </SelectItem>
                                            ))}
                                        </SelectContent>
                                    </Select>
                                </div>
                                <div className="space-y-2">
                                    <Label htmlFor="new-playbook-body">
                                        Instruction
                                    </Label>
                                    <Textarea
                                        id="new-playbook-body"
                                        value={newBody}
                                        onChange={(event) =>
                                            setNewBody(event.target.value)
                                        }
                                        placeholder="For example: name the station's priority corridor first."
                                        className="min-h-10"
                                    />
                                </div>
                                <Button
                                    type="button"
                                    variant="outline"
                                    className="gap-2"
                                    onClick={addBlock}
                                    disabled={!newBody.trim()}
                                >
                                    <Plus className="size-4" /> Add block
                                </Button>
                            </div>
                        </section>
                        {playbook && playbook.versions.length > 0 && (
                            <section
                                className="space-y-3"
                                aria-labelledby="playbook-history"
                            >
                                <Separator />
                                <div>
                                    <h2
                                        id="playbook-history"
                                        className="text-sm font-semibold"
                                    >
                                        Version history
                                    </h2>
                                    <p className="text-xs text-muted-foreground">
                                        Restoring creates a new version, so the
                                        current edit remains auditable.
                                    </p>
                                </div>
                                <div className="space-y-2">
                                    {[...playbook.versions]
                                        .reverse()
                                        .map((version) => (
                                            <div
                                                key={version.version}
                                                className="flex items-center justify-between gap-3 rounded-lg border border-border/60 px-3 py-2"
                                            >
                                                <div className="min-w-0 text-xs">
                                                    <span className="font-medium">
                                                        v{version.version}
                                                    </span>
                                                    {version.changeNote && (
                                                        <span className="ml-2 text-muted-foreground">
                                                            {version.changeNote}
                                                        </span>
                                                    )}
                                                </div>
                                                {version.version !==
                                                    playbook.currentVersion && (
                                                    <Button
                                                        type="button"
                                                        variant="ghost"
                                                        size="sm"
                                                        disabled={
                                                            restore.isPending
                                                        }
                                                        onClick={() =>
                                                            void handleRestore(
                                                                version.version
                                                            )
                                                        }
                                                    >
                                                        Restore
                                                    </Button>
                                                )}
                                            </div>
                                        ))}
                                </div>
                            </section>
                        )}
                    </div>
                </StudioCanvas>
            </StudioBody>
            <StudioFooter>
                <div className="flex w-full items-center justify-between gap-3">
                    <p className="text-xs text-muted-foreground">
                        {dirty ? 'Unsaved changes' : 'No unsaved changes'}
                    </p>
                    <div className="flex gap-2">
                        <Button type="button" variant="ghost" onClick={close}>
                            Cancel
                        </Button>
                        <Button
                            type="button"
                            onClick={() => void save()}
                            disabled={saving || loading}
                            className="gap-2"
                        >
                            {saving && (
                                <Loader2 className="size-4 animate-spin" />
                            )}{' '}
                            Save playbook
                        </Button>
                    </div>
                </div>
            </StudioFooter>
            <ConfirmDialog
                open={presetConfirm.isOpen}
                onOpenChange={(next) => {
                    presetConfirm.onOpenChange(next)
                    if (!next) setPendingPreset(null)
                }}
                onConfirm={() => {
                    if (pendingPreset) applyPreset(pendingPreset)
                    setPendingPreset(null)
                }}
                title="Replace playbook customisations?"
                description="This preset will replace the current block edits and remove added blocks. You can review the result before saving."
                confirmLabel="Apply preset"
            />
            <ConfirmDialog
                open={discard.isOpen}
                onOpenChange={discard.onOpenChange}
                onConfirm={() => onOpenChange(false)}
                title="Discard playbook changes?"
                description="Your unsaved edits will be lost."
                confirmLabel="Discard changes"
            />
        </StudioDialog>
    )
}

export const PlaybookStudio = Studio
