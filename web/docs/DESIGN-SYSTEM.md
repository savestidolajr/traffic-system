# Design system

Single source of truth for **icons, type, and color** in `web/`. The app has a
defined visual vocabulary; this doc is the guardrail that keeps it from drifting
back into per-file improvisation. New UI and any code you touch MUST draw from
here.

Two companions, both generated or enforced so they cannot drift:

- **`docs/UI-INVENTORY.md`** — every shared component, the props it requires,
  the variants it offers, how widely it is already used. This doc governs _how
  things look_; that one governs _what already exists_. Read it before writing a
  new component. Regenerate with `npm run ui:inventory`.
- **`.oxlintrc.json`** — the Tailwind conformance gate (`npm run lint:tw`, part
  of `npm run lint`). Deprecated classes, off-token arbitrary values, hardcoded
  colours and `dark:`-without-light are errors, not conventions.

## Colour: three roles, solved not chosen

Every semantic and status colour is used in three distinct ways, and on
2026-07-27 each one was measured and solved to the WCAG AA floor rather than
eyeballed. Do not hand-pick a new value; run the solve.

| Role           | Utility                                   | Constraint                                                    |
| -------------- | ----------------------------------------- | ------------------------------------------------------------- |
| **Fill**       | `bg-warning` + `text-warning-foreground`  | the foreground must clear 4.5:1 on the fill                   |
| **Chip text**  | `text-warning-on-tint` on `bg-warning/10` | must clear 4.5:1 on 10-20% tints of itself over every surface |
| **Plain text** | `text-destructive-on-tint`                | must clear 4.5:1 on all four surfaces                         |

Three rules follow, and all three are enforced:

1. **A fill value and a chip-text value cannot be the same token in dark.** They
   pull opposite ways: a fill has to stay dark enough for white, chip text has
   to out-run the tint. Measured, `--primary` in dark is 5.83:1 under white but
   2.76:1 as chip text. Hence `--<token>-on-tint`, which equals the base in
   light (where one value does serve both) and diverges in dark.
2. **A fill/foreground pair must be solved per theme.** `--warning-foreground`
   is white in light (dark amber fill) and near-black in dark (bright amber
   fill). White in both was 2.13:1.
3. **Never put opacity on a text colour.** The ramp is solved to the 4.5:1
   floor, so any alpha drops it below AA by construction — `text-warning/80`
   measured 4.38:1, `text-muted-foreground/60` 2.57:1. `.oxlintrc.json` makes
   this an error. Use the plain token, `text-muted-foreground-subtle`, or the
   `-on-tint` step.

Same rule for containers: `opacity-70` on a card composites every string inside
it toward the background. Use `saturate-50`/`saturate-0` and a badge instead —
that is what the paused and out-of-scope station cards now do.

### Writing a class name in this file used to ship it

Tailwind v4 auto-detects sources from the **project root**, and the `content`
array in the bridged `tailwind.config.js` does not narrow that. Its `.gitignore`
skip does not save us either: the ignore rules live in the repo-root
`.gitignore` as `web/audit/`, which a scanner rooted at `web/` never matches.

Measured 2026-07-27: `text-warning/80` was compiled into the production bundle
**because this document names it** — as a class it forbids. Worse,
`web/audit/data/*.html` are captured page snapshots, so the UX audit fed classes
back into the artefact it measures; the bundle grew every run, and a class
deleted from source stayed compiled for as long as a stale snapshot mentioned
it. 23 phantom selectors, ~3 kB.

`src/index.css` now opens with `@import 'tailwindcss' source(none)` and lists
real markup explicitly. **`source(none)` is load-bearing** — a bare `@source`
only widens the set. Quote class names freely here; they no longer ship.

## The rule, in one block

- **Status icons** (pipeline job status) → `STATUS_CONFIG` in
  `src/features/jobs/config/status-config.ts`.
- **Generic icons** (every other concept) → `ICON` in `src/lib/design/icons.ts`.
  Never import a lucide glyph directly for a concept `ICON` already names.
- **Color** → semantic tokens only (`primary`, `success`, `warning`,
  `destructive`, `info`, `muted`, `accent`, the `status-*` ramp, `*-foreground`
  pairs). Raw Tailwind palette (`sky-*`, `amber-*`, `slate-*`, …) is banned —
  the **only** exception is `WEATHER_TINT` in `icons.ts`.
