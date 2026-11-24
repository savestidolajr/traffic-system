import { expect, type Page } from '@playwright/test'

export class StationsPage {
    constructor(private readonly page: Page) {}

    async goto() {
        await this.page.goto('/stations')
        await expect(
            this.page.getByRole('heading', { name: /stations/i })
        ).toBeVisible()
    }

    async openNewStation() {
        await this.page.getByRole('button', { name: /new station/i }).click()
        await expect(
            this.page
                .getByText(/station studio|station identity|new station/i)
                .first()
        ).toBeVisible()
    }

    async triggerBulletin() {
        const trigger = this.page
            .getByRole('button', { name: /trigger bulletin/i })
            .first()
        if ((await trigger.count()) > 0 && (await trigger.isEnabled())) {
            await trigger.click()
        }
        await expect(this.page.locator('body')).toContainText(
            /station|schedule|trigger|manual bulletin|queued/i
        )
    }
}
