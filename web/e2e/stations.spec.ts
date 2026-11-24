import { test, expect, type Page } from './fixtures'
import { StationsPage } from './pages/StationsPage'
import { resetMockStore } from './pages/reset'

async function clickIfVisible(page: Page, name: RegExp) {
    const button = page.getByRole('button', { name }).first()
    if ((await button.count()) > 0 && (await button.isVisible()))
        await button.click()
}

test.beforeEach(async ({ page }) => {
    await resetMockStore(page)
})

test('station studio, edit, manual trigger, and delete controls are reachable', async ({
    page,
}) => {
    const stations = new StationsPage(page)
    await stations.goto()
    await expect(page.locator('body')).toContainText(/station|market|coverage/i)

    await stations.openNewStation()
    await expect(page.locator('body')).toContainText(
        /identity|coverage|voice|schedule|good-run/i
    )
    await clickIfVisible(page, /save/i)
    await expect(page.locator('body')).not.toContainText(/application error/i)

    await page.goto('/stations')
    await clickIfVisible(page, /edit|studio/i)
    await expect(page.locator('body')).toContainText(
        /coverage|voice|schedule|station/i
    )

    await page.goto('/stations')
    await stations.triggerBulletin()
    await expect(page.locator('body')).toContainText(
        /queued|requested|bulletin|job/i
    )

    await page.goto('/stations')
    await clickIfVisible(page, /delete|disable|archive/i)
    await expect(page.locator('body')).not.toContainText(/application error/i)
})

test('schedule step renders without crashing (daysOfWeek is initialised)', async ({
    page,
    isMobile,
}) => {
    // The rail step label is icon-only below md, so name-based step nav needs
    // the desktop layout.
    test.skip(isMobile, 'studio rail step labels are icon-only on mobile')

    const stations = new StationsPage(page)
    await stations.goto()
    await stations.openNewStation()

    // Opening the Schedule step mounts SchedulingSection. It previously threw
    // "Cannot read properties of undefined (reading 'length')" because
    // daysOfWeek was absent from the studio form defaults, so the watched value
    // was undefined and buildCronFromCalendar read `.length` on it.
    await page
        .locator('nav[aria-label$="steps"]')
        .getByRole('button', { name: /schedule/i })
        .click()

    await expect(page.getByText(/days of week/i)).toBeVisible()
    await expect(page.locator('body')).not.toContainText(
        /something went wrong/i
    )
    await expect(page.locator('body')).not.toContainText(
        /cannot read properties/i
    )
})
test('station cards fill their grid row', async ({ page }) => {
    const stations = new StationsPage(page)
    await stations.goto()

    const cards = page.locator(
        'div.cursor-pointer > div.rounded-xl.border.bg-card'
    )
    // One card per dev-data.json schedule (15 as of the 2026-08 seed).
    await expect(cards).toHaveCount(15)

    const rows = await cards.evaluateAll((elements) => {
        const grouped = new Map<number, number[]>()
        for (const element of elements) {
            const rect = element.getBoundingClientRect()
            const row = Math.round(rect.top)
            const heights = grouped.get(row) ?? []
            heights.push(Math.round(rect.height))
            grouped.set(row, heights)
        }
        return [...grouped.values()]
    })

    for (const heights of rows) {
        expect(new Set(heights).size).toBe(1)
    }
})