- **Type** → `text-xs` and up, plus the two micro tokens `text-2xs` / `text-3xs`.
  Never `text-[…rem]` / `text-[…px]` (two documented one-offs excepted, below).

---

## Icons — one glyph per concept

Imported once each in `src/lib/design/icons.ts` and exposed as the `ICON`
registry. Sizes come from `ICON_SIZE`.

| key                   | glyph                       | concept                                                 |
| --------------------- | --------------------------- | ------------------------------------------------------- |
| `edit`                | `Pencil`                    | edit entity                                             |
| `duplicate`           | `Copy`                      | duplicate                                               |
| `delete`              | `Trash2`                    | destroy                                                 |
| `more`                | `MoreHorizontal`            | card/row kebab                                          |
| `add`                 | `Plus`                      | create/add (`UserPlus` allowed only for "add operator") |
| `approve`             | `ThumbsUp`                  | approve action (button)                                 |
| `reject`              | `XCircle`                   | reject action                                           |
| `rejectToFallback`    | `CornerDownRight`           | reject → fallback                                       |
| `regenerate`          | `RefreshCw`                 | regenerate                                              |
| `revise`              | `Wand2`                     | open revise                                             |
| `reset`               | `RotateCcw`                 | reset / retry / undo                                    |
| `close`               | `X`                         | dismiss                                                 |
| `search`              | `Search`                    | search (`noResults` → `SearchX`)                        |
| `expand` / `collapse` | `ChevronDown` / `ChevronUp` | disclosure                                              |
| `forward`             | `ArrowRight`                | nav / CTA                                               |
| `send`                | `ArrowUp`                   | submit composer                                         |
| `download`            | `Download`                  | export                                                  |
| `settings`            | `Settings2`                 | configure                                               |
| `play` / `pause`      | `Play` / `Pause`            | audio transport                                         |
| `success`             | `CircleCheck`               | verified / sourced / positive                           |
| `warning`             | `AlertTriangle`             | caution / failure / unverified                          |
| `info`                | `AlertCircle`               | message-level alert (error boundary)                    |
| `traffic`             | `Route`                     | roads / corridors / traffic content                     |
| `weather`             | `CloudSun`                  | weather source                                          |
| `fuel`                | `Fuel`                      | fuel source                                             |
| `incident`            | `AlertTriangle`             | incident marker                                         |
| `broadcast`           | `RadioTower`                | station / on-air / published                            |
| `history`             | `History`                   | version/iteration history                               |
| `market`              | `MapPin`                    | geographic market                                       |
| `org`                 | `Building2`                 | org / market access                                     |
| `voice`               | `Mic2`                      | voice identity                                          |
| `audio`               | `Volume2`                   | audio output                                            |
| `airSlot`             | `RadioTower`                | vitals: air slot                                        |
| `duration`            | `Timer`                     | vitals: duration                                        |
| `coverage`            | `ListChecks`                | vitals: coverage                                        |
| `freshness`           | `Activity`                  | vitals: traffic freshness                               |
| `urgent`              | `Zap`                       | priority / urgent                                       |
| `speed`               | `Gauge`                     | speed / calibration                                     |
| `ai`                  | `Sparkles`                  | AI / creative                                           |
| `safety`              | `ShieldCheck`               | safety / admin / robust                                 |
| `help`                | `HelpCircle`                | explainer                                               |
| `tune`                | `SlidersHorizontal`         | tune / adjust                                           |
| `time`                | `Clock`                     | time / deadline                                         |
| `feedback`            | `MessageSquareText`         | feedback / not-mentioned                                |

### Deliberate distinctions — DO NOT collapse

- `RadioTower` (station / broadcast / on-air) **vs** `Radio` (the HERE-fetched
  traffic _data signal_ pipeline stage — lives in
  `STATUS_CONFIG.HERE_FETCHED`, not `ICON`).
- `AlertTriangle` (warning / failure) **vs** `XCircle` (human rejection) **vs**
  `AlertCircle` (error-boundary, message-level alert).
- `Mic2` (voice identity) **vs** `Volume2` (audio output) **vs** `AudioLines`
  (synthesizing — lives in `STATUS_CONFIG.SYNTHESIZING_AUDIO`).

