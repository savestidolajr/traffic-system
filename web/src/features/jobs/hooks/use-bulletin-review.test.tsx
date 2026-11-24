// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useBulletinReview } from './use-bulletin-review'
import { apiClient } from '@/lib/api/client'
import type { Job } from '../types'
import { z } from 'zod/v4'

const RegenerateBody = z.object({
    revoiceOnly: z.boolean().optional(),
    refreshPronunciation: z.boolean().optional(),
    editedScript: z.string().optional(),
    editedIncidents: z
        .array(z.object({ incidentId: z.string(), included: z.boolean() }))
        .optional(),
    voiceProfileId: z.string().optional(),
})
type RegeneratePayload = z.infer<typeof RegenerateBody>

vi.mock('@/lib/api/client', () => ({
    apiClient: { post: vi.fn().mockResolvedValue({ data: {} }) },
    ApiError: class ApiError extends Error {
        readonly status: number | undefined
        constructor(message: string, status?: number) {
            super(message)
            this.status = status
        }
    },
}))

afterEach(() => {
    cleanup()
    vi.clearAllMocks()
})

function makeWaitingJob(overrides: Partial<Job> = {}): Job {
    return {
        jobId: 'job-1',
        market: 'Sydney',
        status: 'WAITING_APPROVAL',
        script: { text: 'Original script text.' },
        traffic: { incidents: [], corridors: [], flowRoads: [] },
        selectedIncidents: [],
        ...overrides,
    } as unknown as Job
}

function makeWaitingJobWithIncidents(): Job {
    return makeWaitingJob({
        traffic: {
            incidents: [
                { key: 'inc-1', roadName: 'Monash Freeway' },
                { key: 'inc-2', roadName: 'Punt Road' },
            ],
            corridors: [],
            flowRoads: [],
        },
        selectedIncidents: [
            { key: 'inc-1', roadName: 'Monash Freeway' },
            { key: 'inc-2', roadName: 'Punt Road' },
        ],
    } as unknown as Partial<Job>)
}

function makeWaitingJobWithSameRoadCorridors(): Job {
    return makeWaitingJob({
        traffic: {
            incidents: [],
            corridors: [
                { roadName: 'Bruce Highway', rank: 1, isSelected: true },
                { roadName: 'Bruce Highway', rank: 2, isSelected: true },
                { roadName: 'Mulgrave Road', rank: 3, isSelected: true },
            ],
            flowRoads: [],
        },
    } as unknown as Partial<Job>)
}

function renderReview(job: Job) {
    const queryClient = new QueryClient()
    const wrapper = ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={queryClient}>
            {children}
        </QueryClientProvider>
    )
    return renderHook(({ j }: { j: Job }) => useBulletinReview(j), {
        wrapper,
        initialProps: { j: job },
    })
}

