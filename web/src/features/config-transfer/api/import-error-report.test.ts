import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiClient } from '@/lib/api/client'
import { tryImportConfig } from './config-transfer-hooks'

afterEach(() => vi.restoreAllMocks())

const ERROR_REPORT = {
    DryRun: true,
    RequiresApproval: false,
    Applied: false,
    Creates: 0,
    Updates: 0,
    Unchanged: 132,
    Errors: 1,
    Items: [
        {
            Section: 'PriorityRoads',
            Key: 'adelaide',
            Name: 'adelaide (9 roads)',
            Action: 'Error',
            Detail: "FlowTier 'standard' is not recognised.",
            Outcome: null,
        },
    ],
}

/**
 * `POST /config/import` answers an invalid bundle with 400 AND the full dry-run
 * report — one Error row per offending entity. The client's response
 * interceptor converts every failure into `ApiError`, so the old
 * `err.response?.status` check never matched and that report was thrown away:
 * the operator got a single toast and no way to see which entities failed.
 */
describe('tryImportConfig — the 400 body is the report', () => {
    it('returns the report so the review surface can list every error row', async () => {
        vi.spyOn(apiClient, 'post').mockRejectedValue(
            new ApiError(
                "FlowTier 'standard' is not recognised.",
                400,
                undefined,
                ERROR_REPORT
            )
        )

        const outcome = await tryImportConfig({ bundle: {}, dryRun: true })

        expect(outcome.ok).toBe(false)
        if (outcome.ok || !('report' in outcome))
            throw new Error('expected a parsed report')
        expect(outcome.report.errors).toBe(1)
        expect(outcome.report.items[0]?.section).toBe('PriorityRoads')
        expect(outcome.report.items[0]?.detail).toContain('FlowTier')
    })

    it('falls back to the raw error when the failure carries no report', async () => {
        vi.spyOn(apiClient, 'post').mockRejectedValue(
            new ApiError('Import request body exceeds the 2 MB limit.', 413)
        )

        const outcome = await tryImportConfig({ bundle: {}, dryRun: true })

        expect(outcome.ok).toBe(false)
        if (outcome.ok || 'report' in outcome)
            throw new Error('expected the raw error')
        expect(outcome.error).toBeInstanceOf(ApiError)
    })
})
