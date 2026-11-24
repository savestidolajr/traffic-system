---
title: "Performance Testing"
description: "How to test Arn's UI performance: local Lighthouse regression harness (cold-load + runtime/JS-jank probe) and live-deployed-environment waterfall diagnosis"
tags:
  - "operations"
  - "performance"
  - "testing"
  - "lighthouse"
  - "ci"
aliases:
  - "performance testing"
  - "perf audit"
  - "lighthouse"
  - "web vitals"
  - "perf budgets"
purpose: "runbook"
status: "current"
last_verified: 2026-09-01
review_after: 2026-12-01
volatility: "medium"
source_of_truth: "web/scripts/perf-audit.mjs, web/scripts/perf-runtime.mjs, web/perf/, azure-pipelines.yml, and live console profiling"
verified_by:
  - "npm run perf and npm run perf:runtime local smoke procedures"
  - "Live console waterfall diagnosis procedure reviewed 2026-09-01"
related_code:
  - "web/scripts/perf-audit.mjs"
  - "web/scripts/perf-runtime.mjs"
  - "web/scripts/ux-audit.mjs"
  - "web/src/hooks/use-second-tick.ts"
  - "web/src/components/ui/aceternity/aurora-hero.tsx"
  - "web/src/components/ui/aceternity/aurora-wash.tsx"
  - "web/vite.config.ts"
  - "azure-pipelines.yml"
  - "deployment/modules/static-web-app.bicep"
  - "src/ArnBulletinGenerator.Functions/Triggers/Http/BroadcastHttpTrigger.cs"
  - "src/ArnBulletinGenerator.Functions/Triggers/Queues/BroadcastQueueTrigger.cs"
  - "web/src/features/jobs/api/job-hooks.ts"
  - "web/src/pages/OnAirPage.tsx"
common_queries:
  - "How do I performance test the UI?"
  - "Why is the deployed dev app slow?"
  - "Why does the JS/animations feel laggy?"
  - "What are the Lighthouse performance budgets?"
  - "How do I profile a CSS animation for jank?"
see_also:
  - "docs/operations/e2e-testing.md"
---

# Performance Testing

Arn's UI performance has two distinct failure modes that need two distinct tools:

1. **App-level regression** (bundle bloat, N+1 client-side fan-out, unnecessary re-renders, over-eager polling) — a code problem, reproducible offline, caught by a **local Lighthouse harness** in CI.
2. **Infrastructure/network** (wrong CDN region, cold serverless starts, backend latency) — an environment problem, only visible against a **live deployed URL**, diagnosed with a **CDP browser waterfall**.

Conflating the two wastes time: a local Lighthouse run cannot see a CDN misroute, and a live browser trace cannot isolate "is this our bundle or the network" without also running the local harness for comparison.

## Measured live baselines

- **2026-08-28, DEV:** authenticated CDP profiling measured a warm report-route
  change at roughly 125 ms, cold deep-link loads at 2.83–4.00 s, and
  occasional cold market-delta responses near 1 s; audio requests waited for
  Play.
- **2026-08-31, DEV:** App Insights measured 198 list requests with 0 errors
  (p50 220 ms, p95 282 ms, p99 904 ms, maximum 909 ms); sampled manual
  trigger acknowledgements were 201–1,107 ms, mostly 200–630 ms, and
  requests without coverage spent 303–408 ms in HERE geocoding.

## Live waterfall diagnosis

Use an authenticated CDP browser session against the live console domain. For
each route under investigation, capture both a cold deep link and a warm
in-app navigation, then record request count, critical-path ordering, endpoint
latency, cache state, and the time to first visible report. Keep these clocks
separate: HTTP acknowledgement, queue pickup, and jobs-list refresh are not
generation latency.

Treat the trigger response as acknowledgement only; generation, traffic
retrieval, workflow execution, and audio synthesis remain asynchronous. Check
whether the selected report is paying for unrelated queue, history, schedule,
voice, or audio requests, and compare the primary report read with secondary
reads. When a market-delta read is slow, compare cold and warm cache samples
and investigate the storage access path rather than adding a client-side wait.
Verify that active polling accelerates only while a pipeline row is generating
and that quiet pages retain the idle interval. If queue navigation is involved,
check that selection changes preserve the queue viewport and that history
remains reachable at the maximum offset.

