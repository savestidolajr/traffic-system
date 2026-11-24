#!/usr/bin/env node
// =============================================================================
// Build the proposal review document from the captured before/after corpus
// =============================================================================
//
//   node scripts/proposal-shots.mjs before   # on the untouched tree
//   ...apply a proposal...
//   node scripts/proposal-shots.mjs after
//   node scripts/proposal-doc.mjs            # -> audit/proposals/index.html
//
// The narrative lives in PROPOSALS below, beside the scene ids it illustrates,
// so the document cannot drift from the corpus: a scene with no capture renders
// as a visible gap rather than silently disappearing.

import { readFileSync, writeFileSync, existsSync } from 'fs'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'

const ROOT = resolve(
    dirname(fileURLToPath(import.meta.url)),
    '..',
    'audit',
    'proposals'
)

const PROPOSALS = [
    {
        id: 'p0',
        rank: 'Prerequisite',
        title: 'Make the air-slot path reachable at all',
        verdict: 'needed before anything else can be judged',
        problem: `Two independent reasons meant no bulletin in mock mode had ever been attached to an air slot:
all 13 seeded schedules carry <code>Enabled: false</code>, and the job fixtures' station names
(<code>2GB 873 Sydney News/Talk</code>) share no values with the schedule fixtures'
(<code>KIIS 90s</code>). <code>findAirSlot</code> matches on <code>schedule.enabled &amp;&amp; schedule.station === job.station</code>,
so it returned <code>null</code> for every job, the "Air slot" vital never rendered, and the board could
never place a bulletin on a slot. The deadline the whole product exists to beat had never been
seen running locally.`,
        change: `Added two enabled demo schedules to <code>dev-data.json</code> whose <code>Station</code> matches the
two <code>WAITING_APPROVAL</code> job fixtures. Purely additive (+96 lines, nothing existing altered).
Also fixed a real bug this exposed, see P1.`,
        files: ['web/src/lib/api/mock/dev-data.json'],
        scenes: [],
        decide: `Keep the two demo schedules, or migrate the existing 13 to match the job fixtures instead?
Adding was the non-destructive option; renaming would touch 24 test references.`,
    },
    {
        id: 'p1',
        rank: '1',
        title: 'Count down the deadline that actually binds',
        verdict:
            'the highest-leverage change; it decides whether bulletins air',
        problem: `Every urgency signal in the product was driven by <code>getSecondsRemaining</code> =
<code>lastUpdatedAt + 8 minutes</code>: an SLA on how long the operator has held the bulletin, not its
airtime. That one function feeds the red dock counter, the audible chime, the nav badge, the queue
order, the board ranking and the hotter-job banner. The real air slot was used in exactly one place
and rendered as a static grey label.
<br><br>
Three consequences, all verified: a passed slot was indistinguishable from one ten minutes out
(<code>slotStateForJob</code> took no <code>now</code>, so <code>'missed'</code> was only reachable when
<em>no</em> bulletin existed); Approve stayed enabled and confirmed with the same
"Approving cannot be undone" either way; and because <code>lastUpdatedAt</code> moves on every write,
revising a bulletin <strong>reset its urgency to +8:00</strong> while its airtime did not move.`,
        change: `<code>resolveBindingDeadline(job, airSlotAt, now)</code> returns the earlier of the air slot and the
approval window, plus which one it is and whether the slot is missed. The dock counts that and says
which clock it is. <code>slotStateForJob</code> now takes the slot time and <code>now</code>, so a live
bulletin can read <code>missed</code> on the board. The air-slot vital turns destructive once the slot
passes. The approve confirmation names a missed slot. The revise dialog compares its own
"~1 min" estimate against the time left.
<br><br>
Also fixed a latent bug found on the way: <code>enumerateSlots</code> requested only
<code>pastCount * 3 + futureCount</code> occurrences across a 6 hour lookback, which silently truncated
any dense schedule. A station running every 15 minutes has ~24 occurrences in that window, so the
10-occurrence request stopped two hours short and <em>no past slot existed at all</em>. Every
<code>missed</code> state was unreachable for the busiest stations, including the real
<code>0,15,30,45 6-9,15-18</code> Cairns schedule.`,
        files: [
            'web/src/features/jobs/lib/job-meta/urgency.ts',
            'web/src/features/jobs/hooks/use-air-slot.ts (new)',
            'web/src/features/jobs/components/review/DecisionDock.tsx',
            'web/src/features/jobs/components/review/ReviewVitals.tsx',
            'web/src/features/jobs/components/review/JobReviewCockpit.tsx',
            'web/src/features/jobs/components/review/ReviseDialog.tsx',
            'web/src/features/onair/lib/slots.ts',
            'web/src/features/onair/components/board/lane-condition.ts',
            'web/src/features/onair/components/board/CoverageHome.tsx',
            'web/src/features/onair/components/workstation/ReviewPane.tsx',
        ],
        scenes: [
            'p1-dock-slot-missed',
            'p1-cockpit-header',
            'p1-board-lanes',
            'p1-dock-slot-upcoming',
            'p1-queue-order',
        ],
        decide: `Three open decisions. (a) Should Approve be <em>blocked</em> past the slot, or only
re-confirmed? I chose re-confirm, because airing late is sometimes right and the UI should not
decide that. (b) The queue rail and the alarms still order and fire on the 8-minute SLA. Finishing
that means re-ordering the operator's whole work list, which changes behaviour they have muscle
memory for, so I left it and flagged it rather than bundling it here. (c) Should "Use fallback audio"
be <em>promoted</em> to primary when a regenerate cannot make the slot, or is the warning enough?
I shipped the warning only.`,
    },
    {
        id: 'p2',
        rank: '2',
        title: 'Stop retrying permanent failures on irreversible actions',
        verdict:
            'twenty minute fix; it currently misreports successful approvals as failures',
        problem: `<code>mutations: { retry: 1 }</code> retried <em>every</em> failure, including a permanent 4xx.
The queries branch of the same config object already refused 4xx, so the principle was understood
and simply never applied to mutations. The concrete harm: an approve whose response is lost in
transit gets retried with the same now-stale etag, the server answers 412 Precondition Failed, and
the operator is told the approval failed when it landed.`,
        change: `Extracted <code>isPermanentClientError</code> into <code>lib/api/client.ts</code> and used it in
both branches. Covered by a unit test, because this is the one proposal that cannot be
screenshotted.`,
        files: [
            'web/src/lib/api/client.ts',
            'web/src/App.tsx',
            'web/src/lib/api/retry-policy.test.ts (new)',
        ],
        scenes: [],
        proof: `<pre>npx vitest run src/lib/api/retry-policy.test.ts

 ✓ refuses to retry the precondition failure an approve race produces
 ✓ refuses to retry the rest of the 4xx range
 ✓ allows a retry for a server fault or a transport failure
 ✓ treats 399 and 500 as outside the permanent band

 Tests  4 passed</pre>`,
        decide: `Any reason to keep retrying 409/412 that I have missed? I can see none, but you know the
backend's idempotency guarantees better than I do.`,
    },
    {
        id: 'p3',
        rank: '3',
        title: 'Three bounded layout and input fixes',
        verdict: 'half a day; each is independent of the others',
        problem: `<strong>3a, the stranding trap.</strong> The queue rail is gated by a <em>container</em> query
(<code>@4xl/workstation</code>, 896px container) and the back-to-queue button by a <em>viewport</em>
query (<code>md:hidden</code>, 768px viewport). Between them the queue is already hidden and the escape
hatch has already been removed. Measured stranded at 768, 820, 860, 900 and 960px; the queue returns
at 972px. Two rulers for one decision.
<br><br>
<strong>3b, the rundown starves its identity column.</strong> At 1440px the ROAD cell rendered 76px
while <code>Tullamarine Freeway</code> needs 130px, losing 42% of the row's only identifier, while
TYPE (<code>Roadworks</code>) and CLASS (<code>Motorway</code>) each kept full width on
near-constant values. Worse on desktop than on mobile, which is the opposite of what I first assumed.
<br><br>
<strong>3c, the drag had no keyboard path.</strong> <code>RundownTable</code> registered
<code>PointerSensor</code> only, and dragging across the cut was the sole way to include or exclude a
road, on the screen with a deadline. The correct pattern already existed in
<code>OperatorNotesPanel</code>.`,
        change: `Both gates now use the container ruler, so the back button appears exactly when the queue
does not. The road column takes the remaining space and the two near-constant enum columns are
dropped. The rundown gains a keyboard-reachable path to the same state change the drop performs.`,
        files: [
            'web/src/pages/OnAirPage.tsx',
            'web/src/features/onair/components/workstation/ReviewPane.tsx',
            'web/src/features/jobs/components/review/adjust/RundownTable.tsx',
            'web/src/features/jobs/components/review/adjust/RundownTable.test.tsx',
        ],
        scenes: [
            'p3-stranded-768',
            'p3-stranded-960',
            'p3-rundown-desktop',
            'p3-rundown-mobile',
        ],
        decide: `3b removed the TYPE and CLASS columns to pay for the road name. Are those load-bearing for
any operator you know? If so, the alternative is hiding them below a breakpoint rather than removing
them.`,
    },
    {
        id: 'p4',
        rank: '4',
        declined: true,
        title: 'Make the advertised shortcuts real',
        verdict:
            'DECLINED and reverted. The palette hints stay exactly as they were.',
        problem: `<code>routes.ts</code> declares a <code>shortcut</code> for all 11 nav routes and the palette
renders <code>Ctrl+1</code> … <code>Ctrl+9</code>, <code>Ctrl+0</code>, <code>Ctrl+i</code>. No handler
binds any of them; the only keys bound anywhere are <code>r</code>, <code>R</code>,
<code>Enter</code>, <code>j</code>, <code>k</code> and <code>Cmd/Ctrl+K</code>. Recorded here so the
finding is not lost, but left alone on your instruction.`,
        change: `<strong>Nothing.</strong> The proposal was built (a <code>g</code>-prefix sequence, a
<code>useNavShortcuts</code> hook, a resolver test) and then fully reverted:
<code>use-nav-shortcuts.ts</code> deleted, and <code>routes.ts</code>,
<code>CommandPalette.tsx</code> and <code>routes.test.ts</code> returned to their prior state. The
revert was surgical rather than a blanket restore, because all three files also carry your in-flight
Bookends and Stations-role work, which is untouched. Verified by grep: no
<code>NAV_SHORTCUT_PREFIX</code>, <code>resolveNavShortcut</code> or <code>useNavShortcuts</code>
reference remains anywhere in <code>src</code>.`,
        files: [],
        scenes: [],
        decide: `Nothing to decide. If you ever want it, the smaller option is to delete the
<code>shortcut</code> fields and the palette's hint rendering so the UI stops advertising keys that do
nothing, which is a subtraction rather than a new binding.`,
    },
    {
        id: 'p5',
        rank: '5',
        title: 'Land the four drifted library behaviours',
        verdict:
            'partial; the structural extraction is deliberately NOT started',
        problem: `Five "versioned per-station library" features were hand-copied and fixes landed unevenly.
The four user-visible gaps: the backend's 409 in-use message was surfaced by 1 of 4 galleries;
archive UI existed on 2 of 4 although the endpoints exist for all; the dirty-close guard existed on
2 of 4 studios, so typed work was silently lost in the other two; and the live-edit confirmation was
missing entirely from the pronunciation studio, while <code>VoiceStudio</code> imported
<em>Delivery's</em> copy and passed <code>styleName={profile?.name}</code>.`,
        change: `One shared <code>LiveEditConfirmationDialog</code> in <code>components/</code> replacing the two
byte-divergent feature-local copies, one shared 409 helper, archive UI added where it was missing, and
the dirty-close guard added to the two studios that lacked it. The ~250-line gallery page shell and
the four <code>VersionHistory</code> implementations are untouched: that is the larger piece of work
and it needs its own decision.`,
        files: [
            'web/src/components/LiveEditConfirmationDialog.tsx (new, shared)',
            'web/src/lib/library-delete-error.ts (new)',
            'web/src/pages/PronunciationPage.tsx',
            'web/src/pages/VoicesPage.tsx',
            'web/src/pages/DeliveryPage.tsx',
            'web/src/features/bookends/components/BookendGalleryPage.tsx',
            'web/src/features/bookends/components/BookendStudio.tsx',
            'web/src/features/voices/components/VoiceStudio.tsx',
            'web/src/features/bookends/components/LiveEditConfirmationDialog.tsx (deleted)',
            'web/src/features/delivery/components/LiveEditConfirmationDialog.tsx (deleted)',
        ],
        scenes: ['p5-pron-gallery', 'p5-delivery-gallery'],
        decide: `This is the half I am least sure you want. It touches eight files to close gaps nobody has
reported, and it is the only proposal here that is not fixing something I can show breaking. Say the
word and I will drop it entirely and leave the libraries as they are until the structural extraction
is on the table.`,
    },
]

