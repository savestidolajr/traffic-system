// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import { describe, it, expect, afterEach } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { FailureDetailsPanel } from './FailureDetailsPanel'
import type { FailureData } from '../../types'

afterEach(() => {
    cleanup()
})

function failure(message: string): FailureData {
    return {
        stage: 'SYNTHESIZING_AUDIO',
        message,
        occurredAt: '2026-08-05T03:49:33Z',
        attemptCount: 4,
        lastEstimatedDurationSec: null,
        targetDurationSec: 30,
        attemptHistory: [],
    }
}

function renderFailure(message: string) {
    return render(<FailureDetailsPanel failure={failure(message)} />)
}

describe('FailureDetailsPanel — honest failure headlines (bug-bash #52)', () => {
    // The live evidence: a job failed with a banned-word block and the banner
    // said "An unexpected error occurred" because FAILURE_PATTERNS had no
    // safety pattern. A content-safety block must be headlined as what it is —
    // the operator's next action is clear (fix the wording), not "call support".
    it('headlines a content-safety block honestly instead of the generic fallback', () => {
        renderFailure(
            "Content safety violation: Banned word detected: 'standstill'; Banned word detected: 'traffic'"
        )
        expect(
            screen.getByText(
                'Blocked by the content-safety filter: the script contains a banned word or failed the AI safety review'
            )
        ).toBeInTheDocument()
        expect(
            screen.queryByText('An unexpected error occurred')
        ).not.toBeInTheDocument()
    })

    it('headlines an AI safety-review block honestly', () => {
        renderFailure(
            'Content safety violation: graphic injury detail ("blood") in sentence 3'
        )
        expect(
            screen.getByText(
                'Blocked by the content-safety filter: the script contains a banned word or failed the AI safety review'
            )
        ).toBeInTheDocument()
    })

    it('headlines a content-limited underrun (ContentLimited disposition)', () => {
        renderFailure(
            'Content-limited: best spoken 27.4s could not fill the 30s slot within the slow-down cap.'
        )
        expect(
            screen.getByText('Not enough live traffic to fill the slot')
        ).toBeInTheDocument()
    })

    it('headlines a traffic-fetch failure (Retryable disposition)', () => {
        renderFailure('Failed to fetch traffic: HERE request failed')
        expect(
            screen.getByText('Traffic data unavailable right now')
        ).toBeInTheDocument()
    })

    it('headlines a final-quality-gate failure (Permanent disposition)', () => {
        renderFailure(
            'Final grounding check failed: the script could not be verified against the source'
        )
        expect(
            screen.getByText('Script failed the final quality gate')
        ).toBeInTheDocument()
    })

    it('still falls back to the generic message for unrecognised failures', () => {
        renderFailure('some provider error we have never seen')
        expect(
            screen.getByText('An unexpected error occurred')
        ).toBeInTheDocument()
    })
})
