import { useState, useMemo, useEffect, lazy, Suspense } from 'react'
import { useSearchParams } from 'react-router'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import { useScheduleActions } from '@/features/schedules/hooks/use-schedule-actions'
import { useOperatorAssignments } from '@/features/operators/api/operator-hooks'
import { Header, PageContainer } from '@/components/layout'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { CardGridSkeleton } from '@/components/LoadingSkeleton'
import { DataBoundary } from '@/components/DataBoundary'
import { Placeholder } from '@/components/Placeholder'
import { StationProfileTable } from '@/features/stations/components/StationProfileTable'
import { ViewToggle } from '@/components/ViewToggle'
import { useViewMode } from '@/hooks/use-view-mode'
import { StationProfileCards } from '@/features/stations/components/StationProfileCards'
import { useDocumentTitle } from '@/hooks/use-document-title'
import { useIsAdmin } from '@/hooks/use-current-user'
import type { MarketSchedule } from '@/features/schedules/types'
import { Radio } from 'lucide-react'
import { ICON } from '@/lib/design/icons'

// Lazy: each studio pulls in MapPicker/ShapeEditors (the ~1,700-line Leaflet
// coverage-area drawing tool) but only ever renders after a user action
// (Create/Edit/Trigger) — never on the initial station-list paint. Splitting
// them out keeps that weight off the route chunk everyone downloads to just
// browse the station list.
const ScheduleDetailStudio = lazy(() =>
    import('@/features/schedules/components').then((m) => ({
        default: m.ScheduleDetailStudio,
    }))
)
const ScheduleStudio = lazy(() =>
    import('@/features/schedules/components').then((m) => ({
        default: m.ScheduleStudio,
    }))
)
const TriggerStudio = lazy(() =>
    import('@/features/stations/components/TriggerStudio').then((m) => ({
        default: m.TriggerStudio,
    }))
)