const CAVEATS = [
    {
        title: 'The queue rail and the alarms still run on the 8-minute SLA',
        body: `Proposal 1 changes the cockpit and the board. The queue rail still labels that same bulletin
<code>+2:00</code> while the dock beside it now says <code>Slot missed</code>, which you can see in the
P1 screenshots. That inconsistency is deliberate and visible rather than hidden: finishing the change
means re-ordering the operator's work list and re-timing the audible alarm, which is a behavioural
change I did not want to bundle into a screenshot review.`,
    },
    {
        title: 'Six pre-existing test failures in the bookends fixture, not caused by these proposals',
        body: `<code>mock.test.ts</code> fails six assertions because <code>dev-data.json</code>'s
<code>Bookends</code> are still the old <code>Kind</code> + <code>Text</code> shape while the (already
staged) <code>bookend-schemas.ts</code> expects the paired
<code>IntroText</code>/<code>OutroText</code> shape. That is your in-flight Intro/Outro migration with
the fixture not yet migrated. I deliberately did not "fix" it, because pairing those five entries
decides what the seeded bookends are, which is your call not mine. Everything else passes.`,
    },
    {
        title: 'Determinism',
        body: `Both runs pin the clock to 2026-06-22T09:00+10:00 (a Monday, mid peak) and the timezone to
Australia/Sydney, matching <code>ux-audit.mjs</code>. Without the pin every pair differs by the
seconds between runs and the countdowns make the diff unreadable.`,
    },
    {
        title: 'Two numbers in my earlier critique were wrong, and I corrected them here',
        body: `I originally reported the stranding dead zone as 880 to 1079px including iPad landscape, and
the rundown truncation as mobile-only at 22 to 69%. Both came from a browser running at 125% zoom.
Re-measured headless at true CSS pixels: the dead zone is 768 to 971px (iPad <em>portrait</em>, not
landscape), and the truncation is worse on desktop (42%) than on mobile (21%).`,
    },
]