// RISK-31: a FAILED-state retry must know whether the prior attempt was a
// verbatim re-voice (and what text it carried) so it can resubmit revoiceOnly
// instead of silently falling through to a full regeneration that discards
// the operator's edit.
describe('useBulletinReview — lastRevoiceScript (RISK-31)', () => {
    it('starts null before any re-voice is submitted', () => {
        const { result } = renderReview(makeWaitingJob())
        expect(result.current.lastRevoiceScript).toBeNull()
    })

    it('records the submitted text when a verbatim re-voice is requested', async () => {
        const { result } = renderReview(makeWaitingJob())

        await act(async () => {
            result.current.revoice('A brand new edited script.')
        })

        await waitFor(() =>
            expect(result.current.lastRevoiceScript).toBe(
                'A brand new edited script.'
            )
        )
    })

    it('ignores a re-voice submission identical to the current script', async () => {
        const { result } = renderReview(makeWaitingJob())

        await act(async () => {
            result.current.revoice('Original script text.')
        })

        expect(result.current.lastRevoiceScript).toBeNull()
    })

    it('clears the pending re-voice text once a full regenerate succeeds', async () => {
        const { result } = renderReview(makeWaitingJobWithIncidents())

        await act(async () => {
            result.current.revoice('A brand new edited script.')
        })
        await waitFor(() =>
            expect(result.current.lastRevoiceScript).toBe(
                'A brand new edited script.'
            )
        )

        act(() => {
            result.current.toggleInclude('inc-2')
        })
        await waitFor(() => expect(result.current.isDirty).toBe(true))

        await act(async () => {
            result.current.regenerate(false)
        })

        await waitFor(() => expect(result.current.lastRevoiceScript).toBeNull())
    })

    it('keeps the last re-voice text when a rejected retry fails', async () => {
        const { result, rerender } = renderReview(makeWaitingJob())

        await act(async () => {
            result.current.revoice('A brand new edited script.')
        })
        await waitFor(() =>
            expect(result.current.lastRevoiceScript).toBe(
                'A brand new edited script.'
            )
        )

        rerender({ j: makeWaitingJob({ status: 'REJECTED' }) as Job })
        act(() => {
            result.current.notePronunciationEdit('Mooringe', 'Moo-rinj')
        })
        vi.mocked(apiClient.post).mockRejectedValueOnce(
            new Error('regenerate failed')
        )

        await act(async () => {
            result.current.regenerate(false)
        })

        expect(result.current.lastRevoiceScript).toBe(
            'A brand new edited script.'
        )
    })
})

/**
 * A queued pronunciation rule is consumed only after the apply request
 * succeeds. Left behind it re-arms `refreshPronunciation` and keeps the apply
 * button live with nothing left to apply.
 */
describe('useBulletinReview — queued pronunciation edits', () => {
    it('queues a saved rule as a re-voiceable change', () => {
        const { result } = renderReview(makeWaitingJob())

        act(() => {
            result.current.notePronunciationEdit('Mooringe', 'Moo-rinj')
        })

        expect(result.current.changes).toHaveLength(1)
        expect(result.current.changes[0].kind).toBe('pronunciation')
        expect(result.current.applyKind).toBe('revoice')
        expect(result.current.pendingPronunciation).toEqual([
            { word: 'Mooringe', replacement: 'Moo-rinj' },
        ])
    })

    it('keeps ONE change when the same word is corrected twice', () => {
        const { result } = renderReview(makeWaitingJob())

        act(() => {
            result.current.notePronunciationEdit('Mooringe', 'Moo-rinj')
            result.current.notePronunciationEdit('mooringe', 'Mor-inj')
        })

        expect(result.current.changes).toHaveLength(1)
        expect(result.current.pendingPronunciation).toEqual([
            { word: 'mooringe', replacement: 'Mor-inj' },
        ])
    })

    it('clears the queue after the apply request succeeds', async () => {
        const { result, rerender } = renderReview(makeWaitingJob())

        act(() => {
            result.current.notePronunciationEdit('Mooringe', 'Moo-rinj')
        })
        expect(result.current.isDirty).toBe(true)

        await act(async () => {
            result.current.regenerate(true)
        })

        await waitFor(() => {
            expect(result.current.pendingPronunciation).toHaveLength(0)
            expect(result.current.isDirty).toBe(false)
        })

        rerender({ j: makeWaitingJob({ status: 'REQUESTED' }) as Job })
        expect(result.current.changes).toHaveLength(0)
        expect(result.current.applyKind).toBeNull()
        expect(result.current.isDirty).toBe(false)
    })

    it('keeps the queue across a reject to regenerate status handoff', () => {
        const { result, rerender } = renderReview(makeWaitingJob())

        act(() => {
            result.current.notePronunciationEdit('Mooringe', 'Moo-rinj')
        })

        rerender({
            j: makeWaitingJob({
                status: 'REJECTED',
            }) as Job,
        })

        expect(result.current.pendingPronunciation).toEqual([
            { word: 'Mooringe', replacement: 'Moo-rinj' },
        ])
        expect(result.current.applyKind).toBe('revoice')
    })

    it('keeps the queue across a refetch that does not change status', () => {
        // A background poll must never discard a queue the operator is still
        // building, and a failed apply has to stay retryable.
        const { result, rerender } = renderReview(makeWaitingJob())

        act(() => {
            result.current.notePronunciationEdit('Mooringe', 'Moo-rinj')
        })

        rerender({
            j: makeWaitingJob({ lastUpdatedAt: '2026-07-28T02:00:00Z' }) as Job,
        })

        expect(result.current.changes).toHaveLength(1)
        expect(result.current.applyKind).toBe('revoice')
    })

    it('drops the change but keeps the saved rule when the chip is removed', () => {
        const { result } = renderReview(makeWaitingJob())

        act(() => {
            result.current.notePronunciationEdit('Mooringe', 'Moo-rinj')
        })
        act(() => {
            result.current.removeChange('pron-mooringe')
        })

        expect(result.current.changes).toHaveLength(0)
        expect(result.current.applyKind).toBeNull()
    })
})

