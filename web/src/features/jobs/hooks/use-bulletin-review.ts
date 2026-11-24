import {
    useState,
    useCallback,
    useMemo,
    useEffect,
    useRef,
    type Dispatch,
    type SetStateAction,
} from 'react'
import { toast } from 'sonner'
import { ApiError } from '@/lib/api/client'
import type { PendingPronunciation } from '../lib/script-marks'
import type { Job, JobIncident, FlowRoad } from '../types'
import { hasScript, hasTraffic } from '../types'
import { useRegenerateJob, useRejectJob } from '../api/job-hooks'
import { toReviewIncidentPayload } from '../lib/incidents/incident-editing'
import type {
    CorridorCuration,
    IncidentCorridorRef,
    IncidentRow,
} from '../lib/incidents/coverage-grouping'

export type {
    CorridorCuration,
    IncidentCorridorRef,
    IncidentRow,
} from '../lib/incidents/coverage-grouping'
export type ReviewChangeKind =
    | 'pronunciation'
    | 'incident-removed'
    | 'incident-added'
    | 'corridors-curated'
    | 'flow-curated'
    | 'voice'

/** A single pending change shown as a chip in the Requested-changes tray. */
export interface ReviewChange {
    readonly id: string
    readonly kind: ReviewChangeKind
    readonly label: string
    readonly detail?: string
    readonly removable: boolean
}

/**
 * A staged re-voice onto a DIFFERENT voice profile. The backend resolves the
 * profile's current version server-side at apply time, so the operator only
 * ever names the profile — the snapshot (voiceId/model/settings) they hear in
 * an audition and the one that lands on air cannot drift apart. `name`/`model`
 * are carried purely for the chip label and the staged badge in the dialog.
 */
export interface VoiceOverride {
    readonly profileId: string
    readonly name: string
    readonly model: string
}

/**
 * The cheapest pipeline that satisfies every queued change.
 *
 * Pronunciation rules and voice switches only need the audio made again
 * (`revoice`, TTS only). Evidence and curation changes need the script rebuilt
 * from the selected source data (`regenerate`, LLM + review + TTS, ~4x slower).
 * The most expensive queued change wins, and regenerate subsumes revoice, so a
 * mixed batch applies in one pass.
 */
export type ApplyKind = 'revoice' | 'regenerate' | null

const REVOICE_ONLY_KINDS: Record<string, true> = {
    pronunciation: true,
    voice: true,
}

export function applyKindFor(changes: readonly ReviewChange[]): ApplyKind {
    if (changes.length === 0) return null
    return changes.every((c) => REVOICE_ONLY_KINDS[c.kind] === true)
        ? 'revoice'
        : 'regenerate'
}
const EDITABLE_STATES: Record<string, true> = {
    WAITING_APPROVAL: true,
    AUDIO_READY: true,
    FAILED: true,
}
function shorten(text: string, max = 28): string {
    return text.length > max ? `${text.slice(0, max - 1)}…` : text
}

/**
 * The review-state contract: everything a review surface needs to read
 * or mutate the bulletin under review. Owned here; consumers import this
 * name instead of deriving it from the hook.
 */
export interface BulletinReview {
    readonly canEdit: boolean
    // script (verbatim re-voice)
    readonly revoice: (editedText: string, tonedScript?: string) => void
    /** RISK-31: text most recently submitted via `revoice`, so a FAILED-state
     *  retry can resubmit it verbatim instead of discarding the edit. Null
     *  once superseded by a full (non-revoice) regenerate. */
    readonly lastRevoiceScript: string | null
    // incidents
    readonly includedRows: IncidentRow[]
    readonly availableRows: IncidentRow[]
    readonly includedCount: number
    readonly toggleInclude: (key: string) => void
    // corridors
    readonly corridorCuration: CorridorCuration
    // flow / good runs (Flow tab)
    readonly flowRoads: readonly FlowRoad[]
    readonly isFlowSelected: (roadName: string) => boolean
    readonly toggleFlowRoad: (roadName: string) => void
    readonly flowSelectedCount: number
    /** Queue a dictionary rule the operator just saved, pending on this bulletin. */
    readonly notePronunciationEdit: (word: string, replacement: string) => void
    /** Rules saved during this review, not yet applied to this bulletin. */
    readonly pendingPronunciation: readonly PendingPronunciation[]
    // batch
    readonly changes: ReviewChange[]
    readonly isDirty: boolean
    readonly removeChange: (id: string) => void
    /** A staged re-voice onto a different voice profile; null = keep the job's voice. */
    readonly voiceOverride: VoiceOverride | null
    /** Stage (or clear) a different voice for this bulletin's next apply. */
    readonly setVoiceOverride: (override: VoiceOverride | null) => void
    /** Cheapest pipeline that satisfies every queued change; null when nothing is queued. */
    readonly applyKind: ApplyKind
    readonly regenerate: (revoiceOnly?: boolean, tonedScript?: string) => void
    readonly isRegenerating: boolean
    /** The apply currently in flight, or null. Spans the WHOLE reject →
     *  regenerate handoff (see `useMutationOrchestration`), so a review surface
     *  can show one continuous working state instead of flashing through the
     *  transient REJECTED status the handoff passes through. */
    readonly applyInFlight: 'revoice' | 'regenerate' | null
}