// ---------------------------------------------------------------------------

function loadPhase(phase) {
    const p = resolve(ROOT, phase, '_index.json')
    if (!existsSync(p)) return {}
    const idx = JSON.parse(readFileSync(p, 'utf8'))
    return Object.fromEntries(idx.scenes.map((s) => [s.id, s]))
}

const BEFORE = loadPhase('before')
const AFTER = loadPhase('after')

const esc = (s) =>
    String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')

/** Render one measured blob as a compact definition list. */
function measureTable(m) {
    if (!m || Object.keys(m).length === 0)
        return '<em class="muted">no measurement</em>'
    const rows = []
    for (const [k, v] of Object.entries(m)) {
        let val
        if (Array.isArray(v)) {
            val =
                v.length === 0
                    ? '<em class="muted">none</em>'
                    : `<ul>${v
                          .map(
                              (x) =>
                                  `<li>${esc(typeof x === 'object' ? JSON.stringify(x) : x)}</li>`
                          )
                          .join('')}</ul>`
        } else if (v && typeof v === 'object') {
            val = `<code>${esc(JSON.stringify(v))}</code>`
        } else {
            val = `<code>${esc(String(v))}</code>`
        }
        rows.push(`<tr><th>${esc(k)}</th><td>${val}</td></tr>`)
    }
    return `<table class="m">${rows.join('')}</table>`
}

