#!/usr/bin/env node
// =============================================================================
// UX audit harness — deterministic crawl of every route × representative state
// in mock mode, capturing STRUCTURED evidence (accessibility tree + text +
// axe-core WCAG violations + console errors + overflow) alongside screenshots,
// with a pixelmatch visual diff against the previous run.
//
// The split is deliberate: code does the parts an LLM is bad at (exhaustive
// enumeration, deterministic capture, machine checks, diffing); the resulting
// corpus (audit/data/*.json + audit/lexicon.json) is what an LLM then reasons
// over for cross-surface consistency. See web/docs/DESIGN-SYSTEM.md.
//
// Usage:
//   1. start the app in mock mode:  npm run dev:mock      (serves :5173)
//   2. run the audit:               npm run audit
//   Override target with AUDIT_BASE=http://127.0.0.1:5174 npm run audit
//   Watch it run with HEADLESS=0.
// =============================================================================
import {
    existsSync,
    mkdirSync,
    rmSync,
    readFileSync,
    writeFileSync,
    renameSync,
    readdirSync,
} from 'fs'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'
import puppeteer from 'puppeteer'
import { AxePuppeteer } from '@axe-core/puppeteer'
import pixelmatch from 'pixelmatch'
import { PNG } from 'pngjs'

const BASE = process.env.AUDIT_BASE ?? 'http://127.0.0.1:5173'
const HEADLESS = process.env.HEADLESS !== '0'
const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', 'audit')
const D = {
    before: resolve(ROOT, 'before'),
    after: resolve(ROOT, 'after'),
    diff: resolve(ROOT, 'diff'),
    data: resolve(ROOT, 'data'),
}

// Pinned instant + timezone so countdowns, "x minutes ago", and slot clocks
// render identically every run — the screenshots stay byte-diffable.
const FIXED_TIME = Date.parse('2026-06-22T09:00:00+10:00')
const TIMEZONE = 'Australia/Sydney'

const VIEWPORTS = [
    { id: 'desktop', width: 1440, height: 900 },
    { id: 'laptop', width: 1100, height: 800 },
    { id: 'tablet', width: 820, height: 1180 },
    { id: 'mobile', width: 390, height: 844 },
]

// The app ships a full `.dark` token set (src/index.css) behind next-themes
// (`attribute="class" defaultTheme="dark" enableSystem`), so before this axis
// existed the harness captured whichever theme the headless browser's
// `prefers-color-scheme` happened to resolve to — an UNCONTROLLED variable in a
// harness whose entire value is byte-stable determinism, and half the token
// system never seen by axe or a reviewer.
//
// Light is pinned as the baseline so every pre-existing artifact name, stored
// review finding and resolved box stays valid; dark is a new sibling variant.
const THEMES = ['light', 'dark']

/**
 * Capture variants = viewports × themes. The variant id IS the `vp` key used
 * everywhere downstream (`<page>.<vp>.png`, `<page>.<vp>.marks.json`,
 * `ux-review.mjs --vp`), so dark slots into every existing path shape without a
 * parser change: `desktop` / `desktop-dark` / `mobile` / `mobile-dark` / …
 */
const VARIANTS = VIEWPORTS.flatMap((vp) =>
    THEMES.map((theme) => ({
        id: theme === 'light' ? vp.id : `${vp.id}-${theme}`,
        vp,
        theme,
    }))
)

// Routes × representative states. Job ids map to one fixture per lifecycle
// status (see src/lib/api/mock/fixtures.ts), so every job *state* is visited,
// not just the /jobs/:id route once.
//
// COVERAGE CONTRACT: every route in `src/lib/routes.ts` that RENDERS a page must
// appear here. `/schedules` and `/trigger` are the only exemptions — both are
// bare `<Navigate>` redirects to `/stations` in App.tsx and render no UI of
// their own. Four routes (`/delivery`, `/pronunciation`, `/activity`,
// `/config-transfer`) went unaudited until 2026-07-27 and were hiding real
// defects, so treat a new route without a target here as a gap, not a choice.
const TARGETS = [
    { id: '00-home', url: '/', desc: 'Landing dashboard' },
    { id: '01-onair', url: '/jobs', desc: 'On-air board (queue + beam)' },
    {
        id: '02-review-waiting',
        url: '/jobs/arn-job-00007',
        desc: 'Review — WAITING_APPROVAL',
    },
    {
        id: '03-review-adjust',
        // A REAL fixture. This target pointed at `arn-job-00008` until
        // 2026-07-27 — an id that does not exist in the mock store — so for its
        // entire life it captured the job-not-found error state while reporting
        // as covered, and the Adjust tab (map + incident table, the heaviest
        // surface in the app) was never audited at all. Coverage that looks
        // like coverage is worse than a known gap.
        // `f034af33…` is the Melbourne dev capture: 33 real incidents, the
        // densest curation case in the fixtures.
        url: '/jobs/f034af33f0ba4f6288f52e22bce8bb27',
        tab: 'Adjust',
        desc: 'Adjust — incident map + curation (33 incidents)',
    },
    {
        id: '04-review-audit',
        url: '/jobs/arn-job-00007',
        tab: 'Config & audit',
        desc: 'Config & audit tab',
    },
    {
        id: '05-generating',
        url: '/jobs/arn-job-00005',
        desc: 'In-flight — SYNTHESIZING_AUDIO',
    },
    {
        id: '06-approved',
        url: '/jobs/arn-job-00009',
        desc: 'Terminal — APPROVED',
    },
    {
        id: '07-rejected',
        url: '/jobs/arn-job-00010',
        desc: 'Terminal — REJECTED',
    },
    { id: '08-failed', url: '/jobs/arn-job-00013', desc: 'Terminal — FAILED' },
    {
        id: '09-fallback',
        url: '/jobs/arn-job-00012',
        desc: 'Terminal — FALLBACK_USED',
    },
    { id: '10-stations', url: '/stations', desc: 'Station profiles' },
    { id: '11-voices', url: '/voices', desc: 'Voice library' },
    { id: '12-operators', url: '/operators', desc: 'Operator assignments' },
    { id: '13-guidance', url: '/guidance', desc: 'Bulletin rules' },
    { id: '14-history', url: '/history', desc: 'History log' },
    { id: '15-not-found', url: '/nonexistent', desc: '404 state' },
    {
        id: '16-station-studio',
        url: '/stations',
        click: 'New station',
        desc: 'Station create studio',
    },
    {
        id: '17-voice-studio',
        url: '/voices',
        click: 'New variant',
        desc: 'Voice create studio',
    },
    {
        // Was `click: 'Assign operator'` for an assign dialog that no longer
        // exists: as of 2026-09-03 `/operators` offers only Cards, Table and a
        // per-operator History, and market assignment is edited inline on the
        // card rather than in a modal. Retargeted to the Table view, which is a
        // real state reached by a real affordance, so this stops silently
        // re-capturing the Cards view under a dialog's name.
        id: '18-operators-table',
        url: '/operators',
        click: 'Table',
        desc: 'Operator assignments — table view',
    },
    {
        id: '19-revise-dialog',
        url: '/jobs/arn-job-00007',
        click: 'Revise',
        desc: 'Revise dialog',
    },
    { id: '20-delivery', url: '/delivery', desc: 'Delivery-style gallery' },
    {
        id: '21-delivery-studio',
        url: '/delivery',
        click: 'New style',
        desc: 'Delivery-style create studio',
    },
    {
        id: '22-pronunciation',
        url: '/pronunciation',
        desc: 'Pronunciation dictionary gallery',
    },
    {
        id: '23-pronunciation-studio',
        url: '/pronunciation',
        click: 'New dictionary',
        desc: 'Pronunciation create studio',
    },
    {
        id: '24-activity',
        url: '/activity',
        desc: 'Activity / change audit log',
    },
    {
        id: '25-config-transfer',
        url: '/config-transfer',
        desc: 'Config bundle export/import (admin)',
    },
    { id: '26-bookends', url: '/bookends', desc: 'Bookend pair gallery' },
    {
        id: '27-bookend-studio',
        url: '/bookends',
        click: 'New bookend',
        desc: 'Bookend create studio',
    },
    {
        id: '28-desk',
        url: '/desk',
        desc: 'Desk — operator vetting of road events',
    },
]

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

