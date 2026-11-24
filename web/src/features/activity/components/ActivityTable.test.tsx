// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ActivityTable } from './ActivityTable'
import type { ActivityLogEntry } from '../api/activity-schemas'

afterEach(cleanup)

const entry: ActivityLogEntry = {
    atUtc: '2026-09-02T00:00:00Z',
    actorName: 'Operator',
    actorObjectId: 'operator-1',
    method: 'POST',
    path: '/api/guidance',
    resource: 'guidance',
    entityId: 'guidance-1',
    statusCode: 200,
    requestBodyJson: null,
}

describe('ActivityTable mobile disclosure', () => {
    it('uses a real keyboard-reachable button for expansion', async () => {
        const user = userEvent.setup()
        render(
            <ActivityTable
                entries={[entry]}
                diffable={false}
                hasMore={false}
                onLoadMore={() => undefined}
                isLoadingMore={false}
            />
        )

        const toggle = screen.getByRole('button', {
            name: 'Expand activity entry',
        })
        expect(toggle).toHaveAttribute('aria-expanded', 'false')
        await user.click(toggle)
        expect(toggle).toHaveAttribute('aria-expanded', 'true')
        expect(screen.getAllByText(/Actor object ID:/)).toHaveLength(2)
    })
})
