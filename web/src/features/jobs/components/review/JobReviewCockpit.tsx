import { useState, useMemo, useCallback } from 'react'
import type { Job } from '../../types'
import { hasTraffic, isTerminal } from '../../types'
import { SpotlightCard } from '@/components/ui/aceternity/spotlight-card'
import { cn } from '@/lib/utils'
import type { BulletinReview } from '../../hooks/use-bulletin-review'
import { ReviewBulletin } from './ReviewBulletin'
import { ReviewVitals } from './ReviewVitals'
import { ReviewContextLens } from './ReviewContextLens'
import { ReviewSources } from './ReviewSources'
import { EarlierToday } from './EarlierToday'
import { AdjustMap } from './adjust/AdjustMap'
import { RundownTable, type RowDelta } from './adjust/RundownTable'
import { DriveTimesCard } from './adjust/DriveTimesCard'
import { useJobDelta } from '@/features/ledger/api/ledger-hooks'
import { AsAiredCard } from './AsAiredCard'
import { FallbackDetailsPanel } from '../canvas/FallbackDetailsPanel'
import { useAirSlot } from '../../hooks/use-air-slot'
import { useScriptVerification } from '../../hooks/use-script-verification'
import { SupersededTakeCard } from './SupersededTakeCard'
import { AirIntentLine } from './AirIntentLine'
export type FocusKey =
    | { readonly kind: 'sentence'; readonly index: number }
    | { readonly kind: 'incident'; readonly key: string }
    | { readonly kind: 'corridor'; readonly keys: readonly string[] }
    /** A flow corridor with no discrete incidents — identified by road name,
     *  because there is no incident key to point at. */
    | { readonly kind: 'road'; readonly road: string }
    | null

/**
 * The review cockpit, in two panels switched by the host's tabs:
 * `review` is the reading surface (vitals, audio-spine script, context
 * lens); `adjust` is the edit surface (incident board, evidence,
 * change composer). One instance hosts both so focus state and queued
 * changes survive tab switches; `useBulletinReview` lives in the host.
 */