### Resolved alias collapses (same glyph, two lucide names)

These were duplicates and have been unified; do not reintroduce the right side:

- `CheckCircle2` → `CircleCheck`
- `TriangleAlert` → `AlertTriangle`
- `XOctagon` → `AlertTriangle`
- `Car` → `Route`

## Icon sizes — `ICON_SIZE`

| token    | class             | pairs with              |
| -------- | ----------------- | ----------------------- |
| `micro`  | `size-3` (12px)   | `text-2xs` / `text-3xs` |
| `inline` | `size-3.5` (14px) | `text-xs` / `text-sm`   |
| `action` | `size-4` (16px)   | buttons, standalone     |
| `badge`  | `size-5` (20px)   | dialog / header badges  |

Status dots are **not** icons — they stay `size-1.5` / `size-2`.

---

## Type scale

The only allowed sizes (Tailwind defaults + two micro tokens added in
`tailwind.config.js`):

| class                                            | size             | use                                 |
| ------------------------------------------------ | ---------------- | ----------------------------------- |
| `text-3xs`                                       | 0.625rem / 10px  | kbd, mono counters, timeline deltas |
| `text-2xs`                                       | 0.6875rem / 11px | dense metadata / captions           |
| `text-xs`                                        | 12px             | small labels, chips                 |
| `text-sm`                                        | 14px             | **body default**                    |
| `text-base`                                      | 16px             | emphasized body                     |
| `text-lg`                                        | 18px             | sub-headings                        |
| `text-xl` / `text-2xl` / `text-4xl` / `text-5xl` | —                | headings / hero                     |

Rules:

- **No arbitrary sizes.** `text-[…rem]` / `text-[…px]` is banned.
- **Numeric / time / countdown values** use `font-mono tabular-nums`.
- **Headings** use `font-sans` weights. `font-display` is reserved for the
  home hero only.
- The dead named scale (`text-display`, `text-h1..h4`, `text-body`,
  `text-body-lg`, `text-caption`, `text-overline`, `text-mono`) was removed —
  it was the "system nobody follows" anti-pattern (zero usages). `fontFamily`
  (`sans` / `display` / `mono`) is unchanged.

### Sanctioned one-off arbitraries (the only two)

Each carries an inline `/* one-off: … */` comment:

- `ReviewScript.tsx` — `text-[1.0625rem]` teleprompter reading size.
- `ScriptRenderer.tsx` — `text-[0.8em]` direction-cue, relative to the
  teleprompter size on purpose.

---

## Color — semantic tokens only

Allowed: the HSL design tokens (`primary`, `success`, `warning`, `destructive`,
`info`, `muted`, `accent`), the `status-*` ramp, and their `*-foreground`
pairs. Raw Tailwind palette and `text-white` / `text-black` are banned.

### Never use opacity to dim body text

`text-muted-foreground/70`, `/80` (and lower) on text **cannot** meet WCAG AA
in light mode — an alpha over a light background lets through enough white that
the ratio can't reach 4.5:1 no matter how dark the token. Use the **solid**
`text-muted-foreground` for any readable text; reserve `/30`–`/60` opacity for
non-text (dividers, faint hairlines, decorative glyphs). The light
`--muted-foreground` / `--secondary-foreground` tokens are tuned dark enough
(`220 10% 38%`) to clear AA on both the white cards and the page background.

### Sanctioned exceptions (the only two)

- **`WEATHER_TINT`** (`src/lib/design/icons.ts`) — the single place raw palette
  is allowed, because weather color is domain-semantic:

    | tone    | class             |
    | ------- | ----------------- |
    | `sun`   | `text-amber-300`  |
    | `cloud` | `text-slate-300`  |
    | `rain`  | `text-sky-300`    |
    | `storm` | `text-violet-300` |
    | `fog`   | `text-slate-400`  |
    | `snow`  | `text-cyan-200`   |

- **`AppSidebar.tsx`** waiting badge — `text-black` on `bg-status-waiting`
  (amber). `warning-foreground` is white and would fail contrast on amber, so
  black is the documented choice.

---

## Spacing — one modal inset, two rhythm steps

