---
title: "First-Time Sign-In"
description: "Zero-context walkthrough for an ARN content-team participant signing into the operator console for the first time."
tags:
  - "operations"
  - "onboarding"
  - "authentication"
  - "uat"
  - "entra"
aliases:
  - "first time sign in"
  - "console sign-in"
  - "participant login"
  - "ARN console onboarding"
purpose: "runbook"
status: "current"
last_verified: 2026-09-01
review_after: 2026-12-01
volatility: "high"
source_of_truth: "The deployed ARN console addresses and the access controls for each environment"
verified_by:
  - "Reader-facing verification of the published ARN console addresses and sign-in flow"
  - "ARN-managed device verification"
common_queries:
  - "How does an ARN participant sign into the console?"
  - "Why do I see no app access after signing in?"
see_also:
  - "docs/operations/as-built.md"
  - "docs/operations/uat-plan.md"
  - "docs/reference/per-station-configuration.md"
---

# First-Time Sign-In

This walkthrough is for an ARN content-team operator or administrator who has never opened the console. Use the environment assigned to you; `GET /api/schedules` is the authority on which stations are armed and needs the **Operator** role to read. As verified 2026-09-03 no station is armed for playout anywhere: both test stations generate on schedule with playout disabled, both production schedules are disabled, and Dev can never deliver audio. Confirm it live rather than relying on this note.

## 1. Open the correct console

| Environment | Console URL | Use |
|---|---|---|
| **Test** | https://ain-test.arn.com.au | Live pilot: bulletins generate on schedule, playout is disabled on both stations, so nothing is delivered until ARN arms one — check live `/api/schedules` |
| Dev | https://ain-dev.arn.com.au | Maintenance and change verification only; never delivers audio |
| Prod | https://ain.arn.com.au | Running with schedules currently disabled; no station is armed |

Use only the ARN console address for the environment you have been assigned: `ain-test.arn.com.au`, `ain-dev.arn.com.au`, or `ain.arn.com.au`. Do not substitute a saved bookmark, a guessed hostname, or another address.

**If the browser reports `DNS_PROBE_FINISHED_NXDOMAIN`, "can't reach this page", or "server IP address could not be found", the name did not resolve and you have not reached the platform at all.** This is a name-resolution fault, not an access or password fault. Close the browser, reopen the exact ARN console address for your assigned environment, and retry. If the error persists, contact platform support using the contacts table in the [support model](support-model.md) with the environment, address, device, and time of the failure.

## 2. Sign in with the right account

1. Open the ARN console URL for the environment assigned to you.
2. Sign in with your ARN Microsoft account ending in `@arn.com.au`.
3. Never choose a guest account, a personal Microsoft account, or an invited external account. If the account chooser shows the wrong identity, choose **Use another account** and enter your ARN address.
4. Complete the normal Microsoft Entra ID sign-in and return to the console.

Console access is granted by app role, not by the fact that the password worked. A successful sign-in followed by a **No app access** screen or 403 responses on every route means the role assignment has not landed. It does not mean that you typed the wrong password.

If the role is missing, contact the identity team using the contacts table in the [support model](support-model.md) and provide the environment, your ARN account, and the role you need. Ask the identity team to assign either **Operator** or **Admin** access to the appropriate console environment.

Use **Operator** for content review and approval, and for the station's own bulletin rules. Use **Admin** when station schedules, voice and delivery configuration, pronunciation dictionaries, writer-prompt sections (editorial library), banned words, configuration transfer, the activity log, or operator access must be managed.

## 3. What each role sees

An Operator can review and approve bulletins within the assigned station scope. An Admin can do that and manage the station settings and access controls needed to operate the platform. A station-scoped account cannot act on stations outside its assignment.

| Account | What to expect |
|---|---|
| Operator | On air, review, source panel, audio, approval and rejection surfaces, plus the station's own configuration: the station schedule (times, voice, delivery style, pronunciation dictionary, coverage, content), editorial guidance, priority roads, filler lines and pronunciation verdicts, and **Trigger now** for an ad-hoc bulletin. A scoped operator can act only on assigned stations. |
| Admin | Operator workflow plus administration surfaces for adding, duplicating and deleting stations, voice and delivery configuration, pronunciation dictionaries, writer-prompt sections (editorial library), banned words, configuration transfer, the activity log and operator access. |

Per-station configuration is ARN-editable and is resolved by the scheduler at broadcast time. A configuration change takes effect for later scheduled bulletins and does not alter a bulletin already under review. See [per-station configuration](../reference/per-station-configuration.md) and the [UAT plan](uat-plan.md).

## 4. First orientation

These steps fit on one screen:

1. After sign-in, start at **On air**, the list of current and recent station bulletins.
2. Open a bulletin from the On air list, preferably one marked **Waiting approval**.
3. The bulletin opens in the review cockpit. Find the script, playable audio, source panel, and map there.
4. Select a cited source row to check the source for a sentence. Use the review controls for approval, rejection, notes, or revoice.
5. If the list is empty, do not assume sign-in failed. Check the troubleshooting table below and ask platform support using the contacts table in the [support model](support-model.md) whether the pilot schedule is enabled.

## Troubleshooting

| Symptom | Likely cause | Action |
|---|---|---|
| "Can't reach this page", `DNS_PROBE_FINISHED_NXDOMAIN`, or "server IP address could not be found" | The console name did not resolve, so nothing was reached and no sign-in or role was exercised. | Close the browser, reopen the exact assigned `ain*.arn.com.au` console address, and retry. If it persists, tell platform support using the contacts table in the [support model](support-model.md) the environment, address, device, and time. |
| 403 on every route or a **No app access** screen | Sign-in succeeded but the ARN account or its group has no matching app role for the environment. | Contact the identity team using the contacts table in the [support model](support-model.md) with the account, environment, and requested role. Do not change the password or use a guest account. |
| No bulletins visible | The station schedule is disabled between sessions, no scheduler cycle has created a bulletin yet, or a scoped operator has no assignment for the pilot station. | Check live `/api/schedules` (needs the **Operator** role) for the armed station before waiting on a slot. Ask platform support using the contacts table in the [support model](support-model.md) to enable the pilot station schedule and wait for the next scheduler cycle. If the operator is still scoped out, ask platform support to confirm the station assignment. |
| Sign-in loop | The browser is reusing a guest or wrong Microsoft session, or stale Entra cookies. | Sign out of the wrong account, open a private window, and sign in with the ARN account ending in `@arn.com.au`. If the loop continues, send the identity team using the contacts table in the [support model](support-model.md) the account address, exact URL, and time of the loop. |
