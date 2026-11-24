# Diagram Framework — two tools, one source of truth

This project uses **two diagramming pipelines deliberately**:

1. **LikeC4** (`architecture/*.c4`) — the **model**: elements, relationships,
   deployment hierarchy. Interactive dev server, MCP queries, auto-layout
   exports for exploration and sequence views.
2. **Manual orthogonal renders** (this directory) — the **presentation
   layer**: per-view YAML specs with exact coordinates, rendered to SVG/PNG by
   `render.py`, validated against the LikeC4 model export on every build.

LikeC4's auto-layout (like every engine we evaluated: ELK, dagre, D2) cannot
produce orthogonal routing with intentional placement, and bezier spaghetti
does not scale with diagram complexity. Manual coordinates fix that; model
validation stops the hand-drawn diagrams from rotting.

## Decision rule — which pipeline for which diagram

| Use case | Pipeline |
|---|---|
| Interactive exploration, navigation, querying (dev server, MCP) | LikeC4 views |
| Dynamic/sequence views (e.g. `bulletin-lifecycle`) | LikeC4 views |
| Quick context views where layout quality is secondary | LikeC4 views |
| Presentation-grade diagrams (SDD, client decks, training, handover) | Manual render |

## Diagram registry

Single place to see what exists, where it comes from, and who consumes it.
**Update this table when adding or retiring a diagram.**

| Diagram | Pipeline | Class | Consumers |
|---|---|---|---|
| `system-context` | **manual render** | model-backed | SDD context, business owners |
| `platform-architecture` | **manual render** | model-backed | SDD Figure 1 (target architecture) |
| `bulletin-lifecycle` | LikeC4 auto (dynamic) | model | SDD Figure 2 (sequence) |
| `security-overview` | **manual render** | model-backed | SDD security section, security review (ARN security reviewer) |
| `cicd-pipeline` | **manual render** | model-backed | SDD Figure 3 (deployment model) |
| `network-topology` | **manual render** | model-backed | SDD environment topology, infra discussions (ARN infrastructure team) |
| `data-plane` | LikeC4 auto (deployment) | model | engineering exploration |
| `delivery-path` | LikeC4 auto (deployment) | model | engineering exploration |
| `bulletin-state-machine` | **manual render** | **standalone** (code-grounded) | SDD operator workflow, UAT training, handover |
| `per-station-configuration` | **manual render** | **standalone** (code-grounded) | SDD §15 / per-station config reference, operator training, handover |

Planned:
- *environments & promotion topology* — naming/subscription blocker resolved
  (IBP naming confirmed 11 Jun, dev deployed). Deferred until test/prod are
  actually provisioned (only dev is live; test/prod blocked on ARN). The
  promotion flow itself is already covered by `cicd-pipeline`.
- *scheduling window timeline* — if Decision Appendix A ratification needs it

## Two classes of manual render

### Model-backed (default)

Node/container/edge ids are LikeC4 FQNs. `render.py` **fails the export**
(exit 2) when a spec references an FQN absent from the model export, an edge
with no corresponding model relationship (element-or-descendant matching), or
a missing icon. **Rule: every model-backed render must have a companion
LikeC4 view covering the same scope** — the model stays explorable; the
render is just a better picture of the same facts.

### Standalone (code-grounded)

For diagrams whose facts live in **code or policy, not the architecture
model** (state machines, timelines). Nodes are literal (`label` instead of
`id`); edges omit `from`/`to`. There is **no automated drift gate**, so the
spec MUST:

1. Declare its source of truth in the header comment (file + symbol, e.g.
   `JobStatus` in `Domain/Models/Job.cs`), and
2. Be updated in the same change that alters that source.

Prefer model-backed wherever the facts exist in the model; standalone is the
exception, not the norm.

## How it works

```
*.c4 ──(npx likec4 export json)──> model.json ──┐
                                                 ├──> render.py ──> images/<view>.svg ──> .png
renders/views/<view>.yaml (coordinates, edges) ──┘
```

`scripts/export-diagrams.sh` runs both pipelines. Manual renders overwrite
the auto-layout PNG of the same name (rasterised at 2x via `rsvg-convert`),
so document references (`architecture/images/<view>.png`) automatically get
the better diagram.

## Adding a view

1. Model-backed? Make sure the scope exists as a LikeC4 view first
   (`views.c4`). Standalone? Identify and cite the code source of truth.
2. Copy the closest existing spec in `views/`.
3. Plan the layout so primary flows are straight lines: give connected nodes
   a shared row or column; route the rest with single-L bends; avoid
   crossings. Record the invariants in a comment at the top of the spec.
4. Check label clearances: never route an edge through a node's below-icon
   label block; exit side edges instead, or use box nodes (labels inside).
5. Run `./scripts/export-diagrams.sh` — fix drift errors (fatal) and review
   layout-lint warnings (`⚠ text crossed by edge`, `edge passes through
   node`, `texts overlap` — advisory, char-width estimates, so confirm
   visually before chasing 1-px findings).
6. Add the diagram to the registry table above.

## Spec format (YAML)

- `canvas: [w, h]`, `output: <name>`
- `containers:` — `rect: [x, y, w, h]`, `style` (named), `labels` (text items;
  `text` defaults to the model title). `id` (FQN) optional: omit for
  standalone group boxes.
- `nodes:` — `at: [cx, cy]` plus either:
  - `icon: <stem>` — 64px icon from `icons/`, `label`/`sublabel` below
    (`label_pos: right` to move them), or
  - `box: [w, h]` — rounded box with the label inside; optional `fill`,
    `stroke`, `sw`, `size`.
  `id` (FQN) optional: with it, labels default from the model and existence
  is validated; without it, `label` is required.
- `edges:` — `points` (orthogonal polyline), `class`. `from`/`to` (FQNs)
  optional: present → relationship validated; absent → standalone arrow.
  Optional `label: {text, at, anchor, size}`.
- `styles:` — extend/override container styles and edge classes per spec;
  edge classes support `color`, `width`, `dash`.
- `texts:`, `legend:` — free annotations and the colour key (legend honours
  `dash`).

Rendering niceties handled by the engine: orthogonal edges get rounded
corners at bends (radius 10), and every label is drawn with a white halo
(`paint-order: stroke`) so text stays readable when near lines or on
coloured container fills.

## Icons

`icons/` mixes two sources, intentionally distinct in style:

- **Official Azure Architecture Icons** (V23, `aka.ms/architecture-icons`) for
  Azure services: `function-app`, `storage`, `keyvault`, `ai`, `app-insights`,
  `dashboard`, `logic-app`, `person`. Microsoft permits their use in
  architecture diagrams and documentation; don't crop, flip, rotate, or
  recolour them.
- **Custom glyphs** (48x48, dark gradient backplate) for non-Azure parties:
  `entra` (mimics the official mark), `teams`, `here`, `eleven`, `zetta`.
  The style contrast usefully separates Azure services from external systems.

Add new ones as plain SVG; reference by file stem from specs.