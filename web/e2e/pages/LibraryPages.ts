import { expect, type Page } from '@playwright/test'

export class VoicesPage {
    constructor(private readonly page: Page) {}

    async goto() {
        await this.page.goto('/voices')
        await expect(
            this.page.getByRole('heading', { name: /voice/i }).first()
        ).toBeVisible()
    }

    async openNewVariant() {
        await this.page.getByRole('button', { name: /new variant/i }).click()
        await expect(
            this.page.getByText(/voice workshop|variant/i).first()
        ).toBeVisible()
    }
}

export class OperatorsPage {
    constructor(private readonly page: Page) {}

    async goto() {
        await this.page.goto('/operators')
        // Level 1 + exact: /operator/i alone now matches the page title AND a
        // section heading, which is a strict-mode violation rather than a real
        // failure. Pin the page title so adding sections cannot break the check.
        await expect(
            this.page.getByRole('heading', { level: 1, name: 'Operators' })
        ).toBeVisible()
    }

    /**
     * Opens an EXISTING assignment for editing. There is no create path by
     * design: operators come from the directory, and the console only edits the
     * market scope of someone already there. The former "Assign operator" button
     * was removed deliberately, so a spec looking for one was asserting a
     * workflow the product does not have.
     *
     * Driven through the row kebab rather than a card/row click: the page has a
     * card/table ViewToggle, so a click target that exists in one view does not
     * exist in the other, while "Actions for <name>" is present and named in both.
     */
    async openEditDialog() {
        await this.page
            .getByRole('button', { name: /^Actions for / })
            .first()
            .click()
        await this.page
            .getByRole('menuitem', { name: /edit station access/i })
            .click()
        await expect(this.page.getByRole('dialog')).toBeVisible()
    }

    /**
     * Regression guard for the decision above: no create affordance exists.
     *
     * Anchored to the WHOLE accessible name. An unanchored /new operator/i also
     * matched the kebab of a mock operator named "Casey New Operator", so the
     * guard reported a create button that was never there.
     */
    async expectNoCreateAffordance() {
        await expect(
            this.page.getByRole('button', {
                name: /^(assign|add|new) operator$/i,
            })
        ).toHaveCount(0)
    }
}

export class GuidancePage {
    constructor(private readonly page: Page) {}

    async goto() {
        await this.page.goto('/guidance')
        await expect(
            this.page.getByRole('heading', { name: /rules|guidance/i }).first()
        ).toBeVisible()
    }
}

export class LibraryPage {
    constructor(private readonly page: Page) {}

    async delivery() {
        await this.page.goto('/delivery')
        await expect(
            this.page.getByRole('heading', { name: /delivery/i }).first()
        ).toBeVisible()
    }

    async pronunciation() {
        await this.page.goto('/pronunciation')
        await expect(
            this.page.getByRole('heading', { name: /pronunciation/i }).first()
        ).toBeVisible()
    }
}
