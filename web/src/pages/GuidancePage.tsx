import { useMemo, useState, useRef, useEffect, useCallback } from 'react'
import { useSearchParams } from 'react-router'
import { Header, PageContainer } from '@/components/layout'
import { useIsAdmin } from '@/hooks/use-current-user'
import { useDocumentTitle } from '@/hooks/use-document-title'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import { toast } from 'sonner'
import { RuleTab } from '@/components/RuleTab'
import {
    GuidanceComposer,
    type ComposerSubmit,
    type ComposerMode,
} from '@/features/guidance/components/GuidanceComposer'
import { GuidanceInventory } from '@/features/guidance/components/GuidanceInventory'
import {
    Select,
    SelectTrigger,
    SelectValue,
    SelectContent,
    SelectItem,
} from '@/components/ui/select'
import { GuidanceInEffectPanel } from '@/features/guidance/components/GuidanceInEffectPanel'
import { FutureLessonsPanel } from '@/features/guidance/components/FutureLessonsPanel'
import { SafetyTermsManager } from '@/features/safety/components/SafetyTermsManager'
import { PriorityRoadsManager } from '@/features/schedules/components/PriorityRoadsManager'
import { IncidentPrioritiesManager } from '@/features/schedules/components/IncidentPrioritiesManager'
import { FillerStatementsManager } from '@/features/schedules/components/FillerStatementsManager'
import { EditorialSettingsPanel } from '@/features/editorial/components/EditorialSettingsPanel'
import { WriterPromptManager } from '@/features/playbooks/components/WriterPromptManager'
import { VerifiedPronunciationsManager } from '@/features/pronunciation/components/VerifiedPronunciationsManager'
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs'
import {
    useGuidanceInventory,
    useUpsertGlobalGuidance,
    useDeleteGlobalGuidance,
    useUpsertMarketGuidance,
    useDeleteMarketGuidance,
    usePromoteGuidance,
    useGuidanceCandidates,
} from '@/features/guidance/api/guidance-hooks'
import { appliesToStation } from '@/features/guidance/lib/scope-grouping'
import {
    GLOBAL_GROUP_ID,
    EMPTY_FILTERS,
    applyGuidanceFilters,
    groupGuidance,
    type GuidanceFilters,
    type GuidanceGroup,
} from '@/features/guidance/lib/inventory'
import type {
    Guidance,
    GuidanceTypeName,
} from '@/features/guidance/api/schemas'

interface MarketOption {
    readonly marketId: string
    readonly station: string | null
    readonly label: string
    /** Station IANA timezone — carried for the filler availability warning (station-local now). */
    readonly timezone: string | null
}

/** Composer scope value for a Station-scoped directive (value = station name). */
const STATION_SCOPE_PREFIX = 'station:'

function stationScopeValue(station: string): string {
    return `${STATION_SCOPE_PREFIX}${station}`
}

interface PromoteNote {
    readonly jobId: string
    readonly text: string
    readonly scope: string
}

/**
 * Hairline between tab groups. The tabs were previously ordered by which store
 * backs them, which told an operator nothing; they now read left-to-right as
 * what the writer is TOLD · what it must NEVER say · what it talks about ·
 * triage. Presentational only, so it stays out of the tab stop order.
 */
function TabGroupDivider() {
    return (
        <span
            aria-hidden="true"
            className="mx-1.5 my-1 w-px shrink-0 self-stretch bg-border"
        />
    )
}

