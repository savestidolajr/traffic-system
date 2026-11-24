import { expect, type Page } from '@playwright/test'

export class Nav {
    constructor(private readonly page: Page) {}

    async goto(path: string) {
        await this.page.goto(path)
        await expect(this.page.locator('body')).toBeVisible()
    }

    async expectRoute(path: string) {
        await expect(this.page).toHaveURL(
            new RegExp(`${path.replace('/', '\\/')}(?:$|[/?#])`)
        )
    }

    async openCommandPalette() {
        // A previously-opened dialog may still be mid fade-out (Radix's
        // 200ms exit animation) — its overlay covers the viewport and
        // intercepts the search-button click underneath it. Wait for any
        // dialog to fully leave the DOM first (resolves immediately if none
        // exist).
        await this.page
            .locator('[role="dialog"]')
            .first()
            .waitFor({ state: 'hidden', timeout: 2000 })
            .catch(() => {})
        const searchButton = this.page
            .getByRole('button', { name: /search/i })
            .first()
        if (
            (await searchButton.count()) > 0 &&
            (await searchButton.isVisible())
        ) {
            await searchButton.click()
        } else {
            await this.page.keyboard.press(
                process.platform === 'darwin' ? 'Meta+K' : 'Control+K'
            )
        }
        const input = this.page.getByPlaceholder(/search pages/i)
        if ((await input.count()) > 0) {
            await expect(input).toBeVisible()
        }
    }

    async searchCommand(text: string) {
        await this.openCommandPalette()
        const input = this.page.getByPlaceholder(/search pages/i)
        if ((await input.count()) === 0) return
        await input.fill(text)
        await expect(
            this.page.locator('[cmdk-list], [role="listbox"], body').first()
        ).toBeVisible()
    }

    async openNotifications() {
        await this.page
            .getByRole('button', { name: /notification|waiting|alarm/i })
            .first()
            .click()
        await expect(this.page.locator('body')).not.toContainText(
            /application error/i
        )
    }

    async openUserMenu() {
        const userButton = this.page
            .getByRole('button', { name: /user|local dev|account/i })
            .first()
        if ((await userButton.count()) === 0 || !(await userButton.isVisible()))
            return
        await userButton.click()
        await expect(this.page.locator('body')).not.toContainText(
            /application error/i
        )
    }
}
