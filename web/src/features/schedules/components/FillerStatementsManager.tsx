import { useMemo, useState } from 'react'
import { toast } from 'sonner'
import { EmptyState } from '@/components/EmptyState'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ComposerField } from '@/components/ComposerField'
import { RuleRow } from '@/components/RuleRow'
import { RuleTab } from '@/components/RuleTab'
import { ScopedActivityHistory } from '@/features/activity/components/ScopedActivityHistory'
import { RuleComposer } from '@/components/RuleComposer'
import { RuleToolbar } from '@/components/RuleToolbar'
import {
    ScopeGroupedList,
    type ScopeGroup,
} from '@/components/ScopeGroupedList'
import {
    Dialog,
    DialogContent,
    DialogHeader,
    DialogTitle,
    DialogDescription,
    DialogFooter,
} from '@/components/ui/dialog'
import { ICON } from '@/lib/design/icons'
import { cn } from '@/lib/utils'
import {
    useAllFillerStatements,
    useSetFillerStatements,
    FILLER_STATEMENTS_MAX,
    FILLER_STATEMENT_MAX_LENGTH,
    type FillerStatementEntry,
    type FillerAvailabilityEntry,
} from '../api/filler-statements-hooks'
import {
    availabilityWindowError,
    getStationLocalNow,
    isEligibleNow,
    summariseAvailability,
} from '../lib/filler-availability'
import { useIsAdmin } from '@/hooks/use-current-user'
import { extractTokens } from '@/features/bookends/lib/tokens'

const AddIcon = ICON.add
const EditIcon = ICON.edit
const ResetIcon = ICON.reset
const ClockIcon = ICON.time
const WarningIcon = ICON.warning

interface MarketOption {
    readonly marketId: string
    readonly label: string
    /** Station IANA timezone — the availability windows are evaluated in station-local
     *  time (UTC fallback, exactly like the backend), so the warning below agrees with
     *  what would actually air. */
    readonly timezone?: string | null
}

interface FillerRow {
    readonly marketId: string
    readonly entry: FillerStatementEntry
}

const EMPTY_AVAILABILITY: FillerAvailabilityEntry = {
    startDate: null,
    endDate: null,
    days: null,
    startLocal: null,
    endLocal: null,
}

const DAY_BUTTONS: readonly {
    readonly name: string
    readonly short: string
}[] = [
    { name: 'Monday', short: 'Mon' },
    { name: 'Tuesday', short: 'Tue' },
    { name: 'Wednesday', short: 'Wed' },
    { name: 'Thursday', short: 'Thu' },
    { name: 'Friday', short: 'Fri' },
    { name: 'Saturday', short: 'Sat' },
    { name: 'Sunday', short: 'Sun' },
]

/** True when the draft constrains nothing — saving it should store null (always eligible). */
const isEmptyAvailability = (draft: FillerAvailabilityEntry): boolean =>
    !draft.startDate &&
    !draft.endDate &&
    !draft.days?.length &&
    !draft.startLocal &&
    !draft.endLocal

/**
 * Per-market filler-statement manager, hosted in the Bulletin-rules hub. A filler
 * statement provides editorial padding text that the deterministic filler selector
 * can use when traffic/weather/fuel/minor-traffic content still leaves room in a
 * bulletin slot. Selection is costed from the statement's word count and voice pace.
 *
 * A statement may carry an optional availability window (date range, days of week,
 * time of day, all in station-local time). The rule is STRICT: a statement outside
 * its window never airs and nothing silently substitutes — so the list warns when
 * every enabled statement in a market is currently ineligible.
 */
