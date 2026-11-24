#!/usr/bin/env node
// =============================================================================
// Proposal before/after capture harness
// =============================================================================
//
// Captures one screenshot + one measurement blob per scene, into
// `audit/proposals/<phase>/`. Run once with `before`, apply a proposal, run
// again with `after`, then `node scripts/proposal-doc.mjs` to build the
// review document.
//
// Determinism: the clock and timezone are pinned exactly as in ux-audit.mjs
// (2026-06-22T09:00+10:00, Monday, Sydney peak) so countdowns, slot clocks and
// "x minutes ago" strings render identically across the two runs. Without the
// pin, every before/after pair differs by the seconds that elapsed between
// runs and the diff is unreadable.
//
//   node scripts/proposal-shots.mjs before
//   node scripts/proposal-shots.mjs after --only=p1-dock-overdue
//
// Requires the mock dev server: `npm run dev:mock`.

import { mkdirSync, writeFileSync, rmSync, existsSync } from 'fs'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'
import puppeteer from 'puppeteer'

const BASE = process.env.SHOTS_BASE ?? 'http://127.0.0.1:5173'
const HEADLESS = process.env.HEADLESS !== '0'
const ROOT = resolve(
    dirname(fileURLToPath(import.meta.url)),
    '..',
    'audit',
    'proposals'
)

// Same pinned instant as ux-audit.mjs. 2026-06-22 is a Monday, so the weekday
// peak crons fire, and 09:00 sits mid-peak.
const FIXED_TIME = Date.parse('2026-06-22T09:00:00+10:00')
const TIMEZONE = 'Australia/Sydney'

// The dead-zone viewports are measured, not guessed: a selected bulletin hides
// both the queue rail and the back button from viewport 768px (the `md`
// breakpoint that hides the back button) up to 971px (where the workstation
// container finally crosses `@4xl` = 896px and the queue returns).
const VP = {
    desktop: { width: 1440, height: 900 },
    deadzoneWide: { width: 960, height: 800 },
    deadzoneNarrow: { width: 768, height: 1024 },
    mobile: { width: 390, height: 844 },
}

// ---------------------------------------------------------------------------
// Page helpers, injected into every scene's prep/measure closure
// ---------------------------------------------------------------------------

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

async function selectQueueJob(page, stationFragment) {
    await page.evaluate((frag) => {
        const b = Array.from(document.querySelectorAll('button')).find((e) =>
            (e.textContent || '').includes(frag)
        )
        if (b) b.click()
    }, stationFragment)
    await sleep(1600)
}

/** Radix tabs ignore synthetic clicks in some states; focus + Enter is reliable. */
async function activateTab(page, label) {
    await page.evaluate((l) => {
        const el = Array.from(document.querySelectorAll('[role=tab]')).find(
            (e) => (e.textContent || '').trim() === l
        )
        if (el) el.focus()
    }, label)
    await page.keyboard.press('Enter')
    await sleep(1500)
}

/** Text of the decision dock, plus whether Approve is actionable. */
const measureDock = () => {
    const dock = document.querySelector('.fixed.inset-x-0.bottom-0')
    if (!dock) return { present: false }
    const approve = Array.from(dock.querySelectorAll('button')).find((b) =>
        /approve/i.test(b.textContent || '')
    )
    const r = dock.getBoundingClientRect()
    return {
        present: true,
        text: (dock.textContent || '').replace(/\s+/g, ' ').trim(),
        heightPx: Math.round(r.height),
        approveDisabled: approve ? approve.disabled : null,
        approveLabel: approve ? approve.textContent.trim() : null,
    }
}

/** Every vital chip on the cockpit header, in order. */
const measureVitals = () => {
    const row = document.querySelector('[class*="flex-wrap"][class*="gap-x-5"]')
    return {
        vitals: row
            ? (row.textContent || '').replace(/\s+/g, ' ').trim()
            : null,
    }
}

/** Queue rail: group headings and the ordered rows under each. */
const measureQueue = () => {
    const aside = document.querySelector('aside')
    if (!aside) return { present: false }
    const rows = Array.from(aside.querySelectorAll('button'))
        .map((b) => (b.textContent || '').replace(/\s+/g, ' ').trim())
        .filter((t) => t.length > 4)
    return { present: true, rows: rows.slice(0, 14) }
}

