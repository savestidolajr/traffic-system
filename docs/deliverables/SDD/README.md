---
title: "SDD Deliverable Package"
description: "Isolated ARN-facing Solution Design Document package for Stage Gate 2 review and sign-off."
tags:
  - deliverables
  - solution-design
  - stage-gate-2
  - client-facing
aliases:
  - "SDD package"
  - "ARN SG2 package"
  - "client SDD"
purpose: "reference"
status: "stale"
last_verified: 2026-07-24
review_after: 2026-08-07
volatility: "high"
source_of_truth: "docs/deliverables/SDD/ARN-Traffic-Bulletin-Platform-Solution-Design-Document.typ"
verified_by: "typst compile; python3 scripts/docs.py check; re-verified 2026-07-24 (point-in-time SG2 deliverable, unchanged)"
common_queries:
  - "Where is the ARN-facing SDD package?"
  - "Which file should be sent to ARN for SG2 review?"
  - "What assets are included in the SDD deliverable package?"
see_also:
  - "docs/canonical/architecture.md"
related_code: []
---

# SDD Deliverable Package

This folder contains the isolated ARN-facing Solution Design Document package for Stage Gate 2 review.

## Send to ARN

Primary review artifact (opens in a new tab from the docs portal):

- [ARN Traffic Bulletin Platform - Solution Design Document - SG2 Review.pdf](ARN%20Traffic%20Bulletin%20Platform%20-%20Solution%20Design%20Document%20-%20SG2%20Review.pdf)

The PDF is self-contained. It is Arinco-branded and includes a revision and sign-off sheet, the review guide, SDD, decision appendices, open dependencies, and embedded diagrams.

## Source files

- `ARN-Traffic-Bulletin-Platform-Solution-Design-Document.typ` - Typst source used to generate the PDF.
- `fonts/Montserrat-Regular.ttf`, `fonts/Montserrat-Bold.ttf` - the Arinco brand font (SIL OFL). Required at compile via `--font-path fonts`.
- `brand/arinco-logo.png`, `brand/arinco-cover.png` - Arinco logo and cover graphic.
- `assets/platform-architecture.png` - embedded architecture diagram (LikeC4 `platform-architecture` view).
- `assets/bulletin-lifecycle.png` - embedded bulletin lifecycle diagram (LikeC4 `bulletin-lifecycle` view).
- `assets/cicd-pipeline.png` - embedded CI/CD diagram (LikeC4 `cicd-pipeline` view).

## Content boundary

The package is written for ARN reviewers. It intentionally excludes internal repo paths, docs portal links, agent-facing notes, raw task-board phrasing, and unresolved internal implementation breadcrumbs.

Weather and fuel are treated as candidate HERE-backed supplementary sources unless ARN confirms Release 1 scope. Ads, Aquira, and live-read sponsorship remain an open ARN business and sales-process question. Zetta delivery is treated as core scope, with PoC/test SMB mechanics known and production/test environment deltas still to confirm.

## Regenerate

Diagrams are LikeC4 views in `architecture/`. The SDD figures use a clean export (no legend or title banner) so they sit cleanly under the Typst figure captions. The live `./scripts/export-diagrams.sh` set is for the docs portal and standalone sharing and bakes in a legend and banner, so do not copy from `architecture/images/` here. From the repo root:

```bash
( cd architecture && npx likec4 export png --theme dark \
    -f platform-architecture -f bulletin-lifecycle -f cicd-pipeline \
    -o /tmp/sdd-figs . )
cp /tmp/sdd-figs/platform-architecture.png /tmp/sdd-figs/bulletin-lifecycle.png /tmp/sdd-figs/cicd-pipeline.png docs/deliverables/SDD/assets/
```

Then rebuild the PDF from this folder:

```bash
typst compile --font-path fonts "ARN-Traffic-Bulletin-Platform-Solution-Design-Document.typ" "ARN Traffic Bulletin Platform - Solution Design Document - SG2 Review.pdf"
```