function useResetOnChange<T>(
    signature: string,
    initialValue: T
): [T, Dispatch<SetStateAction<T>>] {
    const [value, setValue] = useState(initialValue)
    const [previousSignature, setPreviousSignature] = useState(signature)
    if (signature !== previousSignature) {
        setPreviousSignature(signature)
        setValue(initialValue)
    }
    return [value, setValue]
}

interface IncidentSelection {
    readonly byKey: Map<string, JobIncident>
    readonly initialIncludedSet: Set<string>
    readonly lists: { included: string[]; available: string[] }
    readonly includedRows: IncidentRow[]
    readonly availableRows: IncidentRow[]
    readonly includedCount: number
    readonly incidentsDirty: boolean
    readonly toggleInclude: (key: string) => void
}

function useIncidentSelection(job: Job): IncidentSelection {
    const { byKey, initialIncluded, initialAvailable } = useMemo(() => {
        // `'selectedIncidents' in job` here is a TYPE narrowing, not a data
        // guard: `hasTraffic` widens to JobWithTraffic, whose HERE_FETCHED
        // member does not declare the field at all. At runtime the key is
        // always present (JobSchema maps it `?? []`) — so never copy this
        // shape as a "has data" check. That confusion is what made
        // `isCompleteJob`'s `field in candidate` a silent no-op and produced
        // the AsAiredCard crash.
        const selected =
            hasTraffic(job) &&
            'selectedIncidents' in job &&
            job.selectedIncidents != null
                ? job.selectedIncidents
                : []
        const pool =
            hasTraffic(job) && job.traffic?.incidents
                ? job.traffic.incidents
                : []
        const map = new Map<string, JobIncident>()
        for (const incident of pool) map.set(incident.key, incident)
        for (const incident of selected) {
            if (!map.has(incident.key)) map.set(incident.key, incident)
        }

        const selectedCorridorKeys = new Set(
            (hasTraffic(job) ? (job.traffic?.corridors ?? []) : [])
                .filter((corridor) => corridor.isSelected)
                .flatMap((corridor) => corridor.incidentKeys ?? [])
        )
        const includedKeys =
            selectedCorridorKeys.size > 0
                ? selected
                      .filter((incident) =>
                          selectedCorridorKeys.has(incident.key)
                      )
                      .map((incident) => incident.key)
                : selected.map((incident) => incident.key)
        const includedSet = new Set(includedKeys)
        return {
            byKey: map,
            initialIncluded: includedKeys,
            initialAvailable: [...map.keys()].filter(
                (key) => !includedSet.has(key)
            ),
        }
    }, [job])

    const incidentSignature =
        initialIncluded.join(',') + '|' + initialAvailable.join(',')
    const [lists, setLists] = useResetOnChange(incidentSignature, {
        included: initialIncluded,
        available: initialAvailable,
    })

    const corridorByKey = useMemo(() => {
        const map = new Map<string, IncidentCorridorRef>()
        const corridors = hasTraffic(job) ? (job.traffic?.corridors ?? []) : []
        for (const corridor of corridors) {
            for (const key of corridor.incidentKeys ?? []) {
                const existing = map.get(key)
                if (!existing || corridor.rank < existing.rank) {
                    map.set(key, {
                        roadName: corridor.roadName,
                        rank: corridor.rank,
                        isSelected: corridor.isSelected,
                        magnitude: corridor.magnitude ?? null,
                        severity: corridor.severity,
                    })
                }
            }
        }
        return map
    }, [job])

    const toRow = useCallback(
        (
            key: string,
            rank: number | null,
            isFirst: boolean,
            isLast: boolean
        ): IncidentRow | null => {
            const incident = byKey.get(key)
            if (!incident) return null
            return {
                incident,
                rank,
                included: rank !== null,
                isFirst,
                isLast,
                corridor: corridorByKey.get(key) ?? null,
            }
        },
        [byKey, corridorByKey]
    )

    const includedRows = useMemo<IncidentRow[]>(() => {
        const n = lists.included.length
        return lists.included
            .map((key, index) =>
                toRow(key, index + 1, index === 0, index === n - 1)
            )
            .filter((row): row is IncidentRow => row !== null)
    }, [lists.included, toRow])

    const availableRows = useMemo<IncidentRow[]>(
        () =>
            lists.available
                .map((key) => toRow(key, null, false, false))
                .filter((row): row is IncidentRow => row !== null),
        [lists.available, toRow]
    )

    const initialIncludedSet = useMemo(
        () => new Set(initialIncluded),
        [initialIncluded]
    )
    const sameMembership =
        lists.included.length === initialIncluded.length &&
        lists.included.every((key) => initialIncludedSet.has(key))
    const incidentsDirty =
        !sameMembership ||
        lists.included.some((key, index) => key !== initialIncluded[index])

    const toggleInclude = useCallback(
        (key: string) => {
            setLists((previous) => {
                if (previous.included.includes(key)) {
                    return {
                        included: previous.included.filter(
                            (includedKey) => includedKey !== key
                        ),
                        available: [key, ...previous.available],
                    }
                }
                const machineRank = initialIncluded.indexOf(key)
                const insertAt =
                    machineRank < 0
                        ? previous.included.length
                        : previous.included.filter(
                              (includedKey) =>
                                  initialIncluded.indexOf(includedKey) >= 0 &&
                                  initialIncluded.indexOf(includedKey) <
                                      machineRank
                          ).length
                const included = [...previous.included]
                included.splice(insertAt, 0, key)
                return {
                    included,
                    available: previous.available.filter(
                        (availableKey) => availableKey !== key
                    ),
                }
            })
        },
        [initialIncluded, setLists]
    )

    return {
        byKey,
        initialIncludedSet,
        lists,
        includedRows,
        availableRows,
        includedCount: lists.included.length,
        incidentsDirty,
        toggleInclude,
    }
}

