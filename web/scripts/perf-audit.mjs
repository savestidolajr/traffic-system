#!/usr/bin/env node
// =============================================================================
// Frontend quality audit harness — Lighthouse against a PRODUCTION build served
// in mock mode, for a representative set of routes. Companion to ux-audit.mjs
// (screenshots/axe/consistency/VLM review): this one is the deterministic
// regression gate — bundle weight, render timing, main-thread cost, and the two
// scored quality categories below.
//
// Three categories, three different questions:
//   performance       — is the app fast? (bundle, render, main thread)
//   accessibility     — can a human with assistive tech use it? Lighthouse runs
//                       axe under the hood but SCORES it, and covers audits the
//                       ux-audit crawl does not surface as violations.
//   agentic-browsing  — can a MACHINE use it? Added in Lighthouse 13.3: the
//                       accessibility tree an agent navigates by, WebMCP tool
//                       declarations, llms.txt discoverability, and layout
//                       stability (a late shift makes an agent misclick).
//                       Reported by Lighthouse as a pass ratio, not a weighted
//                       0-100 like the others.
//
// Why a production build, not `vite dev`: dev serves unbundled ESM modules
// (hundreds of small requests, no minification/tree-shaking) — its timings
// are meaningless for real-world perf. `npm run build:mock` produces the same
// optimized bundle that ships, wired to the deterministic mock API so pages
// render real content offline (no backend, no auth wall, byte-stable runs).
//
// Usage:
//   npm run perf              — build:mock + audit + budget-gate (exit 1 on breach)
//   npm run perf:nobuild       — reuse the existing dist-mock/ (fast iteration)
//   npm run perf -- --no-fail  — report only, never fail the process
//
// Output: web/perf/<route-id>.json (full Lighthouse LHR) + web/perf/report.md
// (scores/metrics table + budget verdicts) + web/perf/history.jsonl (one line
// appended per run, so trend regressions are diffable over time).
// =============================================================================
import { mkdirSync, writeFileSync, appendFileSync, existsSync } from 'fs'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'
import { spawn } from 'child_process'
import net from 'net'
import * as chromeLauncher from 'chrome-launcher'
import lighthouse from 'lighthouse'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const OUT = resolve(ROOT, 'perf')
const PORT = Number(process.env.PERF_PREVIEW_PORT ?? 4173)
const BASE = process.env.PERF_BASE_URL ?? `http://127.0.0.1:${PORT}`
const NO_FAIL = process.argv.includes('--no-fail')

// Every route in `src/lib/routes.ts` that RENDERS a page. `/schedules` and
// `/trigger` are exempt — both are bare `<Navigate>` redirects to `/stations`
// in App.tsx. The list was 7 routes until 2026-07-27; the six it omitted had
// never been scored for accessibility or agentic-browsing at all, which is how
// four H1→H3 heading skips survived on /delivery and /pronunciation.
const ROUTES = [
    { id: 'home', path: '/', desc: 'Landing dashboard' },
    { id: 'onair', path: '/jobs', desc: 'On-air board (queue + beam)' },
    {
        id: 'review-adjust',
        // Was `arn-job-00008` — an id with no mock fixture, so this route
        // scored the job-not-found ERROR PAGE, not the review surface, for its
        // whole life. Every metric it reported for the heaviest screen in the
        // app was measuring an empty state.
        path: '/jobs/f034af33f0ba4f6288f52e22bce8bb27',
        desc: 'Job review — Adjust tab (map + 33 incidents)',
    },
    { id: 'stations', path: '/stations', desc: 'Station profiles' },
    { id: 'voices', path: '/voices', desc: 'Voice library' },
    { id: 'guidance', path: '/guidance', desc: 'Bulletin rules hub' },
    { id: 'history', path: '/history', desc: 'History log' },
    { id: 'operators', path: '/operators', desc: 'Operator assignments' },
    { id: 'delivery', path: '/delivery', desc: 'Delivery-style gallery' },
    {
        id: 'pronunciation',
        path: '/pronunciation',
        desc: 'Pronunciation dictionary gallery',
    },
    { id: 'activity', path: '/activity', desc: 'Activity / change audit log' },
    {
        id: 'config-transfer',
        path: '/config-transfer',
        desc: 'Config bundle export/import',
    },
]