export function StationsPage() {
    useDocumentTitle('Stations')
    const isAdmin = useIsAdmin()
    const { data, isLoading, isError, error, refetch } = useSchedules()
    const actions = useScheduleActions()

    // Admin-only context: which operators are assigned to each market.
    // Matches scope semantics elsewhere — keys are lowercased market ids.
    const { data: operatorDirectory } = useOperatorAssignments({
        enabled: isAdmin,
    })
    const assignments = operatorDirectory?.operators
    const operatorsByMarket = useMemo(() => {
        if (!isAdmin || !assignments?.length) return undefined
        const map = new Map<string, string[]>()
        for (const assignment of assignments) {
            const name = assignment.displayName || assignment.objectId
            for (const market of assignment.markets) {
                const key = market.toLowerCase()
                map.set(key, [...(map.get(key) ?? []), name])
            }
        }
        return map
    }, [isAdmin, assignments])

    const [triggerTarget, setTriggerTarget] = useState<MarketSchedule | null>(
        null
    )
    const [search, setSearch] = useState('')
    const [view, setView] = useViewMode('stations')

    const schedules = useMemo(() => data?.schedules ?? [], [data])
    const filteredSchedules = useMemo(() => {
        const needle = search.trim().toLowerCase()
        if (!needle) return schedules
        return schedules.filter((schedule) => {
            const haystack = [
                schedule.station ?? '',
                schedule.name ?? '',
                schedule.marketId,
                schedule.timezone ?? '',
            ]
                .join(' ')
                .toLowerCase()
            return haystack.includes(needle)
        })
    }, [schedules, search])

    // Deep-link from Cmd+K: `?open=<marketId>` opens that station's edit
    // sheet once schedules have loaded, then the param is stripped so
    // back/refresh doesn't re-trigger it.
    const [searchParams, setSearchParams] = useSearchParams()
    useEffect(() => {
        const openId = searchParams.get('open')
        if (!openId || !data) return
        const schedule = schedules.find((s) => s.marketId === openId)
        if (schedule) actions.handleEdit(schedule)
        setSearchParams(
            (prev) => {
                const next = new URLSearchParams(prev)
                next.delete('open')
                return next
            },
            { replace: true }
        )
    }, [searchParams, setSearchParams, schedules, data, actions])

    // One CTA per state: while the empty hero shows its focal action, drop the
    // duplicate header button. Create and Duplicate stay admin-only by choice, not by the
    // matrix: the PUT would succeed for a market the operator already holds, but standing a
    // station up is an onboarding act, and for any other market id it 403s on the market guard
    // — an affordance that mostly fails is worse than one that isn't offered.
    const showHeaderAction =
        isAdmin && (isLoading || isError || schedules.length > 0)

    const handleTrigger = (schedule: MarketSchedule) => {
        setTriggerTarget(schedule)
    }

    const handleCreate = () => {
        actions.handleCreate()
    }

    const handleEdit = (schedule: MarketSchedule) => {
        actions.handleEdit(schedule)
    }

    const handleDuplicate = (schedule: MarketSchedule) => {
        actions.handleDuplicate(schedule)
    }

    return (
        <PageContainer>
            <Header
                title="Stations"
                description="Station profiles: schedules, voices, and coverage"
            >
                {showHeaderAction && (
                    <Button onClick={handleCreate} className="gap-2">
                        <ICON.add className="size-4" />
                        New station
                    </Button>
                )}
            </Header>

            <DataBoundary
                query={{ isError, error, isLoading, data: schedules, refetch }}
                errorFallback="Unable to retrieve stations. Please try again."
                loading={<CardGridSkeleton />}
                empty={
                    <Placeholder
                        icon={Radio}
                        message="No stations configured"
                        detail={
                            isAdmin
                                ? undefined
                                : 'No station is assigned to you yet. Ask an ARN administrator to add one.'
                        }
                        action={
                            isAdmin ? (
                                <Button
                                    onClick={handleCreate}
                                    className="gap-2"
                                >
                                    <ICON.add className="size-4" />
                                    Add your first station
                                </Button>
                            ) : undefined
                        }
                    />
                }
            >
                <div className="mb-4 flex items-center gap-3">
                    <Input
                        value={search}
                        onChange={(event) => setSearch(event.target.value)}
                        placeholder="Search stations, markets, timezones, or models..."
                        aria-label="Search stations"
                        className="flex-1"
                    />
                    <ViewToggle value={view} onChange={setView} />
                </div>
                {filteredSchedules.length === 0 ? (
                    <Placeholder
                        icon={Radio}
                        message="No matching stations"
                        detail="Try a different station, market, timezone, or model."
                    />
                ) : view === 'table' ? (
                    <StationProfileTable
                        schedules={filteredSchedules}
                        operatorsByMarket={operatorsByMarket}
                        onTrigger={handleTrigger}
                        onView={actions.handleView}
                        onEdit={handleEdit}
                        onDuplicate={handleDuplicate}
                        onToggle={actions.handleToggleEnabled}
                        onTogglePlayout={actions.handleTogglePlayout}
                        onDelete={(s) => actions.handleDelete(s.marketId)}
                    />
                ) : (
                    <StationProfileCards
                        schedules={filteredSchedules}
                        operatorsByMarket={operatorsByMarket}
                        onTrigger={handleTrigger}
                        onView={actions.handleView}
                        onEdit={handleEdit}
                        onDuplicate={handleDuplicate}
                        onToggle={actions.handleToggleEnabled}
                        onTogglePlayout={actions.handleTogglePlayout}
                        onDelete={(s) => actions.handleDelete(s.marketId)}
                    />
                )}
            </DataBoundary>

            {/* Trigger dialog — opened from a station's "Trigger now" */}
            {triggerTarget && (
                <Suspense fallback={null}>
                    <TriggerStudio
                        open={!!triggerTarget}
                        onOpenChange={(open) => {
                            if (!open) setTriggerTarget(null)
                        }}
                        schedule={triggerTarget}
                    />
                </Suspense>
            )}

            {actions.showDetailSheet && actions.viewingSchedule && (
                <Suspense fallback={null}>
                    <ScheduleDetailStudio
                        key={actions.viewingSchedule.marketId}
                        schedule={actions.viewingSchedule}
                        open={actions.showDetailSheet}
                        onOpenChange={(open) => {
                            if (!open) actions.handleCancel()
                        }}
                        onEdit={handleEdit}
                        onToggle={actions.handleToggleEnabled}
                    />
                </Suspense>
            )}

            {/* Mounted per target: useForm captures defaults on first
                render, so the studio must remount when the subject changes */}
            {actions.showEditSheet && (
                <Suspense fallback={null}>
                    <ScheduleStudio
                        key={actions.editingSchedule?.marketId ?? 'new'}
                        schedule={actions.editingSchedule}
                        open={actions.showEditSheet}
                        onOpenChange={(open) => {
                            if (!open) actions.handleCancel()
                        }}
                        onSubmit={actions.handleSubmit}
                        isSubmitting={actions.isSubmitting}
                        flattenConfirm={actions.flattenConfirm}
                    />
                </Suspense>
            )}
        </PageContainer>
    )
}