interface CorridorSelection {
    readonly machineCorridorOrder: string[]
    readonly corridorOrder: string[] | null
    readonly corridorCuration: CorridorCuration
    readonly curationDirty: boolean
}

function useCorridorCuration(job: Job): CorridorSelection {
    const machineCorridorOrder = useMemo<string[]>(() => {
        const corridors = hasTraffic(job) ? (job.traffic?.corridors ?? []) : []
        const seenRoads = new Set<string>()
        const explicitlySelected = corridors.filter(
            (corridor) => corridor.isSelected
        )
        const candidates =
            explicitlySelected.length > 0
                ? explicitlySelected
                : corridors.slice(0, 10)
        return candidates
            .slice()
            .sort((a, b) => a.rank - b.rank)
            .map((corridor) => corridor.roadName)
            .filter((roadName) => {
                if (seenRoads.has(roadName)) return false
                seenRoads.add(roadName)
                return true
            })
    }, [job])
    const corridorSignature = machineCorridorOrder.join(',')
    const [corridorOrder, setCorridorOrder] = useResetOnChange(
        corridorSignature,
        null as string[] | null
    )
    const machineOrderRef = useRef(machineCorridorOrder)
    useEffect(() => {
        machineOrderRef.current = machineCorridorOrder
    })

    const toggleCorridor = useCallback(
        (roadName: string) => {
            setCorridorOrder((previous) => {
                const base = previous ?? machineOrderRef.current
                if (base.includes(roadName)) {
                    if (base.length === 1) return previous
                    return base.filter((road) => road !== roadName)
                }
                return [...base, roadName]
            })
        },
        [setCorridorOrder]
    )
    const moveCorridor = useCallback(
        (roadName: string, delta: -1 | 1) => {
            setCorridorOrder((previous) => {
                const base = [...(previous ?? machineOrderRef.current)]
                const index = base.indexOf(roadName)
                const nextIndex = index + delta
                if (index < 0 || nextIndex < 0 || nextIndex >= base.length)
                    return previous
                ;[base[index], base[nextIndex]] = [base[nextIndex], base[index]]
                return base
            })
        },
        [setCorridorOrder]
    )
    const moveCorridorUp = useCallback(
        (roadName: string) => moveCorridor(roadName, -1),
        [moveCorridor]
    )
    const moveCorridorDown = useCallback(
        (roadName: string) => moveCorridor(roadName, 1),
        [moveCorridor]
    )
    const reset = useCallback(() => setCorridorOrder(null), [setCorridorOrder])
    const curationDirty =
        corridorOrder !== null &&
        corridorOrder.length > 0 &&
        (corridorOrder.length !== machineCorridorOrder.length ||
            corridorOrder.some(
                (road, index) => road !== machineCorridorOrder[index]
            ))
    const corridorCuration = useMemo<CorridorCuration>(() => {
        const effective = corridorOrder ?? machineCorridorOrder
        return {
            order: corridorOrder,
            effective,
            isOn: (roadName) => effective.includes(roadName),
            toggle: toggleCorridor,
            moveUp: moveCorridorUp,
            moveDown: moveCorridorDown,
            reset,
        }
    }, [
        corridorOrder,
        machineCorridorOrder,
        toggleCorridor,
        moveCorridorUp,
        moveCorridorDown,
        reset,
    ])
    return {
        machineCorridorOrder,
        corridorOrder,
        corridorCuration,
        curationDirty,
    }
}