export function GuidancePage() {
    useDocumentTitle('Bulletin rules')
    const [searchParams, setSearchParams] = useSearchParams()
    const tabFromUrl = searchParams.get('tab') ?? 'guidance'
    const [activeTab, setActiveTab] = useState(tabFromUrl)
    const isAdmin = useIsAdmin()
    const { data: scheduleData, isLoading: schedulesLoading } = useSchedules()

    const markets = useMemo<MarketOption[]>(() => {
        const seen = new Map<string, MarketOption>()
        for (const s of scheduleData?.schedules ?? []) {
            seen.set(s.marketId, {
                marketId: s.marketId,
                station: s.station,
                label: s.station ? `${s.station} · ${s.marketId}` : s.marketId,
                timezone: s.timezone,
            })
        }
        return [...seen.values()].sort((a, b) => a.label.localeCompare(b.label))
    }, [scheduleData])

    const marketIds = useMemo(() => markets.map((m) => m.marketId), [markets])
    const { data: inventory, isLoading } = useGuidanceInventory(
        marketIds,
        isAdmin,
        !schedulesLoading
    )

    const upsertGlobal = useUpsertGlobalGuidance()
    const deleteGlobal = useDeleteGlobalGuidance()
    const upsertMarket = useUpsertMarketGuidance()
    const deleteMarket = useDeleteMarketGuidance()
    const promoteMutation = usePromoteGuidance()
    const submitting =
        upsertGlobal.isPending ||
        upsertMarket.isPending ||
        promoteMutation.isPending

    const [filters, setFilters] = useState<GuidanceFilters>(EMPTY_FILTERS)
    const [editing, setEditing] = useState<Guidance | null>(null)
    const [promoteNote, setPromoteNote] = useState<PromoteNote | null>(null)
    const [composerNonce, setComposerNonce] = useState(0)
    const [composerOpen, setComposerOpen] = useState(false)
    const stationFromUrl = searchParams.get('station')
    const tabStripRef = useRef<HTMLDivElement>(null)
    const composerRef = useRef<HTMLDivElement>(null)
    const [canScrollRight, setCanScrollRight] = useState(false)

    // The page owns the ONE station context: every tab renders that station's
    // rules over the ARN defaults. There is no "all stations" state - the first
    // station is the landing default and the choice persists in the URL.
    const station = useMemo(() => {
        if (
            stationFromUrl &&
            markets.some((m) => m.marketId === stationFromUrl)
        )
            return stationFromUrl
        return markets[0]?.marketId ?? ''
    }, [stationFromUrl, markets])
    const setStation = useCallback(
        (next: string) => {
            setSearchParams((prev) => {
                const params = new URLSearchParams(prev)
                params.set('station', next)
                return params
            })
        },
        [setSearchParams]
    )

    // Right-edge fade: only while a tab is genuinely cut off (Studio.tsx
    // StudioRail pattern) so the affordance never fakes overflow.
    useEffect(() => {
        const strip = tabStripRef.current
        if (!strip) return
        const update = () => {
            setCanScrollRight(
                strip.scrollLeft < strip.scrollWidth - strip.clientWidth - 2
            )
        }
        update()
        strip.addEventListener('scroll', update, { passive: true })
        const ro = new ResizeObserver(update)
        ro.observe(strip)
        return () => {
            strip.removeEventListener('scroll', update)
            ro.disconnect()
        }
    }, [])

    /** Scroll the focused/clicked tab trigger into view within the strip. */
    const scrollTabIntoView = useCallback(
        (e: React.FocusEvent<HTMLElement> | React.MouseEvent<HTMLElement>) => {
            const node = e.currentTarget as HTMLElement
            if (!tabStripRef.current) return
            const strip = tabStripRef.current
            const stripRect = strip.getBoundingClientRect()
            const nodeRect = node.getBoundingClientRect()
            if (nodeRect.left < stripRect.left) {
                strip.scrollBy({
                    left: nodeRect.left - stripRect.left - 8,
                    behavior: 'smooth',
                })
            } else if (nodeRect.right > stripRect.right) {
                strip.scrollBy({
                    left: nodeRect.right - stripRect.right + 8,
                    behavior: 'smooth',
                })
            }
        },
        []
    )

    // Station-scoped rows are grouped by the station's display name; label the
    // group as a Station so it cannot be mistaken for a market partition.
    const stationNames = useMemo(
        () =>
            new Set(
                (scheduleData?.schedules ?? [])
                    .map((s) => s.station)
                    .filter((s): s is string => Boolean(s))
            ),
        [scheduleData]
    )
    const labelFor = (scopeId: string) => {
        if (stationNames.has(scopeId)) return `Station \u00B7 ${scopeId}`
        return markets.find((m) => m.marketId === scopeId)?.label ?? scopeId
    }

    const groups = useMemo<GuidanceGroup[]>(
        () =>
            groupGuidance(
                applyGuidanceFilters(inventory ?? [], filters).filter((g) =>
                    appliesToStation(g, station)
                ),
                labelFor
            ),
        // labelFor depends on markets; inventory/filters/station drive the rest.
        // eslint-disable-next-line react-hooks/exhaustive-deps
        [inventory, filters, station, markets]
    )

    // ── Composer state derivation ─────────────────────────────────────

    const mode: ComposerMode = editing
        ? 'edit'
        : promoteNote
          ? 'promote'
          : 'create'
    /* The chosen station is the operator's declared context; a new note lands
       on it. */
    const defaultScope = station || (markets[0]?.marketId ?? '')
    const composerInitial: ComposerSubmit = editing
        ? {
              id: editing.id,
              scope:
                  editing.scope === 'Global'
                      ? GLOBAL_GROUP_ID
                      : editing.scope === 'Station'
                        ? stationScopeValue(editing.scopeId)
                        : editing.scopeId,
              type: editing.type as GuidanceTypeName,
              text: editing.text,
              enabled: editing.enabled,
          }
        : promoteNote
          ? {
                scope: promoteNote.scope,
                type: 'Editorial',
                text: promoteNote.text,
                enabled: true,
            }
          : { scope: defaultScope, type: 'Editorial', text: '', enabled: true }
    const composerKey = editing
        ? `edit-${editing.id}`
        : promoteNote
          ? `promote-${promoteNote.jobId}`
          : `new-${composerNonce}-${defaultScope}`

    const resetComposer = () => {
        setEditing(null)
        setPromoteNote(null)
        setComposerNonce((n) => n + 1)
        setComposerOpen(false)
    }

    const composerMounted = composerOpen || mode !== 'create'

    /**
     * Edit and promote retarget the ONE composer at the top of the tab. The app
     * scrolls inside `<main>`, so clicking Edit on a row below the fold used to
     * swap an off-screen composer and look like nothing happened. Pull it back
     * into view; `create` is excluded so a successful save doesn't yank the list.
     */
    useEffect(() => {
        if (!composerMounted || mode === 'create') return
        composerRef.current?.scrollIntoView({
            behavior: 'smooth',
            block: 'nearest',
        })
    }, [composerMounted, mode, composerKey])

    // ── Handlers ──────────────────────────────────────────────────────
    const handleSubmit = async (v: ComposerSubmit) => {
        try {
            if (mode === 'promote') {
                await promoteMutation.mutateAsync({
                    scope: v.scope === GLOBAL_GROUP_ID ? 'Global' : 'Market',
                    scopeId: v.scope === GLOBAL_GROUP_ID ? '' : v.scope,
                    type: v.type,
                    text: v.text,
                    jobId: promoteNote?.jobId,
                })
                toast.success('Note promoted to standing guidance')
            } else if (v.scope.startsWith(STATION_SCOPE_PREFIX)) {
                await upsertGlobal.mutateAsync({
                    id: v.id,
                    type: v.type,
                    text: v.text,
                    enabled: v.enabled,
                    scope: 'Station',
                    scopeId: v.scope.slice(STATION_SCOPE_PREFIX.length),
                })
                toast.success(v.id ? 'Guidance updated' : 'Guidance added')
            } else if (v.scope === GLOBAL_GROUP_ID) {
                await upsertGlobal.mutateAsync({
                    id: v.id,
                    type: v.type,
                    text: v.text,
                    enabled: v.enabled,
                })
                toast.success(v.id ? 'Guidance updated' : 'Guidance added')
            } else {
                await upsertMarket.mutateAsync({
                    market: v.scope,
                    id: v.id,
                    type: v.type,
                    text: v.text,
                    enabled: v.enabled,
                })
                toast.success(v.id ? 'Guidance updated' : 'Guidance added')
            }
            resetComposer()
        } catch (err: unknown) {
            toast.error('Failed to save guidance', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const handleAdd = () => {
        if (mode !== 'create') {
            // Add from edit/promote starts a blank create form and keeps it open.
            setEditing(null)
            setPromoteNote(null)
            setComposerNonce((n) => n + 1)
            setComposerOpen(true)
            return
        }
        setComposerOpen((open) => !open)
    }

    const handleEdit = (item: Guidance) => {
        setPromoteNote(null)
        setEditing(item)
        setComposerOpen(true)
    }

    const handleToggle = async (item: Guidance) => {
        const body = {
            id: item.id,
            type: item.type as GuidanceTypeName,
            text: item.text,
            enabled: !item.enabled,
        }
        try {
            if (item.scope === 'Global') await upsertGlobal.mutateAsync(body)
            else if (item.scope === 'Station')
                await upsertGlobal.mutateAsync({
                    ...body,
                    scope: 'Station',
                    scopeId: item.scopeId,
                })
            else
                await upsertMarket.mutateAsync({
                    market: item.scopeId,
                    ...body,
                })
        } catch (err: unknown) {
            toast.error('Failed to update guidance', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const handleDelete = async (item: Guidance) => {
        try {
            if (item.scope === 'Global')
                await deleteGlobal.mutateAsync({ id: item.id })
            else if (item.scope === 'Station')
                await deleteGlobal.mutateAsync({
                    id: item.id,
                    scope: 'Station',
                    scopeId: item.scopeId,
                })
            else
                await deleteMarket.mutateAsync({
                    market: item.scopeId,
                    id: item.id,
                })
            if (editing?.id === item.id) resetComposer()
            toast.success('Guidance removed')
        } catch (err: unknown) {
            toast.error('Failed to remove guidance', {
                description:
                    err instanceof Error && err.message
                        ? err.message
                        : 'Your changes were not saved. Try again.',
            })
        }
    }

    const handlePromote = (jobId: string, text: string) => {
        setEditing(null)
        setPromoteNote({ jobId, text, scope: defaultScope })
        setComposerOpen(true)
    }

    const canEditScope = (group: GuidanceGroup) =>
        group.scope === 'Global' ? isAdmin : true

    const selectedMarket = markets.find((m) => m.marketId === station)

    /* Active lesson count on the governance tab label. Market-scoped, so it
       only loads once a station is chosen; the hook is disabled on `undefined`. */
    const lessonCount =
        useGuidanceCandidates(selectedMarket?.marketId ?? null).data?.length ??
        0

    const guidanceComposer = composerMounted ? (
        <div ref={composerRef}>
            <GuidanceComposer
                key={composerKey}
                mode={mode}
                initial={composerInitial}
                submitting={submitting}
                onSubmit={handleSubmit}
                onCancel={resetComposer}
            />
        </div>
    ) : null

    const guidanceBody = (
        <RuleTab>
            {selectedMarket && (
                <GuidanceInEffectPanel
                    marketId={selectedMarket.marketId}
                    station={selectedMarket.station}
                    label={selectedMarket.label}
                />
            )}

            <GuidanceInventory
                groups={groups}
                isLoading={isLoading}
                filters={filters}
                station={station}
                stationLabel={selectedMarket?.label}
                onFilterChange={(patch) =>
                    setFilters((f) => ({ ...f, ...patch }))
                }
                onClear={() => setFilters(EMPTY_FILTERS)}
                onAdd={handleAdd}
                addActive={composerOpen || mode !== 'create'}
                composer={guidanceComposer}
                canEditScope={canEditScope}
                onEdit={handleEdit}
                onToggle={handleToggle}
                onDelete={handleDelete}
            />
        </RuleTab>
    )

    return (
        <PageContainer>
            <Header
                title="Bulletin rules"
                description={
                    'One station at a time: its own rules over the ARN defaults every station starts from.'
                }
                titleAfter={
                    <Select value={station} onValueChange={setStation}>
                        <SelectTrigger
                            aria-label="Station these rules belong to"
                            className="w-full sm:w-64"
                        >
                            <SelectValue placeholder="Choose a station" />
                        </SelectTrigger>
                        <SelectContent>
                            {markets.map((m) => (
                                <SelectItem key={m.marketId} value={m.marketId}>
                                    {m.label}
                                </SelectItem>
                            ))}
                        </SelectContent>
                    </Select>
                }
            />

            <Tabs
                value={activeTab}
                onValueChange={(val) => {
                    setActiveTab(val)
                    setSearchParams((prev) => {
                        const n = new URLSearchParams(prev)
                        n.set('tab', val)
                        return n
                    })
                }}
                className="mt-1"
            >
                <div className="relative -mx-4 px-4 sm:mx-0 sm:px-0">
                    <div
                        ref={tabStripRef}
                        className="flex gap-0 overflow-x-auto [-ms-overflow-style:none] [scrollbar-width:none] [-webkit-overflow-scrolling:touch] [&::-webkit-scrollbar]:hidden sm:overflow-visible"
                        role="presentation"
                    >
                        {/* Right-edge affordance: visible only when content overflows */}
                        {canScrollRight && (
                            <div
                                className="pointer-events-none absolute right-0 top-0 bottom-0 w-8 bg-linear-to-l from-background to-transparent sm:hidden"
                                aria-hidden="true"
                            />
                        )}
                        <TabsList className="flex-nowrap sm:flex-wrap sm:justify-center">
                            {/* Group 1 — what the writer is TOLD (soft prompt layers,
                                strongest scope first). These two are two strengths
                                of the same prompt; keeping them adjacent is the point. */}
                            <TabsTrigger
                                onFocus={scrollTabIntoView}
                                onClick={scrollTabIntoView}
                                value="guidance"
                                className="px-4 sm:px-3"
                            >
                                Editorial guidance
                            </TabsTrigger>
                            {isAdmin && (
                                <TabsTrigger
                                    onFocus={scrollTabIntoView}
                                    onClick={scrollTabIntoView}
                                    value="editorial"
                                    className="px-4 sm:px-3"
                                >
                                    Writer prompt
                                </TabsTrigger>
                            )}
                            {isAdmin && (
                                <TabsTrigger
                                    onFocus={scrollTabIntoView}
                                    onClick={scrollTabIntoView}
                                    value="limits"
                                    className="px-4 sm:px-3"
                                >
                                    Limits
                                </TabsTrigger>
                            )}
                            <TabGroupDivider />
                            {/* Group 2 — what it must NEVER say (deterministic, enforced). */}
                            {isAdmin && (
                                <TabsTrigger
                                    onFocus={scrollTabIntoView}
                                    onClick={scrollTabIntoView}
                                    value="safety"
                                    className="px-4 sm:px-3"
                                >
                                    Banned words
                                </TabsTrigger>
                            )}
                            <TabGroupDivider />
                            {/* Group 3 — what it talks about (content inputs). */}
                            <TabsTrigger
                                onFocus={scrollTabIntoView}
                                onClick={scrollTabIntoView}
                                value="roads"
                                className="px-4 sm:px-3"
                            >
                                Priority roads
                            </TabsTrigger>
                            <TabsTrigger
                                onFocus={scrollTabIntoView}
                                onClick={scrollTabIntoView}
                                value="incident-priorities"
                                className="px-4 sm:px-3"
                            >
                                Incident priorities
                            </TabsTrigger>
                            <TabsTrigger
                                onFocus={scrollTabIntoView}
                                onClick={scrollTabIntoView}
                                value="filler"
                                className="px-4 sm:px-3"
                            >
                                Filler statements
                            </TabsTrigger>
                            <TabsTrigger
                                onFocus={scrollTabIntoView}
                                onClick={scrollTabIntoView}
                                value="verified-pronunciations"
                                className="px-4 sm:px-3"
                            >
                                Verified words
                            </TabsTrigger>
                            <TabGroupDivider />
                            {/* Group 4 — explicit future-lesson governance.
                                Active station instructions can be promoted or dismissed here. */}
                            <TabsTrigger
                                onFocus={scrollTabIntoView}
                                onClick={scrollTabIntoView}
                                value="future-lessons"
                                className="px-4 sm:px-3"
                            >
                                Future lessons
                                {lessonCount > 0 && (
                                    <span className="ml-1.5 text-2xs tabular-nums text-muted-foreground">
                                        {lessonCount}
                                    </span>
                                )}
                            </TabsTrigger>
                        </TabsList>
                    </div>
                </div>
                <TabsContent value="guidance" className="mt-4">
                    {guidanceBody}
                </TabsContent>
                {isAdmin && (
                    <TabsContent value="safety" className="mt-4">
                        <SafetyTermsManager
                            markets={markets}
                            station={station}
                        />
                    </TabsContent>
                )}
                <TabsContent value="roads" className="mt-4">
                    <PriorityRoadsManager markets={markets} station={station} />
                </TabsContent>
                <TabsContent value="incident-priorities" className="mt-4">
                    <IncidentPrioritiesManager
                        markets={markets}
                        station={station}
                    />
                </TabsContent>
                <TabsContent value="filler" className="mt-4">
                    <FillerStatementsManager
                        markets={markets}
                        station={station}
                    />
                </TabsContent>
                <TabsContent value="verified-pronunciations" className="mt-4">
                    <VerifiedPronunciationsManager
                        markets={markets}
                        station={station}
                    />
                </TabsContent>
                {isAdmin && (
                    <TabsContent value="editorial" className="mt-4">
                        <WriterPromptManager marketId={station || null} />
                    </TabsContent>
                )}
                {isAdmin && (
                    <TabsContent value="limits" className="mt-4">
                        <RuleTab>
                            <EditorialSettingsPanel
                                scope="Market"
                                scopeId={station}
                                market={station}
                                station={selectedMarket?.station ?? null}
                            />
                        </RuleTab>
                    </TabsContent>
                )}
                <TabsContent value="future-lessons" className="mt-4">
                    <RuleTab>
                        <FutureLessonsPanel
                            marketId={selectedMarket?.marketId ?? null}
                            label={selectedMarket?.label ?? null}
                            canPromote={isAdmin}
                            onPromote={handlePromote}
                        />
                    </RuleTab>
                </TabsContent>
            </Tabs>
        </PageContainer>
    )
}