const REDUCE_MOTION =
    '*,*::before,*::after{animation-duration:0s!important;animation-delay:0s!important;transition-duration:0s!important;transition-delay:0s!important;scroll-behavior:auto!important;caret-color:transparent!important}'

// Polls for the affordance instead of probing once. A single probe races the
// route's own data fetch: on 2026-09-03 `16-station-studio` and
// `19-revise-dialog` both reported `no "<label>"` and silently captured the
// BASE PAGE INSTEAD OF THE DIALOG, and both of those screenshots are published
// to ARN. The buttons existed and clicked fine by hand — the probe simply ran
// before the detail view mounted its action bar. A missing affordance is still
// reported, just only after the element has had time to appear.
async function clickByText(
    page,
    text,
    sel = 'button,a,[role=tab],[role=button],[role=menuitem]',
    { timeout = 6000, interval = 250 } = {}
) {
    const deadline = Date.now() + timeout
    for (;;) {
        const handle = await page.evaluateHandle(
            (sel, text) => {
                const els = [...document.querySelectorAll(sel)]
                return (
                    els.find(
                        (e) =>
                            (e.textContent || '')
                                .trim()
                                .toLowerCase()
                                .includes(text.toLowerCase()) &&
                            // an element with no box cannot be clicked by a user
                            (e.offsetWidth || e.offsetHeight)
                    ) ?? null
                )
            },
            sel,
            text
        )
        const el = handle.asElement()
        if (el) {
            try {
                await el.click({ delay: 20 })
            } catch {
                await el.evaluate((n) => n.click())
            }
            return true
        }
        if (Date.now() >= deadline) return false
        await sleep(interval)
    }
}

function flattenA11y(node, out = []) {
    if (!node) return out
    if (node.role && node.role !== 'generic' && (node.name || node.value)) {
        out.push({
            role: node.role,
            name: (node.name || '').slice(0, 80),
            ...(node.value ? { value: String(node.value).slice(0, 40) } : {}),
        })
    }
    for (const c of node.children || []) flattenA11y(c, out)
    return out
}

async function settle(page, url) {
    try {
        await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 20000 })
    } catch (e) {
        return { navError: String(e.message || e) }
    }
    // Wait for the SPA to paint real content rather than a blank shell.
    await page
        .waitForFunction(
            () => {
                const r = document.querySelector('main, #root, [role=main]')
                return !!r && (r.textContent || '').trim().length > 40
            },
            { timeout: 8000 }
        )
        .catch(() => {})
    await page.addStyleTag({ content: REDUCE_MOTION }).catch(() => {})
    await sleep(450)
    return {}
}

/**
 * Switch the live app between themes without renavigating.
 *
 * next-themes owns the `class` on `<html>` and syncs across tabs by listening
 * for `storage`, so writing the key and firing the event drives the real
 * provider — no class is forced behind React's back, and the result is exactly
 * what an operator flipping the toggle would see.
 *
 * Resolves once `<html>` actually carries the requested class, so a screenshot
 * can never race the swap.
 */
async function setTheme(page, theme) {
    await page.emulateMediaFeatures([
        { name: 'prefers-color-scheme', value: theme },
    ])
    await page.evaluate((t) => {
        localStorage.setItem('theme', t)
        window.dispatchEvent(
            new StorageEvent('storage', { key: 'theme', newValue: t })
        )
    }, theme)
    await page
        .waitForFunction(
            (t) => document.documentElement.classList.contains(t),
            { timeout: 3000 },
            theme
        )
        .catch(() => {})
    await sleep(120)
}