interface FlowSelection {
    readonly flowRoads: readonly FlowRoad[]
    readonly flowSelection: string[]
    readonly initialFlowSelection: string[]
    readonly isFlowSelected: (roadName: string) => boolean
    readonly toggleFlowRoad: (roadName: string) => void
    readonly resetFlow: () => void
    readonly flowDirty: boolean
}

function useFlowSelection(job: Job): FlowSelection {
    const flowRoads = useMemo<readonly FlowRoad[]>(
        () => (hasTraffic(job) ? (job.traffic?.flowRoads ?? []) : []),
        [job]
    )
    const initialFlowSelection = useMemo(
        () =>
            flowRoads
                .filter((flowRoad) => flowRoad.isSelected)
                .map((flowRoad) => flowRoad.roadName),
        [flowRoads]
    )
    const flowSignature =
        initialFlowSelection.join(',') +
        '|' +
        flowRoads.map((flowRoad) => flowRoad.roadName).join(',')
    const [flowSelection, setFlowSelection] = useResetOnChange(
        flowSignature,
        initialFlowSelection
    )
    const toggleFlowRoad = useCallback(
        (roadName: string) => {
            setFlowSelection((previous) =>
                previous.includes(roadName)
                    ? previous.filter((road) => road !== roadName)
                    : [...previous, roadName]
            )
        },
        [setFlowSelection]
    )
    const resetFlow = useCallback(
        () => setFlowSelection(initialFlowSelection),
        [setFlowSelection, initialFlowSelection]
    )
    const isFlowSelected = useCallback(
        (roadName: string) => flowSelection.includes(roadName),
        [flowSelection]
    )
    const flowDirty = useMemo(() => {
        if (flowSelection.length !== initialFlowSelection.length) return true
        const initial = new Set(initialFlowSelection)
        return flowSelection.some((road) => !initial.has(road))
    }, [flowSelection, initialFlowSelection])
    return {
        flowRoads,
        flowSelection,
        initialFlowSelection,
        isFlowSelected,
        toggleFlowRoad,
        resetFlow,
        flowDirty,
    }
}

interface ReviewChangesOptions {
    readonly lists: IncidentSelection['lists']
    readonly initialIncludedSet: Set<string>
    readonly byKey: Map<string, JobIncident>
    readonly corridorOrder: string[] | null
    readonly machineCorridorOrder: string[]
    readonly curationDirty: boolean
    readonly flowSelection: string[]
    readonly initialFlowSelection: string[]
    readonly flowDirty: boolean
    readonly pronunciationEdits: PendingPronunciation[]
    readonly removePronunciationEdit: (word: string) => void
    readonly toggleInclude: (key: string) => void
    readonly resetCorridors: () => void
    readonly resetFlow: () => void
    readonly voiceOverride: VoiceOverride | null
    readonly clearVoiceOverride: () => void
}