export function FillerStatementsManager({
    markets,
    station,
}: {
    readonly markets: readonly MarketOption[]
    readonly station: string
}) {
    const marketIds = useMemo(() => markets.map((m) => m.marketId), [markets])
    const { data: inventory, isLoading } = useAllFillerStatements(marketIds)
    const save = useSetFillerStatements()
    const isAdmin = useIsAdmin()
    const [composerOpen, setComposerOpen] = useState(false)
    // The statement whose TEXT is being edited. Non-null puts the shared composer in
    // edit mode against that row's market; null means the composer creates a new one.
    const [editing, setEditing] = useState<FillerRow | null>(null)
    const composing = composerOpen || editing !== null
    const scope = editing
        ? editing.marketId
        : station || (markets[0]?.marketId ?? '')
    const [text, setText] = useState('')

    const [search, setSearch] = useState('')
    // The statement whose availability WINDOW is being edited (dialog) — a separate flow
    // from `editing`, which is the text composer.
    const [scheduling, setScheduling] = useState<FillerRow | null>(null)
    const [draft, setDraft] = useState<FillerAvailabilityEntry | null>(null)

    const trimmed = text.trim()
    // Mirrors FillerStatementsHttpTrigger: filler is appended VERBATIM — no token
    // substitution exists for it — so bookend-style {token} syntax would be read
    // aloud as literal braces on air. The backend rejects it; block it here first.
    const tokenProblem = (() => {
        const tokens = extractTokens(trimmed)
        if (tokens.length === 0) return null
        return `Filler text cannot use bookend token syntax ({${tokens.join(', ')}}). Tokens are only substituted in bookends. Remove the braces from this statement.`
    })()
    // Saving an edit that changed nothing would evict nothing and store nothing, so an
    // untouched edit keeps the action disabled rather than firing a no-op PUT.
    const unchanged = editing !== null && trimmed === editing.entry.text
    const valid =
        trimmed.length > 0 &&
        trimmed.length <= FILLER_STATEMENT_MAX_LENGTH &&
        !tokenProblem &&
        !unchanged &&
        scope !== ''
    const submitting = save.isPending

    const statementsFor = (marketId: string): readonly FillerStatementEntry[] =>
        inventory?.find((m) => m.marketId === marketId)?.statements ?? []

    const labelFor = (marketId: string) =>
        markets.find((m) => m.marketId === marketId)?.label ?? marketId

    const active = search.trim() !== ''

    const groups = useMemo<ScopeGroup<FillerRow>[]>(() => {
        const q = search.trim().toLowerCase()
        return (inventory ?? [])
            .filter((m) => m.marketId === station)
            .map((m) => ({
                key: m.marketId,
                label: labelFor(m.marketId),
                items: m.statements
                    .filter((s) => !q || s.text.toLowerCase().includes(q))
                    .map((s) => ({ marketId: m.marketId, entry: s })),
            }))
            .filter((g) => g.items.length > 0)
            .sort((a, b) => a.label.localeCompare(b.label))
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [inventory, markets, search, station])

    const handleAdd = async () => {
        if (!valid || submitting) return
        const current = statementsFor(scope)
        if (current.length >= FILLER_STATEMENTS_MAX) {
            toast.error(
                `Maximum ${FILLER_STATEMENTS_MAX} filler statements per market`
            )
            return
        }
        const id = crypto.randomUUID()
        try {
            await save.mutateAsync({
                marketId: scope,
                statements: [
                    ...current,
                    {
                        id,
                        text: trimmed,
                        enabled: true,
                        availability: null,
                    },
                ],
            })
            setText('')
            setComposerOpen(false)
            toast.success('Filler statement added')
        } catch (err: unknown) {
            toast.error('Failed to add filler statement', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const startEdit = (item: FillerRow) => {
        setEditing(item)
        setText(item.entry.text)
        setComposerOpen(false)
    }

    const closeComposer = () => {
        setEditing(null)
        setComposerOpen(false)
        setText('')
    }

    /**
     * Rewrite one existing statement's text in place. The id is preserved, so the backend
     * treats it as an update (and evicts that statement's cached per-voice audio) rather
     * than a delete-plus-add, which is what keeps the availability window and the enabled
     * switch attached to the same statement.
     */
    const handleSaveEdit = async () => {
        if (!editing || !valid || submitting) return
        const current = statementsFor(editing.marketId)
        try {
            await save.mutateAsync({
                marketId: editing.marketId,
                statements: current.map((s) =>
                    s.id === editing.entry.id ? { ...s, text: trimmed } : s
                ),
            })
            closeComposer()
            toast.success('Filler statement updated')
        } catch (err: unknown) {
            toast.error('Failed to update filler statement', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const handleDelete = async (item: FillerRow) => {
        const current = statementsFor(item.marketId)
        try {
            await save.mutateAsync({
                marketId: item.marketId,
                statements: current.filter((s) => s.id !== item.entry.id),
            })
            if (editing?.entry.id === item.entry.id) closeComposer()
            toast.success('Filler statement removed')
        } catch (err: unknown) {
            toast.error('Failed to remove filler statement', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const handleToggleEnabled = async (item: FillerRow) => {
        const current = statementsFor(item.marketId)
        const next = current.map((s) =>
            s.id === item.entry.id ? { ...s, enabled: !s.enabled } : s
        )
        try {
            await save.mutateAsync({
                marketId: item.marketId,
                statements: next,
            })
        } catch (err: unknown) {
            toast.error('Failed to update filler statement', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const openAvailabilityEditor = (item: FillerRow) => {
        const existing = item.entry.availability
        setScheduling(item)
        setDraft(
            existing
                ? {
                      startDate: existing.startDate,
                      endDate: existing.endDate,
                      days: existing.days ? [...existing.days] : null,
                      startLocal: existing.startLocal,
                      endLocal: existing.endLocal,
                  }
                : { ...EMPTY_AVAILABILITY }
        )
    }

    const closeAvailabilityEditor = () => {
        setScheduling(null)
        setDraft(null)
    }

    const handleSaveAvailability = async () => {
        if (!scheduling || !draft || submitting) return
        const error = availabilityWindowError(draft)
        if (error) {
            toast.error(error)
            return
        }
        // An all-empty draft means "no window" — store null so the row stays
        // one-line and the round-trip is byte-identical to never having set one.
        const availability = isEmptyAvailability(draft) ? null : draft
        const current = statementsFor(scheduling.marketId)
        try {
            await save.mutateAsync({
                marketId: scheduling.marketId,
                statements: current.map((s) =>
                    s.id === scheduling.entry.id ? { ...s, availability } : s
                ),
            })
            toast.success(
                availability
                    ? 'Availability window saved'
                    : 'Statement is now always eligible'
            )
            closeAvailabilityEditor()
        } catch (err: unknown) {
            toast.error('Failed to save availability window', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const handleClearAvailability = async () => {
        if (!scheduling || submitting) return
        const current = statementsFor(scheduling.marketId)
        try {
            await save.mutateAsync({
                marketId: scheduling.marketId,
                statements: current.map((s) =>
                    s.id === scheduling.entry.id
                        ? { ...s, availability: null }
                        : s
                ),
            })
            toast.success('Statement is now always eligible')
            closeAvailabilityEditor()
        } catch (err: unknown) {
            toast.error('Failed to clear availability window', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    /**
     * The strict-rule warning: when every ENABLED statement in a market is outside its
     * availability window at the station-local now, no filler will air until a window
     * opens — the exact failure mode windows create. Disabled statements don't count
     * (their switch is the visible signal); the warning is about windows, not toggles.
     */
    const ineligibleWarning = (group: ScopeGroup<FillerRow>): string | null => {
        const enabled = group.items.filter((r) => r.entry.enabled)
        if (enabled.length === 0) return null
        const timezone = markets.find((m) => m.marketId === group.key)?.timezone
        const now = getStationLocalNow(timezone)
        const eligible = enabled.filter((r) =>
            isEligibleNow(r.entry.availability, now)
        )
        if (eligible.length > 0) return null
        return `No enabled statement in this market is eligible right now (station-local time). The strict rule means filler won't air until a window opens.`
    }

    return (
        <RuleTab>
            <RuleToolbar
                add={{
                    what: 'a filler statement',
                    onClick: () => {
                        // Pressing Add mid-edit abandons the edit and starts a blank create,
                        // rather than leaving two competing composer intents on screen.
                        const wasEditing = editing !== null
                        setEditing(null)
                        setText('')
                        setComposerOpen((v) => wasEditing || !v)
                    },
                    active: composing,
                    hint: (
                        <>
                            A short line to fill time when traffic news is
                            light.
                        </>
                    ),
                }}
                search={search}
                onSearchChange={setSearch}
                placeholder="Search statements…"
            />

            {composing && (
                <RuleComposer
                    title={editing ? 'Edit a filler statement' : undefined}
                    onCancel={closeComposer}
                    submit={{
                        label: editing ? 'Save changes' : 'Add',
                        icon: editing ? EditIcon : AddIcon,
                        onClick: editing ? handleSaveEdit : handleAdd,
                        disabled: !valid || submitting,
                        ready: valid,
                        submitting,
                        tooltip: unchanged
                            ? 'Change the wording to save.'
                            : undefined,
                    }}
                >
                    <ComposerField
                        label="Statement text"
                        htmlFor="filler-text"
                        width="flex-1"
                    >
                        <Input
                            id="filler-text"
                            maxLength={FILLER_STATEMENT_MAX_LENGTH}
                            placeholder="for example, Please stay alert out there and stick to the speed limit."
                            value={text}
                            onChange={(e) => setText(e.target.value)}
                            onKeyDown={(e) => {
                                if (e.key !== 'Enter') return
                                if (editing) handleSaveEdit()
                                else handleAdd()
                            }}
                        />
                        {tokenProblem && (
                            <p
                                role="alert"
                                className="text-xs leading-snug text-destructive"
                            >
                                {tokenProblem}
                            </p>
                        )}
                    </ComposerField>
                </RuleComposer>
            )}

            {Boolean(station) && isAdmin && (
                <ScopedActivityHistory
                    key={station}
                    resource="filler-statements"
                    entityId={station}
                    label={`History for ${labelFor(station)}`}
                />
            )}

            <ScopeGroupedList<FillerRow>
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
                            ? 'No statements match these filters.'
                            : 'No filler statements yet. Add a statement above to pad quiet bulletins.'}
                    </EmptyState>
                }
                rowKey={(item) => `${item.marketId}:${item.entry.id}`}
                renderHeaderExtra={(group) => {
                    const warning = ineligibleWarning(group)
                    if (!warning) return null
                    return (
                        <div
                            role="status"
                            className="flex items-start gap-2 border-b border-border bg-warning/10 px-4 py-2 text-xs text-warning"
                        >
                            <WarningIcon
                                className="mt-0.5 size-3.5 shrink-0"
                                aria-hidden
                            />
                            <span>{warning}</span>
                        </div>
                    )
                }}
                renderRow={(item) => (
                    <RuleRow
                        enabled={item.entry.enabled}
                        onToggle={() => handleToggleEnabled(item)}
                        label={item.entry.text}
                        secondary={
                            summariseAvailability(item.entry.availability) ||
                            undefined
                        }
                        actions={
                            <Button
                                variant="ghost"
                                size="sm"
                                className="h-7 gap-1.5 px-2 text-xs text-muted-foreground"
                                onClick={() => openAvailabilityEditor(item)}
                                disabled={submitting}
                                aria-label={
                                    item.entry.availability
                                        ? 'Edit availability window'
                                        : 'Schedule this statement'
                                }
                            >
                                <ClockIcon className="size-3.5" aria-hidden />
                                <span className="hidden sm:inline">
                                    {item.entry.availability
                                        ? 'Edit window'
                                        : 'Schedule'}
                                </span>
                            </Button>
                        }
                        onEdit={() => startEdit(item)}
                        editLabel="Edit filler statement text"
                        onDelete={() => handleDelete(item)}
                        deleteLabel="Delete filler statement"
                        itemLabel="filler statement"
                        disabled={submitting}
                    />
                )}
            />

            <Dialog
                open={scheduling !== null}
                onOpenChange={(open) => {
                    if (!open) closeAvailabilityEditor()
                }}
            >
                <DialogContent className="sm:max-w-md">
                    <DialogHeader>
                        <DialogTitle>
                            {scheduling?.entry.availability
                                ? 'Edit availability window'
                                : 'Schedule statement'}
                        </DialogTitle>
                        <DialogDescription>
                            Restrict when this statement can air. Everything
                            blank means always eligible.
                        </DialogDescription>
                    </DialogHeader>
                    {scheduling && draft && (
                        <AvailabilityEditor draft={draft} onChange={setDraft} />
                    )}
                    <DialogFooter className="gap-2 sm:justify-between">
                        <Button
                            variant="ghost"
                            onClick={handleClearAvailability}
                            disabled={
                                !draft ||
                                isEmptyAvailability(draft) ||
                                submitting
                            }
                        >
                            Always on
                        </Button>
                        <div className="flex gap-2">
                            <Button
                                variant="outline"
                                onClick={closeAvailabilityEditor}
                            >
                                Cancel
                            </Button>
                            <Button
                                onClick={handleSaveAvailability}
                                disabled={
                                    !draft ||
                                    availabilityWindowError(draft) !== null ||
                                    submitting
                                }
                            >
                                Save
                            </Button>
                        </div>
                    </DialogFooter>
                </DialogContent>
            </Dialog>
        </RuleTab>
    )
}

/**
 * The availability-window form fields. Native date/time inputs (type="date" gives
 * yyyy-MM-dd, type="time" HH:mm — the exact wire strings) and a Mon–Sun day toggle,
 * mirroring the time-input pattern of VoiceRotationEditor. Client validation mirrors
 * the backend's FillerAvailability.Create rejections so a bad window never reaches
 * the PUT to come back as a 400.
 */
function AvailabilityEditor({
    draft,
    onChange,
}: {
    readonly draft: FillerAvailabilityEntry
    readonly onChange: (next: FillerAvailabilityEntry) => void
}) {
    const error = availabilityWindowError(draft)

    const toggleDay = (name: string) => {
        const has = draft.days?.includes(name) ?? false
        const next = has
            ? (draft.days ?? []).filter((d) => d !== name)
            : [...(draft.days ?? []), name]
        onChange({ ...draft, days: next.length > 0 ? next : null })
    }

    return (
        <div className="grid gap-4">
            <div className="grid grid-cols-2 gap-3">
                <div className="grid gap-1.5">
                    <Label htmlFor="avail-start-date">Eligible from</Label>
                    <Input
                        id="avail-start-date"
                        type="date"
                        value={draft.startDate ?? ''}
                        onChange={(e) =>
                            onChange({
                                ...draft,
                                startDate: e.target.value || null,
                            })
                        }
                    />
                </div>
                <div className="grid gap-1.5">
                    <Label htmlFor="avail-end-date">Until</Label>
                    <Input
                        id="avail-end-date"
                        type="date"
                        value={draft.endDate ?? ''}
                        onChange={(e) =>
                            onChange({
                                ...draft,
                                endDate: e.target.value || null,
                            })
                        }
                    />
                </div>
            </div>
            <div className="grid gap-1.5">
                <span className="text-sm font-medium">Days of week</span>
                <div
                    className="flex flex-wrap gap-1"
                    role="group"
                    aria-label="Days of week"
                >
                    {DAY_BUTTONS.map((d) => {
                        const active = draft.days?.includes(d.name) ?? false
                        return (
                            <button
                                key={d.name}
                                type="button"
                                aria-pressed={active}
                                onClick={() => toggleDay(d.name)}
                                className={cn(
                                    'rounded-md border px-2.5 py-1.5 text-xs font-medium transition-colors',
                                    active
                                        ? 'border-primary bg-primary/10 text-primary'
                                        : 'text-muted-foreground hover:bg-muted'
                                )}
                            >
                                {d.short}
                            </button>
                        )
                    })}
                </div>
                <p className="text-xs text-muted-foreground">
                    No days selected = every day.
                </p>
            </div>
            <div className="grid grid-cols-2 gap-3">
                <div className="grid gap-1.5">
                    <Label htmlFor="avail-start-time">Start time</Label>
                    <Input
                        id="avail-start-time"
                        type="time"
                        value={draft.startLocal ?? ''}
                        onChange={(e) =>
                            onChange({
                                ...draft,
                                startLocal: e.target.value || null,
                            })
                        }
                    />
                </div>
                <div className="grid gap-1.5">
                    <Label htmlFor="avail-end-time">End time</Label>
                    <Input
                        id="avail-end-time"
                        type="time"
                        value={draft.endLocal ?? ''}
                        onChange={(e) =>
                            onChange({
                                ...draft,
                                endLocal: e.target.value || null,
                            })
                        }
                    />
                </div>
            </div>
            <p className="text-xs text-muted-foreground">
                Evaluated in the station's local timezone. An end time earlier
                than the start time runs past midnight.
            </p>
            {error && (
                <p role="alert" className="text-xs text-destructive">
                    {error}
                </p>
            )}
        </div>
    )
}