export function JobReviewCockpit({
    job,
    review,
    panel,
    onShowLeftOut,
    onUseSupersededTakeAsDraft,
}: {
    readonly job: Job
    readonly review: BulletinReview
    readonly panel: 'review' | 'adjust'
    /** Clicking the vitals' "left out" count jumps to the Adjust tab. */
    readonly onShowLeftOut?: () => void
    /** Seeds a regeneration with the retained take's text. Supplied only for the
     *  statuses whose JobActions already offer a regeneration control, so the take
     *  card stays purely read-only on terminal jobs. */
    readonly onUseSupersededTakeAsDraft?: (text: string) => void
}) {
    // Event Ledger delta: how each incident moved vs the previous bulletin, keyed for the
    // rundown's row badges. Fail-soft — no delta, no badges.
    const { data: jobDelta } = useJobDelta(job.jobId)
    const deltaByKey = useMemo(() => {
        const map: Record<string, RowDelta> = {}
        for (const d of jobDelta?.deltas ?? [])
            map[d.key] = { status: d.status }
        return map
    }, [jobDelta])

    const clearedEvents = useMemo(
        () =>
            (jobDelta?.deltas ?? [])
                .filter((d) => d.status === 'Cleared')
                .map((d) => ({
                    key: d.key,
                    roadName: d.roadName,
                    headline: d.headline,
                })),
        [jobDelta]
    )
    const newSinceLast = useMemo(
        () => (jobDelta?.deltas ?? []).filter((d) => d.status === 'New').length,
        [jobDelta]
    )

    // -- Provenance + claim checking (shared with the decision surface) -------
    const { provenance, isModelProvenance, extendedBySentence } =
        useScriptVerification(job)

    // -- Focus coordination ----------------------------------------------------
    // Three sources, by priority: hover (transient) > pin (sticky, survives
    // scrolling away) > karaoke (ambient, follows playback).
    const [hoverKey, setHoverKey] = useState<FocusKey>(null)
    const [pinnedKey, setPinnedKey] = useState<FocusKey>(null)
    const [karaokeKey, setKaraokeKey] = useState<FocusKey>(null)
    const focusKey = hoverKey ?? pinnedKey ?? karaokeKey

    const handleSentenceHover = useCallback((index: number | null) => {
        setHoverKey(index != null ? { kind: 'sentence', index } : null)
    }, [])

    const handleIncidentHover = useCallback((key: string | null) => {
        setHoverKey(key != null ? { kind: 'incident', key } : null)
    }, [])

    const handleCorridorHover = useCallback(
        (keys: readonly string[] | null) => {
            setHoverKey(
                keys && keys.length > 0 ? { kind: 'corridor', keys } : null
            )
        },
        []
    )

    // Clicking an incident in the evidence index pins it: the highlight
    // persists while the operator scrolls the script. Click again to unpin.
    const handleIncidentPin = useCallback((key: string) => {
        setPinnedKey((prev) =>
            prev?.kind === 'incident' && prev.key === key
                ? null
                : { kind: 'incident', key }
        )
    }, [])

    // A flow-corridor row has no incident key, so its focus is the road name.
    const handleRoadHover = useCallback((road: string | null) => {
        setHoverKey(road != null ? { kind: 'road', road } : null)
    }, [])

    const handleRoadPin = useCallback((road: string) => {
        setPinnedKey((prev) =>
            prev?.kind === 'road' && prev.road === road
                ? null
                : { kind: 'road', road }
        )
    }, [])

    const handleActiveSentenceChange = useCallback((index: number | null) => {
        setKaraokeKey(index != null ? { kind: 'sentence', index } : null)
    }, [])

    // Derive which incident keys should be highlighted from focusKey
    const highlightedIncidentKeys = useMemo<readonly string[]>(() => {
        if (!focusKey) return []
        if (focusKey.kind === 'incident') return [focusKey.key]
        if (focusKey.kind === 'corridor') return focusKey.keys
        // kind === 'road' — a flow corridor carries no incidents to highlight
        if (focusKey.kind === 'road') return []
        // kind === 'sentence'
        const entry = provenance?.bySentence[focusKey.index]
        return entry?.incidentKeys ?? []
    }, [focusKey, provenance])

    // Keys currently included in the bulletin, for the lens's in/out tags
    const includedKeys = useMemo(
        () => review.includedRows.map((r) => r.incident.key),
        [review.includedRows]
    )

    // Sentences lit up by hovering an evidence row in the rail — an incident
    // row via its lineage, a flow-corridor row via its road name.
    const litSentenceIndexes = useMemo<readonly number[]>(() => {
        if (!provenance) return []
        if (focusKey?.kind === 'incident')
            return provenance.byIncidentKey[focusKey.key]?.sentenceIndexes ?? []
        if (focusKey?.kind === 'road')
            return (
                provenance.byCorridorRoad[focusKey.road.toLowerCase()]
                    ?.sentenceIndexes ?? []
            )
        return []
    }, [focusKey, provenance])

    // The broadcast slot this bulletin was generated for, when resolvable
    const airSlotAt = useAirSlot(job)

    return (
        <div className="@container/cockpit flex flex-col gap-5 pb-24">
            <ReviewVitals
                job={job}
                includedCount={review.includedCount}
                availableCount={review.availableRows.length}
                airSlotAt={airSlotAt}
                onShowLeftOut={panel === 'review' ? onShowLeftOut : undefined}
                newSinceLast={newSinceLast}
            />

            {/* What is going out and by when, at a glance — the fallback panel
                below carries the detailed explanation. */}
            <AirIntentLine job={job} />

            {/* Why this slot was covered by a standby bulletin — surfaced up
                top so the operator never has to infer it from the script. */}
            {job.status === 'FALLBACK_USED' && job.fallback && (
                <FallbackDetailsPanel
                    fallback={job.fallback}
                    targetDurationSec={job.targetDurationSec}
                />
            )}

            {/* Reading surface: script at a comfortable measure, one
                contextual evidence panel beside it. Stays mounted (hidden)
                while adjusting so audio playback survives tab switches. */}
            <div
                className={cn(
                    'grid gap-3 @4xl/cockpit:grid-cols-[minmax(0,1fr)_minmax(360px,460px)] @4xl/cockpit:items-start',
                    panel !== 'review' && 'hidden'
                )}
            >
                <div className="flex min-w-0 max-w-4xl flex-col gap-4">
                    <SpotlightCard className="p-5">
                        <ReviewBulletin
                            job={job}
                            canEdit={review.canEdit}
                            onRevoice={review.revoice}
                            isRevoicing={review.isRegenerating}
                            editingGuidance={job.editingGuidance}
                            sentences={provenance?.sentences}
                            bySentence={
                                extendedBySentence ?? provenance?.bySentence
                            }
                            onActiveSentenceChange={handleActiveSentenceChange}
                            onSentenceHover={handleSentenceHover}
                            verificationDegraded={
                                !isModelProvenance && hasTraffic(job)
                            }
                            litSentenceIndexes={litSentenceIndexes}
                            onPronunciationEdit={review.notePronunciationEdit}
                            pendingPronunciation={review.pendingPronunciation}
                        />
                    </SpotlightCard>
                    {/* Current read, then the read that was replaced, then
                        prior reads: the reading column stays chronological. */}
                    <SupersededTakeCard
                        job={job}
                        onUseAsDraft={onUseSupersededTakeAsDraft}
                    />
                    {/* The day's earlier bulletins for this station — generated
                        bulletins build on these, so show what aired before
                        (script + audio), right under the current script. */}
                    <EarlierToday job={job} />
                    {/* Non-traffic context sits under the script (not the
                        rail): weather, fuel and good-run claims laid out
                        across the reading column's width. */}
                    <ReviewSources job={job} />
                </div>

                {/* The rail must survive narrow studio windows: stacked
                        below the script under lg, sticky beside it from lg. */}
                <div className="flex flex-col gap-4 lg:sticky lg:top-28">
                    {/* Settled bulletins (terminal, or approved awaiting
                            publish) answer "what aired?" — lead with the
                            record. Approved isn't terminal but is a logged
                            human decision, so it gets the audit trail too. */}
                    {(isTerminal(job) || job.status === 'APPROVED') && (
                        <AsAiredCard job={job} />
                    )}
                    {/* Evidence first: the traffic index (incidents plus the
                            flow corridors that carry no incident) verifies the
                            claims that form the bulletin's spine. */}
                    {(!isTerminal(job) ||
                        (hasTraffic(job) &&
                            ((job.traffic?.incidents.length ?? 0) > 0 ||
                                (job.traffic?.corridors.length ?? 0) > 0))) && (
                        <ReviewContextLens
                            job={job}
                            focusKey={focusKey}
                            provenance={provenance}
                            modelProvenance={isModelProvenance}
                            includedKeys={includedKeys}
                            onIncidentHover={handleIncidentHover}
                            onIncidentPin={handleIncidentPin}
                            pinnedIncidentKey={
                                pinnedKey?.kind === 'incident'
                                    ? pinnedKey.key
                                    : null
                            }
                            onRoadHover={handleRoadHover}
                            onRoadPin={handleRoadPin}
                            pinnedRoad={
                                pinnedKey?.kind === 'road'
                                    ? pinnedKey.road
                                    : null
                            }
                        />
                    )}
                </div>
            </div>

            {panel === 'adjust' && (
                /* Edit surface: one ranked Rundown table on the left (road
                   on/off + order + per-incident picks, with flowing roads
                   folded in) and spatial context on the right. All edits batch
                   into the Revise dialog. */
                <div className="grid gap-3 @4xl/cockpit:grid-cols-[minmax(0,1fr)_minmax(280px,360px)]">
                    <div className="flex min-w-0 flex-col gap-3">
                        {/* Measured route times sit ABOVE the rundown: they are
                            market-level facts, where the rundown is per-road
                            detail. Read-only — a monitored route is configured
                            per station, not curated per bulletin. */}
                        <DriveTimesCard
                            routes={
                                hasTraffic(job)
                                    ? (job.traffic?.monitoredRoutes ?? [])
                                    : []
                            }
                        />
                        <RundownTable
                            deltaByKey={deltaByKey}
                            clearedEvents={clearedEvents}
                            corridors={
                                hasTraffic(job)
                                    ? (job.traffic?.corridors ?? [])
                                    : []
                            }
                            includedRows={review.includedRows}
                            availableRows={review.availableRows}
                            flowRoads={review.flowRoads}
                            canEdit={review.canEdit}
                            curation={
                                review.canEdit
                                    ? review.corridorCuration
                                    : undefined
                            }
                            isFlowSelected={review.isFlowSelected}
                            toggleFlowRoad={review.toggleFlowRoad}
                            onToggleInclude={review.toggleInclude}
                            provenance={provenance}
                            highlightedIncidentKeys={highlightedIncidentKeys}
                            pinnedIncidentKey={
                                pinnedKey?.kind === 'incident'
                                    ? pinnedKey.key
                                    : null
                            }
                            onIncidentHover={handleIncidentHover}
                            onCorridorHover={handleCorridorHover}
                        />
                    </div>
                    <div className="flex min-w-0 flex-col gap-3 lg:sticky lg:top-4">
                        {/* The map card grows to fill the column's leftover
                            height (the rundown table is the taller item), so
                            the coverage map gets the space instead of dead
                            space under a short card. */}
                        <div className="min-h-0 flex-1">
                            <AdjustMap
                                job={job}
                                includedRows={review.includedRows}
                                availableRows={review.availableRows}
                                highlightedIncidentKeys={
                                    highlightedIncidentKeys
                                }
                                onMarkerClick={handleIncidentPin}
                                focusedKey={
                                    pinnedKey?.kind === 'incident'
                                        ? pinnedKey.key
                                        : hoverKey?.kind === 'incident'
                                          ? hoverKey.key
                                          : hoverKey?.kind === 'corridor' &&
                                              hoverKey.keys.length === 1
                                            ? hoverKey.keys[0]
                                            : null
                                }
                            />
                        </div>
                    </div>
                </div>
            )}
        </div>
    )
}
