import { useMemo, useState } from 'react'
import { toast } from 'sonner'
import {
    AlertTriangle,
    Check,
    ChevronDown,
    ChevronRight,
    Eye,
    History,
    Loader2,
    MoreHorizontal,
    Plus,
    RotateCcw,
    Trash2,
} from 'lucide-react'
import { ApiError } from '@/lib/api/client'
import { cn } from '@/lib/utils'
import { formatRelativeTime } from '@/lib/formatting'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { RuleTab } from '@/components/RuleTab'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/dialog'
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { useConfirmDialog } from '@/hooks/use-confirm-dialog'
import {
    useSchedules,
    useUpdateSchedule,
} from '@/features/schedules/api/schedule-hooks'
import {
    useCreatePlaybook,
    useDefaultPlaybook,
    usePlaybook,
    usePlaybooks,
    usePreviewPlaybook,
    useRestorePlaybookVersion,
    useUpdatePlaybook,
} from '../api/playbook-hooks'
import type {
    BulletinPlaybook,
    PlaybookPreview,
    PlaybookSection,
} from '../types'
import {
    PRESETS,
    PROMPT_AGENTS,
    buildBlocks,
    buildOverlay,
    isReadOnlySection,
    labelForBlock,
    lockedBlockReason,
    statusFor,
    type EditableBlock,
    type Preset,
    type PromptAgent,
} from './writer-prompt/shared'
const EDITABLE_GROUPS: readonly {
    readonly section: PlaybookSection
    readonly title: string
}[] = [
    { section: 'role', title: 'Presenter' },
    { section: 'voice', title: 'Voice' },
    { section: 'length', title: 'Length' },
]

/** One rule as a compact card: title + state, body lives in the editor dialog. */
function BlockCard({
    block,
    onOpen,
}: {
    readonly block: EditableBlock
    readonly onOpen: () => void
}) {
    const state = statusFor(block)
    return (
        <button
            type="button"
            onClick={onOpen}
            className={cn(
                'flex w-full items-center gap-2 rounded-lg border px-3 py-2 text-left text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
                state === 'Default' &&
                    'border-border/60 bg-card/30 hover:border-primary/40',
                state === 'Edited' && 'border-primary/50 bg-primary/[0.04]',
                state === 'Off' &&
                    'border-border/50 bg-card/20 opacity-60 hover:opacity-90'
            )}
        >
            <span
                className={cn(
                    'size-1.5 shrink-0 rounded-full',
                    state === 'Default' && 'bg-muted-foreground/40',
                    state === 'Edited' && 'bg-primary',
                    state === 'Off' && 'bg-muted-foreground/30'
                )}
                aria-hidden
            />
            <span className="min-w-0 flex-1 truncate font-medium">
                {labelForBlock(block.id)}
            </span>
            {block.isAdded ? (
                <Badge variant="default" className="shrink-0">
                    New
                </Badge>
            ) : state !== 'Default' ? (
                <Badge
                    variant={state === 'Edited' ? 'default' : 'muted'}
                    className="shrink-0"
                >
                    {state}
                </Badge>
            ) : null}
        </button>
    )
}

