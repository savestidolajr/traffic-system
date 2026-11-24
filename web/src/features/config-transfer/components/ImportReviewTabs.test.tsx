// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ImportReviewTabs } from './ImportReviewTabs'
import type {
    ConfigImportItem,
    ConfigImportReport,
} from '../api/config-transfer-schemas'

afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
})

/**
 * The backend plans id-less library entries (voice profiles, delivery styles,
 * pronunciation dictionaries, safety terms) with the report key falling back
 * to the entity NAME (or "(unnamed)"/"(missing)" for errors), so two entries
 * in one section can legitimately share a report key. This mirrors the exact
 * wire shape those plans emit: two Create rows keyed "Morning Voice".
 */
function item(overrides: Partial<ConfigImportItem>): ConfigImportItem {
    return {
        section: 'VoiceProfiles',
        key: 'Morning Voice',
        name: 'Morning Voice',
        action: 'Create',
        detail: null,
        outcome: null,
        ...overrides,
    }
}

function report(items: ConfigImportItem[]): ConfigImportReport {
    return {
        dryRun: true,
        requiresApproval: false,
        applied: false,
        creates: items.filter((i) => i.action === 'Create').length,
        updates: items.filter((i) => i.action === 'Update').length,
        unchanged: items.filter((i) => i.action === 'Unchanged').length,
        errors: items.filter((i) => i.action === 'Error').length,
        items,
    }
}

const AFTER = {
    VoiceProfiles: [
        { Id: 'vp-1', Name: 'Morning Voice', BaseVoiceId: 'v1', Model: 'm1' },
        { Id: 'vp-2', Name: 'Morning Voice', BaseVoiceId: 'v2', Model: 'm2' },
    ],
}

const rowButtons = () =>
    screen.getAllByRole('button', { name: 'Create Morning Voice' })

describe('ImportReviewTabs with previously hidden sections', () => {
    it('renders a tab and rows when the only changes are in Bookends', () => {
        render(
            <ImportReviewTabs
                report={report([
                    item({
                        section: 'Bookends',
                        key: 'intro-1',
                        name: 'Morning intro',
                        action: 'Update',
                    }),
                ])}
                beforeBundle={{}}
                afterBundle={{}}
            />
        )

        // Bookends used to be filtered out by `isSection`, which rendered the
        // "nothing differs" empty state while the metric cards above still
        // showed Update 1. It must now surface as a tab with its row.
        expect(
            screen.queryByText(/Nothing in this bundle differs/)
        ).not.toBeInTheDocument()
        expect(
            screen.getByRole('tab', { name: /Bookends/ })
        ).toBeInTheDocument()
        expect(
            screen.getByRole('button', { name: 'Update Morning intro' })
        ).toBeInTheDocument()
    })

    /**
     * The plan reports validation failures under pseudo-sections that are not in
     * `ConfigSections` at all: `Unknown` for a typo'd top-level property (dev,
     * 2026-08-10: `"Schedule"` instead of `"Schedules"`) and `SchemaVersion` for
     * a version mismatch. Both were filtered out of the review, so the operator
     * read "Fix the 1 error below" with nothing below it.
     */
    it('surfaces an error row filed under a pseudo-section', () => {
        render(
            <ImportReviewTabs
                report={report([
                    item({
                        section: 'Unknown',
                        key: 'Schedule',
                        name: 'Schedule',
                        action: 'Error',
                        detail: "Unrecognized top-level property 'Schedule' is not a known config section.",
                    }),
                ])}
                beforeBundle={{}}
                afterBundle={{}}
            />
        )

        expect(
            screen.queryByText(/Nothing in this bundle differs/)
        ).not.toBeInTheDocument()
        expect(screen.getByRole('tab', { name: /Unknown/ })).toBeInTheDocument()
        expect(
            screen.getByText(/Unrecognized top-level property/)
        ).toBeInTheDocument()
    })

    /**
     * The section maps are hand-maintained, so the NEXT section added to
     * `ConfigSections.All` will reach a build that cannot name it. It must still
     * be reviewable, under a humanised label, rather than silently dropped while
     * its rows keep counting toward the metric cards.
     */
    it('reviews a section this build has never heard of', () => {
        render(
            <ImportReviewTabs
                report={report([
                    item({
                        section: 'SomeFutureSection',
                        key: 'sf-1',
                        name: 'Future thing',
                        action: 'Update',
                    }),
                ])}
                beforeBundle={{}}
                afterBundle={{}}
            />
        )

        expect(
            screen.getByRole('tab', { name: /Some future section/ })
        ).toBeInTheDocument()
        expect(
            screen.getByRole('button', { name: 'Update Future thing' })
        ).toBeInTheDocument()
    })
})

describe('ImportReviewTabs with duplicate report keys', () => {
    it('keeps both rows on screen and independently expandable', async () => {
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
        const user = userEvent.setup()
        render(
            <ImportReviewTabs
                report={report([item({}), item({})])}
                beforeBundle={{}}
                afterBundle={AFTER}
            />
        )

        // Both rows render from the start.
        expect(rowButtons()).toHaveLength(2)

        await user.click(rowButtons()[0])

        // Expanding one row must not drop its twin (duplicate React keys would
        // orphan the second row's DOM on re-render) and must not expand both
        // (a shared expansion identity would toggle them together).
        const after = rowButtons()
        expect(after).toHaveLength(2)
        expect(
            after.filter((b) => b.getAttribute('aria-expanded') === 'true')
        ).toHaveLength(1)

        // And React must never see two children with the same key.
        expect(
            errorSpy.mock.calls.some((call) =>
                String(call[0]).includes('same key')
            )
        ).toBe(false)
    })
})
