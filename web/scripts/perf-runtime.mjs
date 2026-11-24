#!/usr/bin/env node
// =============================================================================
// Runtime performance probe — Lighthouse User Flow, TIMESPAN mode.
//
// perf-audit.mjs (Lighthouse navigation mode) answers "is the cold pageload
// fast" — LCP/TBT/CLS for a fresh route. It CANNOT see the failure mode this
// script targets: a page that has already loaded and sits idle, or is mid
// interaction, burning main-thread JS on background work (re-render loops,
// rAF ticks, setInterval polling) — exactly what shows up to a user as
// "the JS/animations are laggy" with no navigation involved at all.
//
// Timespan mode (Lighthouse v10+, github.com/GoogleChrome/lighthouse/blob/main/docs/user-flows.md)
// records main-thread activity over an arbitrary window — page already
// loaded, zero or more interactions — and reports the SAME script/style/
// layout/paint cost breakdown, without needing a fresh navigation to anchor
// on. It is Google's own tool for exactly this question.
//
// CPU throttling: Lighthouse's `desktopConfig` defaults to
// cpuSlowdownMultiplier=1 (no throttling) — meaningless on a fast dev
// workstation. Every scenario below runs at BOTH 1x (this machine's real
// speed) and 4x (Lighthouse's own mobile-equivalent throttle, a reasonable
// stand-in for a weaker/older operator workstation) via
// `Emulation.setCPUThrottlingRate` — the budget is checked against the
// throttled run, since "does it stay smooth on real-world hardware" is the
// actual question, not "is it fast on the author's machine".
//
// Three scenarios, chosen from a static read of the client-side re-render
// hooks (useSecondTick/useNow: web/src/hooks/use-second-tick.ts), the
// AudioWaveformPlayer rAF loop, and the map-heavy Adjust tab:
//
//   idle-onair    — /jobs, nothing clicked. Isolates ambient background cost:
//                   the On-Air board's 1Hz countdown tick re-renders
//                   QueueBeam + CoverageHome's full (unmemoized) subtree every
//                   second even with zero user interaction.
//   audio-review  — open a job, click Play, let it run. Isolates
//                   AudioWaveformPlayer's requestAnimationFrame loop (fires
//                   onTimeUpdate every frame while playing, ~60Hz) and the
//                   karaoke recompute chain it drives in ReviewBulletin →
//                   JobReviewCockpit.
//   adjust-map    — open a job, click the "Adjust" tab (mounts the evidence
//                   map slot + RundownTable), let it settle+idle. Mock mode has
//                   no HERE key, so the slot is `MapUnavailable`: this measures
//                   the edit surface's idle churn, not a basemap mount.
//
// Usage:
//   npm run perf:runtime             — build:mock + probe + budget-gate
//   npm run perf:runtime -- --no-fail
//
// Sampling: the 1x run is single-sample (informational, fast). The gated 4x
// run is MEDIAN-OF-3 — devtools CPU throttling multiplies live host
// performance rather than simulating a fixed absolute rate, so on a shared
// dev machine under concurrent load the same scenario can legitimately
// swing 2-3x run to run (observed: idle-onair 4x ranged 460-1570ms across
// single-sample runs at load average 4.6-5.66 on a 16-core box). Median-of-3
// absorbs that without needing to widen budgets into meaninglessness.
//
// Output: web/perf/runtime-<scenario>-<throttle>x.json (full LHR of the
// MEDIAN 4x sample; all 1x/4x runs share the JSON naming — only the median
// is persisted to keep output size bounded) + web/perf/runtime-<scenario>-
// <throttle>x.html (its flow report) + web/perf/runtime-report.md (summary
// + verdicts, showing all 3 raw 4x samples alongside the median).
// =============================================================================
import { mkdirSync, writeFileSync } from 'fs'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'
import { spawn } from 'child_process'
import puppeteer from 'puppeteer'
import { startFlow, desktopConfig } from 'lighthouse'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const OUT = resolve(ROOT, 'perf')
const PORT = Number(process.env.PERF_PREVIEW_PORT ?? 4173)
const BASE = process.env.PERF_BASE_URL ?? `http://127.0.0.1:${PORT}`
const NO_FAIL = process.argv.includes('--no-fail')
const IDLE_MS = 10_000
const CPU_MULTIPLIERS = [1, 4]

