---
title: "Client Deliverables"
description: "Separates ARN-facing handoff artifacts from internal canonical, working, reference, and generated documentation."
tags:
  - deliverables
  - client-facing
  - stage-gate-2
aliases:
  - "deliverables"
  - "client artifacts"
purpose: "reference"
status: "current"
last_verified: 2026-09-04
review_after: 2026-12-04
volatility: "medium"
source_of_truth: "docs/deliverables/"
verified_by: "manual review 2026-09-04: each listed deliverable file checked present; audience table reworded for ARN readers"
common_queries:
  - "Where are ARN-facing deliverables stored?"
  - "What is the difference between internal docs and client deliverables?"
  - "Which documents can be sent to ARN?"
see_also:
  - "docs/operations/solution-overview.md"
related_code: []
---

# Client Deliverables

This folder is for **ARN-facing deliverables**: documents and packaged artifacts intended to be sent to ARN for review, approval, or handover.

It is deliberately separate from the internal documentation areas:

| Area | Audience | Purpose |
|---|---|---|
| `docs/canonical/` | ARN platform team | Durable design records and decision history behind the platform. |
| `docs/reference/` | ARN platform team | Working inventories, stakeholder maps, task boards and detailed references. |
| `docs/_*` | Tooling | Generated indexes, search and portal data. |
| `docs/deliverables/` | ARN reviewers and approvers | Client-ready artifacts with internal paths, repo-specific references, and agent-facing language removed. |

## Rule

Anything in this folder should be safe to package and send externally after a final review pass. It should not depend on local file links, Markdown-only repo navigation, internal task-board phrasing, raw `[TBD]` markers, or generated docs portal behavior.

## Current deliverables

- SDD package: [`SDD/README.md`](SDD/README.md)
- SDD review PDF: [ARN Traffic Bulletin Platform - Solution Design Document - SG2 Review.pdf](SDD/ARN%20Traffic%20Bulletin%20Platform%20-%20Solution%20Design%20Document%20-%20SG2%20Review.pdf)
- Operations Handover Pack (signed cover): [ARN Traffic Bulletin Platform - Operations Handover Pack.pdf](Handover-Pack/ARN%20Traffic%20Bulletin%20Platform%20-%20Operations%20Handover%20Pack.pdf) — the signed cover for the handover; what acceptance means and what the signatories confirm lives at [acceptance](../operations/acceptance.md).
- Infrastructure Provisioning Brief: [IBP Infrastructure Provisioning Brief.pdf](IBP-Infra-Brief/IBP%20Infrastructure%20Provisioning%20Brief.pdf)
- Azure cost estimate: [`cost-estimate/arn-azure-cost-estimate.md`](cost-estimate/arn-azure-cost-estimate.md) (with the calculator export `cost-estimate/ExportedEstimate-ARN-full-solution.xlsx` and `cost-estimate/arn-azure-cost-estimate.csv`)
- Handover session deck: [`arn-handover-2026-09-04.html`](arn-handover-2026-09-04.html) and [ARN Handover - 4 September 2026.pdf](ARN%20Handover%20-%204%20September%202026.pdf)