function useReviewChanges(options: ReviewChangesOptions) {
    const {
        lists,
        initialIncludedSet,
        byKey,
        corridorOrder,
        machineCorridorOrder,
        curationDirty,
        flowSelection,
        initialFlowSelection,
        flowDirty,
        pronunciationEdits,
        removePronunciationEdit,
        toggleInclude,
        resetCorridors,
        resetFlow,
        voiceOverride,
        clearVoiceOverride,
    } = options
    const changes = useMemo<ReviewChange[]>(() => {
        const result: ReviewChange[] = []
        for (const key of lists.available) {
            if (initialIncludedSet.has(key)) {
                result.push({
                    id: `excl-${key}`,
                    kind: 'incident-removed',
                    label: `Dropped ${shorten(byKey.get(key)?.headline ?? key)}`,
                    removable: true,
                })
            }
        }
        for (const key of lists.included) {
            if (!initialIncludedSet.has(key)) {
                result.push({
                    id: `add-${key}`,
                    kind: 'incident-added',
                    label: `Added ${shorten(byKey.get(key)?.headline ?? key)}`,
                    removable: true,
                })
            }
        }
        if (curationDirty && corridorOrder) {
            const dropped = machineCorridorOrder.filter(
                (road) => !corridorOrder.includes(road)
            ).length
            result.push({
                id: 'corridors',
                kind: 'corridors-curated',
                label: `Curated corridors: ${corridorOrder.length} on air${dropped > 0 ? `, ${dropped} dropped` : ''}`,
                detail: 'applies to a fresh traffic check',
                removable: true,
            })
        }
        if (flowDirty) {
            const dropped = initialFlowSelection.filter(
                (road) => !flowSelection.includes(road)
            ).length
            const added = flowSelection.filter(
                (road) => !initialFlowSelection.includes(road)
            ).length
            result.push({
                id: 'flow',
                kind: 'flow-curated',
                label: `Curated flowing roads: ${flowSelection.length} on air${added > 0 ? `, ${added} added` : ''}${dropped > 0 ? `, ${dropped} dropped` : ''}`,
                detail: 'applies to a fresh traffic check',
                removable: true,
            })
        }
        // A dictionary rule the operator changed during THIS review. The rule is
        // already saved (a library asset stands on its own), but the bulletin in
        // front of them still carries the old audio, so it queues here with
        // everything else and one apply re-voices the lot.
        for (const edit of pronunciationEdits) {
            result.push({
                id: `pron-${edit.word.toLowerCase()}`,
                kind: 'pronunciation',
                label: `Pronounce ${edit.word}`,
                detail: `now says “${edit.replacement}”`,
                removable: true,
            })
        }
        // A staged different voice is its own queue entry: it must survive in
        // the tray like any other change, and removing its chip is the discard
        // path that unstages the override for the next apply.
        if (voiceOverride) {
            result.push({
                id: 'voice',
                kind: 'voice',
                label: `Voice → ${voiceOverride.name}`,
                removable: true,
            })
        }
        return result
    }, [
        lists,
        initialIncludedSet,
        byKey,
        pronunciationEdits,
        curationDirty,
        corridorOrder,
        machineCorridorOrder,
        flowDirty,
        flowSelection,
        initialFlowSelection,
        voiceOverride,
    ])
    const removeChange = useCallback(
        (id: string) => {
            if (id === 'corridors') resetCorridors()
            else if (id === 'flow') resetFlow()
            else if (id.startsWith('excl-'))
                toggleInclude(id.slice('excl-'.length))
            else if (id.startsWith('add-'))
                toggleInclude(id.slice('add-'.length))
            // Drops the change from THIS bulletin only — the dictionary rule
            // stays saved, because it is a library edit the operator made
            // deliberately and other stations may already depend on it.
            else if (id.startsWith('pron-'))
                removePronunciationEdit(id.slice('pron-'.length))
            // The voice override is bulletin-local by definition: the station
            // schedule keeps its configured voice, so removing the chip fully
            // unstages it.
            else if (id === 'voice') clearVoiceOverride()
        },
        [
            resetCorridors,
            resetFlow,
            toggleInclude,
            removePronunciationEdit,
            clearVoiceOverride,
        ]
    )
    return { changes, isDirty: changes.length > 0, removeChange }
}

interface MutationOptions {
    readonly job: Job
    readonly isDirty: boolean
    readonly incidentsDirty: boolean
    readonly lists: IncidentSelection['lists']
    readonly curationDirty: boolean
    readonly corridorOrder: string[] | null
    readonly flowDirty: boolean
    readonly flowSelection: string[]
    readonly currentScript: string
    readonly refreshPronunciation: boolean
    readonly voiceOverride: VoiceOverride | null
    readonly clearVoiceOverride: () => void
    readonly clearPronunciationEdits: () => void
}

