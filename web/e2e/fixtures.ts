import { test as base, expect, type Page } from '@playwright/test'

const fixedIso = '2026-06-22T09:00:00+10:00'
const fixedTime = Date.parse(fixedIso)

async function installDeterminism(page: Page) {
    await page.addInitScript((time) => {
        const RealDate = Date
        // Freeze ONLY the "current instant": a bare `new Date()` / `Date.now()`
        // returns the fixed time. Every argument form must pass through
        // untouched — an earlier version forwarded just the first argument, so
        // `new Date(year, month, day)` collapsed to `Date(year)` = epoch+2s,
        // which made date-math's daysInMonth report 1 day for every month and
        // sent the cron enumerator's month recursion off a stack overflow on
        // the review page (e2e-only, invisible at real clock time).
        class FrozenDate extends RealDate {
            constructor(...args: ConstructorParameters<typeof Date>) {
                if (args.length === 0) super(time)
                else super(...args)
            }

            static now() {
                return time
            }
        }

        Object.defineProperty(FrozenDate, 'UTC', { value: RealDate.UTC })
        Object.defineProperty(FrozenDate, 'parse', { value: RealDate.parse })
        Object.defineProperty(window, 'Date', { value: FrozenDate })
    }, fixedTime)

    await page.addStyleTag({
        content:
            '*,*::before,*::after{animation-duration:0s!important;animation-delay:0s!important;transition-duration:0s!important;transition-delay:0s!important;scroll-behavior:auto!important;caret-color:transparent!important}',
    })
}

export const test = base.extend<{ deterministicPage: Page }>({
    page: async ({ page }, use) => {
        await installDeterminism(page)
        await use(page)
    },
    deterministicPage: async ({ page }, use) => {
        await use(page)
    },
})

export { expect }
