import { test, expect, type Page } from './fixtures'
import { VoicesPage } from './pages/LibraryPages'
import { resetMockStore } from './pages/reset'
import DEV_DATA from '../src/lib/api/mock/dev-data.json' with { type: 'json' }

// Derive the expected library content from the same fixture the mock store
// seeds from, so a dev-data.json refresh (a live dev export) can't strand a
// hardcoded voice name (bit CI 2026-07-16: "ARN Hudson" left the export).
// The JSON import is statically typed via resolveJsonModule.
const firstVoiceProfileName = DEV_DATA.VoiceProfiles[0].Name

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

test('voice variant workflow exposes preview, version, restore, and remix controls', async ({
    page,
}) => {
    const voices = new VoicesPage(page)
    await voices.goto()
    await expect(page.locator('body')).toContainText(firstVoiceProfileName)
    await voices.openNewVariant()
    // The variant studio rail abbreviates the character summary ("stab 50% · sim 80%")
    // and shows the speed multiplier as "1.00×" (voices redesign, 2026-07-03).
    await expect(page.locator('body')).toContainText(
        /stab|similarity|style|preview|remix/i
    )
    await expect(page.locator('body')).toContainText(/pace|speed|\d\.\d{2}×/i)
    await clickIfEnabled(page, /preview/i)
    await clickIfEnabled(page, /save/i)
    await clickIfEnabled(page, /restore/i)
    await clickIfEnabled(page, /remix/i)
    await expect(page.locator('body')).not.toContainText(/application error/i)
})
