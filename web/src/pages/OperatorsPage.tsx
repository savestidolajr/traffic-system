import { useState, useMemo, useEffect } from 'react'
import { ICON } from '@/lib/design/icons'
import { useSearchParams } from 'react-router'
import { Header, PageContainer } from '@/components/layout'
import { CARD_GRID } from '@/lib/layout'
import { Input } from '@/components/ui/input'
import { CardGridSkeleton } from '@/components/LoadingSkeleton'
import { DataBoundary } from '@/components/DataBoundary'
import { Placeholder } from '@/components/Placeholder'
import { Alert, AlertTitle, AlertDescription } from '@/components/ui/alert'
import { useDocumentTitle } from '@/hooks/use-document-title'
import { useOperatorAssignments } from '@/features/operators/api/operator-hooks'
import { useSchedules } from '@/features/schedules/api/schedule-hooks'
import { OperatorAssignmentCard } from '@/features/operators/components/OperatorAssignmentCard'
import { OperatorAssignmentTable } from '@/features/operators/components/OperatorAssignmentTable'
import { AssignOperatorDialog } from '@/features/operators/components/AssignOperatorDialog'
import { ViewToggle } from '@/components/ViewToggle'
import { useViewMode } from '@/hooks/use-view-mode'
import type { OperatorDirectoryEntry } from '@/features/operators/api/schemas'

export function OperatorsPage() {
    useDocumentTitle('Operators')

    const { data, isLoading, isError, error, refetch } =
        useOperatorAssignments()

    const [dialogOpen, setDialogOpen] = useState(false)
    const [editingEntry, setEditingEntry] =
        useState<OperatorDirectoryEntry | null>(null)
    const [search, setSearch] = useState('')
    const [view, setView] = useViewMode('operators')

    const entries = useMemo(
        () =>
            [...(data?.operators ?? [])].sort((a, b) =>
                a.displayName.localeCompare(b.displayName)
            ),
        [data]
    )
    const directoryStatus = data?.directoryStatus ?? 'Unavailable'

    // Friendly chip labels: assignments store marketIds; show the station
    // ("2GB · Sydney Breakfast") instead of the raw id.
    const { data: schedulesResult } = useSchedules()
    const marketLabels = useMemo(() => {
        const map = new Map<string, string>()
        for (const schedule of schedulesResult?.schedules ?? []) {
            const label = [schedule.station, schedule.name]
                .filter(Boolean)
                .join(' · ')
            if (label) map.set(schedule.marketId.toLowerCase(), label)
        }
        return map
    }, [schedulesResult])

    const filteredEntries = useMemo(() => {
        const query = search.trim().toLowerCase()
        if (!query) return entries
        return entries.filter((entry) => {
            const marketText = entry.markets
                .map(
                    (market) => marketLabels.get(market.toLowerCase()) ?? market
                )
                .join(' ')
            const searchable = [
                entry.displayName,
                entry.userPrincipalName ?? '',
                entry.roles.join(' '),
                entry.source,
                marketText,
            ]
                .join(' ')
                .toLowerCase()
            return searchable.includes(query)
        })
    }, [entries, marketLabels, search])

    const handleEdit = (entry: OperatorDirectoryEntry) => {
        setEditingEntry(entry)
        setDialogOpen(true)
    }

    // Deep-link from Cmd+K: `?open=<objectId>` opens that directory entry's
    // station-access dialog once the live directory has loaded.
    const [searchParams, setSearchParams] = useSearchParams()
    useEffect(() => {
        const openId = searchParams.get('open')
        if (!openId || !data) return
        const entry = entries.find((candidate) => candidate.objectId === openId)
        // Deep-link consumption only: guarded by openId, and the URL param is
        // cleared below so this can only fire once per id.
        // eslint-disable-next-line react-hooks/set-state-in-effect
        if (entry) handleEdit(entry)
        setSearchParams(
            (previous) => {
                const next = new URLSearchParams(previous)
                next.delete('open')
                return next
            },
            { replace: true }
        )
    }, [searchParams, setSearchParams, entries, data])

    return (
        <PageContainer>
            <Header
                title="Operators"
                description="Entra directory members and their station access."
            />

            <DataBoundary
                query={{ isError, error, isLoading, data, refetch }}
                errorFallback="Failed to load operator directory"
                loading={<CardGridSkeleton />}
                empty={null}
                isEmpty={() => false}
            >
                {/* `Unavailable` is the STEADY state until an ARN tenant admin consents to
                    Graph directory read, so this is deliberately an informational note, not a
                    warning: a permanent alarm here would train people to ignore warnings, and
                    the raw Graph body does not belong in front of an operator. It has to say
                    SOMETHING though. Without it the page shows the saved assignments and
                    nothing else, and an admin reads a one-row list as "one person has access"
                    when the truth is "the directory could not be read" (2026-08-10 UI audit,
                    live on dev). So: state the scope of the list plainly, keep the 403 body
                    behind a disclosure for whoever escalates the consent. */}
                {data?.directoryStatus === 'Unavailable' && (
                    <Alert className="mb-4">
                        <ICON.info className="size-4" />
                        <AlertTitle>Showing saved assignments only</AlertTitle>
                        <AlertDescription>
                            <p>
                                The Entra directory could not be read, so anyone
                                who has access through an Entra group is missing
                                from this list.
                            </p>
                            {data?.directoryError && (
                                <details className="mt-1">
                                    <summary className="cursor-pointer text-xs font-medium text-muted-foreground">
                                        Technical detail
                                    </summary>
                                    <p className="mt-1 wrap-break-word text-xs text-muted-foreground">
                                        {data.directoryError}
                                    </p>
                                </details>
                            )}
                        </AlertDescription>
                    </Alert>
                )}
                <div className="mb-4 flex items-center gap-3">
                    <Input
                        className="flex-1"
                        value={search}
                        onChange={(event) => setSearch(event.target.value)}
                        placeholder="Search operators, roles, or stations..."
                        aria-label="Search operators"
                    />
                    <ViewToggle value={view} onChange={setView} />
                </div>

                {filteredEntries.length === 0 ? (
                    <Placeholder
                        icon={ICON.safety}
                        message={
                            data?.directoryStatus === 'Unavailable'
                                ? 'No one has signed in yet'
                                : 'No operators found in the directory'
                        }
                        detail={
                            search
                                ? 'Try a different operator, role, or station.'
                                : 'Operators appear here when they hold an Entra application role.'
                        }
                    />
                ) : view === 'table' ? (
                    <OperatorAssignmentTable
                        entries={filteredEntries}
                        directoryStatus={directoryStatus}
                        marketLabels={marketLabels}
                        onEdit={handleEdit}
                    />
                ) : (
                    <div className={CARD_GRID}>
                        {filteredEntries.map((entry) => (
                            <OperatorAssignmentCard
                                key={entry.objectId}
                                entry={entry}
                                directoryStatus={directoryStatus}
                                marketLabels={marketLabels}
                                onEdit={handleEdit}
                            />
                        ))}
                    </div>
                )}
            </DataBoundary>

            <AssignOperatorDialog
                open={dialogOpen}
                onOpenChange={setDialogOpen}
                entry={editingEntry}
            />
        </PageContainer>
    )
}
