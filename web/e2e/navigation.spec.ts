import { test, expect } from './fixtures'
import { Nav } from './pages/Nav'
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

test('desktop and mobile routes render primary content', async ({ page }) => {
    const nav = new Nav(page)
    for (const route of routes) {
        await nav.goto(route)
        await expect(
            page.locator('main').or(page.locator('body')).first()
        ).toBeVisible()
        await expect(page.locator('body')).not.toContainText(
            /application error|failed to fetch/i
        )
    }
})

test('command palette finds pages and configurable entities', async ({
    page,
}) => {
    const nav = new Nav(page)
    await nav.goto('/')
    for (const query of [
        'On air',
        'Melbourne',
        'ARN Hudson',
        'Parramatta',
        'Operator',
    ]) {
        await nav.searchCommand(query)
        await page.keyboard.press('Escape')
    }
})

test('notifications and user menu open from shell', async ({ page }) => {
    const nav = new Nav(page)
    await nav.goto('/jobs')
    await nav.openNotifications()
    await page.keyboard.press('Escape')
    await nav.openUserMenu()
})

test('shell exposes pending reviews and remembers optional desktop labels', async ({
    page,
}) => {
    const nav = new Nav(page)
    await nav.goto('/jobs')
    const viewportWidth = page.viewportSize()?.width ?? 0

    // The count is exact on purpose — it is the whole point of the badge, and an
    // exact assertion is what caught the fixture change that restored
    // arn-job-00008. It is COUPLED to how many WAITING_APPROVAL jobs the mock
    // seeds (currently 00007 + 00008 + the Adelaide dev job + the
    // arn-job-cairns-degraded source-diagnostics fixture, which is spread from
    // 00007 and inherits its status): change that set and this number changes
    // with it.
    if (viewportWidth < 768) {
        await expect(
            page.getByRole('button', {
                name: /toggle menu; 4 bulletins awaiting review/i,
            })
        ).toBeVisible()
        await page
            .getByRole('button', {
                name: /toggle menu; 4 bulletins awaiting review/i,
            })
            .click()
        await expect(
            page.locator('a[aria-label*="bulletins awaiting review"]:visible')
        ).toContainText('4 awaiting review')
        return
    }

    await expect(
        page.locator('a[aria-label*="bulletins awaiting review"]:visible')
    ).toBeVisible()
    if (viewportWidth < 1280) return

    await page.getByRole('navigation', { name: 'Primary' }).hover()
    await page.getByRole('button', { name: 'Pin navigation labels' }).click()
    await page.mouse.move(viewportWidth - 100, 200)
    await expect(
        page.getByRole('button', { name: 'Unpin navigation labels' })
    ).toBeVisible()
    await expect
        .poll(() =>
            page.evaluate(() => localStorage.getItem('arn-sidebar-pinned'))
        )
        .toBe('true')

    await page.reload()
    await expect(
        page.getByRole('button', { name: 'Unpin navigation labels' })
    ).toBeVisible()
})

test('operational filters stay reachable without stealing mobile height', async ({
    page,
}) => {
    const nav = new Nav(page)
    await nav.goto('/activity')
    const rail = page.locator('[data-slot="sticky-filter-rail"]')
    await expect(rail).toBeVisible()
    const viewportWidth = page.viewportSize()?.width ?? 0

    if (viewportWidth < 768) {
        await expect
            .poll(() =>
                rail.evaluate((element) => getComputedStyle(element).position)
            )
            .toBe('static')
        return
    }

    await expect
        .poll(() =>
            rail.evaluate((element) => getComputedStyle(element).position)
        )
        .toBe('sticky')
    await page.locator('main').evaluate((element) => {
        element.scrollTop = 500
    })
    await expect
        .poll(
            async () =>
                (await rail.boundingBox())?.y ?? Number.POSITIVE_INFINITY
        )
        .toBeLessThanOrEqual(10)
})
