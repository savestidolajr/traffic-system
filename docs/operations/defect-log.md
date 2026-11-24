---
title: "Defect Register"
description: "The register ARN records acceptance and integration findings in: observable behaviour, condition, workaround and severity."
tags:
  - "operations"
  - "acceptance"
  - "testing"
  - "integration"
  - "defects"
aliases:
  - "defect register"
  - "defect log"
  - "acceptance findings"
purpose: "runbook"
status: "current"
last_verified: 2026-09-03
review_after: 2026-11-17
volatility: "high"
source_of_truth: "Observed platform behaviour and acceptance testing records"
verified_by:
  - "manual review"
common_queries:
  - "What defects remain open?"
  - "How do I record a new defect?"
see_also:
  - "docs/operations/uat-plan.md"
  - "docs/operations/uat-scenario-sheet.md"
  - "docs/operations/integration-test-plan.md"
  - "docs/operations/known-limitations.md"
---

# Defect Register

This is the register ARN records acceptance and integration findings in. It starts empty: the behaviours identified before handover are documented as current platform behaviour, with their workarounds, on [known limitations](known-limitations.md), because they are accepted characteristics of the delivered platform rather than findings awaiting triage.

Each entry records the observable behaviour, the condition that triggers it, a practical workaround and a severity. A status stays Open until the behaviour is corrected and witnessed again.

## Finding workflow

During an acceptance run, the participant tells the acceptance coordinator before moving to the next case. The coordinator records the case, environment, time, participant role, bulletin reference, observable result, expected result, severity, workaround and evidence. Each finding receives the next identifier in the `UAT-###` sequence. Link that identifier in the scenario sheet.

The acceptance coordinator triages each UAT finding with platform support. The coordinator confirms whether it is a defect, duplicate, expected behaviour or feedback, and assigns severity. A finding is closed only when the correction or agreed disposition is recorded and the affected behaviour has been witnessed again in TEST with retained evidence. Closed findings remain on this register with Status Closed; retain the closure evidence in the scenario sheet and acceptance record.

## Severity and recording rules

Severity is assessed as follows:

- **Blocker:** the workflow cannot complete. It blocks acceptance until fixed and witnessed again.
- **Major:** the listener may hear a wrong on-air outcome, or an essential operational control does not work. It remains open for exit purposes unless fixed and witnessed again, or explicitly waived in writing by the acceptance coordinator.
- **Minor:** the issue is cosmetic or affects wording without changing the on-air outcome. It does not prevent exit, but remains recorded until closed or accepted as a known limitation.

A finding is recorded with its observable result, expected result, severity, workaround and evidence, and keeps the identifier it received in the UAT sequence. The status remains Open until the behaviour is corrected and witnessed again; once closed, keep the row on this page with Status Closed.

## Findings

No findings are open. Add a row per finding from each acceptance or integration run.

| Finding | Severity | Status | Observable behaviour | Conditions | Workaround | Waiver or disposition |
|---|---|---|---|---|---|---|
| | | | | | | |

## Close-out

Before completing an acceptance record, confirm that every failed case has an entry here with its evidence and workaround. Keep a defect on this page after closure; mark it Closed only after the observable behaviour has been checked again in the affected environment and the evidence has been retained. Record a written Major waiver in the Waiver or disposition column with the identifier, rationale, scope and acceptance coordinator approval. Quality observations about tone, pacing or style are recorded as feedback and do not become defects unless they breach an explicit acceptance expectation.
