// =============================================================================
// Air-plan screenshots — local experiment harness
// =============================================================================
// Captures the "Set to publish" table on the on-air board in mock mode, plus the
// candidate picker open. Same launch flags as scripts/ux-audit.mjs so the shots
// match what that harness would produce.
//
//   npm run dev:mock            # in another shell
//   node scripts/air-plan-shots.mjs
//
// Output: web/audit/air-plan/*.png

import puppeteer from 'puppeteer'
import { mkdir } from 'node:fs/promises'

const BASE = process.env.BASE_URL ?? 'http://localhost:5173'
const OUT = 'audit/air-plan'

async function main() {
    await mkdir(OUT, { recursive: true })
    const browser = await puppeteer.launch({
        headless: true,
        args: [
            '--no-sandbox',
            '--disable-setuid-sandbox',
            '--disable-dev-shm-usage',
        ],
    })
    const page = await browser.newPage()
    await page.setViewport({ width: 1600, height: 1000, deviceScaleFactor: 2 })

    page.on('console', (m) => {
        if (m.type() === 'error') console.log('[console error]', m.text())
    })
    page.on('pageerror', (e) => console.log('[page error]', e.message))

    await page.goto(`${BASE}/jobs`, {
        waitUntil: 'networkidle2',
        timeout: 60_000,
    })

    // The plan renders once jobs + schedules land.
    await page
        .waitForFunction(
            () =>
                [
                    ...document.querySelectorAll(
                        'button[aria-label^="Choose what airs"]'
                    ),
                ].length > 0,
            { timeout: 30_000 }
        )
        .catch(() => console.log('WARN: no lane forecast appeared'))

    await new Promise((r) => setTimeout(r, 1200))
    await page.screenshot({ path: `${OUT}/board.png`, fullPage: false })

    // Geometry proof rather than an eyeball: report the table's own rows.
    // Read the lane rows themselves: label text, and whether the cutoff tick
    // is positioned inside its own track rather than clamped to an edge.
    const rows = await page.evaluate(() => {
        // Measure the cards: equal widths/heights is the symmetry claim, and
        // "the whole tile opens the bulletin" is a real cursor, not a hope.
        return [...document.querySelectorAll('.group\\/lane')].map((card) => {
            const box = card.getBoundingClientRect()
            // The track is the 2nd cell: measure the window bar's placement so
            // "each station shows its own schedule" is verified, not assumed.
            const track = card.children[1]
            const trackBox = track?.getBoundingClientRect()
            const bar = track?.querySelector('span[class*="rounded-sm"]')
            const barBox = bar?.getBoundingClientRect()
            const ticks = track?.querySelectorAll('span.w-px') ?? []
            return {
                text: [...card.children]
                    .map((c) => c.innerText?.trim())
                    .filter(Boolean)
                    .join(' | '),
                h: Math.round(box.height),
                clickable: getComputedStyle(card).cursor === 'pointer',
                barLeftPct:
                    barBox && trackBox
                        ? Math.round(
                              ((barBox.left - trackBox.left) / trackBox.width) *
                                  100
                          )
                        : null,
                barWidthPct:
                    barBox && trackBox
                        ? Math.round((barBox.width / trackBox.width) * 100)
                        : null,
                laterTicks: ticks.length,
                action: card.children[4]?.innerText?.trim() ?? null,
                // Column geometry: every cell must start at the same x across
                // rows, or the table is not aligned.
                cellLefts: [...card.children].map((c) =>
                    Math.round(c.getBoundingClientRect().left)
                ),
                actionBtnWidth: Math.round(
                    card.children[4]
                        ?.querySelector('button')
                        ?.getBoundingClientRect().width ?? 0
                ),
            }
        })
    })
    console.log(JSON.stringify(rows, null, 1))

    // The clock check: the rail's countdown for a station must match the board's.
    const rail = await page.evaluate(() => {
        const items = [...document.querySelectorAll('button')]
            .filter((b) => /in \d|\+\d/.test(b.innerText || ''))
            .slice(0, 6)
        return items.map((b) => b.innerText.replace(/\n+/g, ' · ').trim())
    })
    console.log('--- rail ---\n' + rail.join('\n'))

    // Open the audition panel on a row that has options to hear. The forecast
    // itself is the trigger, so its label is the "what airs" sentence.
    const forecast = await page.$('button[aria-label*="Hear or change"]')
    if (forecast) {
        await forecast.click()
        await page
            .waitForSelector('[data-radix-popper-content-wrapper]', {
                timeout: 10_000,
            })
            .catch(() => console.log('WARN: audition panel never opened'))
        await new Promise((r) => setTimeout(r, 900))
        await page.screenshot({ path: `${OUT}/picker.png`, fullPage: false })

        const panel = await page.evaluate(() => {
            const d = document.querySelector(
                '[data-radix-popper-content-wrapper]'
            )
            if (!d) return null
            const rows = [...d.querySelectorAll('li')].map((li) => {
                const btns = [...li.querySelectorAll('button')].map((b) =>
                    (b.getAttribute('aria-label') || b.innerText).trim()
                )
                return `${li.innerText.replace(/\n/g, ' | ')}   [${btns.join(' + ')}]`
            })
            return [
                'HEADER>> ' + d.innerText.split('\n').slice(0, 3).join(' /// '),
                ...rows,
            ].join('\n')
        })
        console.log('--- panel ---\n' + panel)
    } else {
        console.log('WARN: no forecast trigger found')
    }

    await browser.close()
}

main().catch((e) => {
    console.error(e)
    process.exit(1)
})