Measured 2026-07-29 on the station studio's Voice step, which had drifted to
**four** horizontal insets stacked in one column (title bar 16px, rail 12px,
canvas 24px, error bar and footer 20px) and a content rhythm that alternated
2 / 7 / 8 / 12 / 16px. Nothing was individually wrong; the column just never
shared an edge. The rules that came out of the fix:

- **One inset for the studio chrome: `px-5` / `p-5` (20px).** `StudioDialog`'s
  title bar, `StudioCanvas`, `StudioErrorSummary` and `StudioFooter` all use it,
  so the title, the "Fix issues in:" row and the footer controls share a left
  edge (the rail's `p-3` + button `px-2.5` lands its icon on the same 20px, ±1px
  — verify with `getBoundingClientRect().left`, not by eye). Do not reintroduce
  a per-row padding.
- **Two steps inside a step: `space-y-2` / `gap-2` (8px) within a group,
  `space-y-4` / `gap-4` (16px) between groups**, with `StudioCanvas`'s `gap-6`
  (24px) separating whole step sections. Nothing in between: no `gap-1.5` (6px)
  field wrapper, no 12px middle step, no `pt-1` one-offs. A list's "add another"
  control is a peer of the list, so it gets the 16px step — not 8px, which reads
  as one more row.
- **An explanation belongs in a tooltip, not under the control.** Studio forms
  had drifted to a `text-sm text-muted-foreground` paragraph under every field;
  three or four of them push the content the operator came to edit below the
  fold (measured 2026-08-06: the pronunciation studio's rule table started
  ~200px into a 651px canvas, and the canvas scrolled 1095px). Use
  `FieldLabel` (`src/components/FieldLabel.tsx`), which pairs the `Label` with an
  `InfoHint` help-circle carrying the explanation — `hint` requires `hintLabel`
  (the trigger's accessible name), enforced by a TS union. `SectionHeader` and
  `EntitySelectorField` take the same `hint` / `hintLabel` pair. A `<p>` under a
  control survives only for a fact that must be read without hovering (the
  time-of-day voice fallback note); when it does, it is `text-sm
text-muted-foreground` matching `FormDescription`/`FormItem`, and a bespoke
  section hosting a widget instead of an input (the voice timeline) follows the
  same order and size — a `text-xs` hint above one widget beside a `text-sm` one
  below the next is the exact drift that rule closes.
- **Copy that restates its label is deleted, not tooltipped.** "Dictionary name"
    - "Use a name operators can recognise quickly" carries nothing; neither does a
      description that repeats the selected option. Only a real fact (precedence, a
      model limitation, where a value surfaces, an irreversible consequence) earns a
      `hint`.
- **One "this is live" banner: `LiveAssignmentNotice`**
  (`src/components/LiveAssignmentNotice.tsx`). Voices, delivery styles, bookends
  and pronunciation dictionaries had three phrasings of the same warning, and the
  voice studio's 12-station list wrapped to three lines. Past three stations the
  list collapses to a count with the names in a tooltip; the names are
  load-bearing, so they are never dropped.
- **Controls sharing a row share a height.** A row mixing a 40px `Input`/
  `SelectTrigger` with a 36px `size="sm"` button is the single most repeated
  spacing defect found: it hit all five gallery toolbars (search + `ViewToggle`),
  the history filter rail (selects + Clear + Export), the review action cluster,
  and the voice-window rows. Pick the row's height and make every control in it
  match; the only sanctioned exception is a deliberately larger primary (the
  36px `rounded-full` play button among 32px transport controls).

### Three traps that measure differently than they read

- **`SpotlightCard`'s `className` does not lay out its children.** It lands on
  the outer element, whose only in-flow child is an inner `relative h-full`
  wrapper, so a `flex flex-col gap-*` there spaces one item and the gap renders
  as **zero**. Twelve callsites had declared an inert gap this way (home ops
  cards at 0px against a declared 10px). Padding, borders, background and
  `h-full` on the card are fine; children needing layout get their own wrapper
  div. Same shape for any wrapper component that nests `children`.
- **An inline element in a `space-y-*` stack eats the gap.** Tailwind v4's
  `space-y-*` puts `margin-bottom` on every child but the last; an inline box is
  the font em box (16.7px for 14px `leading-none` text) rather than the 14px line
  box, so it overhangs its own margin. A Radix `Label` is `display:inline`, which
  made `FormItem`'s declared 8px measure **3px** to the control — while the same
  label wrapped in a flex div (`EntitySelectorField`) measured a true 8px. One
  relationship, two rendered gaps. `FormLabel` now carries `block`; keep section
  labels block-level too (`<p className="text-sm font-medium leading-none">`).
- **`space-y-*` on a `Tabs` root loses to `TabsContent`.** Both emit a margin;
  `TabsContent`'s own `mt-2` won, so a declared `space-y-4` silently rendered 8px
  between the tab bar and its panel. Follow `GuidancePage.tsx`: no spacing
  utility on the `Tabs` root, an explicit `mt-4` on each `TabsContent`.

### Cards in a grid

Entity cards are `gap-4 p-5` inside `CARD_GRID` (`lib/layout.ts`) and MUST carry
`h-full` on both the click wrapper and the `SpotlightCard`, or the shortest card
in a row leaves a ragged bottom edge (measured 13px on `/operators`). A trailing
CTA needs `mt-auto` on a `h-full` flex column, or it strands itself mid-card when
that card's content is short (measured 80px of drift between the three home ops
cards).

