import { expect, type Page } from '@playwright/test'

export class JobsPage {
    constructor(private readonly page: Page) {}

    async openJob(jobId: string) {
        await this.page.goto(`/jobs/${jobId}`)
        await expect(this.page.locator('body')).toContainText(
            /script|audio|traffic|failed|fallback|approved|review/i
        )
        await expect(this.page.locator('body')).not.toContainText(
            /application error/i
        )
    }

    async switchTab(name: string) {
        await this.page
            .getByRole('tab', { name: new RegExp(name, 'i') })
            .click()
        await expect(
            this.page.getByRole('tab', { name: new RegExp(name, 'i') })
        ).toHaveAttribute('aria-selected', 'true')
    }

    async approve() {
        await this.page
            .getByRole('button', { name: 'Approve', exact: true })
            .click()
        await expect(this.page.locator('body')).toContainText(
            /approved|approval/i
        )
    }

    async revise() {
        await this.page
            .getByRole('button', { name: /revise|reject/i })
            .first()
            .click()
        await expect(this.page.locator('body')).toContainText(
            /revise|feedback|reject/i
        )
    }
}
