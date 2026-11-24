import { defineConfig, devices } from '@playwright/test'
import { cpus, freemem } from 'node:os'
import { readFileSync } from 'node:fs'

// E2E_BASE_URL must drive BOTH the browser and the dev server it waits on. It used to set
// only `use.baseURL` while `webServer.url` stayed pinned to 5173, so overriding the port
// could never work: Playwright still blocked on 5173 and timed out. That matters locally,
// where a stale vite bound to [::1]:5173 (IPv6 only) leaves 127.0.0.1:5173 unreachable and
// the whole suite unrunnable — a good way for a UI regression to reach CI unseen.
const baseURL = process.env.E2E_BASE_URL ?? 'http://127.0.0.1:5173'
const basePort = new URL(baseURL).port || '5173'

/** Memory a worker needs in practice: its Chromium, the page under test, and trace/video buffers. */
const GB_PER_WORKER = 2.5
/** Ceiling regardless of headroom — `scripts/verify.sh` runs this lane beside four others. */
const MAX_LOCAL_WORKERS = 4

/**
 * Memory actually reclaimable for new processes. Node's `freemem()` reports Linux's `MemFree`,
 * which EXCLUDES the page cache and so reads near zero on a warm machine; `MemAvailable` is the
 * kernel's own estimate and the number that predicts whether another Chromium will survive.
 */
function availableGb(): number {
    try {
        const kb = /^MemAvailable:\s+(\d+) kB$/m.exec(
            readFileSync('/proc/meminfo', 'utf8')
        )?.[1]
        if (kb) return Number(kb) / 1024 ** 2
    } catch {
        // Not Linux, or /proc unreadable — fall through to the portable approximation.
    }
    return freemem() / 1024 ** 3
}

// Worker count is bounded by MEMORY locally, not by cores. Playwright defaults to cores/2 workers,
// each launching its own Chromium, so the more capable the machine the more browsers run at once —
// on a 16-core box that is 8, nested inside verify.sh's 5 parallel lanes. The suite died with
// "Page crashed"/"Target crashed", which is browser OOM rather than any assertion, while passing
// 39/39 single-worker. CI never hit it because hosted agents have 2 cores, so an unbounded fan-out
// bug looked machine-specific. Workers are memory-bound, not CPU-bound, so cores are the wrong
// dimension: measured on this box, 4 workers crashed and 2 passed 39/39 at 5.4 GB available —
// hence ~2.5 GB each.
//
// The discriminator is memory, NOT an environment flag. An earlier version of this exempted
// `process.env.CI` on the reasoning that hosted agents were fine — but CI=1 is also set by local
// agent harnesses, so the exemption silently restored the 8-worker default on a 16-core dev box
// and defeated the cap it was meant to protect. That is exactly why this failure looked
// machine-specific for so long. Bounding unconditionally cannot regress real CI either: the
// cores/2 term is still applied, so a 2-core hosted agent resolves to 1 worker just as before.
// PW_WORKERS overrides everything.
const workers = process.env.PW_WORKERS
    ? Number(process.env.PW_WORKERS)
    : Math.max(
          1,
          Math.min(
              Math.floor(availableGb() / GB_PER_WORKER),
              Math.max(1, Math.floor(cpus().length / 2)),
              MAX_LOCAL_WORKERS
          )
      )

export default defineConfig({
    testDir: './e2e',
    workers,
    // One retry on CI only. A browser suite on a shared two-core agent loses the
    // occasional run to agent scheduling rather than to the app, and a gate that
    // fails at random gets ignored. A test that fails twice still fails the build,
    // and locally there is no retry so a flake stays visible while it is written.
    retries: process.env.CI ? 1 : 0,
    timeout: 60_000,
    expect: { timeout: 10_000 },
    use: {
        baseURL,
        actionTimeout: 10_000,
        trace: 'retain-on-failure',
        screenshot: 'only-on-failure',
        video: 'retain-on-failure',
    },
    webServer: {
        command: `npm run dev:mock -- --host 127.0.0.1 --port ${basePort} --strictPort`,
        url: baseURL,
        reuseExistingServer: !process.env.CI,
        timeout: 120_000,
        env: {
            TZ: 'Australia/Sydney',
        },
    },
    projects: [
        {
            name: 'chromium-desktop',
            use: {
                ...devices['Desktop Chrome'],
                viewport: { width: 1440, height: 900 },
            },
        },
        {
            name: 'chromium-mobile',
            use: {
                ...devices['Pixel 5'],
                viewport: { width: 390, height: 844 },
                isMobile: true,
            },
        },
    ],
})