`mt-auto` goes on the **bottom cluster, not the last element**. It collects all
of a card's slack into the one gap directly above whatever carries it, so
putting it on the action button opens a hole between the footer divider and its
own button. On the station card that gap measured 44px on some cards and 16px on
others; moving `mt-auto` up onto the `border-t` footer row glues divider +
timestamp + action together and lets the slack open above the divider, where a
card is supposed to breathe.

Which means **a card fed by `mt-auto` must not contain a wrap-if-it-fits row.**
A `flex-wrap justify-between` meta row is 1 line on short content and 2 on long,
and that content variance is exactly what `mt-auto` amplifies into a visible
hole — the station card's stats block measured 19px and 47px across 13 cards for
no reason but the length of a cron string. Stack such rows deterministically
(`flex flex-col gap-1.5`) and `truncate` anything unbounded inside them, so
every card in the gallery has identical internal geometry.

Right-edge alignment between a scrolling `StudioCanvas` and the non-scrolling
footer is off by the platform scrollbar width and is **not** worth chasing;
equal insets are the guarantee, not equal absolute edges.

---

## Visual primitives

Small, presentational micro-viz that replace dense text with a glance. They
live in `src/lib/design/` (one component per file, alongside `icons.ts`) and
consume the tokens above. Reach for these before hand-rolling a bar/dot.

### Built

- **`BudgetBar({ measuredSec, targetSec })`** (`src/lib/design/BudgetBar.tsx`) —
  fill vs a target tick; over → `bg-destructive`, on → `bg-success`, under →
  `bg-warning`. Exact figures live in its `title`. Used in `ReviewVitals`.
- **`FreshnessDot({ ageMs })`** (`src/lib/design/FreshnessDot.tsx`) — `<5m`
  `bg-success` / `5–15m` `bg-warning` / `>15m` `bg-destructive`. Pairs with the
  "traffic Xm ago" label in `ReviewVitals`.
- **`SourceGlyphs({ traffic, weather, fuel })`**
  (`src/lib/design/SourceGlyphs.tsx`) — `ICON.traffic/weather/fuel` at
  `ICON_SIZE.micro`, lit `text-foreground` / dim `text-muted-foreground/30`.

### Specified, not yet built

- **`StatusDot({ verdict })`** — reuse `GUTTER_META` tokens from
  `ScriptRenderer.tsx`: `sourced` → `bg-success/60`, `check` → `bg-destructive`,
  `unverified` → `bg-warning`, `branding` → `bg-muted-foreground/30`.
- **`VerdictBar({ counts })`** — stacked success / warning / destructive
  segments from the same tokens.

### Loading states — one shimmer, one rhythm

Every data-bound surface shows the **same shimmer** (`animate-shimmer`) while it
loads — never a bare spinner for full-content waits, never nothing. Compose the
shared placeholders; do not hand-roll bespoke skeleton blocks per page.

- **`Skeleton`** (`src/components/ui/skeleton.tsx`) — the shimmer primitive.
- **`CardGridSkeleton({ count })`** (`src/components/LoadingSkeleton.tsx`) —
  entity-card pages (Stations, Voices, Operators); mirrors `CARD_GRID` so the
  placeholder occupies the loaded footprint.