async function overflow(page) {
    return page.evaluate(() => {
        const de = document.documentElement
        const vw = de.clientWidth
        const W = de.scrollWidth || 1
        const H = de.scrollHeight || 1
        const clamp = (n) => Math.max(0, Math.min(100, +n.toFixed(2)))
        const offenders = []
        for (const el of document.querySelectorAll('body *')) {
            const r = el.getBoundingClientRect()
            if (r.width > vw + 2 && el.scrollWidth > vw + 2) {
                const cn = typeof el.className === 'string' ? el.className : ''
                const cls = cn
                    ? '.' + cn.split(/\s+/).slice(0, 2).join('.')
                    : ''
                offenders.push({
                    sel: el.tagName.toLowerCase() + cls,
                    // 4 of 5 overflow flags are oversized glow/gradient layers;
                    // tag them so the review page can hide them by default.
                    decorative: /pointer-events-none|-inset-\[|inset-\[-/.test(
                        cn
                    )
                        ? 1
                        : 0,
                    box: {
                        x: clamp(((r.left + window.scrollX) / W) * 100),
                        y: clamp(((r.top + window.scrollY) / H) * 100),
                        w: clamp((r.width / W) * 100),
                        h: clamp((r.height / H) * 100),
                    },
                })
                if (offenders.length >= 6) break
            }
        }
        return offenders
    })
}

// Resolve a CSS selector to its bounding box as PERCENTAGES of the full
// document, so the highlight overlays the fullPage screenshot regardless of
// devicePixelRatio (both rect and scrollWidth are in CSS px → ratio is scale-free).
async function boxFor(page, selector) {
    return page.evaluate((sel) => {
        let el
        try {
            el = document.querySelector(sel)
        } catch {
            return null
        }
        if (!el) return null
        const de = document.documentElement
        const W = de.scrollWidth || 1
        const H = de.scrollHeight || 1
        const r = el.getBoundingClientRect()
        const clamp = (n) => Math.max(0, Math.min(100, +n.toFixed(2)))
        return {
            x: clamp(((r.left + window.scrollX) / W) * 100),
            y: clamp(((r.top + window.scrollY) / H) * 100),
            w: clamp((r.width / W) * 100),
            h: clamp((r.height / H) * 100),
        }
    }, selector)
}

// Set-of-Mark grounding: tag every significant element with a numbered badge,
// returning the legend (mark → exact %-box + role/name). A reviewer reads a mark
// number off the element instead of guessing coordinates; ux-review.mjs resolves
// the box from the DOM rect. (2026 SOTA for VLM localization — VLMs are poor at
// freehand coords but reliable at reading a number drawn on the target.)
async function markPage(page) {
    return page.evaluate(() => {
        const de = document.documentElement
        const W = de.scrollWidth || 1
        const H = de.scrollHeight || 1
        const clamp = (n) => Math.max(0, Math.min(100, +n.toFixed(2)))
        // Full-page screenshots fool VLM reviewers into two recurring false
        // overflow/overlap findings; flag the cause deterministically so the
        // legend can discount them. `fixed`: el (or an ancestor) is
        // position:fixed/sticky — in a stitched full-page shot it paints at its
        // viewport-anchored spot, so any "overlap" with nearby content is an
        // artifact, not a layout bug. `clipped`: el sits past a scrollable
        // ancestor's box — "clipped at the edge" is a scroll affordance, not an
        // overflow defect (the content scrolls into view).
        const isFixed = (node) => {
            for (let p = node; p && p !== de; p = p.parentElement) {
                const pos = getComputedStyle(p).position
                if (pos === 'fixed' || pos === 'sticky') return true
            }
            return false
        }
        const isClipped = (node) => {
            const nr = node.getBoundingClientRect()
            for (
                let p = node.parentElement;
                p && p !== de;
                p = p.parentElement
            ) {
                const ps = getComputedStyle(p)
                const sx =
                    (ps.overflowX === 'auto' || ps.overflowX === 'scroll') &&
                    p.scrollWidth > p.clientWidth + 1
                const sy =
                    (ps.overflowY === 'auto' || ps.overflowY === 'scroll') &&
                    p.scrollHeight > p.clientHeight + 1
                if (!sx && !sy) continue
                const pr = p.getBoundingClientRect()
                if (sx && (nr.right > pr.right + 1 || nr.left < pr.left - 1))
                    return true
                if (sy && (nr.bottom > pr.bottom + 1 || nr.top < pr.top - 1))
                    return true
            }
            return false
        }
        const sels = [
            'button',
            'a[href]',
            'input',
            'select',
            'textarea',
            '[role=button]',
            '[role=tab]',
            '[role=combobox]',
            '[role=checkbox]',
            '[role=switch]',
            '[role=menuitem]',
            '[role=radio]',
            '[role=slider]',
            'h1',
            'h2',
            'h3',
            'h4',
            '[role=heading]',
            'li',
            'tr',
            '[role=listitem]',
            '[role=row]',
            '[data-testid]',
            'section',
            'article',
            '[class*="card"]',
            '[class*="Card"]',
        ].join(',')
        const seen = new Set()
        const out = []
        const interactiveSel =
            'button,a[href],input,select,textarea,[role=button],[role=tab],[role=combobox],[role=checkbox],[role=switch],[role=menuitem],[role=radio],[role=slider]'
        for (const el of document.querySelectorAll(sels)) {
            const r = el.getBoundingClientRect()
            if (r.width < 10 || r.height < 10) continue
            const wpct = (r.width / W) * 100
            const hpct = (r.height / H) * 100
            if (wpct > 95 && hpct > 92) continue
            const st = getComputedStyle(el)
            if (
                st.visibility === 'hidden' ||
                st.display === 'none' ||
                +st.opacity === 0
            )
                continue
            const interactive = el.matches(interactiveSel)
            const name = (
                el.getAttribute('aria-label') ||
                el.textContent ||
                el.getAttribute('placeholder') ||
                el.getAttribute('alt') ||
                ''
            )
                .replace(/\s+/g, ' ')
                .trim()
                .slice(0, 40)
            // Drop empty structural wrappers (noise); keep anything interactive.
            if (!interactive && !name) continue
            const x = clamp(((r.left + window.scrollX) / W) * 100)
            const y = clamp(((r.top + window.scrollY) / H) * 100)
            const rectKey = `${Math.round(x)},${Math.round(y)},${Math.round(wpct)},${Math.round(hpct)}`
            if (seen.has(rectKey)) continue
            // Thin stacked badges: at most one mark per top-left corner (±1%), so
            // nested boxes don't pile numbers at one spot — the failure mode that
            // made dense-desktop badge-reading unreliable in testing.
            const cornerKey = 'c:' + Math.round(x) + ',' + Math.round(y)
            if (seen.has(cornerKey)) continue
            seen.add(rectKey)
            seen.add(cornerKey)
            out.push({
                box: { x, y, w: clamp(wpct), h: clamp(hpct) },
                role: el.getAttribute('role') || el.tagName.toLowerCase(),
                name,
                tag: el.tagName.toLowerCase(),
                ...(isFixed(el) ? { fixed: true } : {}),
                ...(isClipped(el) ? { clipped: true } : {}),
            })
            if (out.length >= 160) break
        }
        out.forEach((m, i) => (m.mark = i + 1))
        const ov = document.createElement('div')
        ov.id = '__ux_marks__'
        ov.style.cssText = `position:absolute;left:0;top:0;width:${W}px;height:${H}px;z-index:2147483647;pointer-events:none;font:700 11px system-ui,sans-serif`
        for (const m of out) {
            const b = document.createElement('div')
            b.style.cssText = `position:absolute;left:${m.box.x}%;top:${m.box.y}%;width:${m.box.w}%;height:${m.box.h}%;outline:1px dashed rgba(255,45,120,.6);box-sizing:border-box`
            const lab = document.createElement('span')
            lab.textContent = m.mark
            lab.style.cssText = `position:absolute;left:0;top:0;background:#ff2d78;color:#fff;font:700 12px system-ui,sans-serif;padding:1px 4px;border-radius:0 0 4px 0;min-width:15px;text-align:center;box-shadow:0 0 0 1px rgba(0,0,0,.9)`
            b.appendChild(lab)
            ov.appendChild(b)
        }
        document.body.appendChild(ov)
        return out
    })
}

async function unmarkPage(page) {
    await page.evaluate(() => document.getElementById('__ux_marks__')?.remove())
}

async function extractStrings(page) {
    return page.evaluate(() => {
        const txt = (el) => (el.textContent || '').replace(/\s+/g, ' ').trim()
        const uniq = (a) => [...new Set(a.filter(Boolean))]
        return {
            title: document.title,
            headings: uniq(
                [...document.querySelectorAll('h1,h2,h3')].map(txt)
            ).slice(0, 30),
            buttons: uniq(
                [...document.querySelectorAll('button,[role=button]')].map(txt)
            ).slice(0, 50),
            links: uniq([...document.querySelectorAll('a')].map(txt)).slice(
                0,
                50
            ),
        }
    })
}

// Computed-style fingerprints for roles that SHOULD render identically across
// pages/modals — the deterministic basis for spacing-symmetry + cross-surface
// consistency review (VLMs eyeball px; this measures it). Captured once per
// page at the desktop viewport. The cross-page diff lives in buildConsistency.
async function styleFingerprint(page) {
    return page.evaluate(() => {
        const ROLES = [
            ['h1', 'h1'],
            ['h2', 'h2'],
            ['h3', 'h3'],
            ['card', '[data-slot=card]'],
            ['card-header', '[data-slot=card-header]'],
            ['card-content', '[data-slot=card-content]'],
            ['card-title', '[data-slot=card-title]'],
            ['dialog', '[role=dialog]'],
            ['label', 'label'],
            // text-like inputs only — checkbox/radio/button backing-inputs (e.g.
            // a Switch's hidden native checkbox) are not text-field styling.
            [
                'input',
                'input:not([type=hidden]):not([type=checkbox]):not([type=radio]):not([type=button]):not([type=submit]):not([type=reset])',
            ],
            ['textarea', 'textarea'],
            ['tablist', '[role=tablist]'],
            ['tab', '[role=tab]'],
        ]
        const px = (v) => Math.round(parseFloat(v) || 0)
        const de = document.documentElement
        const W = de.scrollWidth || 1
        const H = de.scrollHeight || 1
        const clamp = (n) => Math.max(0, Math.min(100, +n.toFixed(2)))
        const out = []
        for (const [role, sel] of ROLES) {
            const groups = {}
            for (const el of document.querySelectorAll(sel)) {
                const r = el.getBoundingClientRect()
                if (r.width < 4 || r.height < 4) continue
                const cs = getComputedStyle(el)
                const props = {
                    pt: px(cs.paddingTop),
                    pr: px(cs.paddingRight),
                    pb: px(cs.paddingBottom),
                    pl: px(cs.paddingLeft),
                    gap: px(cs.rowGap) || px(cs.columnGap),
                    radius: px(cs.borderTopLeftRadius),
                    fs: px(cs.fontSize),
                    lh: px(cs.lineHeight),
                    fw: px(cs.fontWeight),
                    mb: px(cs.marginBottom),
                }
                const sig = JSON.stringify(props)
                if (!groups[sig])
                    groups[sig] = {
                        role,
                        props,
                        count: 0,
                        box: {
                            x: clamp(((r.left + scrollX) / W) * 100),
                            y: clamp(((r.top + scrollY) / H) * 100),
                            w: clamp((r.width / W) * 100),
                            h: clamp((r.height / H) * 100),
                        },
                    }
                groups[sig].count++
            }
            out.push(...Object.values(groups))
        }
        return out
    })
}

/**
 * Variants already adjudicated as intentional. A drift report that re-raises a
 * settled decision every run trains people to skim it, so a role stays in the
 * digest but only its UNACCEPTED variants count toward `inconsistent`.
 *
 * Each entry is a role → list of {fs, …} property subsets that are known-good,
 * with the reason. Anything not matched here is genuinely new drift.
 */
const ACCEPTED_VARIANTS = {
    // Documented size tiers, not drift: hero, page title, job-detail masthead,
    // 404. See web/docs/DESIGN-SYSTEM.md.
    h1: [
        { fs: 48, why: 'home hero' },
        { fs: 30, why: 'page title' },
        { fs: 18, why: 'job-detail masthead — matches the 9 detail headers' },
        { fs: 72, why: '404' },
    ],
    // Two distinct roles that share a heading LEVEL for accessibility reasons
    // (H1→H3 skips are an axe violation) but not a visual one.
    h2: [
        { fs: 14, why: 'SectionHeader' },
        { fs: 16, why: 'gallery card title' },
    ],
    // A leading-icon search field needs the left inset; unified 2026-06-24.
    input: [{ pl: 36, why: 'search field with a leading icon' }],
}

/** True when a variant matches an accepted entry on every listed property. */
function isAccepted(role, props) {
    return (ACCEPTED_VARIANTS[role] ?? []).some((a) =>
        Object.entries(a).every(([k, v]) => k === 'why' || props[k] === v)
    )
}

// Cross-page diff: a role with >1 distinct fingerprint across pages is a
// consistency candidate; pl≠pr or pt≠pb is an asymmetric-padding candidate.
// The VLM swarm adjudicates real-defect vs intentional-variant, and settled
// verdicts move into ACCEPTED_VARIANTS so they stop being re-reported.
function buildConsistency(records) {
    const PROPS = [
        'pt',
        'pr',
        'pb',
        'pl',
        'gap',
        'radius',
        'fs',
        'lh',
        'fw',
        'mb',
    ]
    const roles = {}
    for (const r of records)
        for (const s of r.styles || []) {
            const m = (roles[s.role] ??= new Map())
            const key = JSON.stringify(s.props)
            const e = m.get(key) || { props: s.props, pages: [] }
            e.pages.push(r.id)
            m.set(key, e)
        }
    const report = {}
    for (const [role, m] of Object.entries(roles)) {
        const variants = [...m.values()].sort(
            (a, b) => b.pages.length - a.pages.length
        )
        const unsettled = variants.filter((v) => !isAccepted(role, v.props))
        const varyingProps = PROPS.filter(
            (p) => new Set(unsettled.map((v) => v.props[p])).size > 1
        )
        const asymmetric = unsettled.some(
            (v) => v.props.pl !== v.props.pr || v.props.pt !== v.props.pb
        )
        report[role] = {
            // Only UNSETTLED variants can make a role inconsistent — an
            // adjudicated tier is a decision, not a defect.
            inconsistent: unsettled.length > 1,
            asymmetric,
            varyingProps,
            accepted: variants.length - unsettled.length,
            variants: variants.map((v) => ({
                props: v.props,
                pages: [...new Set(v.pages)],
                accepted: isAccepted(role, v.props) || undefined,
            })),
        }
    }
    return report
}

/**
 * Fail loudly when a rendered route has no target.
 *
 * Coverage is the one hole a corpus cannot report on itself: an unvisited route
 * contributes zero findings and reads exactly like a clean one. Four routes sat
 * unaudited for months on that basis. Derived from `src/lib/routes.ts` so adding
 * a route to the app is what trips it, not remembering to update a list here.
 */
function assertRouteCoverage() {
    // Redirect-only routes render no UI of their own (App.tsx: `<Navigate/>`).
    const REDIRECT_ONLY = new Set(['/schedules', '/trigger'])
    const routesFile = resolve(ROOT, '..', 'src', 'lib', 'routes.ts')
    if (!existsSync(routesFile)) return
    const declared = [
        ...readFileSync(routesFile, 'utf8').matchAll(
            /export const ROUTE_\w+\s*=\s*'([^']+)'/g
        ),
    ].map((m) => m[1])
    const seg = (u) => '/' + u.split('?')[0].split('/').filter(Boolean)[0]
    const covered = new Set(
        TARGETS.map((t) => (t.url === '/' ? '/' : seg(t.url)))
    )
    const missing = declared
        .filter((r) => !REDIRECT_ONLY.has(r))
        .filter((r) => !covered.has(r === '/' ? '/' : seg(r)))
    if (missing.length) {
        console.error(
            `\n✗ route coverage: ${missing.join(', ')} render a page but have no TARGETS entry.\n` +
                `  Add one (see the COVERAGE CONTRACT above TARGETS) or add it to REDIRECT_ONLY if it only redirects.\n`
        )
        process.exit(1)
    }
    console.log(
        `   coverage: ${declared.length - REDIRECT_ONLY.size}/${declared.length - REDIRECT_ONLY.size} rendered routes have targets ` +
            `(${[...REDIRECT_ONLY].join(', ')} exempt — redirects)`
    )
}