// Categories requested from Lighthouse. `performance` drives the metric
// budgets; the other two are scored quality gates (see header).
const CATEGORIES = ['performance', 'accessibility', 'agentic-browsing']

// Audits deliberately excluded from the scored gate, with the reason. Excluded
// audits are removed from BOTH the numerator and the denominator when the
// category score is recomputed (recomputeCategoryScore), so a waiver lowers the
// bar for nothing else — the remaining audits still have to pass outright.
//
// Keep this list short and argued. An audit that is merely inconvenient does
// not belong here; only ones that cannot apply to this product.
const WAIVED_AUDITS = {
    'llms-txt':
        'Agent-facing site summary at the domain root. This is an authenticated internal operator console behind Entra ID — there is no anonymous agent to serve it to, and the SWA root is the app shell.',
}

// Hard budgets — a breach fails the run (CI-gating). Chosen from Lighthouse's
// own "good" thresholds for the Core Web Vitals, applied under simulated
// throttling (Lighthouse default: mid-tier mobile CPU + slow 4G) so the
// numbers reflect an operator on a real network, not this machine's loopback.
const BUDGETS = {
    performanceScore: { min: 50 }, // Lighthouse 0-100; <50 = "poor"
    largestContentfulPaint: { max: 4000 }, // ms; Google "poor" threshold
    // ms. Google's "poor" threshold is 600, and the two heaviest routes sit just
    // under it on a developer machine (measured 2026-09-03: stations 570,
    // review-adjust 458) but just over it on a two-core hosted agent, which
    // breached the gate on CI builds 3403 and 3408 even after the median-of-3
    // retry. A budget that depends on which machine runs it gates nothing and
    // blocks every release, so the ceiling is the agent's measured spread plus
    // headroom. A genuine regression still has to clear 800ms to pass.
    totalBlockingTime: { max: 800 },
    cumulativeLayoutShift: { max: 0.25 }, // unitless; Google "poor" threshold
    transferSize: { max: 3_000_000 }, // bytes; total page weight incl. JS/CSS/img
    // Quality categories: 100, not "better than last time". Both were at or one
    // defect away from 100 when they were first measured (2026-07-27), so the
    // only honest bar is the one the app already meets — anything lower just
    // licenses the next regression.
    accessibilityScore: { min: 100 },
    agenticBrowsingScore: { min: 100 },
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

async function ensurePreviewPortAvailable() {
    await new Promise((resolveProbe, rejectProbe) => {
        const probe = net.createServer()
        probe.once('error', (error) => {
            if (error.code === 'EADDRINUSE') {
                rejectProbe(
                    new Error(
                        `preview port ${PORT} is already in use; free port ${PORT} before running the performance audit`
                    )
                )
                return
            }
            rejectProbe(error)
        })
        probe.listen(PORT, '127.0.0.1', () => {
            probe.close((error) => {
                if (error) rejectProbe(error)
                else resolveProbe()
            })
        })
    })
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

/**
 * Recompute a category's score from its own auditRefs, skipping WAIVED_AUDITS.
 *
 * Lighthouse's published `category.score` includes every audit, so a waived one
 * would permanently cap the category below its budget. Reweighting from the
 * refs keeps the arithmetic Lighthouse's own — same weights, same per-audit
 * scores — minus exactly the audits named (and argued for) in WAIVED_AUDITS.
 *
 * Weight-0 refs are informational in Lighthouse's own scoring and contribute
 * nothing here either. `agentic-browsing` reports a pass ratio rather than a
 * weighted score, which is the same formula with every weight equal to 1.
 *
 * @returns {{score: number|null, failed: string[], waived: string[]}} score is
 *   0-100, or null when the category is absent from the run.
 */
function scoreCategory(lhr, categoryId) {
    const category = lhr.categories[categoryId]
    if (!category) return { score: null, failed: [], waived: [] }

    let earned = 0
    let total = 0
    const failed = []
    const waived = []

    for (const ref of category.auditRefs) {
        const audit = lhr.audits[ref.id]
        // score === null means "not applicable to this page" — Lighthouse drops
        // those from its own denominator, and so must we.
        if (!audit || audit.score === null) continue
        if (ref.id in WAIVED_AUDITS) {
            if (audit.score < 1) waived.push(ref.id)
            continue
        }
        if (audit.score < 1) failed.push(ref.id)
        const weight = ref.weight ?? 0
        if (weight <= 0) continue
        earned += audit.score * weight
        total += weight
    }

    return {
        score: total > 0 ? Math.round((earned / total) * 100) : 100,
        failed,
        waived,
    }
}

function metricsFrom(lhr) {
    const audits = lhr.audits
    const a11y = scoreCategory(lhr, 'accessibility')
    const agentic = scoreCategory(lhr, 'agentic-browsing')
    return {
        performanceScore: Math.round(
            (lhr.categories.performance?.score ?? 0) * 100
        ),
        accessibilityScore: a11y.score,
        agenticBrowsingScore: agentic.score,
        firstContentfulPaint: audits['first-contentful-paint']?.numericValue,
        largestContentfulPaint:
            audits['largest-contentful-paint']?.numericValue,
        totalBlockingTime: audits['total-blocking-time']?.numericValue,
        cumulativeLayoutShift: audits['cumulative-layout-shift']?.numericValue,
        speedIndex: audits['speed-index']?.numericValue,
        interactive: audits['interactive']?.numericValue,
        transferSize: audits['total-byte-weight']?.numericValue ?? 0,
        requestCount: audits['network-requests']?.details?.items?.length,
    }
}

/** Failing/waived audit ids per quality category, for the report. */
function qualityDetail(lhr) {
    return {
        accessibility: scoreCategory(lhr, 'accessibility'),
        'agentic-browsing': scoreCategory(lhr, 'agentic-browsing'),
    }
}

function checkBudgets(metrics) {
    const verdicts = []
    for (const [key, budget] of Object.entries(BUDGETS)) {
        const value = metrics[key]
        if (value == null) continue
        const breached =
            (budget.min != null && value < budget.min) ||
            (budget.max != null && value > budget.max)
        verdicts.push({ key, value, budget, breached })
    }
    return verdicts
}

async function run() {
    mkdirSync(OUT, { recursive: true })

    let previewProc = null
    if (process.env.PERF_REUSE_SERVER !== '1') {
        await ensurePreviewPortAvailable()
        console.log(`\n── starting preview server on :${PORT} ──`)
        previewProc = startPreviewServer()
    }
    await waitForServer(BASE)

    console.log(`── Arn Performance Audit (${ROUTES.length} routes) ──`)
    console.log(`   base: ${BASE}\n`)

    const chrome = await chromeLauncher.launch({
        chromeFlags: ['--headless=new', '--no-sandbox', '--disable-gpu'],
    })

    const results = []
    let anyBreach = false

    try {
        for (const route of ROUTES) {
            const url = BASE + route.path

            const auditOnce = async () => {
                const { lhr } = await lighthouse(url, {
                    port: chrome.port,
                    onlyCategories: CATEGORIES,
                    output: 'json',
                    logLevel: 'error',
                    formFactor: 'desktop',
                    screenEmulation: {
                        mobile: false,
                        width: 1440,
                        height: 900,
                        deviceScaleFactor: 1,
                        disabled: false,
                    },
                    // Desktop throttling profile — operators use the console on
                    // workstation hardware/networks, not mobile; matches the
                    // deployment's actual usage pattern (contrast: a public
                    // marketing site would want the mobile default).
                    throttling: {
                        rttMs: 40,
                        throughputKbps: 10 * 1024,
                        cpuSlowdownMultiplier: 2,
                    },
                })
                return lhr
            }

            // Retry-on-breach, median-of-3: cpuSlowdownMultiplier MULTIPLIES the
            // live host's speed, so on a loaded shared CI agent a single sample
            // can swing wildly (observed: home TBT 948ms then 659ms on identical
            // code, budget 600ms). A single clean sample stays cheap; a breach
            // triggers two more samples and the per-metric MEDIAN decides — noise
            // converges under the budget, a genuine regression stays over it.
            let lhr = await auditOnce()
            let metrics = metricsFrom(lhr)
            let verdicts = checkBudgets(metrics)
            let breached = verdicts.filter((v) => v.breached)
            let retried = false

            if (breached.length) {
                retried = true
                console.log(
                    `  … ${route.id} breached on first sample (${breached.map((b) => b.key).join(', ')}) — taking median of 3`
                )
                const samples = [metrics]
                for (let i = 0; i < 2; i++) {
                    const extraLhr = await auditOnce()
                    samples.push(metricsFrom(extraLhr))
                    lhr = extraLhr // keep the last full LHR for the artifact
                }
                const median = (xs) => {
                    const s = xs.filter((x) => x != null).sort((a, b) => a - b)
                    return s.length ? s[Math.floor(s.length / 2)] : null
                }
                metrics = Object.fromEntries(
                    Object.keys(samples[0]).map((k) => [
                        k,
                        median(samples.map((m) => m[k])),
                    ])
                )
                verdicts = checkBudgets(metrics)
                breached = verdicts.filter((v) => v.breached)
            }

            if (breached.length) anyBreach = true

            writeFileSync(
                resolve(OUT, `${route.id}.json`),
                JSON.stringify(lhr, null, 2)
            )
            results.push({
                route,
                metrics,
                verdicts,
                quality: qualityDetail(lhr),
            })

            const flag = breached.length
                ? `BUDGET BREACH: ${breached.map((b) => b.key).join(', ')}`
                : retried
                  ? 'ok (median of 3)'
                  : 'ok'
            console.log(
                `  ${breached.length ? '✗' : '✓'} ${route.id} — perf ${metrics.performanceScore} · a11y ${metrics.accessibilityScore} · agentic ${metrics.agenticBrowsingScore} · LCP ${Math.round(metrics.largestContentfulPaint ?? 0)}ms · TBT ${Math.round(metrics.totalBlockingTime ?? 0)}ms · ${(metrics.transferSize / 1024).toFixed(0)}KB — ${flag}`
            )
        }
    } finally {
        await chrome.kill()
        if (previewProc) killPreviewServer(previewProc)
    }

    writeReport(results)
    appendHistory(results)

    console.log(`\n   report: web/perf/report.md`)
    console.log(`   raw:    web/perf/<route>.json (full Lighthouse LHR)\n`)

    if (anyBreach && !NO_FAIL) {
        console.error('Performance budget breached — see web/perf/report.md')
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
    const rows = results.map(({ route, metrics, verdicts }) => {
        const breach = verdicts.some((v) => v.breached) ? ' ⚠️' : ''
        return `| ${route.id}${breach} | ${route.desc} | ${metrics.performanceScore} | ${metrics.accessibilityScore ?? '-'} | ${metrics.agenticBrowsingScore ?? '-'} | ${Math.round(metrics.firstContentfulPaint ?? 0)} | ${Math.round(metrics.largestContentfulPaint ?? 0)} | ${Math.round(metrics.totalBlockingTime ?? 0)} | ${(metrics.cumulativeLayoutShift ?? 0).toFixed(3)} | ${(metrics.transferSize / 1024).toFixed(0)}KB | ${metrics.requestCount ?? '-'} |`
    })
    const breaches = results.flatMap(({ route, verdicts }) =>
        verdicts
            .filter((v) => v.breached)
            .map(
                (v) =>
                    `- **${route.id}** \`${v.key}\`: ${Math.round(v.value)} exceeds budget ${JSON.stringify(v.budget)}`
            )
    )

    // Every failing audit by id, so a score drop is actionable without opening
    // the 5MB LHR. Waived audits are listed separately, never silently dropped.
    const qualityLines = results.flatMap(({ route, quality }) =>
        Object.entries(quality ?? {}).flatMap(([category, detail]) =>
            detail.failed.length
                ? [
                      `- **${route.id}** · ${category}: ${detail.failed.map((id) => `\`${id}\``).join(', ')}`,
                  ]
                : []
        )
    )
    const waivedSeen = new Set(
        results.flatMap(({ quality }) =>
            Object.values(quality ?? {}).flatMap((d) => d.waived)
        )
    )

    const md = `# Frontend Quality Audit Report

Generated ${new Date().toISOString()}. Desktop Lighthouse run (1440x900, 40ms RTT / 10Mbps / 2x CPU slowdown) against a production build (\`vite build\`) in mock mode — measures the app's own render/bundle cost, isolated from network/backend variance.

Scores are 0-100. **A11y** is Lighthouse's accessibility category (axe, scored). **Agentic** is the \`agentic-browsing\` category added in Lighthouse 13.3 — accessibility-tree integrity, WebMCP tool declarations, llms.txt discoverability and layout stability, i.e. whether a *machine* can drive the page. Both are recomputed from their auditRefs excluding the waived audits listed below.

| Route | Description | Perf | A11y | Agentic | FCP (ms) | LCP (ms) | TBT (ms) | CLS | Weight | Requests |
|---|---|---|---|---|---|---|---|---|---|---|
${rows.join('\n')}

${
    breaches.length
        ? `## Budget breaches\n\n${breaches.join('\n')}\n`
        : '## No budget breaches\n'
}
${
    qualityLines.length
        ? `## Failing quality audits\n\n${qualityLines.join('\n')}\n`
        : '## No failing accessibility or agentic-browsing audits\n'
}
## Budgets

${Object.entries(BUDGETS)
    .map(([k, b]) => `- \`${k}\`: ${JSON.stringify(b)}`)
    .join('\n')}

## Waived audits

${Object.entries(WAIVED_AUDITS)
    .map(
        ([id, why]) =>
            `- \`${id}\`${waivedSeen.has(id) ? '' : ' *(currently passing anyway)*'} — ${why}`
    )
    .join('\n')}

## Notes

- This measures **app-level** performance only (bundle weight, hydration cost, render-blocking work). It does NOT measure real network/CDN latency to a deployed environment — see \`docs/operations/performance-testing.md\` for the live-environment waterfall procedure.
- Accessibility here is scored, route-level and light-theme only. The exhaustive per-state, per-viewport, per-theme axe crawl is \`npm run audit\` (\`scripts/ux-audit.mjs\`); the two are complementary, not redundant.
- Full Lighthouse result (opportunities, diagnostics, trace) per route: \`web/perf/<route-id>.json\`.
- Trend history: \`web/perf/history.jsonl\` (one line per run — diff against a prior commit's run to catch slow regressions a single run's budget wouldn't trip).
`
    writeFileSync(resolve(OUT, 'report.md'), md)
}

function appendHistory(results) {
    const line = JSON.stringify({
        at: new Date().toISOString(),
        routes: Object.fromEntries(
            results.map(({ route, metrics }) => [route.id, metrics])
        ),
    })
    const path = resolve(OUT, 'history.jsonl')
    if (!existsSync(path)) writeFileSync(path, '')
    appendFileSync(path, line + '\n')
}

run().catch((e) => {
    console.error(e)
    process.exit(1)
})