- **`ListSkeleton({ rows })`** (`src/components/LoadingSkeleton.tsx`) — bordered
  list containers (`ScopeGroupedList`, the broadcast log); mirrors the header +
  row rhythm.
- **`Spinner`** (`src/components/ui/spinner.tsx`) — inline / in-button pending
  state only (a submitting action), never a full-content load.

Tabbed hubs prefetch **every** tab on open (`forceMount` +
`data-[state=inactive]:hidden` on each `TabsContent`) so the queries fire in
parallel and switching is instant — see `GuidancePage`.

---

## Durable frontend contracts

Conventions learned the hard way. Each is enforced in code where possible;
where it is not, this section is the enforcement.

- **Accessible names are type-enforced, not reviewed.** `SelectTrigger` and
  `Slider` require `aria-label` or `aria-labelledby` in their props types —
  `role="combobox"` takes no name from its contents, and unnamed controls
  shipped silently before the requirement existed.
- **Tooltip tests hover; mouseEnter never opens one.** Radix Tooltip listens
  on pointer events (`fireEvent.mouseEnter` does nothing) and stays
  suppressed after a click on its trigger — tests use `userEvent.hover`,
  and hover BEFORE clicking.
- **Script marks merge once, in one place.** `buildScriptMarks`
  (`lib/script-marks.ts`) folds dictionary substitutions, pre-review suspect
  words, and unconfirmed figures into ONE non-overlapping `ScriptMark[]` —
  a word carrying both a rule and a flag is one highlight, one popover.
  The pre-review badge counts from those same marks so it cannot disagree
  with the script; a flagged word the edited script no longer contains is
  named explicitly, never dropped.
- **Actions get a popover; nothing to do gets a tooltip.** A popover with no
  action inside reads as broken — an unconfirmed figure or a read-only view
  states its explanation in a plain tooltip instead.
- **`SelectItem` explainers stay out of the trigger.** Per-option
  descriptions render in the `description` slot, outside
  `SelectPrimitive.ItemText` — Radix clones `ItemText` into the trigger, so
  anything inside it spills out of the fixed-height control.
- **Tables sort off one engine.** `lib/table-sort.ts` owns the comparator,
  the tri-state cycle (first direction → flipped → source order), and the
  two shared decisions: one `Intl.Collator({ numeric, sensitivity: 'base' })`
  and empty values sorting last in BOTH directions. Tables declare only
  per-column accessors and first directions; the choice persists per table
  under `arn:table-sort:<key>`.
- **`RuleToolbar` order is search → filters → Add, pinned right.** The Add
  button reads `Add` on every tab and takes a `what` prop composed into its
  `aria-label` — never a per-tab label, which resized the control and shoved
  the row on every tab switch. Composers open from the toolbar; they are
  never permanently mounted above the list.
- **The review map is curation-first.** STYLE encodes the bulletin decision
  (solid magnitude-coloured in-bulletin vs faint dashed neutral available);
  COLOUR encodes magnitude. Incidents draw as polylines from the geometry
  snapshotted on the job, never as pins.
- **Review copy vocabulary is fixed.** In/out of the bulletin is **"in
  bulletin" / "available"**; an audio-duration delta is **"short" /
  "long"** — never under/over, which collides with airtime-deadline
  over/overdue.
- **Table scroll wrappers are `overflow-x-auto`, never `overflow-auto`.**
  The global `scrollbar-gutter: stable` rule then cannot reserve a dead
  15px vertical gutter on a table that only ever scrolls sideways.
- **Composer defaults derive; they do not mirror.** A pre-selected scope is
  a derived default plus a nullable override, reset when the composer
  opens — never `useEffect` state mirroring state.
- **Suppression keeps its evidence.** A resolved flag collapses behind a
  count disclosure in the same surface (`N words verified` with an undo
  path) — it is never removed from the DOM, so a wrong verdict stays
  noticeable and reversible.

## Future hardening (deferred)

- **Import-path uniformity**: the registry is enforced for new and touched code,
  not yet across all callsites. A hard guarantee would be an ESLint
  `no-restricted-imports` rule on `lucide-react` outside `icons.ts` plus a full
  callsite migration.
- **Weather tokens**: to reach zero raw palette, add `--weather-*` HSL tokens to
  `index.css` and map `WEATHER_TINT` to them.