async function run() {
    console.log(`\n── Arn UX Audit (${TARGETS.length} targets) ──`)
    console.log(`   base: ${BASE}`)
    assertRouteCoverage()
    console.log('')

    // Rotate previous after/ → before/ for the visual diff.
    if (existsSync(D.after)) {
        rmSync(D.before, { recursive: true, force: true })
        renameSync(D.after, D.before)
    }
    for (const d of [D.after, D.diff, D.data]) mkdirSync(d, { recursive: true })

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
    // Boot every navigation in the light baseline. Without this, next-themes'
    // `enableSystem` resolves from the headless browser's prefers-color-scheme
    // and the captured theme silently depends on the host.
    await page.evaluateOnNewDocument(() => {
        try {
            localStorage.setItem('theme', 'light')
        } catch {
            /* storage unavailable — setTheme still forces it post-load */
        }
    })
    await page.emulateMediaFeatures([
        { name: 'prefers-color-scheme', value: 'light' },
    ])

    const consoleBuf = []
    page.on('console', (m) => {
        const t = m.type()
        if (t === 'error' || t === 'warning')
            consoleBuf.push({ type: t, text: m.text().slice(0, 300) })
    })
    page.on('pageerror', (e) =>
        consoleBuf.push({ type: 'pageerror', text: String(e).slice(0, 300) })
    )
    // "Failed to load resource: … 502" in the console names no URL, which made a
    // real mock-mode request failure unactionable for as long as it shipped.
    // Record the response itself so the offender is in the corpus.
    page.on('response', (res) => {
        const s = res.status()
        if (s >= 400)
            consoleBuf.push({
                type: 'response',
                text: `${s} ${res.request().method()} ${res.url().slice(0, 220)}`,
            })
    })
    page.on('requestfailed', (req) =>
        consoleBuf.push({
            type: 'requestfailed',
            text: `${req.failure()?.errorText ?? 'failed'} ${req.url().slice(0, 220)}`,
        })
    )

    const records = []
    const t0 = Date.now()

    for (const tgt of TARGETS) {
        consoleBuf.length = 0
        const rec = { id: tgt.id, url: tgt.url, desc: tgt.desc, ok: true }
        try {
            await page.setViewport(VIEWPORTS[0])
            const nav = await settle(page, BASE + tgt.url)
            if (nav.navError) rec.navError = nav.navError
            if (tgt.tab) {
                await clickByText(page, tgt.tab, '[role=tab]')
                await sleep(700)
            }
            if (tgt.click) {
                const hit = await clickByText(page, tgt.click)
                if (!hit) rec.clickMiss = tgt.click
                await sleep(800)
            }
            rec.finalUrl = page.url().replace(BASE, '')

            const strings = await extractStrings(page)
            Object.assign(rec, strings)
            rec.text = (
                await page.evaluate(() => document.body.innerText)
            ).slice(0, 12000)
            // Guard: a mock bundle must never capture the auth wall. When the
            // static build's auth wiring breaks, every target renders the same
            // "Sign in required" card and the whole corpus silently reports as
            // covered (it did — 28 targets × 8 variants of one screen).
            // Coverage that looks like coverage is worse than a known gap.
            if (/Sign in required|Access not assigned/.test(rec.text)) {
                rec.ok = false
                rec.navError = 'captured auth wall, not the target page'
            }
            rec.a11y = flattenA11y(
                await page.accessibility.snapshot({ interestingOnly: true })
            ).slice(0, 250)
            rec.styles = await styleFingerprint(page)
            writeFileSync(
                resolve(D.data, `${tgt.id}.html`),
                await page.content()
            )

            // axe, once per theme. Contrast is the whole reason: `color-contrast`
            // is evaluated against COMPUTED colours, so a light-only pass can
            // never see a dark-token regression. Structural violations repeat
            // across both passes, which is the point — a fix must hold in both.
            const runAxe = async () => {
                const axe = await new AxePuppeteer(page)
                    .analyze()
                    .catch((e) => ({ violations: [], error: String(e) }))
                const violations = (axe.violations || []).map((v) => ({
                    id: v.id,
                    impact: v.impact,
                    help: v.help,
                    nodes: v.nodes.length,
                    targets: v.nodes.slice(0, 4).map((n) => ({
                        target: Array.isArray(n.target)
                            ? n.target.join(' ')
                            : String(n.target),
                        html: (n.html || '').replace(/\s+/g, ' ').slice(0, 160),
                        summary: (n.failureSummary || '')
                            .replace(/\s+/g, ' ')
                            .slice(0, 240),
                    })),
                }))
                const counts = {}
                for (const v of violations)
                    counts[v.impact || 'minor'] =
                        (counts[v.impact || 'minor'] || 0) + 1
                const out = { counts, violations }
                if (axe.error) out.error = axe.error
                // Bounding boxes for axe targets — captured at the desktop
                // viewport (where axe runs), so they overlay <id>.desktop*.png
                // in axe-findings.html.
                for (const v of out.violations)
                    for (const t of v.targets)
                        t.box = await boxFor(page, t.target)
                return out
            }

            // Warm-up cycle BEFORE either measurement. Without it the two
            // passes see different DOM states, not different themes: the dark
            // pass runs later, so lazily-mounted subtrees (Radix tab panels,
            // deferred lists) are present for it and absent for light — and
            // every structural violation inside them reads as "dark-only".
            // Flipping both ways first puts both passes on an equally settled
            // page, so the delta is genuinely the token set.
            await setTheme(page, 'dark')
            await setTheme(page, 'light')
            await sleep(250)

            rec.axe = await runAxe()
            await setTheme(page, 'dark')
            rec.axeDark = await runAxe()
            await setTheme(page, 'light')

            rec.overflow = {}
            rec.shots = {}
            for (const variant of VARIANTS) {
                await page.setViewport(variant.vp)
                await setTheme(page, variant.theme)
                await sleep(250)
                const off = await overflow(page)
                rec.overflow[variant.id] = off.length ? off : false
                const shot = `${tgt.id}.${variant.id}.png`
                await page.screenshot({
                    path: resolve(D.after, shot),
                    fullPage: true,
                })
                rec.shots[variant.id] = shot
                // Set-of-Mark: number every significant element and capture a
                // marked overlay; reviewers reference mark numbers (ux-review.mjs
                // resolves the box from the DOM rect — no freehand coordinates).
                const marks = await markPage(page)
                await page.screenshot({
                    path: resolve(
                        D.after,
                        `${tgt.id}.${variant.id}.marked.png`
                    ),
                    fullPage: true,
                })
                await unmarkPage(page)
                writeFileSync(
                    resolve(D.data, `${tgt.id}.${variant.id}.marks.json`),
                    JSON.stringify(marks)
                )
            }
            await setTheme(page, 'light')
            rec.console = [...consoleBuf]
        } catch (e) {
            rec.ok = false
            rec.error = String(e.message || e)
        }
        writeFileSync(
            resolve(D.data, `${tgt.id}.json`),
            JSON.stringify(rec, null, 2)
        )
        records.push(rec)
        const a = rec.axe?.violations?.length ?? 0
        const c = rec.console?.length ?? 0
        // Only the DARK-ONLY delta is worth a column: everything structural
        // repeats in both passes and is already counted by `a`.
        const lightIds = new Set((rec.axe?.violations ?? []).map((v) => v.id))
        const darkOnly = (rec.axeDark?.violations ?? []).filter(
            (v) => !lightIds.has(v.id)
        )
        const flags = [
            !rec.ok && 'ERR',
            rec.clickMiss && `no "${rec.clickMiss}"`,
            a && `${a} axe`,
            darkOnly.length && `${darkOnly.length} axe(dark-only)`,
            c && `${c} console`,
        ]
            .filter(Boolean)
            .join(' · ')
        console.log(
            `  ${rec.ok ? '✓' : '✗'} ${tgt.id}${flags ? ` — ${flags}` : ''}`
        )
    }

    await browser.close()

    // Visual diff vs previous run.
    const diffs = {}
    let changed = 0
    for (const f of readdirSync(D.after)) {
        const bp = resolve(D.before, f)
        const ap = resolve(D.after, f)
        if (!existsSync(bp)) continue
        try {
            const b = PNG.sync.read(readFileSync(bp))
            const a = PNG.sync.read(readFileSync(ap))
            if (b.width !== a.width || b.height !== a.height) {
                diffs[f] = 'size-changed'
                changed++
                continue
            }
            const d = new PNG({ width: b.width, height: b.height })
            const n = pixelmatch(b.data, a.data, d.data, b.width, b.height, {
                threshold: 0.1,
            })
            if (n > 0) {
                writeFileSync(resolve(D.diff, f), PNG.sync.write(d))
                diffs[f] = n
                changed++
            }
        } catch {
            /* ignore unreadable */
        }
    }

    // Cross-surface lexicon: every user-facing label → where it appears, so a
    // reviewer can spot the same concept worded differently across surfaces.
    const lexicon = { headings: {}, buttons: {}, links: {} }
    for (const r of records)
        for (const kind of ['headings', 'buttons', 'links'])
            for (const s of r[kind] || []) (lexicon[kind][s] ??= []).push(r.id)
    writeFileSync(
        resolve(ROOT, 'lexicon.json'),
        JSON.stringify(lexicon, null, 2)
    )

    // Cross-surface style consistency: computed-style fingerprints diffed per
    // role → spacing-symmetry + page/modal consistency candidates (exact px).
    const consistency = buildConsistency(records)
    writeFileSync(
        resolve(ROOT, 'style-lexicon.json'),
        JSON.stringify(consistency, null, 2)
    )
    const incRoles = Object.entries(consistency).filter(
        ([, v]) => v.inconsistent || v.asymmetric
    )

    writeReport(records, diffs)
    writeMachineScan(records)

    const sec = ((Date.now() - t0) / 1000).toFixed(1)
    const axeTotal = records.reduce(
        (n, r) => n + (r.axe?.violations?.length ?? 0),
        0
    )
    const darkOnlyTotal = records.reduce((n, r) => {
        const lightIds = new Set((r.axe?.violations ?? []).map((v) => v.id))
        return (
            n +
            (r.axeDark?.violations ?? []).filter((v) => !lightIds.has(v.id))
                .length
        )
    }, 0)
    const consoleTotal = records.reduce(
        (n, r) => n + (r.console?.length ?? 0),
        0
    )
    console.log(
        `\n── ${records.length} targets × ${VARIANTS.length} variants (${VIEWPORTS.length} viewports × ${THEMES.join('/')}) · ${axeTotal} axe · ${darkOnlyTotal} dark-only axe · ${consoleTotal} console · ${changed} visual diffs · ${sec}s ──`
    )
    console.log(`   corpus:  audit/data/*.json + audit/lexicon.json`)
    console.log(
        `   style:   audit/style-lexicon.json — ${incRoles.length} role(s) with cross-page variance/asymmetry: ${incRoles.map(([r]) => r).join(', ') || 'none'}`
    )
    console.log(
        `   reports: audit/report.md · audit/index.html · audit/axe-findings.html (machine pre-scan)\n`
    )
}