/**
 * The frontend half of the backend `RefreshPronunciation` contract. If the flag
 * does not reach the API the queued rule is a no-op: synthesis replays the
 * version the job aired with and the operator hears no change.
 */
describe('useBulletinReview — refreshPronunciation on the wire', () => {
    async function lastRegenerateBody(): Promise<RegeneratePayload | null> {
        const { apiClient } = await import('@/lib/api/client')
        const calls = vi.mocked(apiClient.post).mock.calls
        for (let i = calls.length - 1; i >= 0; i--) {
            const [path, body] = calls[i]
            if (!String(path).includes('regenerate')) continue
            return RegenerateBody.parse(body)
        }
        return null
    }

    it('sends the flag with a re-voice when a rule is queued', async () => {
        const { result } = renderReview(makeWaitingJob())

        act(() => {
            result.current.notePronunciationEdit('Keilor', 'Kee-lor')
        })
        await act(async () => {
            result.current.regenerate(true)
        })

        const body = await lastRegenerateBody()
        expect(body?.refreshPronunciation).toBe(true)
        expect(body?.revoiceOnly).toBe(true)
    })

    it('sends the flag on the script-editor re-voice too', async () => {
        // A second door onto the same pipeline — editing the words must not
        // discard the rule queued beside them.
        const { result } = renderReview(makeWaitingJob())

        act(() => {
            result.current.notePronunciationEdit('Keilor', 'Kee-lor')
        })
        await act(async () => {
            result.current.revoice('A brand new edited script.')
        })

        const body = await lastRegenerateBody()
        expect(body?.refreshPronunciation).toBe(true)
        expect(body?.editedScript).toBe('A brand new edited script.')
    })

    it('drops the flag once the operator removes the chip', async () => {
        const { result } = renderReview(makeWaitingJobWithIncidents())

        act(() => {
            result.current.notePronunciationEdit('Keilor', 'Kee-lor')
        })
        act(() => {
            result.current.removeChange('pron-keilor')
        })
        act(() => {
            result.current.toggleInclude('inc-2')
        })
        await act(async () => {
            result.current.regenerate(false)
        })

        const body = await lastRegenerateBody()
        expect(body?.refreshPronunciation).toBe(false)
    })
})

/**
 * The script editor is a second door onto the re-voice. It used to send only the
 * edited words, so incidents the operator had reordered or excluded beside the edit
 * were discarded with no warning — and because the curation stayed queued against a
 * job that had already moved status, the next apply answered 409.
 */