/** Can the operator get back to the queue from a selected bulletin? */
const measureEscape = () => {
    const vis = (e) =>
        !!e &&
        getComputedStyle(e).display !== 'none' &&
        e.getBoundingClientRect().width > 0
    const aside = document.querySelector('aside')
    const back = Array.from(document.querySelectorAll('button,a')).find((e) =>
        /back to queue/i.test(
            (e.getAttribute('aria-label') || '') + (e.textContent || '')
        )
    )
    return {
        queueVisible: vis(aside),
        backVisible: back ? vis(back) : false,
        stranded: !vis(aside) && !(back && vis(back)),
    }
}

/**
 * Truncation loss on the rundown's road names, plus the column budget that
 * causes it.
 *
 * Two earlier versions of this measure were wrong and both under-reported:
 * a `tr`-scoped scan found nothing (the rundown is a div grid, not a
 * `<table>`), and a `document.body` fallback swept in the collapsed nav
 * labels, which are truncated on purpose and read as 99% loss. Exclude the
 * chrome explicitly and report the ROAD column width against the table's
 * unused horizontal space, which is the actual argument.
 */
const measureTruncation = () => {
    const inChrome = (el) => !!el.closest('nav, aside, header, [role=dialog]')
    const out = []
    for (const el of document.querySelectorAll('[class*=truncate]')) {
        if (inChrome(el)) continue
        if (
            el.scrollWidth > el.clientWidth + 2 &&
            (el.textContent || '').trim().length > 4
        ) {
            out.push({
                text: el.textContent.trim().slice(0, 32),
                shownPx: el.clientWidth,
                needsPx: el.scrollWidth,
                lostPct: Math.round(
                    (1 - el.clientWidth / el.scrollWidth) * 100
                ),
            })
        }
    }
    // Column budget: the ROAD header cell against the row it sits in, which is
    // the argument. A previous version referenced an undeclared `card` and threw.
    const headers = Array.from(document.querySelectorAll('*')).filter(
        (e) =>
            e.children.length === 0 &&
            /^road$/i.test((e.textContent || '').trim()) &&
            e.getBoundingClientRect().width > 0
    )
    const roadHeader = headers.find((e) => !inChrome(e))
    const headerRow = roadHeader?.parentElement ?? null
    const budget = roadHeader
        ? {
              roadColPx: Math.round(roadHeader.getBoundingClientRect().width),
              headerRowPx: headerRow
                  ? Math.round(headerRow.getBoundingClientRect().width)
                  : null,
              otherColumns: headerRow
                  ? Array.from(headerRow.children)
                        .filter((c) => c !== roadHeader)
                        .map((c) => ({
                            label: (c.textContent || '').trim().slice(0, 12),
                            px: Math.round(c.getBoundingClientRect().width),
                        }))
                        .filter((c) => c.px > 0)
                  : [],
          }
        : null
    return {
        truncated: out.sort((a, b) => b.lostPct - a.lostPct).slice(0, 12),
        truncatedCount: out.length,
        worstLostPct: out.length ? Math.max(...out.map((o) => o.lostPct)) : 0,
        columnBudget: budget,
    }
}

/**
 * Command palette: the shortcut hint rendered on each page row.
 *
 * Read from the hint element rather than by regex over the row's text: after
 * whitespace collapse a row reads `On airg1`, which no word-boundary pattern
 * can split, so a text scan reported zero advertised shortcuts even while they
 * were on screen.
 */
const measurePalette = () => {
    const items = Array.from(document.querySelectorAll('[cmdk-item]'))
    return {
        advertised: items
            .map((i) => {
                const hint = i.querySelector(
                    '[class*=tracking-widest], kbd, span:last-child'
                )
                const label = (i.firstChild?.textContent || i.textContent || '')
                    .replace(/\s+/g, ' ')
                    .trim()
                const chord = hint
                    ? (hint.textContent || '').replace(/\s+/g, ' ').trim()
                    : ''
                return chord ? `${label} -> ${chord}` : null
            })
            .filter((t) => t !== null)
            .slice(0, 14),
    }
}

/** Board lanes: label plus the condition phrase rendered on each. */
const measureBoard = () => {
    const lanes = Array.from(document.querySelectorAll('[class*="h-12"]'))
        .map((e) => (e.textContent || '').replace(/\s+/g, ' ').trim())
        .filter((t) => t.length > 3)
    return { lanes: lanes.slice(0, 18), laneCount: lanes.length }
}

// ---------------------------------------------------------------------------
// Scenes
// ---------------------------------------------------------------------------