function writeReport(records, diffs) {
    // Markdown digest — the human/LLM-readable summary.
    let md = `# Arn UX Audit\n\n- Base: \`${BASE}\`\n- Frozen clock: ${new Date(FIXED_TIME).toISOString()} (${TIMEZONE})\n- Targets: ${records.length}\n- Variants: ${VARIANTS.map((v) => v.id).join(', ')}\n\n`
    md += `\`axe\` is the light pass; \`axe(dark)\` counts only rules that fail in dark and NOT in light — a structural violation shows up in both and is already in the first column.\n\n`
    md += `| Target | Route | axe | axe(dark) | console | overflow | visual |\n|---|---|---|---|---|---|---|\n`
    const darkOnlyFor = (r) => {
        const lightIds = new Set((r.axe?.violations ?? []).map((v) => v.id))
        return (r.axeDark?.violations ?? []).filter((v) => !lightIds.has(v.id))
    }
    for (const r of records) {
        const a = r.axe?.violations?.length ?? 0
        const c = r.console?.length ?? 0
        const ov = Object.entries(r.overflow || {})
            .filter(([, v]) => v)
            .map(([k]) => k)
            .join(',')
        const vd = Object.keys(diffs)
            .filter((f) => f.startsWith(r.id))
            .map((f) => diffs[f])
            .join(',')
        md += `| ${r.id} | \`${r.finalUrl ?? r.url}\` | ${a || ''} | ${darkOnlyFor(r).length || ''} | ${c || ''} | ${ov} | ${vd} |\n`
    }
    md += `\n## axe violations (light)\n\n`
    for (const r of records) {
        if (!r.axe?.violations?.length) continue
        md += `### ${r.id}\n`
        for (const v of r.axe.violations) {
            md += `- **${v.impact}** \`${v.id}\` ×${v.nodes} — ${v.help}\n`
            for (const t of v.targets || [])
                md += `  - \`${t.target}\` — ${t.summary}\n`
        }
        md += `\n`
    }
    const darkHits = records.filter((r) => darkOnlyFor(r).length)
    md += `\n## axe violations (dark only)\n\n`
    if (!darkHits.length) {
        md += `None — the dark token set introduces no violation the light pass does not already report.\n\n`
    }
    for (const r of darkHits) {
        md += `### ${r.id}\n`
        for (const v of darkOnlyFor(r)) {
            md += `- **${v.impact}** \`${v.id}\` ×${v.nodes} — ${v.help}\n`
            for (const t of v.targets || [])
                md += `  - \`${t.target}\` — ${t.summary}\n`
        }
        md += `\n`
    }
    const consoleHits = records.filter((r) => r.console?.length)
    if (consoleHits.length) {
        md += `## console errors / warnings\n\n`
        for (const r of consoleHits) {
            md += `### ${r.id}\n`
            for (const m of r.console) md += `- ${m.type}: ${m.text}\n`
            md += `\n`
        }
    }
    md += `## cross-surface\n\nReview \`audit/lexicon.json\` for label drift (the same concept worded differently across surfaces).\n`
    writeFileSync(resolve(ROOT, 'report.md'), md)

    // HTML gallery — desktop light/dark shots + diff + badges per target.
    let h = `<!DOCTYPE html><html><head><meta charset="utf-8"><title>Arn UX Audit</title><style>
*{box-sizing:border-box;margin:0;padding:0}body{background:#0b0e14;color:#ddd;font-family:system-ui,sans-serif;padding:2rem;max-width:1500px;margin:auto}
h1{font-size:1.6rem;color:#fff;margin-bottom:.25rem}.sub{color:#888;margin-bottom:2rem;font-size:.85rem}
.card{margin-bottom:2.5rem;border:1px solid #232842;background:#11141f;border-radius:10px;overflow:hidden}
.hd{display:flex;gap:.75rem;align-items:center;flex-wrap:wrap;padding:.7rem 1rem;background:#161a2b}
.hd h2{font-size:.95rem;color:#a8d8ea}.hd .rt{color:#778;font-size:.8rem}
.badge{font-size:.7rem;padding:.1rem .5rem;border-radius:99px;text-transform:uppercase;letter-spacing:.03em}
.b-axe{background:#3a2030;color:#f6a}.b-con{background:#3a3320;color:#fd6}.b-ov{background:#20303a;color:#6cf}.b-ok{background:#1d3324;color:#7d9}
.row{display:flex;gap:4px}.row>div{flex:1;min-width:0}.row img{width:100%;height:auto;display:block;border-top:1px solid #232842}
.lbl{font-size:.7rem;padding:.25rem;text-align:center;background:#0e1019;color:#778;text-transform:uppercase}
.lbl.changed{color:#f6f}.none{display:flex;justify-content:center;align-items:center;height:70px;color:#445;background:#0c0e16;font-size:.8rem}
</style></head><body><h1>🔍 Arn UX Audit</h1><p class="sub">${BASE} · frozen ${new Date(FIXED_TIME).toISOString()}</p>`
    for (const r of records) {
        const a = r.axe?.violations?.length ?? 0
        const c = r.console?.length ?? 0
        const ov = Object.values(r.overflow || {}).some(Boolean)
        const lightIds = new Set((r.axe?.violations ?? []).map((v) => v.id))
        const dk = (r.axeDark?.violations ?? []).filter(
            (v) => !lightIds.has(v.id)
        ).length
        const badges = [
            a ? `<span class="badge b-axe">${a} axe</span>` : '',
            dk ? `<span class="badge b-axe">${dk} axe dark-only</span>` : '',
            c ? `<span class="badge b-con">${c} console</span>` : '',
            ov ? `<span class="badge b-ov">overflow</span>` : '',
            !a && !dk && !c && !ov
                ? `<span class="badge b-ok">clean</span>`
                : '',
        ].join('')
        const shot = r.shots?.desktop
        const darkShot = r.shots?.['desktop-dark']
        const hasDiff = shot && existsSync(resolve(D.diff, shot))
        const hasBefore = shot && existsSync(resolve(D.before, shot))
        h += `<div class="card"><div class="hd"><h2>${r.id}</h2><span class="rt">${r.finalUrl ?? r.url} · ${r.desc}</span>${badges}</div><div class="row">`
        h += `<div><div class="lbl">${hasBefore ? 'Before' : 'New'}</div>${hasBefore ? `<img src="before/${shot}" loading="lazy">` : `<img src="after/${shot}" loading="lazy">`}</div>`
        h += `<div><div class="lbl">After · light</div><img src="after/${shot}" loading="lazy"></div>`
        h += `<div>${darkShot ? `<div class="lbl">After · dark</div><img src="after/${darkShot}" loading="lazy">` : '<div class="none">no dark capture</div>'}</div>`
        h += `<div>${hasDiff ? `<div class="lbl changed">Diff</div><img src="diff/${shot}" loading="lazy">` : '<div class="none">no visual change</div>'}</div>`
        h += `</div></div>`
    }
    writeFileSync(resolve(ROOT, 'index.html'), h + '</body></html>')
}

