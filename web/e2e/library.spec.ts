import { test, expect, type Page } from './fixtures'
import { LibraryPage } from './pages/LibraryPages'
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

test('delivery styles and pronunciation dictionaries are exercised through visible library pages', async ({
    page,
}) => {
    const library = new LibraryPage(page)
    await library.delivery()
    await expect(page.locator('body')).toContainText(
        /delivery|style|tag|preview/i
    )
    await clickIfEnabled(page, /new|create|preview|audition/i)
    await clickIfEnabled(page, /save|restore|archive/i)

    await library.pronunciation()
    await expect(page.locator('body')).toContainText(
        /pronunciation|dictionary|rule|preview/i
    )
    await clickIfEnabled(page, /new|create|preview/i)
    await clickIfEnabled(page, /save|restore|archive/i)
    await expect(page.locator('body')).not.toContainText(/application error/i)
})