Repeat the trace after each change. The local Lighthouse/runtime harness below
diagnoses bundle, render, and main-thread regressions; it cannot establish the
network or backend behaviour of a deployed console.

## 1. Local regression harness (`npm run perf`)

`web/scripts/perf-audit.mjs` runs Lighthouse (desktop profile) against a **production build** (`vite build`, not `vite dev`) served in mock mode, across 7 representative routes (home, on-air board, a job-review tab, stations/voices/guidance/history hubs). Mock mode means every route renders real content with no backend, no auth wall, and byte-stable data — the numbers measure the app's own render/bundle cost, isolated from network and backend variance.

It scores **three** categories, answering three different questions:

| Category | Question | Notes |
|---|---|---|
| `performance` | Is it fast? | Drives the Core Web Vitals budgets below. |
| `accessibility` | Can a human with assistive tech use it? | Lighthouse runs axe and **scores** it. Route-level, light theme only — the exhaustive per-state/per-viewport/per-theme axe crawl is `npm run audit` (`ux-audit.mjs`). Complementary, not redundant. |
| `agentic-browsing` | Can a **machine** use it? | Added in Lighthouse 13.3: accessibility-tree integrity, WebMCP tool declarations, `llms.txt` discoverability, layout stability (a late shift makes an agent misclick). |

Both quality scores are **recomputed from their `auditRefs`** rather than taken from Lighthouse's published `category.score`, so an audit listed in `WAIVED_AUDITS` is dropped from the numerator *and* the denominator — a waiver never lowers the bar for anything else. One audit is waived today: `llms-txt`, because this is an Entra-gated internal operator console with no anonymous agent to serve a site summary to. Every waiver carries its reason in the source and is reprinted in `report.md`.

**Critical build gotcha:** the mock build MUST set `VITE_USE_DEV_AUTH=true` alongside `VITE_USE_MOCKS=true` (`build:mock` in `package.json` does this). `src/lib/auth.ts`'s dev-auth bypass checks `import.meta.env.DEV`, which is `true` under `vite dev`/`dev:mock` but **`false` under any `vite build`** — a mock build without the explicit dev-auth flag hits the real sign-in wall (`fetchAuthData` calls `/.auth/me`, which 404s against `vite preview`, so `isAuthenticated` is `false`) and every route silently renders the tiny "Sign in required" placeholder instead of real content. If all routes report suspiciously identical metrics, confirm this flag before diagnosing the app.

```bash
cd web
npm run perf              # build:mock + audit + budget-gate (exit 1 on breach)
npm run perf:nobuild      # reuse the existing dist-mock/ build (fast iteration)
npm run perf -- --no-fail # report only, never fail the process
```

Output:

- `web/perf/report.md` — scores/metrics table + budget verdicts (human-readable).
- `web/perf/<route-id>.json` — full Lighthouse result per route (opportunities, diagnostics, trace) for deep-diving a regression.
- `web/perf/history.jsonl` — one line appended per run, so a slow multi-week regression that never trips a single-run budget is still diffable.

Budgets (`BUDGETS` in `perf-audit.mjs`) are deliberately loose — Google's documented "poor" thresholds, not aspirational targets — so the gate only fires on a genuine regression, not day-to-day noise:

| Metric | Budget | Rationale |
|---|---|---|
| Performance score | ≥ 50 | Below this is Lighthouse's own "poor" band |
| Largest Contentful Paint | ≤ 4000ms | Google Core Web Vitals "poor" threshold |
| Total Blocking Time | ≤ 600ms | Google Core Web Vitals "poor" threshold |
| Cumulative Layout Shift | ≤ 0.25 | Google Core Web Vitals "poor" threshold |
| Page weight | ≤ 3MB | Total transfer size incl. JS/CSS/images |
| Accessibility score | = 100 | Keep the full score to prevent regressions |
| Agentic-browsing score | = 100 | Keep the full score to prevent regressions |

`report.md` lists every **failing quality audit by id per route**, so a score drop is actionable without opening the multi-MB LHR.

Wired into CI (`azure-pipelines.yml`, `Package` → `Web` job) after the Playwright E2E step and before `npm run build`; the report publishes as the `perf-report` artifact even when the budget gate fails (`condition: always()`).

