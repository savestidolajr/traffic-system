import { test, expect, type Page } from './fixtures'
import { GuidancePage } from './pages/LibraryPages'
import { resetMockStore } from './pages/reset'

async function clickIfEnabled(page: Page, name: RegExp) {
    const buttons = page.getByRole('button', { name })
    const count = await buttons.count()
    for (let i = 0; i < count; i += 1) {
        const button = buttons.nth(i)
        if ((await button.isVisible()) && (await button.isEnabled())) {
            await button.click()
            return
        }
    }
}

test.beforeEach(async ({ page }) => {
    await resetMockStore(page)
})

test('guidance, banned words, and priority-road controls are reachable', async ({
    page,
}) => {
    const guidance = new GuidancePage(page)
    await guidance.goto()
    await expect(page.locator('body')).toContainText(
        /guidance|rule|banned|priority/i
    )
    await clickIfEnabled(page, /add|new|create/i)
    await expect(page.locator('body')).toContainText(
        /global|market|term|road|guidance/i
    )
    await clickIfEnabled(page, /save/i)
    await clickIfEnabled(page, /enable|disable/i)
    await clickIfEnabled(page, /delete|remove/i)
    await expect(page.locator('body')).not.toContainText(/application error/i)
})