/** An apply the operator submitted, and the status it was submitted FROM. */
interface ApplyingState {
    readonly kind: 'revoice' | 'regenerate'
    readonly from: Job['status']
}

function useMutationOrchestration(options: MutationOptions) {
    const {
        job,
        isDirty,
        incidentsDirty,
        lists,
        curationDirty,
        corridorOrder,
        flowDirty,
        flowSelection,
        currentScript,
        refreshPronunciation,
        voiceOverride,
        clearVoiceOverride,
        clearPronunciationEdits,
    } = options
    const regenerateMutation = useRegenerateJob()
    const rejectMutation = useRejectJob()
    const [lastRevoiceScript, setLastRevoiceScript] = useState<string | null>(
        null
    )
    // An apply is TWO requests: the reject that frees the job, then the
    // regenerate that queues it. Between them the job is REJECTED — a TERMINAL
    // status — so for that instant every status-driven surface swapped the
    // review workspace for the as-aired record card, and swapped again during
    // the settle gap before the refetch landed. This latch spans the whole
    // handoff: it is set on submit and cleared only once the job has genuinely
    // moved somewhere else (a pipeline status, FAILED) or the apply failed. A
    // REJECTED we caused ourselves is part of the apply, not a destination.
    const [applying, setApplying] = useState<ApplyingState | null>(null)
    if (applying && job.status !== applying.from && job.status !== 'REJECTED')
        setApplying(null)
    const regenerate = useCallback(
        async (revoiceOnly = false, tonedScript?: string) => {
            if (!isDirty || regenerateMutation.isPending) return
            setApplying({
                kind: revoiceOnly ? 'revoice' : 'regenerate',
                from: job.status,
            })
            const editedIncidents = incidentsDirty
                ? toReviewIncidentPayload(lists.included, lists.available)
                : undefined
            // The backend only regenerates a REJECTED (or FAILED) job —
            // `JobService.cs:592` returns OnlyRejectedCanRegenerate otherwise —
            // so the reject genuinely has to land first and the pair cannot be
            // reordered. It also cannot be undone, so when the reject commits
            // and the regenerate then fails we must say exactly that: the old
            // message only mentioned regeneration, leaving the operator with a
            // rejected bulletin, nothing queued, and no idea why.
            let rejected = false
            try {
                if (job.status === 'WAITING_APPROVAL') {
                    const notes =
                        incidentsDirty || curationDirty || flowDirty
                            ? 'Source selection changed in review'
                            : 'Revision requested from review'
                    await rejectMutation.mutateAsync({
                        jobId: job.jobId,
                        reasonCode: 'RevisionRequested',
                        notes,
                    })
                    rejected = true
                }
                await regenerateMutation.mutateAsync({
                    jobId: job.jobId,
                    editedIncidents,
                    selectedCorridors:
                        curationDirty && corridorOrder
                            ? [...corridorOrder]
                            : undefined,
                    selectedGoodRuns: flowDirty
                        ? [...flowSelection]
                        : undefined,
                    revoiceOnly,
                    refreshPronunciation,
                    tonedScript,
                    // A staged different voice rides both doors. Undefined is
                    // dropped by JSON serialization, so an unstaged override
                    // never reaches the wire.
                    voiceProfileId: voiceOverride?.profileId,
                })
                toast.success(
                    revoiceOnly
                        ? 'Re-voicing bulletin'
                        : 'Bulletin queued for regeneration'
                )
                // Clear staged changes only after the regenerate request succeeds.
                // A rejected handoff can fail before regeneration starts, so keep
                // every staged value available for the retry.
                clearPronunciationEdits()
                clearVoiceOverride()
                if (!revoiceOnly) setLastRevoiceScript(null)
            } catch (err) {
                setApplying(null)
                const detail =
                    err instanceof ApiError && err.status === 403
                        ? `You're not assigned to ${job.market}`
                        : err instanceof Error
                          ? err.message
                          : 'Failed to regenerate bulletin'
                if (rejected) {
                    // The job is REJECTED by now, so pressing the action again
                    // skips the reject step and retries the regeneration.
                    toast.error(
                        `This bulletin was rejected but regeneration did not start — try again. (${detail})`
                    )
                } else {
                    toast.error(detail)
                }
            }
        },
        [
            isDirty,
            regenerateMutation,
            rejectMutation,
            incidentsDirty,
            lists,
            job.jobId,
            job.market,
            job.status,
            refreshPronunciation,
            curationDirty,
            flowDirty,
            corridorOrder,
            flowSelection,
            voiceOverride,
            clearVoiceOverride,
            clearPronunciationEdits,
        ]
    )
    const revoice = useCallback(
        async (editedText: string, tonedScript?: string) => {
            const next = editedText.trim()
            // Curation the operator queued beside the edit (reordered/excluded incidents,
            // corridor order, flow picks) MUST ride along — see the payload below. It also
            // counts as a real change here: re-voicing with only curation queued used to fall
            // through this guard and do nothing, leaving the operator's edits pending against
            // a job that then moved status, so the next apply answered 409.
            const curationQueued = incidentsDirty || curationDirty || flowDirty
            // Tones alone are a real change: the words can be untouched while the
            // delivery is not, so the no-op guard has to look at all three.
            if (
                !next ||
                (next === currentScript.trim() &&
                    !tonedScript &&
                    !curationQueued) ||
                regenerateMutation.isPending
            )
                return
            setLastRevoiceScript(next)
            setApplying({ kind: 'revoice', from: job.status })
            try {
                if (job.status === 'WAITING_APPROVAL') {
                    await rejectMutation.mutateAsync({
                        jobId: job.jobId,
                        reasonCode: 'RevisionRequested',
                        notes: 'Script edited and re-voiced',
                    })
                }
                await regenerateMutation.mutateAsync({
                    jobId: job.jobId,
                    editedScript: next,
                    tonedScript,
                    revoiceOnly: true,
                    // The script editor is a second door onto the same re-voice. A rule
                    // saved during this review must ride along here too, or editing the
                    // words silently discards the pronunciation fix queued beside them.
                    refreshPronunciation,
                    // Same reason, for the operator's traffic curation: this door dropped
                    // editedIncidents/selectedCorridors/selectedGoodRuns entirely, so
                    // re-voicing after reordering or excluding incidents discarded that work
                    // without saying so. The exclusions persist on the job for the next
                    // regeneration even though a verbatim re-voice does not re-draft the words.
                    editedIncidents: incidentsDirty
                        ? toReviewIncidentPayload(
                              lists.included,
                              lists.available
                          )
                        : undefined,
                    selectedCorridors:
                        curationDirty && corridorOrder
                            ? [...corridorOrder]
                            : undefined,
                    selectedGoodRuns: flowDirty
                        ? [...flowSelection]
                        : undefined,
                    // The script editor is a second door onto the same re-voice;
                    // a staged different voice must ride along here too.
                    voiceProfileId: voiceOverride?.profileId,
                })
                toast.success('Re-voicing bulletin')
                clearPronunciationEdits()
                clearVoiceOverride()
            } catch (err) {
                setApplying(null)
                if (err instanceof ApiError && err.status === 403)
                    toast.error(`You're not assigned to ${job.market}`)
                else
                    toast.error(
                        err instanceof Error
                            ? err.message
                            : 'Failed to re-voice bulletin'
                    )
            }
        },
        [
            currentScript,
            regenerateMutation,
            rejectMutation,
            job.jobId,
            job.market,
            job.status,
            refreshPronunciation,
            incidentsDirty,
            curationDirty,
            flowDirty,
            lists,
            corridorOrder,
            flowSelection,
            voiceOverride,
            clearPronunciationEdits,
            clearVoiceOverride,
        ]
    )
    return {
        revoice,
        lastRevoiceScript,
        regenerate,
        isRegenerating: regenerateMutation.isPending,
        applyInFlight: applying?.kind ?? null,
    }
}
/**
 * Central review state for a bulletin. Incidents come from the full backend
 * pool (`traffic.incidents`); the subset chosen for the script lives in the
 * ordered `included` list (airing order = rank), the rest in `available`.
 * The operator toggles incidents in and out on the Coverage Board (ordering
 * is corridor-level via `corridorCuration`); those source changes batch into a
 * single regenerate to respect the tight on-air window.
 */