export function WriterPromptManager({
    marketId,
}: {
    /** The page-level station picker's market; the tab never picks its own. */
    readonly marketId: string | null
}) {
    const schedulesQuery = useSchedules()
    const playbooksQuery = usePlaybooks()
    const updateSchedule = useUpdateSchedule()
    const schedulesData = schedulesQuery.data?.schedules
    const schedules = useMemo(() => schedulesData ?? [], [schedulesData])
    const playbooks = playbooksQuery.data?.playbooks ?? []
    const selectedSchedule = schedules.find(
        (schedule) => schedule.marketId === marketId
    )
    const selectedMarketId = selectedSchedule?.marketId ?? ''
    const selectedPlaybookId = selectedSchedule?.playbookId ?? null
    const selectedPlaybook = playbooks.find(
        (item) => item.id === selectedPlaybookId
    )
    const selectedPlaybookQuery = usePlaybook(selectedPlaybookId)
    const playbook = selectedPlaybookQuery.data ?? selectedPlaybook
    const [selectedRole, setSelectedRole] =
        useState<PromptAgent['role']>('compose2')
    const defaultQuery = useDefaultPlaybook(selectedRole)
    const create = useCreatePlaybook()
    const update = useUpdatePlaybook()
    const preview = usePreviewPlaybook()
    const restore = useRestorePlaybookVersion()
    const presetConfirm = useConfirmDialog()
    const resetConfirm = useConfirmDialog()
    const restoreConfirm = useConfirmDialog()
    const discardConfirm = useConfirmDialog()
    const [blocks, setBlocks] = useState<EditableBlock[]>([])
    const [hydratedKey, setHydratedKey] = useState('')
    const [dirty, setDirty] = useState(false)
    const [editingId, setEditingId] = useState<string | null>(null)
    const [draftSection, setDraftSection] = useState<PlaybookSection | null>(
        null
    )
    const [draftBody, setDraftBody] = useState('')
    const [previewOpen, setPreviewOpen] = useState(false)
    const [historyOpen, setHistoryOpen] = useState(false)
    const [pendingPreset, setPendingPreset] = useState<Preset | null>(null)
    const [pendingRestore, setPendingRestore] = useState<number | null>(null)
    const [refusals, setRefusals] = useState<string[]>([])

    const defaultBlocksData = defaultQuery.data?.blocks
    const defaultBlocks = useMemo(
        () => defaultBlocksData ?? [],
        [defaultBlocksData]
    )
    const nextHydrationKey = `${selectedMarketId}:${selectedPlaybookId ?? 'default'}:${selectedRole}:${defaultBlocks.length}:${playbook?.updatedAt ?? ''}`
    if (
        !defaultQuery.isLoading &&
        !selectedPlaybookQuery.isLoading &&
        nextHydrationKey !== hydratedKey
    ) {
        setBlocks(buildBlocks(defaultBlocks, playbook, selectedRole))
        setHydratedKey(nextHydrationKey)
        setDirty(false)
        setRefusals([])
        setEditingId(null)
        setDraftSection(null)
    }

    const editableBlocks = blocks.filter(
        (block) => !isReadOnlySection(block.section) && !block.locked
    )
    const protectedBlocks = blocks.filter(
        (block) => isReadOnlySection(block.section) || block.locked
    )
    const adjustmentCount = editableBlocks.filter(
        (block) =>
            block.isAdded || block.body !== block.originalBody || !block.enabled
    ).length
    const hasCustomisations = adjustmentCount > 0
    const editingBlock = editableBlocks.find((block) => block.id === editingId)

    const patchBlock = (id: string, patch: Partial<EditableBlock>) => {
        setBlocks((current) =>
            current.map((block) =>
                block.id === id ? { ...block, ...patch } : block
            )
        )
        setDirty(true)
    }
    const addDraftRule = () => {
        if (!draftSection || !draftBody.trim()) return
        const section = draftSection
        setBlocks((current) => [
            ...current,
            {
                id: `station.${section}.${Date.now()}`,
                section,
                body: draftBody.trim(),
                order: current.filter((block) => block.section === section)
                    .length,
                locked: false,
                enabled: true,
                originalBody: '',
                isAdded: true,
            },
        ])
        setDirty(true)
        setDraftSection(null)
        setDraftBody('')
    }
    const removeBlock = (id: string) => {
        setBlocks((current) => current.filter((block) => block.id !== id))
        setDirty(true)
        setEditingId(null)
    }
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
        setPendingPreset(null)
    }
    const choosePreset = (preset: Preset) => {
        if (hasCustomisations) {
            setPendingPreset(preset)
            presetConfirm.open()
        } else applyPreset(preset)
    }
    const resetAll = () => {
        setBlocks((current) =>
            current
                .filter((block) => !block.isAdded)
                .map((block) => ({
                    ...block,
                    body: block.originalBody,
                    enabled: true,
                }))
        )
        setDirty(true)
        setRefusals([])
    }
    const save = async () => {
        if (!selectedSchedule) return
        setRefusals([])
        try {
            const overlay = buildOverlay(
                blocks,
                selectedRole,
                playbook?.currentOverlay
            )
            const saved = selectedPlaybookId
                ? await update.mutateAsync({
                      id: selectedPlaybookId,
                      body: {
                          name:
                              playbook?.name ??
                              `${selectedSchedule.name ?? selectedSchedule.marketId} writer instructions`,
                          description: playbook?.description ?? null,
                          overlay,
                          changeNote: null,
                      },
                  })
                : await create.mutateAsync({
                      name: `${selectedSchedule.name ?? selectedSchedule.marketId} writer instructions`,
                      description: null,
                      role: selectedRole,
                      overlay,
                      changeNote: null,
                  })
            if (!selectedPlaybookId) {
                await updateSchedule.mutateAsync({
                    marketId: selectedSchedule.marketId,
                    schedule: { playbookId: saved.id },
                })
            }
            setDirty(false)
            toast.success('Writer instructions saved')
        } catch (error) {
            const body =
                error instanceof ApiError &&
                typeof error.body === 'object' &&
                error.body !== null
                    ? (error.body as { Refused?: unknown; Error?: unknown })
                    : null
            const refused = Array.isArray(body?.Refused)
                ? body.Refused.filter(
                      (item): item is string => typeof item === 'string'
                  )
                : []
            setRefusals(refused)
            toast.error(
                typeof body?.Error === 'string'
                    ? body.Error
                    : 'Could not save writer instructions'
            )
        }
    }
    const discard = () => {
        setBlocks(buildBlocks(defaultBlocks, playbook, selectedRole))
        setDirty(false)
        setRefusals([])
    }
    const previewDraft = async () => {
        if (!selectedMarketId) return
        setPreviewOpen(true)
        if (preview.isPending) return
        try {
            await preview.mutateAsync({
                role: selectedRole,
                market: selectedMarketId,
                overlay: buildOverlay(
                    blocks,
                    selectedRole,
                    playbook?.currentOverlay
                ),
            })
        } catch {
            // The preview dialog renders the friendly error state.
        }
    }
    const restoreVersion = async () => {
        if (!playbook || pendingRestore === null) return
        try {
            await restore.mutateAsync({
                id: playbook.id,
                version: pendingRestore,
            })
            toast.success(`Restored playbook v${pendingRestore}`)
            setPendingRestore(null)
            setHistoryOpen(false)
        } catch {
            toast.error('Could not restore that version')
        }
    }
    const loading =
        schedulesQuery.isLoading ||
        playbooksQuery.isLoading ||
        defaultQuery.isLoading ||
        selectedPlaybookQuery.isLoading
    const stationName =
        selectedSchedule?.station ?? selectedSchedule?.name ?? selectedMarketId
    const savePending =
        create.isPending || update.isPending || updateSchedule.isPending

    if (schedulesQuery.isLoading) {
        return (
            <RuleTab>
                <div className="space-y-3">
                    <Skeleton className="h-9 w-72" />
                    <Skeleton className="h-56 w-full" />
                </div>
            </RuleTab>
        )
    }
    if (!selectedSchedule) {
        return (
            <RuleTab>
                <div className="rounded-lg border border-dashed border-border/70 p-8 text-sm text-muted-foreground">
                    This station has no schedule yet. Add a station schedule
                    before editing writer instructions.
                </div>
            </RuleTab>
        )
    }

    return (
        <RuleTab>
            <section
                className="rounded-xl border border-border/70 bg-card/20 p-4"
                aria-labelledby="writer-editor-heading"
            >
                {/* ── One-line toolbar ────────────────────────────────── */}
                <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="flex min-w-0 flex-wrap items-baseline gap-x-2">
                        <h3
                            id="writer-editor-heading"
                            className="text-sm font-semibold"
                        >
                            How {stationName} reads
                        </h3>
                        <span className="text-xs text-muted-foreground">
                            {adjustmentCount > 0
                                ? `${adjustmentCount} adjustment${adjustmentCount === 1 ? '' : 's'}`
                                : 'ARN house style'}
                            {playbook?.currentVersion
                                ? ` · v${playbook.currentVersion}`
                                : ''}
                            {playbook?.updatedAt
                                ? ` · ${formatRelativeTime(playbook.updatedAt)}`
                                : ''}
                        </span>
                    </div>
                    <div className="flex items-center gap-2">
                        <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            className="h-8 gap-1.5"
                            onClick={() => void previewDraft()}
                            disabled={loading}
                        >
                            {preview.isPending ? (
                                <Loader2 className="size-3.5 animate-spin" />
                            ) : (
                                <Eye className="size-3.5" />
                            )}{' '}
                            Preview
                        </Button>
                        <DropdownMenu>
                            <DropdownMenuTrigger asChild>
                                <Button
                                    type="button"
                                    variant="ghost"
                                    size="icon"
                                    className="size-8"
                                    aria-label="Writer instructions actions"
                                >
                                    <MoreHorizontal className="size-4" />
                                </Button>
                            </DropdownMenuTrigger>
                            <DropdownMenuContent align="end">
                                <DropdownMenuItem
                                    onClick={() => setHistoryOpen(true)}
                                >
                                    <History className="size-4" />
                                    Version history
                                </DropdownMenuItem>
                                <DropdownMenuItem
                                    onClick={() => resetConfirm.open()}
                                >
                                    <RotateCcw className="size-4" />
                                    Reset all to the house style
                                </DropdownMenuItem>
                                <DropdownMenuSeparator />
                                <DropdownMenuLabel>
                                    Start from a preset
                                </DropdownMenuLabel>
                                {PRESETS.map((preset) => (
                                    <DropdownMenuItem
                                        key={preset.value}
                                        onClick={() => choosePreset(preset)}
                                    >
                                        {preset.label}
                                    </DropdownMenuItem>
                                ))}
                            </DropdownMenuContent>
                        </DropdownMenu>
                    </div>
                </div>
                {/* ── Agent selector tabs ─────────────────────────────── */}
                <div className="mt-4 grid gap-2 sm:grid-cols-3">
                    {PROMPT_AGENTS.map((agent) => {
                        const active = agent.role === selectedRole
                        return (
                            <button
                                key={agent.role}
                                type="button"
                                onClick={() => setSelectedRole(agent.role)}
                                className={cn(
                                    'flex flex-col gap-1 rounded-lg border p-3 text-left transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
                                    active
                                        ? 'border-primary/60 bg-primary/[0.08] shadow-xs'
                                        : 'border-border/60 bg-card/20 hover:border-border/90 hover:bg-card/40'
                                )}
                            >
                                <span className="flex items-center justify-between text-xs font-semibold">
                                    <span
                                        className={
                                            active
                                                ? 'text-primary'
                                                : 'text-foreground'
                                        }
                                    >
                                        {agent.title}
                                    </span>
                                    {active && (
                                        <Badge
                                            variant="default"
                                            className="text-3xs px-1.5 py-0"
                                        >
                                            Active
                                        </Badge>
                                    )}
                                </span>
                                <span className="text-2xs leading-relaxed text-muted-foreground line-clamp-2">
                                    {agent.blurb}
                                </span>
                            </button>
                        )
                    })}
                </div>

                {/* ── Category card grid ──────────────────────────────── */}
                <div className="mt-4 grid gap-4 md:grid-cols-3">
                    {EDITABLE_GROUPS.map((group) => {
                        const groupBlocks = editableBlocks.filter(
                            (block) => block.section === group.section
                        )
                        return (
                            <section
                                key={group.section}
                                aria-labelledby={`writer-group-${group.section}`}
                            >
                                <h4
                                    id={`writer-group-${group.section}`}
                                    className="mb-1.5 text-xs font-semibold uppercase tracking-wide text-muted-foreground"
                                >
                                    {group.title}
                                </h4>
                                <div className="space-y-1.5">
                                    {loading && groupBlocks.length === 0 ? (
                                        <Skeleton className="h-24 w-full" />
                                    ) : (
                                        groupBlocks.map((block) => (
                                            <BlockCard
                                                key={block.id}
                                                block={block}
                                                onOpen={() =>
                                                    setEditingId(block.id)
                                                }
                                            />
                                        ))
                                    )}
                                    <button
                                        type="button"
                                        onClick={() => {
                                            setDraftBody('')
                                            setDraftSection(group.section)
                                        }}
                                        className="flex w-full items-center gap-2 rounded-lg border border-dashed border-border/60 px-3 py-2 text-left text-sm text-muted-foreground transition-colors hover:border-primary/40 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                                    >
                                        <Plus
                                            className="size-3.5"
                                            aria-hidden
                                        />
                                        Add a rule
                                    </button>
                                </div>
                            </section>
                        )
                    })}
                </div>

                {/* ── Protected footer ────────────────────────────────── */}
                <ProtectedStrip blocks={protectedBlocks} />

                {/* ── Sticky save bar ─────────────────────────────────── */}
                {dirty && (
                    <div className="sticky bottom-2 z-10 mt-4 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-primary/30 bg-background/95 px-4 py-2.5 shadow-lg backdrop-blur-sm">
                        <div className="flex flex-wrap items-center gap-2 text-sm font-medium">
                            <span>Unsaved changes</span>
                            {refusals.map((refusal) => (
                                <Badge key={refusal} variant="destructive">
                                    {refusal}
                                </Badge>
                            ))}
                        </div>
                        <div className="flex gap-2">
                            <Button
                                type="button"
                                variant="ghost"
                                size="sm"
                                onClick={() => discardConfirm.open()}
                            >
                                Discard
                            </Button>
                            <Button
                                type="button"
                                size="sm"
                                onClick={() => void save()}
                                disabled={savePending}
                            >
                                {savePending && (
                                    <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                                )}
                                Save
                            </Button>
                        </div>
                    </div>
                )}
            </section>

            {/* ── Rule editor ─────────────────────────────────────────── */}
            <Dialog
                open={editingBlock !== undefined}
                onOpenChange={(open) => {
                    if (!open) setEditingId(null)
                }}
            >
                {editingBlock && (
                    <DialogContent className="sm:max-w-lg">
                        <DialogHeader>
                            <DialogTitle>
                                {labelForBlock(editingBlock.id)}
                            </DialogTitle>
                            <DialogDescription>
                                Changes apply to {stationName} only, once saved.
                            </DialogDescription>
                        </DialogHeader>
                        <Textarea
                            value={editingBlock.body}
                            onChange={(event) =>
                                patchBlock(editingBlock.id, {
                                    body: event.target.value,
                                })
                            }
                            className="min-h-32 text-sm"
                            aria-label={`Instruction for ${labelForBlock(editingBlock.id)}`}
                        />
                        <div className="flex items-center justify-between gap-3">
                            <div className="flex items-center gap-2">
                                <Switch
                                    id="writer-block-enabled"
                                    checked={editingBlock.enabled}
                                    onCheckedChange={(checked) =>
                                        patchBlock(editingBlock.id, {
                                            enabled: checked,
                                        })
                                    }
                                />
                                <Label
                                    htmlFor="writer-block-enabled"
                                    className="text-sm"
                                >
                                    Read on air
                                </Label>
                            </div>
                            <div className="flex gap-1">
                                {editingBlock.isAdded ? (
                                    <Button
                                        type="button"
                                        variant="ghost"
                                        size="sm"
                                        className="gap-1.5 text-destructive hover:text-destructive"
                                        onClick={() =>
                                            removeBlock(editingBlock.id)
                                        }
                                    >
                                        <Trash2 className="size-3.5" /> Remove
                                    </Button>
                                ) : (
                                    editingBlock.body !==
                                        editingBlock.originalBody && (
                                        <Button
                                            type="button"
                                            variant="ghost"
                                            size="sm"
                                            className="gap-1.5"
                                            onClick={() =>
                                                patchBlock(editingBlock.id, {
                                                    body: editingBlock.originalBody,
                                                })
                                            }
                                        >
                                            <RotateCcw className="size-3.5" />
                                            Reset to house style
                                        </Button>
                                    )
                                )}
                            </div>
                        </div>
                        <DialogFooter>
                            <Button
                                type="button"
                                size="sm"
                                onClick={() => setEditingId(null)}
                            >
                                Done
                            </Button>
                        </DialogFooter>
                    </DialogContent>
                )}
            </Dialog>

            {/* ── New rule ────────────────────────────────────────────── */}
            <Dialog
                open={draftSection !== null}
                onOpenChange={(open) => {
                    if (!open) setDraftSection(null)
                }}
            >
                <DialogContent className="sm:max-w-lg">
                    <DialogHeader>
                        <DialogTitle>
                            New{' '}
                            {EDITABLE_GROUPS.find(
                                (group) => group.section === draftSection
                            )?.title.toLowerCase()}{' '}
                            rule
                        </DialogTitle>
                        <DialogDescription>
                            One standing instruction, read on every{' '}
                            {stationName} bulletin once saved.
                        </DialogDescription>
                    </DialogHeader>
                    <Textarea
                        autoFocus
                        value={draftBody}
                        onChange={(event) => setDraftBody(event.target.value)}
                        placeholder="Write one standing instruction for this station"
                        className="min-h-28 text-sm"
                        aria-label="New rule"
                    />
                    <DialogFooter>
                        <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            onClick={() => setDraftSection(null)}
                        >
                            Cancel
                        </Button>
                        <Button
                            type="button"
                            size="sm"
                            onClick={addDraftRule}
                            disabled={!draftBody.trim()}
                        >
                            Add rule
                        </Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>

            {/* ── Preview ─────────────────────────────────────────────── */}
            <Dialog open={previewOpen} onOpenChange={setPreviewOpen}>
                <DialogContent className="sm:max-w-3xl">
                    <DialogHeader>
                        <DialogTitle className="flex items-center gap-2">
                            <Eye className="size-4 text-primary" aria-hidden />
                            Preview with today&apos;s data
                        </DialogTitle>
                        <DialogDescription>
                            A dry run against {stationName}&apos;s latest
                            bulletin. Nothing airs.
                        </DialogDescription>
                    </DialogHeader>
                    <PreviewBody preview={preview} />
                </DialogContent>
            </Dialog>

            {/* ── History ─────────────────────────────────────────────── */}
            <Dialog open={historyOpen} onOpenChange={setHistoryOpen}>
                <DialogContent className="sm:max-w-lg">
                    <DialogHeader>
                        <DialogTitle>Version history</DialogTitle>
                        <DialogDescription>
                            Restoring creates a new version, so the current edit
                            remains auditable.
                        </DialogDescription>
                    </DialogHeader>
                    <HistoryPane
                        playbook={playbook}
                        restorePending={restore.isPending}
                        onRestore={(version) => {
                            setPendingRestore(version)
                            restoreConfirm.open()
                        }}
                    />
                </DialogContent>
            </Dialog>

            <ConfirmDialog
                open={presetConfirm.isOpen}
                onOpenChange={(open) => {
                    presetConfirm.onOpenChange(open)
                    if (!open) setPendingPreset(null)
                }}
                onConfirm={() => {
                    if (pendingPreset) applyPreset(pendingPreset)
                }}
                title="Replace station adjustments?"
                description="This preset replaces the current edits and removes added rules. You can review it before saving."
                confirmLabel="Apply preset"
            />
            <ConfirmDialog
                open={resetConfirm.isOpen}
                onOpenChange={resetConfirm.onOpenChange}
                onConfirm={resetAll}
                title="Reset all to the house style?"
                description="All station edits and added rules will be cleared. Save the result to apply the reset."
                confirmLabel="Reset rules"
            />
            <ConfirmDialog
                open={restoreConfirm.isOpen}
                onOpenChange={(open) => {
                    restoreConfirm.onOpenChange(open)
                    if (!open) setPendingRestore(null)
                }}
                onConfirm={restoreVersion}
                title="Restore this version?"
                description="Restoring creates a new current version and keeps the change auditable."
                confirmLabel="Restore version"
            />
            <ConfirmDialog
                open={discardConfirm.isOpen}
                onOpenChange={discardConfirm.onOpenChange}
                onConfirm={discard}
                title="Discard unsaved changes?"
                description="Your station edits will be lost."
                confirmLabel="Discard changes"
            />
        </RuleTab>
    )
}

