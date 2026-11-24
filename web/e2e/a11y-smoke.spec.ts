import { test, expect } from './fixtures'
import { resetMockStore } from './pages/reset'

const routes = [
    '/',
    '/jobs',
    '/stations',
    '/voices',
    '/operators',
    '/guidance',
    '/history',
]

test.beforeEach(async ({ page }) => {
    await resetMockStore(page)
})

test('top-level routes have landmarks, headings or buttons and no critical console errors', async ({
    page,
}) => {
    const critical: string[] = []
    page.on('console', (message) => {
        if (message.type() === 'error') critical.push(message.text())
    })

    for (const route of routes) {
        await page.goto(route)
        await expect(page.locator('body')).toBeVisible()
        const structuralCount =
            (await page.getByRole('heading').count()) +
            (await page.getByRole('button').count())
        expect(structuralCount).toBeGreaterThan(0)
    }

    expect(critical.filter((m) => !m.includes('favicon')).join('\n')).toBe('')
})