const SCENES = [
    // --- Proposal 1: the time model -----------------------------------------
    {
        id: 'p1-dock-slot-missed',
        proposal: 'p1',
        label: 'Cockpit decision dock, bulletin whose 08:45 slot has passed',
        note: 'arn-job-00008 (3AW Melbourne): approval window is +2:00 over; the air slot passed 15 minutes ago.',
        url: '/jobs/arn-job-00008',
        vp: 'desktop',
        prep: async (page) => {
            await page.evaluate(() => window.scrollTo(0, 99999))
            await sleep(600)
        },
        measure: [measureDock, measureVitals],
    },
    {
        id: 'p1-dock-slot-upcoming',
        proposal: 'p1',
        label: 'Cockpit decision dock, bulletin whose slot is still ahead',
        note: 'arn-job-00007 (2GB Sydney): 09:00 slot is 4 minutes away and the approval window has 6 minutes left.',
        url: '/jobs/arn-job-00007',
        vp: 'desktop',
        prep: async (page) => {
            await page.evaluate(() => window.scrollTo(0, 99999))
            await sleep(600)
        },
        measure: [measureDock, measureVitals],
    },
    {
        id: 'p1-cockpit-header',
        proposal: 'p1',
        label: 'Cockpit vitals header on the missed-slot bulletin',
        note: 'Where the air slot is currently a static grey chip.',
        url: '/jobs/arn-job-00008',
        vp: 'desktop',
        measure: [measureVitals],
    },
    {
        id: 'p1-queue-order',
        proposal: 'p1',
        label: 'Queue rail ordering',
        note: 'Ordered by the 8-minute dwell SLA rather than by time to air.',
        url: '/jobs',
        vp: 'desktop',
        measure: [measureQueue],
    },
    {
        id: 'p1-board-lanes',
        proposal: 'p1',
        label: 'On-air board lanes',
        note: 'The two enabled stations now carry real slots; a passed slot holding an unapproved bulletin should read as missed.',
        url: '/jobs',
        vp: 'desktop',
        measure: [measureBoard],
    },

    // --- Proposal 3: layout ---------------------------------------------------
    {
        id: 'p3-stranded-768',
        proposal: 'p3',
        label: 'Selected bulletin at 768x1024 (iPad portrait)',
        note: 'Queue hidden by the container ruler (@4xl/workstation = 896px container), back button hidden by the viewport ruler (md = 768px viewport). No way back to the other bulletins.',
        url: '/jobs',
        vp: 'deadzoneNarrow',
        prep: async (page) => {
            await selectQueueJob(page, '3AW 693 Melbourne Current')
        },
        measure: [measureEscape],
    },
    {
        id: 'p3-stranded-960',
        proposal: 'p3',
        label: 'Selected bulletin at 960x800 (half of a 1920 screen)',
        note: 'Top of the same dead zone. The queue returns at 972px.',
        url: '/jobs',
        vp: 'deadzoneWide',
        prep: async (page) => {
            await selectQueueJob(page, '3AW 693 Melbourne Current')
        },
        measure: [measureEscape],
    },
    {
        id: 'p3-rundown-desktop',
        proposal: 'p3',
        label: 'Adjust rundown at 1440px',
        note: 'ROAD is the row identity and is the most squeezed column, while TYPE, CLASS and SEVERITY each keep their full width and the card leaves horizontal space unused.',
        url: '/jobs/arn-job-00008',
        vp: 'desktop',
        prep: async (page) => {
            await activateTab(page, 'Adjust')
        },
        measure: [measureTruncation],
    },
    {
        id: 'p3-rundown-mobile',
        proposal: 'p3',
        label: 'Adjust rundown at 390px',
        note: 'Same column budget, less room. The road name is the only thing that identifies the row being re-ranked.',
        url: '/jobs/arn-job-00008',
        vp: 'mobile',
        prep: async (page) => {
            await activateTab(page, 'Adjust')
            await page.evaluate(() => window.scrollTo(0, 240))
            await sleep(500)
        },
        measure: [measureTruncation],
    },

    // --- Proposal 4: shortcuts ----------------------------------------------
    {
        id: 'p4-palette',
        proposal: 'p4',
        label: 'Command palette shortcut hints',
        note: 'Ctrl+1 .. Ctrl+9, Ctrl+0 and Ctrl+i are advertised; no handler binds any of them.',
        url: '/jobs',
        vp: 'desktop',
        prep: async (page) => {
            await page.keyboard.down('Control')
            await page.keyboard.press('k')
            await page.keyboard.up('Control')
            await sleep(900)
        },
        measure: [measurePalette],
    },

    // --- Proposal 5: library drift ------------------------------------------
    {
        id: 'p5-pron-gallery',
        proposal: 'p5',
        label: 'Pronunciation gallery toolbar',
        note: 'No archive control and no show-archived toggle, unlike Delivery and Bookends.',
        url: '/pronunciation',
        vp: 'desktop',
        measure: [
            () => {
                const labels = Array.from(
                    document.querySelectorAll('button,label')
                )
                    .map((e) => (e.textContent || '').trim())
                    .filter(Boolean)
                return {
                    hasArchivedToggle: labels.some((t) => /archived/i.test(t)),
                    toolbar: labels.slice(0, 12),
                }
            },
        ],
    },
    {
        id: 'p5-delivery-gallery',
        proposal: 'p5',
        label: 'Delivery gallery toolbar (the sibling that has the control)',
        note: 'Same entity shape, different capabilities. This is the drift, side by side.',
        url: '/delivery',
        vp: 'desktop',
        measure: [
            () => {
                const labels = Array.from(
                    document.querySelectorAll('button,label')
                )
                    .map((e) => (e.textContent || '').trim())
                    .filter(Boolean)
                return {
                    hasArchivedToggle: labels.some((t) => /archived/i.test(t)),
                    toolbar: labels.slice(0, 12),
                }
            },
        ],
    },
]