// Budgets checked ONLY against the 4x-throttled run (see header). Calibrated
// to the measured floor AFTER fixing two real bugs found via this harness
// (2026-07-01): QueueBeam/CoverageHome/ReviewPane subscribing the whole
// subtree to a 1Hz tick instead of isolating it to the leaf that displays a
// countdown, and AuroraHero/AuroraWash animating `background-position`
// (forces main-thread layout+paint every frame) instead of `transform`
// (compositor-only). Measured post-fix: idle-onair ~520ms (legitimate —
// 8 WAITING_APPROVAL rows each ticking their own countdown SVG),
// audio-review ~1570ms (legitimate — AudioWaveformPlayer's rAF-driven
// karaoke highlight, intentional while audio plays), adjust-map ~760ms
// (Leaflet mount). Budgets = measured + ~35% headroom, not the pre-fix
// guess — a regression back toward the pre-fix numbers (1900ms+) trips this.
const BUDGETS = {
    'idle-onair': {
        totalMainThreadMs: { max: 700 }, // ms of main-thread work across a 10s IDLE window, 4x CPU
        totalBlockingTime: { max: 100 },
    },
    'audio-review': {
        totalMainThreadMs: { max: 2100 }, // rAF loop running is expected/intentional (karaoke sync)
        totalBlockingTime: { max: 500 },
    },
    'adjust-map': {
        totalMainThreadMs: { max: 1700 }, // Leaflet mount + marker/polyline layout — observed 760-1233ms across runs (map tile load timing varies)
        totalBlockingTime: { max: 500 },
    },
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

async function waitForServer(url, timeoutMs = 20000) {
    const start = Date.now()
    while (Date.now() - start < timeoutMs) {
        try {
            const res = await fetch(url)
            if (res.ok || res.status === 404) return true
        } catch {
            /* not up yet */
        }
        await sleep(300)
    }
    throw new Error(`preview server did not come up at ${url}`)
}

function startPreviewServer() {
    const proc = spawn(
        'npx',
        [
            'vite',
            'preview',
            '--outDir',
            'dist-mock',
            '--host',
            '127.0.0.1',
            '--port',
            String(PORT),
            '--strictPort',
        ],
        // detached => own process group, so the group kill below reaches the real
        // vite child behind the npx wrapper (killing only npx orphans vite, which
        // keeps the CI step's pipes open forever — observed hung run 20260703.9).
        { cwd: ROOT, stdio: 'pipe', detached: true }
    )
    proc.stderr.on('data', (d) => process.stderr.write(d))
    return proc
}

/** Click a button/tab by its visible text — no test-id plumbing needed for a
 *  probe script; matches the interaction a real operator performs. */
async function clickByText(page, text) {
    const handle = await page.evaluateHandle((t) => {
        const els = Array.from(document.querySelectorAll('button,[role=tab],a'))
        return (
            els.find((el) => (el.textContent || '').trim().includes(t)) ?? null
        )
    }, text)
    const el = handle.asElement()
    if (!el) {
        console.warn(`  (click target not found: "${text}")`)
        return false
    }
    await el.click()
    return true
}

/** Pull the diagnostic numbers out of a timespan LHR — no performance score
 *  exists in timespan mode (Lighthouse doesn't have a moment to score from),
 *  so these three audits ARE the result. */
function extractRuntimeMetrics(lhr) {
    const audits = lhr.audits
    const mtb = audits['mainthread-work-breakdown']
    const breakdown = mtb?.details?.items ?? []
    const byGroup = Object.fromEntries(
        breakdown.map((i) => [i.groupLabel, Math.round(i.duration)])
    )
    return {
        totalBlockingTime: audits['total-blocking-time']?.numericValue ?? 0,
        // Authoritative total main-thread cost for the window, regardless of
        // which category bucket small tasks land in (at low absolute
        // durations Chrome often buckets everything under "Other" — reading
        // byGroup['Script Evaluation'] alone would silently under-report).
        totalMainThreadMs: Math.round(mtb?.numericValue ?? 0),
        scriptingMs: byGroup['Script Evaluation'] ?? 0,
        renderingMs: byGroup['Rendering'] ?? 0,
        layoutMs: byGroup['Layout'] ?? 0,
        paintingMs: byGroup['Painting'] ?? 0,
        otherMs: byGroup['Other'] ?? 0,
        bootupScriptingMs: Math.round(audits['bootup-time']?.numericValue ?? 0),
        mainThreadBreakdown: byGroup,
    }
}

function checkBudgets(scenario, metrics) {
    const budget = BUDGETS[scenario]
    const verdicts = []
    for (const [key, b] of Object.entries(budget)) {
        const value = metrics[key]
        if (value == null) continue
        const breached = b.max != null && value > b.max
        verdicts.push({ key, value, budget: b, breached })
    }
    return verdicts
}

/** Run one timespan window at a given CPU throttle multiplier. `setup` does
 *  navigation/interaction BEFORE the window opens (page in the right state);
 *  `duringWindow` runs after `startTimespan` (the thing being measured). */
async function probeScenario(page, { id, cpuMultiplier, setup, duringWindow }) {
    await setup(page)
    await sleep(500) // let the post-setup state settle before the window opens

    const flow = await startFlow(page, {
        config: {
            ...desktopConfig,
            settings: {
                ...desktopConfig.settings,
                throttling: {
                    ...desktopConfig.settings.throttling,
                    cpuSlowdownMultiplier: cpuMultiplier,
                },
            },
        },
        flags: { screenEmulation: { disabled: true } },
        name: `${id}-${cpuMultiplier}x`,
    })
    await flow.startTimespan()
    await duringWindow(page)
    await flow.endTimespan()

    const flowResult = await flow.createFlowResult()
    const lhr = flowResult.steps[0].lhr
    return { lhr, report: await flow.generateReport() }
}

async function run() {
    mkdirSync(OUT, { recursive: true })

    let previewProc = null
    if (process.env.PERF_REUSE_SERVER !== '1') {
        console.log(`\n── starting preview server on :${PORT} ──`)
        previewProc = startPreviewServer()
    }
    await waitForServer(BASE)

    console.log(`── Arn Runtime Performance Probe (Lighthouse timespan) ──`)
    console.log(`   base: ${BASE}\n`)

    const browser = await puppeteer.launch({
        headless: true,
        args: ['--no-sandbox', '--disable-setuid-sandbox'],
    })
    const page = await browser.newPage()
    await page.setViewport({ width: 1440, height: 900 })

    const scenarios = [
        {
            id: 'idle-onair',
            desc: '/jobs, zero interaction (ambient tick cost)',
            setup: async (p) => {
                await p.goto(BASE + '/jobs', { waitUntil: 'networkidle0' })
            },
            duringWindow: async () => {
                await sleep(IDLE_MS)
            },
        },
        {
            id: 'audio-review',
            desc: 'open a job + click Play + let it run',
            setup: async (p) => {
                await p.goto(BASE + '/jobs/arn-job-00008', {
                    waitUntil: 'networkidle0',
                })
                await p
                    .waitForSelector('audio', { timeout: 10000 })
                    .catch(() => {})
            },
            duringWindow: async (p) => {
                await p
                    .evaluate(() => {
                        document
                            .querySelector('audio')
                            ?.play()
                            .catch(() => {})
                    })
                    .catch(() => {})
                await sleep(8000)
            },
        },
        {
            id: 'adjust-map',
            desc: 'open a job + click Adjust tab (edit surface; no basemap in mock mode)',
            setup: async (p) => {
                await p.goto(BASE + '/jobs/arn-job-00008', {
                    waitUntil: 'networkidle0',
                })
                await p
                    .waitForSelector('[role=tab]', { timeout: 10000 })
                    .catch(() => {})
            },
            duringWindow: async (p) => {
                const clicked = await clickByText(p, 'Adjust')
                if (!clicked)
                    throw new Error(
                        'adjust-map: "Adjust" tab not found — job may be gated behind auth or in the wrong status'
                    )
                await sleep(1500) // let the map mount/tiles settle
                await sleep(6500) // idle with the map+table on screen
            },
        },
    ]

    const results = []
    let anyBreach = false

    try {
        for (const scenario of scenarios) {
            // 1x: single sample, informational only.
            const oneX = await probeScenario(page, {
                ...scenario,
                cpuMultiplier: 1,
            })
            const oneXMetrics = extractRuntimeMetrics(oneX.lhr)
            results.push({
                scenario: scenario.id,
                desc: scenario.desc,
                cpuMultiplier: 1,
                metrics: oneXMetrics,
                verdicts: [],
            })
            console.log(
                `  ${scenario.id}-1x: total=${oneXMetrics.totalMainThreadMs}ms (script=${oneXMetrics.scriptingMs} render=${oneXMetrics.renderingMs} layout=${oneXMetrics.layoutMs} paint=${oneXMetrics.paintingMs} other=${oneXMetrics.otherMs}) TBT=${Math.round(oneXMetrics.totalBlockingTime)}ms`
            )

            // 4x: THREE samples — the gated multiplier, median absorbs
            // host-load-driven variance in devtools CPU throttling (see
            // header). All three raw samples are kept for the report; only
            // the median sample's full LHR/flow-report is persisted.
            const fourXSamples = []
            for (let i = 0; i < 3; i++) {
                const { lhr, report } = await probeScenario(page, {
                    ...scenario,
                    cpuMultiplier: 4,
                })
                fourXSamples.push({
                    metrics: extractRuntimeMetrics(lhr),
                    lhr,
                    report,
                })
            }
            fourXSamples.sort(
                (a, b) =>
                    a.metrics.totalMainThreadMs - b.metrics.totalMainThreadMs
            )
            const medianSample = fourXSamples[1]
            const verdicts = checkBudgets(scenario.id, medianSample.metrics)
            if (verdicts.some((v) => v.breached)) anyBreach = true

            results.push({
                scenario: scenario.id,
                desc: scenario.desc,
                cpuMultiplier: 4,
                metrics: medianSample.metrics,
                verdicts,
                allSamples: fourXSamples.map(
                    (s) => s.metrics.totalMainThreadMs
                ),
            })
            const tag = `${scenario.id}-4x`
            writeFileSync(
                resolve(OUT, `runtime-${tag}.json`),
                JSON.stringify(medianSample.lhr, null, 2)
            )
            writeFileSync(
                resolve(OUT, `runtime-${tag}.html`),
                medianSample.report
            )

            const sampleStr = fourXSamples
                .map((s) => s.metrics.totalMainThreadMs)
                .join('/')
            console.log(
                `  ${tag}: median=${medianSample.metrics.totalMainThreadMs}ms (samples: ${sampleStr}ms) script=${medianSample.metrics.scriptingMs} render=${medianSample.metrics.renderingMs} TBT=${Math.round(medianSample.metrics.totalBlockingTime)}ms${verdicts.some((v) => v.breached) ? '  ⚠️ BUDGET BREACH' : ''}`
            )
        }
    } finally {
        await browser.close()
        if (previewProc) killPreviewServer(previewProc)
    }

    writeReport(results)

    console.log(`\n   report: web/perf/runtime-report.md`)
    console.log(
        `   flow reports: web/perf/runtime-<scenario>-<throttle>x.html (open in a browser)\n`
    )

    if (anyBreach && !NO_FAIL) {
        console.error(
            'Runtime performance budget breached (4x-throttled run) — see web/perf/runtime-report.md'
        )
        process.exit(1)
    }
    // Explicit success exit: never linger on stray event-loop handles in CI.
    process.exit(0)
}

function killPreviewServer(proc) {
    try {
        process.kill(-proc.pid, 'SIGTERM') // whole group: npx wrapper + vite
    } catch {
        proc.kill()
    }
}

function writeReport(results) {
    const byScenario = new Map()
    for (const r of results) {
        if (!byScenario.has(r.scenario)) byScenario.set(r.scenario, [])
        byScenario.get(r.scenario).push(r)
    }

    const sections = [...byScenario.entries()].map(([scenario, runs]) => {
        const desc = runs[0].desc
        const rows = runs
            .map((r) => {
                const label =
                    r.cpuMultiplier === 4
                        ? `4x (median of ${r.allSamples.join('/')}ms)`
                        : '1x'
                return `| ${label} | ${r.metrics.totalMainThreadMs}ms | ${r.metrics.scriptingMs}ms | ${r.metrics.renderingMs}ms | ${r.metrics.layoutMs}ms | ${r.metrics.paintingMs}ms | ${Math.round(r.metrics.totalBlockingTime)}ms |`
            })
            .join('\n')
        const breach = runs.flatMap((r) => r.verdicts.filter((v) => v.breached))
        return `### ${scenario}

${desc}

| CPU throttle | Total | Script | Rendering | Layout | Painting | TBT |
|---|---|---|---|---|---|---|
${rows}

${breach.length ? `**Budget breached (4x median):** ${breach.map((b) => `\`${b.key}\`=${Math.round(b.value)}ms (budget ${JSON.stringify(b.budget)})`).join(', ')}` : 'No breaches (4x median within budget).'}
`
    })

    const md = `# Runtime Performance Probe Report

Generated ${new Date().toISOString()}. Lighthouse **timespan mode** (github.com/GoogleChrome/lighthouse User Flows) against a production build in mock mode — measures main-thread JS cost DURING a session (idle background work, real click-driven interactions), as opposed to \`npm run perf\`'s navigation-mode cold-load metrics. This is the tool for "the JS/animations feel laggy while using the app" reports, which cold-load LCP/TBT cannot see.

Every scenario runs at **1x** (single sample, informational — this machine's native speed) and **4x** CPU throttle (Lighthouse's own mobile-equivalent slowdown, standing in for a weaker/older operator workstation; **median of 3 samples** — devtools CPU throttling multiplies live host performance rather than simulating a fixed absolute rate, so a single 4x sample on a busy dev machine can swing 2-3x run to run; median-of-3 absorbs that). The budget gate checks the **4x median only**.

${sections.join('\n')}
## Notes

- \`idle-onair\` isolates ambient background cost with **zero user interaction** — nonzero \`scriptingMs\` here is re-render/polling/animation-driver overhead from code that runs whether or not the operator does anything (\`web/src/hooks/use-second-tick.ts\`, consumed by \`QueueBeam\`/\`CoverageHome\`).
- \`audio-review\` isolates the audio-playback karaoke-sync path (\`AudioWaveformPlayer\`'s per-frame \`requestAnimationFrame\` loop → \`ReviewBulletin.handleTimeUpdate\` → \`JobReviewCockpit\` re-render).
- \`adjust-map\` isolates the edit surface's mount + idle cost (mock mode has no HERE key, so no basemap is mounted).
- Full Lighthouse flow report for the median 4x sample: \`web/perf/runtime-<scenario>-4x.html\` — open in a browser for the same trace breakdown DevTools' Performance panel shows, plus Lighthouse's own annotations. This is the artifact to inspect if a budget breaches — it names the actual long tasks.
- Mock data scale (17 jobs, 19 stations) is smaller than a production fleet across many markets — if these probes stay clean but the deployed app is still reported laggy, re-run against a scaled-up mock dataset or capture a live CDP trace per \`docs/operations/performance-testing.md\` §2.
- **CI treats this probe as informational, NOT a hard merge gate** (unlike \`npm run perf\`'s navigation-mode audit) — the variance above, even after median-of-3, is real and CI runners aren't guaranteed a quiet host either. A budget breach is a strong signal to investigate (open the flow-report HTML), not an automatic block. See \`docs/operations/performance-testing.md\`.
- This does NOT replace \`npm run perf\` (cold-load regression gate) or live CDP profiling (network/infra diagnosis).
`
    writeFileSync(resolve(OUT, 'runtime-report.md'), md)
}

run().catch((e) => {
    console.error(e)
    process.exit(1)
})