describe('useBulletinReview — re-voice carries queued curation', () => {
    function lastRegenerateBody(): RegeneratePayload | null {
        const calls = vi.mocked(apiClient.post).mock.calls
        for (let i = calls.length - 1; i >= 0; i--) {
            const [path, body] = calls[i]
            if (!String(path).includes('regenerate')) continue
            return RegenerateBody.parse(body)
        }
        return null
    }

    it('sends the excluded incident with a script-editor re-voice', async () => {
        const { result } = renderReview(makeWaitingJobWithIncidents())

        act(() => {
            result.current.toggleInclude('inc-2')
        })
        expect(result.current.includedCount).toBe(1)

        await act(async () => {
            result.current.revoice('A brand new edited script.')
        })

        const body = lastRegenerateBody()
        expect(body?.revoiceOnly).toBe(true)
        expect(body?.editedIncidents).toBeDefined()
        expect(
            body?.editedIncidents?.find((e) => e.incidentId === 'inc-2')
                ?.included
        ).toBe(false)
    })

    it('re-voices on curation alone, without a word changed', async () => {
        // Previously the no-op guard compared only the text, so this returned
        // silently and the operator's exclusion was never applied.
        const { result } = renderReview(makeWaitingJobWithIncidents())

        act(() => {
            result.current.toggleInclude('inc-2')
        })
        await act(async () => {
            result.current.revoice('Original script text.')
        })

        const body = lastRegenerateBody()
        expect(body).not.toBeNull()
        expect(body?.editedIncidents).toBeDefined()
    })
})

/**
 * A staged different voice rides the apply as `voiceProfileId` on BOTH doors —
 * the batch regenerate and the script-editor re-voice. On its own it is a
 * re-voice-only change (switching voices never pays the LLM); mixed with
 * source changes it escalates to a regenerate that still carries the voice.
 * Removing the chip is the discard path: the next apply goes back to the job's
 * configured voice.
 */
describe('useBulletinReview — voice override', () => {
    const voice = { profileId: 'vp-lucy', name: 'Lucy', model: 'eleven_v3' }

    async function lastRegenerateBody(): Promise<RegeneratePayload | null> {
        const calls = vi.mocked(apiClient.post).mock.calls
        for (let i = calls.length - 1; i >= 0; i--) {
            const [path, body] = calls[i]
            if (!String(path).includes('regenerate')) continue
            return RegenerateBody.parse(body)
        }
        return null
    }

    it('stages a voice change chip that applies as a re-voice', () => {
        const { result } = renderReview(makeWaitingJob())

        act(() => {
            result.current.setVoiceOverride(voice)
        })

        expect(result.current.voiceOverride).toEqual(voice)
        expect(result.current.changes).toHaveLength(1)
        expect(result.current.changes[0]).toMatchObject({
            kind: 'voice',
            label: 'Voice → Lucy',
            removable: true,
        })
        expect(result.current.applyKind).toBe('revoice')
    })

    it('sends voiceProfileId on a re-voice-only apply', async () => {
        const { result } = renderReview(makeWaitingJob())

        act(() => {
            result.current.setVoiceOverride(voice)
        })
        await act(async () => {
            result.current.regenerate(true)
        })

        const body = await lastRegenerateBody()
        expect(body?.voiceProfileId).toBe('vp-lucy')
        expect(body?.revoiceOnly).toBe(true)
    })

    it('sends voiceProfileId on the script-editor re-voice too', async () => {
        const { result } = renderReview(makeWaitingJob())

        act(() => {
            result.current.setVoiceOverride(voice)
        })
        await act(async () => {
            result.current.revoice('A brand new edited script.')
        })

        const body = await lastRegenerateBody()
        expect(body?.voiceProfileId).toBe('vp-lucy')
        expect(body?.editedScript).toBe('A brand new edited script.')
    })

    it('escalates to a regenerate when source changes ride along, keeping the voice', async () => {
        const { result } = renderReview(makeWaitingJobWithIncidents())

        act(() => {
            result.current.setVoiceOverride(voice)
            result.current.toggleInclude('inc-2')
        })
        expect(result.current.applyKind).toBe('regenerate')

        await act(async () => {
            result.current.regenerate(false)
        })

        const body = await lastRegenerateBody()
        expect(body?.voiceProfileId).toBe('vp-lucy')
        expect(body?.revoiceOnly).toBe(false)
    })

    it('drops voiceProfileId once the chip is removed', async () => {
        const { result } = renderReview(makeWaitingJobWithIncidents())

        act(() => {
            result.current.setVoiceOverride(voice)
        })
        act(() => {
            result.current.removeChange('voice')
        })
        expect(result.current.voiceOverride).toBeNull()

        act(() => {
            result.current.toggleInclude('inc-2')
        })
        await act(async () => {
            result.current.regenerate(false)
        })

        const body = await lastRegenerateBody()
        expect(body?.voiceProfileId).toBeUndefined()
    })

    it('clears the override once the apply succeeds', async () => {
        const { result } = renderReview(makeWaitingJob())

        act(() => {
            result.current.setVoiceOverride(voice)
        })
        await act(async () => {
            result.current.regenerate(true)
        })

        await waitFor(() => expect(result.current.voiceOverride).toBeNull())
        expect(result.current.changes).toHaveLength(0)
    })

    it('keeps the override across a refetch of the same job', async () => {
        // A background poll must never drop a staged voice the operator is
        // still deciding on — and a failed apply has to stay retryable with
        // the same voice.
        const { result, rerender } = renderReview(makeWaitingJob())

        act(() => {
            result.current.setVoiceOverride(voice)
        })

        rerender({
            j: makeWaitingJob({ lastUpdatedAt: '2026-07-28T02:00:00Z' }) as Job,
        })

        expect(result.current.voiceOverride).toEqual(voice)
    })
})