/** The locked platform rules as a one-line footer disclosure. */
function ProtectedStrip({
    blocks,
}: {
    readonly blocks: readonly EditableBlock[]
}) {
    const [expanded, setExpanded] = useState(false)
    return (
        <div className="mt-4 border-t border-border/60 pt-2.5">
            <button
                type="button"
                onClick={() => setExpanded((value) => !value)}
                className="flex w-full items-center gap-2 text-left text-xs text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                aria-expanded={expanded}
            >
                {expanded ? (
                    <ChevronDown className="size-3.5" aria-hidden />
                ) : (
                    <ChevronRight className="size-3.5" aria-hidden />
                )}
                <span className="font-medium text-foreground">
                    Protected by the platform
                </span>
                <span className="hidden sm:inline">
                    facts, values, running order, reply format
                </span>
            </button>
            {expanded && (
                <div className="mt-2 grid gap-1.5 pl-5 md:grid-cols-2">
                    {blocks.map((block) => (
                        <div
                            key={block.id}
                            className="rounded-md bg-muted/20 px-3 py-2"
                        >
                            <div className="text-xs font-medium text-muted-foreground">
                                {labelForBlock(block.id)}
                            </div>
                            <div className="mt-0.5 line-clamp-2 text-xs text-muted-foreground">
                                {block.body}
                            </div>
                            <div className="mt-0.5 text-[10px] text-muted-foreground">
                                {lockedBlockReason(block.id)}
                            </div>
                        </div>
                    ))}
                </div>
            )}
        </div>
    )
}