function sceneBlock(id) {
    const b = BEFORE[id]
    const a = AFTER[id]
    if (!b && !a) {
        return `<div class="scene missing"><h4>${esc(id)}</h4><p class="muted">Not captured. This is a gap, not a choice.</p></div>`
    }
    const meta = a ?? b
    const both = Boolean(b && a)

    /**
     * Both phases: one stacked comparator. The before image is the base and
     * sets the intrinsic box; the after image overlays it and is revealed from
     * the handle rightwards. A real `<input type=range>` spans the whole box,
     * so pointer drag, touch and arrow keys all work without any of it being
     * hand-rolled, and the control is reachable and announced.
     */
    const comparator = both
        ? `<div class="cmp" style="--pos:50%">
      <img class="base" loading="lazy" src="before/${id}.png" alt="Before: ${esc(meta.label)}">
      <img class="over" loading="lazy" src="after/${id}.png" alt="After: ${esc(meta.label)}">
      <div class="split" aria-hidden="true"><span class="grip"></span></div>
      <span class="tag l" aria-hidden="true">Before</span>
      <span class="tag r" aria-hidden="true">After</span>
      <input class="swipe" type="range" min="0" max="100" step="0.5" value="50"
             aria-label="Reveal the after screenshot. Left is before, right is after.">
    </div>
    <p class="cmphint muted">Drag the handle, or focus it and use the arrow keys.
      <button type="button" class="mini" data-pos="0">All before</button>
      <button type="button" class="mini" data-pos="50">Split</button>
      <button type="button" class="mini" data-pos="100">All after</button>
    </p>`
        : `<figure class="single">
      <img loading="lazy" src="${b ? 'before' : 'after'}/${id}.png" alt="${esc(meta.label)}">
      <figcaption>${b ? 'Before' : 'After'} only. The other phase was not captured for this scene.</figcaption>
    </figure>`

    return `<div class="scene">
  <h4>${esc(meta.label)}</h4>
  <p class="note">${esc(meta.note ?? '')}</p>
  <p class="muted mono">${esc(meta.url)} &middot; ${esc(meta.viewport)}</p>
  ${comparator}
  <div class="pair measures">
    <div><h5>Measured before</h5>${measureTable(b?.measured)}</div>
    <div><h5>Measured after</h5>${measureTable(a?.measured)}</div>
  </div>
</div>`
}

