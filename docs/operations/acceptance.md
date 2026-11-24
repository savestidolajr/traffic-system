---
title: "Handover Acceptance"
description: "What accepting the handover means, what the signatories confirm and where the signature record lives."
tags:
  - "handover"
  - "governance"
  - "acceptance"
aliases:
  - "acceptance"
  - "sign off"
  - "what am I signing"
  - "handover acceptance"
purpose: "reference"
status: "current"
last_verified: 2026-09-01
review_after: 2026-11-27
volatility: "medium"
source_of_truth: "ARN's controlled operational records"
verified_by:
  - "manual review"
common_queries:
  - "What does accepting the handover mean?"
  - "Who signs the handover?"
  - "What are the acceptance conditions?"
see_also:
  - "docs/operations/open-items.md"
  - "docs/operations/support-model.md"
  - "docs/operations/as-built.md"
  - "docs/operations/runbook.md"
---

# Handover Acceptance

Acceptance records ARN's confirmation that the operational handover has been received and can be owned after the engagement. The current deliverable is the single-model platform and its documented three-environment operating model: centralized Azure OpenAI script generation, deterministic post-workflow slot fitting and safety gates, multi-source traffic diagnostics, Phase 1 console-only emergency data, and one armed playout environment per station. It is not a declaration that every future change has been delivered and it is not acceptance of a managed service.
## Acceptance conditions

Acceptance will record that all three conditions are met.

1. **The handover material and environments are received.** ARN will receive this documentation site and the self-contained offline bundle, together with the development, test and production Azure environments described in the [as-built record](as-built.md). The evidence will be the published documentation set, the offline copy, and the environment access and configuration records. The [runbook](runbook.md) is the operational authority, including its ARN Self-Service Operations Guide.
2. **Support is trained and equipped.** At least two named platform support contacts will have completed operational training and will hold access to the runbook, dashboards and alerts needed to operate and support the platform. The evidence will be the completion record, confirmed access, and the responsibilities and escalation paths in the [support model](support-model.md). Each contact will be able to follow the runbook, interpret operational dashboards and alerts, triage coverage diagnostics, and take the platform support path after handover.
3. **Open work has ARN ownership.** Every item accepted as open at handover will have an ARN owner and an agreed action or decision path. The evidence will be the [open-items register](open-items.md), maintained with its owners and due-by period. An open item will not prevent acceptance when its ownership and treatment are recorded. It will remain visible as work for ARN to manage.

These conditions ensure that the operating material, access, support capability and remaining work have an accountable owner. The support model provides the operating recommendation, while ARN owns the decision about ongoing support.

## What is being accepted

The accepted handover consists of:

- the ARN-facing documentation site and its offline bundle;
- the development, test and production environments, including the operational access and configuration transferred for each environment; and
- the handover sessions to be delivered, including the runbook walkthrough and the practical orientation needed by operators and platform support contacts.

The formal sign-off record will be captured at the handover session and retained in ARN's controlled operational records. This page does not reproduce the signing form.

## Where the signature block lives

The formal sign-off record will be captured at the handover session and retained in ARN's controlled operational records. This page does not reproduce a signature grid or signature lines.

## After acceptance

ARN platform support responds through the agreed support channels to defects and operational questions, and provides defect triage, deployment support and alert interpretation. A defect is behaviour that deviates from documented or acceptance-tested behaviour. New capability is a feature and follows the normal change decision. Severity follows the acceptance scheme of Blocker, Major and Minor.

ARN maintenance owns routine operation and defect handling. Any further platform remediation is arranged through the support and change process agreed by ARN.

## Requesting a change after handover

A **defect** is behaviour that deviates from documented or acceptance-tested behaviour. It is recorded on the [defect register](defect-log.md) and handled through the support channels in the [support model](support-model.md).

A **change** is anything else: new capability, different content handling, altered behaviour or a new integration. A change is raised through the agreed support and change process rather than recorded as a defect, and it follows the normal change decision.

To request a change, raise it with the platform support role, stating what is being asked for, why, and which environment it affects. The ARN technology lead decides whether to accept, defer or reject it, in line with the ownership recorded on the [open items register](open-items.md). An accepted change is built, tested and promoted to an environment by [The Delivery Pipeline](delivery-pipeline.md): the same build, validation, gate and deployment stages that deliver every release, so a change reaches development, test and production only through the pipeline and its approvals.

ARN maintenance owns the change decision and the pipeline release.

Acceptance closes the handover process while leaving the [open-items register](open-items.md), support ownership and ordinary platform maintenance with ARN.
