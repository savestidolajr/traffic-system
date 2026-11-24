import type { Page } from '@playwright/test'

declare global {
    interface Window {
        __ARN_MOCK_RESET__?: () => void
    }
}

export async function resetMockStore(page: Page) {
    await page.goto('/')
    await page.evaluate(() => {
        window.__ARN_MOCK_RESET__?.()
    })
}