// ---------------------------------------------------------------------------
// Runner
// ---------------------------------------------------------------------------

async function main() {
    const phase = process.argv[2]
    if (phase !== 'before' && phase !== 'after') {
        console.error('usage: proposal-shots.mjs <before|after> [--only=a,b]')
        process.exit(2)
    }
    const onlyArg = process.argv.find((a) => a.startsWith('--only='))
    const only = onlyArg ? new Set(onlyArg.slice(7).split(',')) : null
    const scenes = only ? SCENES.filter((s) => only.has(s.id)) : SCENES

    const outDir = resolve(ROOT, phase)
    if (!only && existsSync(outDir))
        rmSync(outDir, { recursive: true, force: true })
    mkdirSync(outDir, { recursive: true })

    const browser = await puppeteer.launch({
        headless: HEADLESS,
        args: [
            '--no-sandbox',
            '--disable-setuid-sandbox',
            '--disable-dev-shm-usage',
        ],
    })
    const page = await browser.newPage()
    await page.emulateTimezone(TIMEZONE).catch(() => {})
    await page.setExtraHTTPHeaders({ 'Accept-Language': 'en-AU' })
    await page.evaluateOnNewDocument((fixed) => {
        const _Date = Date
        class FrozenDate extends _Date {
            constructor(...args) {
                super(...(args.length ? args : [fixed]))
            }
            static now() {
                return fixed
            }
        }

        Date = FrozenDate
    }, FIXED_TIME)
    await page.evaluateOnNewDocument(() => {
        try {
            localStorage.setItem('theme', 'dark')
        } catch {
            /* storage unavailable */
        }
    })
    await page.emulateMediaFeatures([
        { name: 'prefers-color-scheme', value: 'dark' },
    ])

    const errors = []
    page.on('pageerror', (e) => errors.push(String(e).slice(0, 300)))

    const results = []
    for (const scene of scenes) {
        errors.length = 0
        const vp = VP[scene.vp]
        await page.setViewport({ ...vp, deviceScaleFactor: 2 })
        await page.goto(BASE + scene.url, {
            waitUntil: 'networkidle2',
            timeout: 45000,
        })
        await sleep(2200)
        if (scene.prep) await scene.prep(page)
        await sleep(400)

        let measured = {}
        for (const fn of scene.measure ?? []) {
            try {
                Object.assign(measured, await page.evaluate(fn))
            } catch (e) {
                measured.measureError = String(e).slice(0, 200)
            }
        }

        const png = resolve(outDir, `${scene.id}.png`)
        await page.screenshot({ path: png })
        const record = {
            id: scene.id,
            proposal: scene.proposal,
            label: scene.label,
            note: scene.note,
            url: scene.url,
            viewport: `${vp.width}x${vp.height}`,
            measured,
            pageErrors: [...errors],
        }
        writeFileSync(
            resolve(outDir, `${scene.id}.json`),
            JSON.stringify(record, null, 2) + '\n'
        )
        results.push(record)
        console.log(
            `  ${phase}/${scene.id}  ${vp.width}x${vp.height}${errors.length ? `  (${errors.length} page errors)` : ''}`
        )
    }

    writeFileSync(
        resolve(outDir, '_index.json'),
        JSON.stringify(
            {
                phase,
                fixedTime: new Date(FIXED_TIME).toISOString(),
                timezone: TIMEZONE,
                scenes: results,
            },
            null,
            2
        ) + '\n'
    )
    await browser.close()
    console.log(
        `\n${phase}: ${results.length} scenes -> audit/proposals/${phase}/`
    )
}

main().catch((e) => {
    console.error(e)
    process.exit(1)
})