interface PreviewMutationState {
    readonly isPending: boolean
    readonly error: unknown
    readonly data: PlaybookPreview | undefined
}

function PreviewBody({ preview }: { readonly preview: PreviewMutationState }) {
    if (preview.isPending)
        return (
            <div className="flex items-center gap-2 rounded-lg bg-muted/30 p-4 text-sm text-muted-foreground">
                <Loader2 className="size-4 animate-spin" />
                Writing a test bulletin with your rules · about 30 seconds
            </div>
        )
    if (preview.error instanceof ApiError && preview.error.status === 404)
        return (
            <div className="rounded-lg border border-dashed border-border/70 bg-muted/20 p-4 text-sm text-muted-foreground">
                No recent bulletin for this station to preview against.
            </div>
        )
    if (preview.error)
        return (
            <div className="flex items-center gap-2 rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
                <AlertTriangle className="size-4" aria-hidden />
                Could not generate the preview. Try again.
            </div>
        )
    if (!preview.data) return null
    return (
        <div className="space-y-3">
            <div className="flex flex-wrap gap-2">
                {preview.data.predictedSeconds != null && (
                    <Badge variant="info">
                        {preview.data.predictedSeconds.toFixed(1)}s predicted
                    </Badge>
                )}
                {preview.data.refused.map((item) => (
                    <Badge key={item} variant="destructive">
                        {item}
                    </Badge>
                ))}
            </div>
            {preview.data.notes.length > 0 && (
                <ul className="list-disc space-y-1 pl-5 text-xs text-muted-foreground">
                    {preview.data.notes.map((note) => (
                        <li key={note}>{note}</li>
                    ))}
                </ul>
            )}
            <div className="grid gap-3 sm:grid-cols-2">
                {preview.data.baselineScript && (
                    <div>
                        <h5 className="mb-1 text-xs font-semibold text-muted-foreground">
                            Baseline
                        </h5>
                        <p className="rounded-lg border border-border/60 bg-muted/20 p-3 text-sm leading-relaxed text-muted-foreground">
                            {preview.data.baselineScript}
                        </p>
                    </div>
                )}
                <div>
                    <h5 className="mb-1 text-xs font-semibold text-muted-foreground">
                        With your rules
                    </h5>
                    <p className="rounded-lg border border-primary/30 bg-primary/[0.03] p-3 text-sm leading-relaxed">
                        {preview.data.script}
                    </p>
                </div>
            </div>
        </div>
    )
}

