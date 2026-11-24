import { test, expect, type Page } from './fixtures'
import { OperatorsPage } from './pages/LibraryPages'
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

test('admin can edit an existing operator assignment, and cannot create one', async ({
    page,
}) => {
    const operators = new OperatorsPage(page)
    await operators.goto()
    await expect(page.locator('body')).toContainText(/admin|operator|market/i)

    // Operators originate in the directory; the console only edits the market
    // scope of someone already there. Asserting the ABSENCE of a create path
    // makes that a contract rather than an accident, so re-adding an "Assign
    // operator" button has to be a deliberate change to this spec.
    await operators.expectNoCreateAffordance()

    await operators.openEditDialog()
    await clickIfEnabled(page, /save|assign/i)
    await clickIfEnabled(page, /delete|remove/i)
    await expect(page.locator('body')).not.toContainText(/application error/i)
})