function writeMachineScan(records) {
    // Annotated review page: each screenshot with the offending element boxed
    // in place, so a human can eyeball every machine-found issue and skim by
    // severity. Boxes are % of the full-page image, so they overlay exactly.
    const C = {
        critical: '#ff5c7a',
        serious: '#ff9f43',
        moderate: '#ffd93d',
        minor: '#5cc8ff',
        layout: '#c084fc',
    }
    const esc = (s) =>
        String(s ?? '').replace(
            /[&<>"]/g,
            (c) =>
                ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]
        )
    const tally = {}
    let decorativeCount = 0
    const sections = []
    for (const r of records) {
        const views = {}
        const push = (vp, f) => {
            ;(views[vp] ??= []).push(f)
        }
        for (const v of r.axe?.violations || [])
            for (const t of v.targets || [])
                push('desktop', {
                    impact: v.impact || 'minor',
                    label: `${v.help} (${v.id})`,
                    sel: t.target,
                    summary: t.summary,
                    box: t.box || null,
                    decorative: 0,
                })
        // Dark violations overlay the dark desktop shot. Structural rules
        // repeat here by design — a fix has to hold in both themes.
        for (const v of r.axeDark?.violations || [])
            for (const t of v.targets || [])
                push('desktop-dark', {
                    impact: v.impact || 'minor',
                    label: `${v.help} (${v.id})`,
                    sel: t.target,
                    summary: t.summary,
                    box: t.box || null,
                    decorative: 0,
                })
        for (const vp of VARIANTS.map((v) => v.id))
            for (const o of Array.isArray(r.overflow?.[vp])
                ? r.overflow[vp]
                : [])
                push(vp, {
                    impact: 'layout',
                    label: 'horizontal overflow',
                    sel: o.sel,
                    summary: 'element renders wider than the viewport',
                    box: o.box || null,
                    decorative: o.decorative ? 1 : 0,
                })
        const vps = Object.keys(views).filter((vp) => views[vp].length)
        if (!vps.length) continue
        let inner = ''
        for (const vp of vps) {
            let boxes = ''
            let list = ''
            views[vp].forEach((f, i) => {
                const n = i + 1
                const col = C[f.impact] || C.minor
                if (f.decorative) decorativeCount++
                else tally[f.impact] = (tally[f.impact] || 0) + 1
                if (f.box)
                    boxes += `<div class="box" data-n="${n}" data-imp="${f.impact}" data-dec="${f.decorative}" style="left:${f.box.x}%;top:${f.box.y}%;width:${f.box.w}%;height:${f.box.h}%;--c:${col}"><span class="num">${n}</span></div>`
                list += `<li class="fi" data-n="${n}" data-imp="${f.impact}" data-dec="${f.decorative}" style="--c:${col}"><span class="num">${n}</span><div><span class="imp">${f.impact}</span> ${esc(f.label)}<br><code>${esc(f.sel)}</code>${f.box ? '' : ' <em class="nf">(not located)</em>'}${f.summary ? `<div class="sum">${esc(f.summary)}</div>` : ''}</div></li>`
            })
            const src = r.shots?.[vp] ?? `${r.id}.${vp}.png`
            inner += `<div class="view"><div class="vpl">${vp}</div><div class="shotwrap"><div class="shot"><img src="after/${src}" loading="lazy">${boxes}</div></div><ol class="legend">${list}</ol></div>`
        }
        sections.push(
            `<section class="page"><h2>${esc(r.id)} <span class="rt">${esc(r.finalUrl ?? r.url)} · ${esc(r.desc)}</span></h2>${inner}</section>`
        )
    }
    const tallyStr =
        Object.entries(tally)
            .map(
                ([k, v]) =>
                    `<span class="imp" style="--c:${C[k]}">${v} ${k}</span>`
            )
            .join(' ') || 'no findings'
    const html = `<!DOCTYPE html><html><head><meta charset="utf-8"><title>Arn UX · machine pre-scan</title><style>
*{box-sizing:border-box;margin:0;padding:0}
body{background:#0b0e14;color:#dde;font-family:system-ui,sans-serif;padding:1.5rem 2rem 4rem;max-width:1600px;margin:auto}
h1{font-size:1.5rem;color:#fff}.sub{color:#889;font-size:.82rem;margin:.2rem 0 1rem}
.controls{position:sticky;top:0;z-index:50;background:rgba(11,14,20,.93);backdrop-filter:blur(6px);padding:.7rem 0;border-bottom:1px solid #232842;display:flex;gap:1rem;flex-wrap:wrap;align-items:center;font-size:.8rem}
.controls label{display:inline-flex;gap:.3rem;align-items:center;cursor:pointer;color:#bcd}
.imp{font-size:.7rem;text-transform:uppercase;letter-spacing:.04em;color:var(--c);font-weight:700}
.page{margin:2rem 0;border-top:1px solid #1c2236;padding-top:1rem}
.page h2{font-size:1rem;color:#a8d8ea}.page .rt{color:#667;font-size:.78rem;font-weight:400}
.view{display:grid;grid-template-columns:minmax(0,1.4fr) minmax(0,1fr);gap:1rem;margin-top:1rem}
@media(max-width:1100px){.view{grid-template-columns:1fr}}
.vpl{grid-column:1/-1;font-size:.7rem;text-transform:uppercase;letter-spacing:.05em;color:#667}
.shotwrap{max-height:82vh;overflow:auto;border:1px solid #232842;border-radius:8px;background:#000}
.shot{position:relative;display:block;width:100%}
.shot img{display:block;width:100%;height:auto}
.box{position:absolute;border:2px solid var(--c);border-radius:6px;box-shadow:0 0 0 2px rgba(0,0,0,.55),0 0 14px var(--c);min-width:10px;min-height:10px;pointer-events:none}
.box .num,.fi .num{flex:none;width:20px;height:20px;border-radius:50%;background:var(--c);color:#000;font-size:.7rem;font-weight:700;display:flex;align-items:center;justify-content:center}
.box .num{position:absolute;top:-11px;left:-11px}
.box.pulse{animation:pulse 1.1s ease-out 2}
@keyframes pulse{0%{box-shadow:0 0 0 2px rgba(0,0,0,.55),0 0 0 0 var(--c)}50%{box-shadow:0 0 0 2px rgba(0,0,0,.55),0 0 0 10px transparent}100%{box-shadow:0 0 0 2px rgba(0,0,0,.55),0 0 14px var(--c)}}
.legend{list-style:none;display:flex;flex-direction:column;gap:.5rem;align-self:start}
.fi{display:flex;gap:.6rem;padding:.5rem .6rem;background:#11141f;border-left:3px solid var(--c);border-radius:0 6px 6px 0;cursor:pointer;font-size:.82rem;line-height:1.4}
.fi:hover{background:#161a2b}
.fi code{color:#9cf;font-size:.72rem;word-break:break-all}
.fi .sum{color:#889;font-size:.75rem;margin-top:.2rem}
.fi .nf{color:#c97;font-style:italic}
body.hide-critical [data-imp=critical],body.hide-serious [data-imp=serious],body.hide-moderate [data-imp=moderate],body.hide-minor [data-imp=minor],body.hide-layout [data-imp=layout]{display:none!important}
body:not(.show-dec) [data-dec="1"]{display:none!important}
</style></head><body>
<h1>🤖 Arn UX · machine pre-scan <span style="font-size:.8rem;color:#667;font-weight:400">— axe + overflow; see review.html for reviewer findings</span></h1>
<p class="sub">${esc(BASE)} · frozen ${new Date(FIXED_TIME).toISOString()} · click any finding to flash its box · ${tallyStr}${decorativeCount ? ` · <span style="color:#778">${decorativeCount} likely-decorative overflow hidden</span>` : ''}</p>
<div class="controls"><strong style="color:#889">Show</strong>${['critical', 'serious', 'moderate', 'minor', 'layout'].map((k) => `<label><input type="checkbox" data-imp="${k}" checked><span class="imp" style="--c:${C[k]}">${k}</span></label>`).join('')}<label style="margin-left:auto"><input type="checkbox" id="dec"> likely-decorative overflow</label></div>
${sections.join('\n')}
<script>
document.querySelectorAll('input[data-imp]').forEach(cb=>cb.addEventListener('change',()=>document.body.classList.toggle('hide-'+cb.dataset.imp,!cb.checked)));
document.getElementById('dec').addEventListener('change',e=>document.body.classList.toggle('show-dec',e.target.checked));
document.querySelectorAll('.view').forEach(v=>v.querySelector('.legend').addEventListener('click',e=>{const li=e.target.closest('.fi');if(!li)return;const b=v.querySelector('.box[data-n="'+li.dataset.n+'"]');if(b){b.classList.remove('pulse');void b.offsetWidth;b.classList.add('pulse');b.scrollIntoView({block:'center',behavior:'smooth'});}}));
</script></body></html>`
    writeFileSync(resolve(ROOT, 'axe-findings.html'), html)
}

run().catch((e) => {
    console.error('Audit failed:', e)
    process.exit(1)
})