**When a route breaches a budget:** `cpuSlowdownMultiplier` multiplies the
LIVE host's speed, so on a loaded shared CI agent a single sample can breach on
identical code. Keep the clean first sample single-run (cheap); re-measure a
breaching route twice and let the per-metric **median of 3** decide. Noise
converges under the budget; a genuine regression stays over it on all samples.
Report rows show "ok (median of 3)" when the retry path fired.

### Retained lessons: route-level regressions

- **Measured baseline (2026-06-23):** the `/guidance` route took about 10 s
  because it issued one request per market for two datasets, mounted all tabs
  eagerly, and polled the full job list every 10 s; consolidate fan-out,
  defer inactive surfaces, and scope polling when diagnosing a recurrence.
- **Measured baseline (2026-07-22):** lazy-loading on-demand Studio dialogs
  raised the `stations` Lighthouse score from 78 to 93, reduced TBT from
  316 ms to 52 ms, and reduced LCP from 1,823 ms to 1,509 ms; components
  rendered only after user action are code-split candidates.

### Retained lesson: React Compiler evaluation

- **Measured baseline (2026-07-22):** the React Compiler increased bundle
  weight by 10–25 KB, grew every chunk by 18–49%, and increased cold build
  time from about 570 ms to 8.6 s; LCP was flat to slightly worse, while a
  first runtime pairing was 15–19% faster but did not reproduce on the second
  pairing, so it was not adopted.

### Extending the harness

