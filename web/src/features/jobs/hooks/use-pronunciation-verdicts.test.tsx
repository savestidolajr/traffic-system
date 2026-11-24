// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { usePronunciationVerdicts } from './use-pronunciation-verdicts'
import type { Dictionary } from '@/features/pronunciation/types'

const mocks = vi.hoisted(() => ({
    addRule: vi.fn(),
    setVerified: vi.fn(),
    toastError: vi.fn(),
    toastInfo: vi.fn(),
}))

vi.mock('@/features/pronunciation/api/pronunciation-hooks', () => ({
    useAddJobPronunciationRule: () => ({ mutateAsync: mocks.addRule }),
}))

vi.mock('@/features/pronunciation/api/verified-pronunciation-hooks', () => ({
    useVerifiedPronunciations: () => ({ data: { words: [] } }),
    useSetVerifiedPronunciations: () => ({ mutateAsync: mocks.setVerified }),
    pronunciationKey: (word: string) => word.trim().toLowerCase(),
}))

vi.mock('@/hooks/use-current-user', () => ({
    useIsAdmin: () => false,
    useIsOperator: () => true,
}))

vi.mock('sonner', () => ({
    toast: {
        success: vi.fn(),
        error: mocks.toastError,
        info: mocks.toastInfo,
        message: vi.fn(),
    },
}))

afterEach(() => {
    vi.clearAllMocks()
})

function makeDictionary(overrides: Partial<Dictionary> = {}): Dictionary {
    return {
        id: 'pd-sydney',
        dictionaryId: 'eleven-dict-1',
        name: 'Sydney Roads',
        description: null,
        status: 'active',
        currentVersion: 2,
        createdAt: '2026-07-01T00:00:00Z',
        updatedAt: '2026-07-28T00:00:00Z',
        etag: 'etag-old',
        createdBy: null,
        assignedStations: [],
        rules: [
            {
                type: 'alias',
                stringToReplace: 'Coomera',
                replacement: 'Koomera',
                alphabet: null,
                caseSensitive: false,
                wordBoundaries: true,
                appliesOnAllModels: true,
            },
        ],
        versions: [],
        ...overrides,
    } as Dictionary
}

describe('usePronunciationVerdicts — saving a pronunciation fix', () => {
    it('sends the one-word fix addressed by JOB, with no rule list and no ETag', async () => {
        mocks.addRule.mockResolvedValue({
            outcome: 'changed',
            dictionary: makeDictionary(),
        })
        const onEdit = vi.fn()

        const { result } = renderHook(() =>
            usePronunciationVerdicts(
                'job-42',
                'Sydney',
                makeDictionary(),
                onEdit
            )
        )

        await act(async () => {
            await result.current.addRule('Coomera', 'Koomerah')
        })

        // Whole-list replay is the server's job now: sending rules or an ETag
        // from here would put the read-modify-write back in the browser, where
        // it needed a 409 round trip the operator waited through.
        expect(mocks.addRule).toHaveBeenCalledTimes(1)
        expect(mocks.addRule).toHaveBeenCalledWith({
            jobId: 'job-42',
            word: 'Coomera',
            replacement: 'Koomerah',
        })
        expect(mocks.toastError).not.toHaveBeenCalled()
        // The saved rule queues as a pending change so the operator can re-voice.
        expect(onEdit).toHaveBeenCalledWith('Coomera', 'Koomerah')
    })

    it('does not queue a pending change when the save failed', async () => {
        mocks.addRule.mockRejectedValue({ status: 409 })
        const onEdit = vi.fn()

        const { result } = renderHook(() =>
            usePronunciationVerdicts(
                'job-42',
                'Sydney',
                makeDictionary(),
                onEdit
            )
        )

        await act(async () => {
            await result.current.addRule('Coomera', 'Koomerah')
        })

        // A pending chip for a rule that was never written would have the
        // operator re-voice into nothing.
        expect(onEdit).not.toHaveBeenCalled()
    })

    it('does not call the server when the dictionary already says exactly this', async () => {
        const onEdit = vi.fn()

        const { result } = renderHook(() =>
            usePronunciationVerdicts(
                'job-42',
                'Sydney',
                makeDictionary(),
                onEdit
            )
        )

        await act(async () => {
            await result.current.addRule('Coomera', 'Koomera')
        })

        expect(mocks.addRule).not.toHaveBeenCalled()
        expect(mocks.toastInfo).toHaveBeenCalled()
        expect(onEdit).not.toHaveBeenCalled()
    })
})