const nav = PROPOSALS.map(
    (p) =>
        `<a href="#${p.id}"${p.declined ? ' class="off"' : ''}><span class="rank${p.declined ? ' off' : ''}">${esc(p.rank)}</span>${esc(p.title)}${p.declined ? '<em class="tagoff">declined</em>' : ''}</a>`
).join('')

const body = PROPOSALS.map(
    (p) => `<section id="${p.id}"${p.declined ? ' class="declined"' : ''}>
  <h2><span class="rank${p.declined ? ' off' : ''}">${esc(p.rank)}</span>${esc(p.title)}</h2>
  <p class="verdict">${esc(p.verdict)}</p>
  <h3>${p.declined ? 'The finding, for the record' : 'What is wrong now'}</h3>
  <div class="prose">${p.problem}</div>
  <h3>${p.declined ? 'What is in the tree' : 'What I changed'}</h3>
  <div class="prose">${p.change}</div>
  ${p.proof ? `<h3>Proof</h3><div class="prose">${p.proof}</div>` : ''}
  ${p.scenes.length ? `<h3>Before and after</h3>${p.scenes.map(sceneBlock).join('')}` : ''}
  ${
      p.files.length
          ? `<h3>Files touched</h3>
  <ul class="files">${p.files.map((f) => `<li><code>${esc(f)}</code></li>`).join('')}</ul>
  <h3>Drop this proposal</h3>
  <pre>git restore ${p.files
      .map((f) => f.replace(/ \((new|deleted|new, shared)\)$/, ''))
      .join(' \\\n  ')}</pre>
  <p class="muted">Paths marked <code>(new)</code> above are untracked, so <code>git restore</code>
  leaves them behind; delete those with <code>rm</code>. Paths marked <code>(deleted)</code> are
  restored by the same command.</p>`
          : ''
  }
  <h3>${p.declined ? 'If you change your mind' : 'For you to decide'}</h3>
  <div class="prose decide">${p.decide}</div>
</section>`
).join('')