/**
 * An apply is a reject followed by a regenerate, so the job is momentarily
 * REJECTED — a TERMINAL status. Every status-driven review surface swapped to the
 * as-aired record card for that instant (and again during the settle gap before
 * the refetch), which is how an operator who pressed Re-voice ended up looking at
 * a rejected bulletin. `applyInFlight` spans the whole handoff.
 */
describe('useBulletinReview — applyInFlight spans the reject → regenerate handoff', () => {
    it('is null before anything is submitted', () => {
        const { result } = renderReview(makeWaitingJob())
        expect(result.current.applyInFlight).toBeNull()
    })

    it('stays latched on a re-voice while the job reads REJECTED', async () => {
        const { result, rerender } = renderReview(makeWaitingJob())

        await act(async () => {
            result.current.revoice('A brand new edited script.')
        })
        expect(result.current.applyInFlight).toBe('revoice')

        // The reject landed and the refetch beat the regenerate.
        rerender({ j: makeWaitingJob({ status: 'REJECTED' }) as Job })
        expect(result.current.applyInFlight).toBe('revoice')
    })

    it('clears once the job reaches the pipeline, and does not re-latch on return', async () => {
        const { result, rerender } = renderReview(makeWaitingJob())

        await act(async () => {
            result.current.revoice('A brand new edited script.')
        })
        rerender({ j: makeWaitingJob({ status: 'REJECTED' }) as Job })
        rerender({ j: makeWaitingJob({ status: 'REQUESTED' }) as Job })
        expect(result.current.applyInFlight).toBeNull()

        // The re-voiced bulletin comes back to the status it was submitted from;
        // a latch keyed only on that status would light up again here.
        rerender({ j: makeWaitingJob() as Job })
        expect(result.current.applyInFlight).toBeNull()
    })

    it('reports a full regeneration as such', async () => {
        const { result } = renderReview(makeWaitingJobWithIncidents())

        act(() => {
            result.current.toggleInclude('inc-2')
        })
        await act(async () => {
            result.current.regenerate(false)
        })

        expect(result.current.applyInFlight).toBe('regenerate')
    })

    it('clears when the apply fails, so the real status is shown again', async () => {
        vi.mocked(apiClient.post).mockRejectedValueOnce(
            new Error('reject failed')
        )
        const { result } = renderReview(makeWaitingJob())

        await act(async () => {
            result.current.revoice('A brand new edited script.')
        })

        await waitFor(() => expect(result.current.applyInFlight).toBeNull())
    })
})

describe('useBulletinReview — same-road corridor curation', () => {
    it('keeps one stable road entry for multiple spatial corridors', () => {
        const { result } = renderReview(makeWaitingJobWithSameRoadCorridors())

        expect(result.current.corridorCuration.effective).toEqual([
            'Bruce Highway',
            'Mulgrave Road',
        ])
    })
})
