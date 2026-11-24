// =============================================================================
// Air-plan commit check — local experiment harness
// =============================================================================
// Proves the audition panel's commit path end to end in mock mode: open the
// panel on a station that has an alternative to hear, press its Air button, and
// report what the row forecasts afterwards.
//
//   npm run dev:mock            # in another shell
//   node scripts/air-plan-commit-check.mjs
//
// Read the two "before"/"after" lines: the forecast must change, and the
// station's tray line must report the new write.

import puppeteer from 'puppeteer'

const BASE = process.env.BASE_URL ?? 'http://localhost:5173'

const rowText = () =>
    [...document.querySelectorAll('.group\\/lane')].map((r) =>
        r.innerText.replace(/\n/g, ' | ')
    )

async function main() {
    const browser = await puppeteer.launch({
        headless: true,
        args: ['--no-sandbox', '--disable-dev-shm-usage'],
    })
    const page = await browser.newPage()
    await page.setViewport({ width: 1600, height: 1000 })
    page.on('pageerror', (e) => console.log('[page error]', e.message))
    page.on('console', (m) => {
        if (m.type() === 'error')
            console.log('[console]', m.text().slice(0, 300))
    })

    await page.goto(`${BASE}/jobs`, { waitUntil: 'networkidle2' })
    await page.waitForFunction(
        () =>
            document.querySelectorAll('button[aria-label*="Hear or change"]')
                .length > 0,
        { timeout: 30_000 }
    )

    // Pick the row whose panel offers something to commit. Opening each panel is
    // the only way to know: the candidate list is derived per station.
    const triggers = await page.$$('button[aria-label*="Hear or change"]')
    for (const [index, trigger] of triggers.entries()) {
        await trigger.click()
        await page
            .waitForSelector(
                '[role="dialog"][data-radix-popper-content-wrapper], [data-radix-popper-content-wrapper]:has(ul)',
                {
                    timeout: 5_000,
                }
            )
            .catch(() => {})
        await new Promise((r) => setTimeout(r, 400))

        const airButton = await page.$(
            '[data-radix-popper-content-wrapper] button[aria-label^="Air "]'
        )
        if (!airButton) {
            await page.keyboard.press('Escape')
            continue
        }

        const label = await page.evaluate(
            (b) => b.getAttribute('aria-label'),
            airButton
        )
        const station = await page.evaluate(
            (b) => b.getAttribute('aria-label').split(' on ').pop(),
            airButton
        )
        const find = (rows) =>
            rows.find((r) => r.startsWith(station)) ?? '(row gone)'
        const before = find(await page.evaluate(rowText))
        console.log('picking:', label)
        console.log('before :', before)

        await airButton.click()
        // The panel closes itself on success, then the row re-renders from the
        // invalidated jobs query.
        await page
            .waitForFunction(
                () =>
                    !document.querySelector(
                        '[role="dialog"][data-radix-popper-content-wrapper], [data-radix-popper-content-wrapper]:has(ul)'
                    ),
                { timeout: 10_000 }
            )
            .catch(async () => {
                const panel = await page.evaluate(() => {
                    const d = document.querySelector(
                        '[data-radix-popper-content-wrapper]:has(ul)'
                    )
                    return d ? d.innerText : '(gone)'
                })
                console.log(
                    'WARN: panel stayed open:',
                    panel.replace(/\n/g, ' | ')
                )
            })
        await new Promise((r) => setTimeout(r, 1500))

        const after = find(await page.evaluate(rowText))
        console.log('after  :', after)
        await browser.close()
        return
    }

    console.log('WARN: no station offered an Air action')
    await browser.close()
}

main().catch((e) => {
    console.error(e)
    process.exit(1)
})