const html = `<!doctype html>
<html lang="en-AU">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Arn proposals: before and after</title>
<style>
  :root {
    --bg: #0b0d12; --panel: #12151d; --panel2: #171b25; --line: #262c3a;
    --fg: #e7eaf0; --muted: #8f98ab; --accent: #a78bfa; --warn: #f0b429;
    --bad: #f2555a; --good: #34d399;
  }
  * { box-sizing: border-box; }
  body { margin: 0; background: var(--bg); color: var(--fg);
    font: 15px/1.65 ui-sans-serif, system-ui, -apple-system, "Segoe UI", sans-serif; }
  .wrap { display: grid; grid-template-columns: 300px minmax(0, 1fr); }
  nav { position: sticky; top: 0; align-self: start; height: 100vh; overflow: auto;
    border-right: 1px solid var(--line); padding: 28px 20px; background: var(--panel); }
  nav h1 { font-size: 16px; margin: 0 0 6px; }
  nav p { color: var(--muted); font-size: 13px; margin: 0 0 20px; }
  nav a { display: flex; gap: 10px; align-items: baseline; padding: 9px 10px; margin: 0 -10px;
    color: var(--fg); text-decoration: none; border-radius: 8px; font-size: 14px; }
  nav a:hover { background: var(--panel2); }
  main { padding: 40px 44px 120px; max-width: 1400px; }
  .rank { display: inline-flex; align-items: center; justify-content: center; min-width: 22px;
    height: 22px; padding: 0 6px; border-radius: 6px; background: var(--accent); color: #14101f;
    font: 600 12px/1 ui-monospace, monospace; flex: none; }
  h2 .rank { margin-right: 10px; vertical-align: 3px; }
  section { border-top: 1px solid var(--line); padding-top: 34px; margin-top: 46px; }
  section:first-of-type { border-top: 0; margin-top: 0; padding-top: 0; }
  h2 { font-size: 26px; margin: 0 0 6px; }
  h3 { font-size: 13px; text-transform: uppercase; letter-spacing: .09em; color: var(--muted);
    margin: 30px 0 10px; }
  h4 { font-size: 16px; margin: 0 0 4px; }
  h5 { font-size: 12px; text-transform: uppercase; letter-spacing: .08em; color: var(--muted);
    margin: 0 0 8px; }
  .verdict { color: var(--warn); margin: 0 0 4px; font-size: 14px; }
  .rank.off { background: var(--muted); color: #0b0d12; }
  nav a.off { color: var(--muted); }
  .tagoff { margin-left: auto; font: 600 10px/1 ui-monospace, monospace; text-transform: uppercase;
    letter-spacing: .07em; color: var(--muted); font-style: normal; }
  section.declined h2 { color: var(--muted); }
  section.declined .verdict { color: var(--muted); }
  .prose { max-width: 78ch; }
  .prose code, .files code, .mono { font: 13px/1.5 ui-monospace, SFMono-Regular, Menlo, monospace;
    background: var(--panel2); padding: 1px 5px; border-radius: 4px; }
  .files { list-style: none; padding: 0; margin: 0; display: flex; flex-wrap: wrap; gap: 6px; }
  .files li { margin: 0; }
  .decide { border-left: 3px solid var(--accent); padding-left: 14px; }
  .scene { background: var(--panel); border: 1px solid var(--line); border-radius: 12px;
    padding: 20px; margin: 0 0 18px; }
  .scene.missing { border-color: var(--bad); }
  .note { color: var(--fg); margin: 0 0 6px; max-width: 88ch; }
  .muted { color: var(--muted); font-size: 13px; }
  .pair { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-top: 14px; }
  figure { margin: 0; }
  figure img { width: 100%; height: auto; display: block; border: 1px solid var(--line);
    border-radius: 8px; background: #000; }
  figure.absent .ph { display: grid; place-items: center; height: 160px; color: var(--muted);
    border: 1px dashed var(--line); border-radius: 8px; font-size: 13px; }
  figcaption { color: var(--muted); font-size: 12px; margin-top: 6px; }
  .measures { align-items: start; }

  /* ── Swipe comparator ─────────────────────────────────────────────────── */
  .cmp { position: relative; margin-top: 14px; border: 1px solid var(--line);
    border-radius: 8px; overflow: hidden; background: #000; touch-action: pan-y;
    user-select: none; -webkit-user-select: none; }
  /* The before image is the base and sets the box height. */
  .cmp .base { display: block; width: 100%; height: auto; }
  /* The after image overlays it and is revealed from the handle rightwards. */
  .cmp .over { position: absolute; inset: 0; width: 100%; height: 100%;
    object-fit: fill; clip-path: inset(0 0 0 var(--pos)); }
  .cmp .split { position: absolute; top: 0; bottom: 0; left: var(--pos);
    width: 2px; margin-left: -1px; background: var(--accent);
    box-shadow: 0 0 0 1px rgba(0,0,0,.6); pointer-events: none; z-index: 2; }
  .cmp .grip { position: absolute; top: 50%; left: 50%; width: 30px; height: 30px;
    transform: translate(-50%, -50%); border-radius: 50%; background: var(--accent);
    box-shadow: 0 2px 10px rgba(0,0,0,.55); }
  /* Two chevrons, drawn rather than shipped as an asset. */
  .cmp .grip::before, .cmp .grip::after { content: ''; position: absolute; top: 50%;
    width: 7px; height: 7px; border-top: 2px solid #14101f; border-right: 2px solid #14101f; }
  .cmp .grip::before { left: 7px; transform: translateY(-50%) rotate(-135deg); }
  .cmp .grip::after { right: 7px; transform: translateY(-50%) rotate(45deg); }
  .cmp .tag { position: absolute; top: 10px; z-index: 2; padding: 3px 9px; border-radius: 999px;
    font: 600 11px/1.5 ui-monospace, monospace; letter-spacing: .05em;
    background: rgba(11,13,18,.82); border: 1px solid var(--line); color: var(--fg);
    pointer-events: none; }
  .cmp .tag.l { left: 10px; }
  .cmp .tag.r { right: 10px; }
  /* The real control: full-box, invisible, so drag/touch/keys are all native. */
  .cmp .swipe { position: absolute; inset: 0; width: 100%; height: 100%; margin: 0;
    appearance: none; -webkit-appearance: none; background: transparent;
    cursor: ew-resize; z-index: 3; }
  .cmp .swipe::-webkit-slider-thumb { -webkit-appearance: none; width: 1px; height: 100%; }
  .cmp .swipe::-moz-range-thumb { width: 1px; height: 100%; border: 0; background: transparent; }
  .cmp .swipe::-moz-range-track { background: transparent; }
  .cmp .swipe:focus-visible { outline: none; }
  .cmp:has(.swipe:focus-visible) { outline: 2px solid var(--accent); outline-offset: 2px; }
  .cmphint { margin: 8px 0 0; display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .mini { font: 500 12px/1 inherit; color: var(--fg); background: var(--panel2);
    border: 1px solid var(--line); border-radius: 6px; padding: 5px 9px; cursor: pointer; }
  .mini:hover { border-color: var(--accent); }
  figure.single img { border: 0; border-radius: 8px; }
  @media (prefers-reduced-motion: no-preference) {
    .cmp .split, .cmp .over { transition: none; }
  }
  kbd { font: 600 11px/1.4 ui-monospace, monospace; background: var(--panel2);
    border: 1px solid var(--line); border-bottom-width: 2px; border-radius: 4px;
    padding: 1px 5px; }
  table.m { width: 100%; border-collapse: collapse; font-size: 12.5px; }
  table.m th { text-align: left; color: var(--muted); font-weight: 500; vertical-align: top;
    padding: 4px 10px 4px 0; white-space: nowrap; width: 1%; }
  table.m td { padding: 4px 0; vertical-align: top; }
  table.m ul { margin: 0; padding-left: 16px; }
  table.m code { font-size: 12px; }
  pre { background: var(--panel2); border: 1px solid var(--line); border-radius: 8px;
    padding: 14px 16px; overflow: auto; font: 12.5px/1.6 ui-monospace, monospace; }
  .caveats { background: var(--panel); border: 1px solid var(--warn); border-radius: 12px;
    padding: 22px 24px; margin-top: 50px; }
  .caveats h2 { color: var(--warn); font-size: 20px; }
  .caveats h4 { margin-top: 20px; }
  .lede { background: var(--panel); border: 1px solid var(--line); border-radius: 12px;
    padding: 22px 24px; margin-bottom: 34px; }
  .lede p { margin: 0 0 10px; max-width: 90ch; }
  .lede p:last-child { margin-bottom: 0; }
  @media (max-width: 1000px) {
    .wrap { grid-template-columns: 1fr; }
    nav { position: static; height: auto; border-right: 0; border-bottom: 1px solid var(--line); }
    main { padding: 24px 18px 80px; }
    .pair { grid-template-columns: 1fr; }
  }
</style>
</head>
<body>
<div class="wrap">
<nav>
  <h1>Proposals: before and after</h1>
  <p>Nothing is committed. Each item reverts independently.</p>
  ${nav}
  <a href="#caveats"><span class="rank" style="background:var(--warn)">!</span>Caveats and corrections</a>
</nav>
<main>
  <div class="lede">
    <p><strong>How to read this.</strong> Every panel is a real screenshot of the app running against
    mock data, captured by <code>scripts/proposal-shots.mjs</code> with the clock pinned to
    2026-06-22T09:00+10:00 so the two runs are comparable. The numbers under each pair come from
    <code>getBoundingClientRect</code> and the live DOM, not from reading the image.</p>
    <p><strong>Every pair is a swipe.</strong> The two screenshots are stacked: drag the handle to wipe
    between them, or focus it and use the arrow keys. Left of the handle is before, right is after.
    The three buttons under each pair jump straight to all-before, an even split, or all-after.</p>
    <p><strong>Nothing is committed.</strong> The working tree holds all five proposals; each one lists
    the files it touches so you can drop any of them without disturbing the others.</p>
    <p><strong>Reproduce:</strong> <code>npm run dev:mock</code>, then
    <code>node scripts/proposal-shots.mjs after</code>, then <code>node scripts/proposal-doc.mjs</code>.</p>
  </div>
  ${body}
  <div class="caveats" id="caveats">
    <h2>Caveats and corrections</h2>
    ${CAVEATS.map((c) => `<h4>${esc(c.title)}</h4><div class="prose">${c.body}</div>`).join('')}
  </div>
</main>
</div>
<script>
// Comparator wiring. Vanilla and inline on purpose: this document is opened
// straight off disk, so it cannot depend on a bundler or a network fetch.
(function () {
  var clamp = function (n) { return Math.max(0, Math.min(100, n)) }

  function apply(cmp, value) {
    var v = clamp(parseFloat(value))
    cmp.style.setProperty('--pos', v + '%')
    var input = cmp.querySelector('.swipe')
    if (input && parseFloat(input.value) !== v) input.value = String(v)
  }

  document.querySelectorAll('.cmp').forEach(function (cmp) {
    var input = cmp.querySelector('.swipe')
    if (!input) return
    input.addEventListener('input', function () { apply(cmp, input.value) })
  })

  // The three jump buttons are the only non-drag affordance, deliberately.
  //
  // A double-click-to-recentre handler and hover-plus-b/a keys were both built
  // and then removed: the invisible range spans the whole box, so its own
  // pointerdown fires after the dblclick and overwrites the recentre (measured
  // 70% where 50% was intended), and the hover keys never armed when the
  // pointer was already inside the box, which is the common case after a drag.
  // Buttons are discoverable, keyboard-reachable, and actually work.
  document.querySelectorAll('.mini[data-pos]').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var scene = btn.closest('.scene')
      var cmp = scene && scene.querySelector('.cmp')
      if (cmp) apply(cmp, btn.dataset.pos)
    })
  })
})()
</script>
</body>
</html>
`

const out = resolve(ROOT, 'index.html')
writeFileSync(out, html)
const captured = new Set([...Object.keys(BEFORE), ...Object.keys(AFTER)])
const referenced = PROPOSALS.flatMap((p) => p.scenes)
const missing = referenced.filter((s) => !captured.has(s))
console.log(`wrote ${out}`)
console.log(
    `  ${PROPOSALS.length} proposals, ${referenced.length} scene refs, ${Object.keys(BEFORE).length} before / ${Object.keys(AFTER).length} after captures`
)
if (missing.length) console.log(`  MISSING captures: ${missing.join(', ')}`)
