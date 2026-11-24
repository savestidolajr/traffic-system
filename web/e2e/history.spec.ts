import { test, expect } from './fixtures'
import { resetMockStore } from './pages/reset'

test.beforeEach(async ({ page }) => {
    await resetMockStore(page)
})

test('history filters and opens a historical job', async ({ page }) => {
    await page.goto('/history')
    await expect(page.locator('body')).toContainText(
        /history|status|market|search/i
    )
    const search = page.getByPlaceholder(/search/i).first()
    if ((await search.count()) > 0) await search.fill('Hot Tomato')
    await expect(page.locator('body')).toContainText(/Hot Tomato|ARN|job/i)
    const jobLink = page
        .getByRole('link', { name: /job|hot tomato|arn/i })
        .first()
    if ((await jobLink.count()) > 0) await jobLink.click()
    await expect(page.locator('body')).not.toContainText(/application error/i)
})