export function useBulletinReview(job: Job): BulletinReview {
    const canEdit = EDITABLE_STATES[job.status] === true
    const incidents = useIncidentSelection(job)
    const corridors = useCorridorCuration(job)
    const flow = useFlowSelection(job)
    // Queued rules are consumed only after the regenerate request succeeds.
    // Key on jobId, not status: the reject -> regenerate handoff briefly
    // reports REJECTED, and a status key would wipe the queue before retry.
    // Failed requests keep the queue so the operator can submit it again.
    const [pronunciationEdits, setPronunciationEdits] = useResetOnChange<
        PendingPronunciation[]
    >(job.jobId, [])
    const clearPronunciationEdits = useCallback(
        () => setPronunciationEdits([]),
        [setPronunciationEdits]
    )
    const notePronunciationEdit = useCallback(
        (word: string, replacement: string) => {
            setPronunciationEdits((prev) => [
                ...prev.filter(
                    (e) => e.word.toLowerCase() !== word.toLowerCase()
                ),
                { word, replacement },
            ])
        },
        [setPronunciationEdits]
    )
    const removePronunciationEdit = useCallback(
        (word: string) => {
            setPronunciationEdits((prev) =>
                prev.filter((e) => e.word.toLowerCase() !== word.toLowerCase())
            )
        },
        [setPronunciationEdits]
    )
    // A staged different voice for this bulletin. Keyed on jobId ONLY (not
    // status like pronunciationEdits): the reject-before-regenerate pair moves
    // the job off WAITING_APPROVAL before the regenerate lands, so a
    // status-keyed reset would wipe the choice mid-apply, and a failed
    // regenerate would then retry on the job's voice without saying so. The
    // apply clears it explicitly on success; a different job clears it here.
    const [voiceOverride, setVoiceOverride] =
        useResetOnChange<VoiceOverride | null>(job.jobId, null)
    const clearVoiceOverride = useCallback(
        () => setVoiceOverride(null),
        [setVoiceOverride]
    )
    const changes = useReviewChanges({
        lists: incidents.lists,
        initialIncludedSet: incidents.initialIncludedSet,
        byKey: incidents.byKey,
        corridorOrder: corridors.corridorOrder,
        machineCorridorOrder: corridors.machineCorridorOrder,
        curationDirty: corridors.curationDirty,
        flowSelection: flow.flowSelection,
        initialFlowSelection: flow.initialFlowSelection,
        flowDirty: flow.flowDirty,
        pronunciationEdits,
        removePronunciationEdit,
        toggleInclude: incidents.toggleInclude,
        resetCorridors: corridors.corridorCuration.reset,
        resetFlow: flow.resetFlow,
        voiceOverride,
        clearVoiceOverride,
    })
    const currentScript = hasScript(job) ? (job.script?.text ?? '') : ''
    const mutations = useMutationOrchestration({
        job,
        isDirty: changes.isDirty,
        incidentsDirty: incidents.incidentsDirty,
        lists: incidents.lists,
        curationDirty: corridors.curationDirty,
        corridorOrder: corridors.corridorOrder,
        flowDirty: flow.flowDirty,
        flowSelection: flow.flowSelection,
        currentScript,
        refreshPronunciation: pronunciationEdits.length > 0,
        voiceOverride,
        clearVoiceOverride,
        clearPronunciationEdits,
    })

    // `pronunciationEdits` is state, so its identity is already stable between
    // renders. It feeds the `buildScriptMarks` memo, which walks the whole script
    // against every rule — deriving a fresh collection here would rebuild every
    // mark on every keystroke and re-render the teleprompter mid-playback.

    return {
        canEdit,
        revoice: mutations.revoice,
        lastRevoiceScript: mutations.lastRevoiceScript,
        includedRows: incidents.includedRows,
        availableRows: incidents.availableRows,
        includedCount: incidents.includedCount,
        toggleInclude: incidents.toggleInclude,
        corridorCuration: corridors.corridorCuration,
        flowRoads: flow.flowRoads,
        isFlowSelected: flow.isFlowSelected,
        toggleFlowRoad: flow.toggleFlowRoad,
        flowSelectedCount: flow.flowSelection.length,
        notePronunciationEdit,
        pendingPronunciation: pronunciationEdits,
        changes: changes.changes,
        isDirty: changes.isDirty,
        removeChange: changes.removeChange,
        voiceOverride,
        setVoiceOverride,
        applyKind: applyKindFor(changes.changes),
        regenerate: mutations.regenerate,
        isRegenerating: mutations.isRegenerating,
        applyInFlight: mutations.applyInFlight,
    }
}