- New route worth tracking → add to `ROUTES` in `perf-audit.mjs` (reuse a job ID from `src/lib/api/mock/fixtures.ts` for job-detail routes, matching the pattern in `web/scripts/ux-audit.mjs`'s `TARGETS`).
- Tightening a budget → edit `BUDGETS`; a route-specific override is not yet implemented (all routes share one budget set) — add one only when a route's baseline is durably better and you want to lock in the gain.

## 2. Runtime performance probe (`npm run perf:runtime`)

`npm run perf` (above) answers "is the cold pageload fast" — it CANNOT see a page that has already loaded and sits idle, or is mid-interaction, burning main-thread JS/CSS on background work. That is a DIFFERENT failure mode with a DIFFERENT symptom: not slow-to-load, but **laggy/janky while in use**.

| Scenario | What it does | Isolates |
|---|---|---|
| `idle-onair` | Load `/jobs`, do nothing for 10s | Ambient background cost with **zero interaction** — any nonzero cost here is re-render/animation-driver overhead that runs whether or not the operator touches anything |
| `audio-review` | Open a job, click Play, let it run 8s | `AudioWaveformPlayer`'s per-frame `requestAnimationFrame` loop → karaoke-highlight re-render chain |
| `adjust-map` | Open a job, click the Adjust tab, idle 8s | Leaflet map mount + edit-surface idle cost |

Every scenario runs at **1x** (native machine speed, single sample, informational) and **4x** CPU throttle (Lighthouse's own mobile-equivalent slowdown — standing in for a weaker/older operator workstation). The **4x run is median-of-3**, not single-sample: devtools CPU throttling multiplies LIVE HOST PERFORMANCE rather than simulating a fixed absolute rate, so on a shared/busy machine a single 4x sample can legitimately swing 2-3x run to run (measured: 460-1570ms for the same scenario at load average 4.6-5.66 on a 16-core box). Median-of-3 absorbs that without needing to widen budgets into meaninglessness — even so, treat this probe as **informational in CI, not a hard gate** (see below).

```bash
cd web
npm run perf:runtime                  # build:mock + probe + budget-check (exit 1 on breach)
npm run perf:runtime:nobuild          # reuse the existing dist-mock/ build
npm run perf:runtime -- --no-fail     # report only, never fail the process
```

Output: `web/perf/runtime-<scenario>-<throttle>x.json` (full LHR of the median 4x sample), `web/perf/runtime-<scenario>-4x.html` (the Lighthouse flow report — open in a browser for the same trace breakdown DevTools' Performance panel would show), `web/perf/runtime-report.md` (summary table with the raw 3-sample spread + verdicts).

Wired into CI (`azure-pipelines.yml`, right after `npm run perf`) with `--no-fail` — it always runs and always publishes its artifact, but never blocks a merge. Treat a reported budget breach as a strong signal to open the flow-report HTML and look for the actual long tasks, not an automatic failure.

### Audio playback startup

`AudioWaveformPlayer` uses `<audio preload="none">` for the short job-review
recording. The browser requests the proxy-backed audio only after the operator
presses **Play**, keeping the initial review-page load from transferring audio
that may not be used. Server-side authorization and Blob-storage streaming
remain unchanged.

### Retained lesson: runtime diagnosis

- **Measured baseline (2026-07-01):** isolating countdown ticks moved
  idle-onair from 1,911 ms to 1,860 ms at 4x, while replacing non-composited
  Aurora background-position animation with a transform layer moved it from
  1,911 ms to 519 ms (3.7x); CSS animation, not React re-rendering, was the
  dominant cost in that sample.

For future investigations, `transform` and `opacity` are the two CSS
properties the compositor can animate independently of the main thread. Treat
keyframes touching `background-position`, `background-size`, `left`/`top`,
`width`/`height`, or similar properties as main-thread suspects, and confirm
with a `prefers-reduced-motion` A/B before attributing lag to React.

### Extending the runtime probe

- New scenario worth tracking → add to the `scenarios` array in `perf-runtime.mjs` (`setup` runs before the timespan opens, `duringWindow` is what gets measured).
- Debugging a specific number → open `web/perf/runtime-<scenario>-4x.html` (the median sample's full Lighthouse flow report) and check the `mainthread-work-breakdown`/`bootup-time` sections for named long tasks — that's the same breakdown DevTools' Performance panel shows.
- Suspect a CSS animation → cross-check with a `prefers-reduced-motion` A/B directly (`await page.emulateMediaFeatures([{ name: 'prefers-reduced-motion', value: 'reduce' }])` before `page.goto`) rather than guessing from the category buckets — the mainthread-work-breakdown's "Other"/"Rendering" buckets don't always cleanly separate JS from CSS at low absolute durations.

## 3. Live deployed-environment diagnosis

The local harness cannot see the network path to a deployed environment (CDN
region, TLS handshake cost, backend cold starts). For a deployed-console
report, profile the live operator domains over an authenticated browser
session (Chrome DevTools protocol against a signed-in tab):
`ain.arn.com.au`, `ain-test.arn.com.au`, and `ain-dev.arn.com.au`. Do not use
the `*.azurestaticapps.net` hostnames as operator addresses. Capture XHR/fetch
timing, compare cold and warm navigations, and defeat bfcache/react-query
cache with `about:blank` between routes when comparing them.

### Edge locality

- **Measured baseline (2026-07-01, DEV default SWA hostname):** DNS resolved
  to an Amsterdam App Service stamp; static assets from Melbourne took
  1.0–3.3 s per request and TLS handshake alone took about 0.5–0.9 s.

The current recommendation is to enable enterprise-grade edge with
`swaEnterpriseEdge=true` on the Static Web Apps. Until enabled, a Static Web
App may still serve from a non-local stamp; re-run DNS and geolocation checks
afterwards to confirm the resolved stamp is in-region.

Other options, in increasing cost/effort, are a manual Azure Front Door
deployment or moving the frontend off Static Web Apps to Australia East-hosted
static hosting behind Front Door/CDN. The enterprise-edge change is the
current recommendation and requires infrastructure sign-off.

## Do not

- Do **not** treat `npm run perf`'s green result as proof a deployed environment is fast — it only proves the bundle/render path is healthy, not the network path.
- Do **not** run Lighthouse against `vite dev` — unbundled ESM module-per-file serving makes the numbers meaningless.
- Do **not** spawn a fresh headless browser against an AAD-gated deployed URL expecting authenticated data — it will hit the login wall. Piggyback the user's session via CDP for anything needing operator/admin data.
- Do **not** build a mock bundle without `VITE_USE_DEV_AUTH=true` for a Lighthouse/puppeteer probe — `import.meta.env.DEV` is `false` in any `vite build` output, so without the explicit flag every route silently renders the "Sign in required" placeholder, not real content, and every metric becomes meaningless (all routes will look suspiciously identical).
- Do **not** treat a single 4x-CPU-throttled timespan sample as ground truth, or hard-fail CI on it — devtools throttling multiplies live host performance, so it inherits whatever load the host is under. Use median-of-3 (see §2) and keep the runtime probe informational in CI.