function HistoryPane({
    playbook,
    restorePending,
    onRestore,
}: {
    readonly playbook: BulletinPlaybook | undefined
    readonly restorePending: boolean
    readonly onRestore: (version: number) => void
}) {
    if (!playbook || playbook.versions.length === 0)
        return (
            <div className="rounded-lg border border-dashed border-border/60 p-8 text-center text-sm text-muted-foreground">
                <Check className="mx-auto mb-2 size-5" aria-hidden />
                No version history while this station uses the house style.
            </div>
        )
    return (
        <div className="max-h-80 space-y-2 overflow-y-auto">
            {[...playbook.versions].reverse().map((version) => (
                <div
                    key={version.version}
                    className="flex items-center justify-between gap-3 rounded-lg border border-border/60 px-3 py-2"
                >
                    <div className="min-w-0 text-xs">
                        <span className="font-medium">v{version.version}</span>
                        <span className="ml-2 text-muted-foreground">
                            {formatRelativeTime(version.createdAt)}
                            {version.createdBy ? ` · ${version.createdBy}` : ''}
                        </span>
                        {version.changeNote && (
                            <span className="ml-2 text-muted-foreground">
                                {version.changeNote}
                            </span>
                        )}
                    </div>
                    {version.version !== playbook.currentVersion && (
                        <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            disabled={restorePending}
                            onClick={() => onRestore(version.version)}
                        >
                            Restore
                        </Button>
                    )}
                </div>
            ))}
        </div>
    )
}
