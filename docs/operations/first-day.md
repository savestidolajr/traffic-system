---
title: "Your First Day Owning This Platform"
description: "An ordered checklist for day one of owning the platform: what to confirm, in what order, and who to call."
tags:
  - "operations"
  - "checklist"
aliases:
  - "first day"
  - "day one"
  - "what do I do first"
  - "taking ownership"
purpose: "runbook"
status: "current"
last_verified: 2026-09-01
review_after: 2026-11-27
volatility: "medium"
source_of_truth: "Published operations procedures, live console behaviour and current environment posture"
verified_by:
  - "manual review"
common_queries:
  - "What do I check on my first day?"
  - "How do I know the alerts work?"
  - "Who do I call if something breaks?"
see_also:
  - "docs/operations/access-and-accounts.md"
  - "docs/operations/support-model.md"
  - "docs/operations/runbook.md"
---

# Your First Day Owning This Platform

Use this checklist in order on the first day ARN owns day-to-day operation of the ARN Information Bulletin Platform. It routes the checks; detailed procedures remain in the linked operations pages.

## Before you start

These prerequisites prevent the first checks from failing because of avoidable access issues.

- [ ] Confirm you have an ARN Microsoft Entra ID account ending in `@arn.com.au`, with the required **Operator** or **Admin** role and any required station scope.
- [ ] Confirm you can open the ARN operations Teams channel and read its messages.
- [ ] Bookmark the [operations runbook](runbook.md) before testing alerts or a bulletin.

- [ ] Bookmark the live consoles: [development](https://ain-dev.arn.com.au), [test](https://ain-test.arn.com.au), and [production](https://ain.arn.com.au). Confirm each custom domain resolves and presents the expected environment.

## 0. Take over the access that ends with the engagement

Everything else on this page can wait a day. These four cannot: each one is
currently held by an account that is removed when the engagement closes, and
three of them fail silently. Verified live on 2026-09-03.

- [ ] **Platform administration.** Add at least two named ARN administrators to
  the platform's own Owner and Contributor Entra groups, then remove the
  delivery representative's two accounts and the direct subscription Owner
  assignment held by the vendor guest account. Today the platform-scoped Owner
  group holds only those two accounts and the Contributor group is empty.
  Nobody is locked out, because ARN's all-subscription Owner and Contributor
  groups still hold the platform team, but no one is named for this platform.
  Verify by activating the just-in-time role and listing the three resource
  groups.
- [ ] **Release approvals.** Add ARN approvers to the `test` and `production`
  environments in Azure DevOps, keeping at least one required approver. The
  only approver today is the delivery representative's ARN account. Once it
  lapses a queued release waits at the approval gate for an approver who cannot
  respond, so no deployment completes, including an emergency fix. Verify by
  queueing a run of the release pipeline and approving its test stage.
- [ ] **Teams delivery.** Re-authorise the Teams notification connection in each
  of the three environments while signed in as a lasting ARN account. All three
  are consented today as the delivery representative's ARN account. The target
  team is ARN-owned, so the channel itself survives; the consent does not, and a
  lapsed connection keeps reporting **Connected** while every post fails.
  Verify with `az monitor action-group test-notifications` and a visible card,
  as in section 3 below.
- [ ] **Sign-in secret expiry.** Ask the identity team for the expiry date of
  the sign-in secret on the test and production application registrations, record
  it in [Access and Accounts](access-and-accounts.md), and set a renewal
  reminder ahead of it. No expiry date is recorded anywhere today. An expired
  secret ends console sign-in for every operator at once, with no alert.

## 1. Confirm support access and ownership

The support model is usable when the role matrix is current and each contact can verify their own access.

- [ ] Open the [support model](support-model.md) contact matrix. Confirm that it lists a responsible contact, a channel, and at least two ARN platform-support contacts.
- [ ] Verify the role matrix, then have each contact self-test their own access to the [Test console](https://ain-test.arn.com.au) and, where authorised, the [Prod console](https://ain.arn.com.au). Never share or impersonate an account; see [First-Time Sign-In](first-time-sign-in.md) for the full address set.
- [ ] Check that each contact sees the surfaces allowed by the assigned role. Use [First-Time Sign-In](first-time-sign-in.md) for the account chooser, role expectations, and first orientation.
- [ ] Treat successful authentication followed by `403` responses on every route, or a **No app access** view, as a missing group or role assignment. Record the account and environment, then route the assignment issue through the ARN technology lead or platform support.

## 2. Repeat alert-delivery verification

The owner repeats this periodic check to confirm that configured alert routes still deliver to their intended recipients.

- [ ] In each environment's Azure Monitor action group, confirm that Teams routing is configured and live. SMS and phone receivers are intentionally not configured at ARN's direction. Each live action group also still carries one interim email receiver, the delivery representative's address, which the next deployment of that environment removes because the committed parameters set none. Decide before then whether an ARN-owned on-call address replaces it: the address must already exist, and the change is a parameter edit plus a pipeline run, per [Change where alerts go](notifications.md#change-where-alerts-go).
- [ ] Send a test notification with `az monitor action-group test-notifications` and confirm a `Succeeded` run plus a card in the destination channel, per [Teams Notifications](notifications.md#change-where-alerts-go). The existence of an alert rule is not proof that delivery works.
- [ ] Have the nominated on-call person confirm receipt of the test notification, including the environment and alert or test identifier. Record the received time.
- [ ] If a notification is missing or the test does not arrive, escalate using the [notifications troubleshooting](notifications.md#troubleshooting) and incident procedures. Do not treat the existence of an alert rule as proof that delivery works.

## 3. Confirm the operations Teams channel receives a card

The channel check proves that the Logic App connection, webhook path, and channel destination work together. The destination must be an ARN-nominated operations channel that already exists; if no channel has been nominated yet, record that and stop this section until one is.

- [ ] Confirm the destination is ARN's nominated operations channel for the environment, rather than a personal channel. The per-environment channels are listed under [Current environment wiring](notifications.md#current-environment-wiring).
- [ ] Send one test event with `az monitor action-group test-notifications` and verify exactly-once behaviour: one logical event produces one visible card, and a retry or redelivery does not produce a duplicate card for that event.
- [ ] Open the card in Teams and record the environment, event type, and received time. Confirm that the card links back to the console or incident context where provided.
- [ ] Check that the Teams connection's authenticated user is an ARN identity. Re-consent must be completed from an ARN account in the ARN tenant, per [Reauthorise a connection in Azure Portal](notifications.md#reauthorise-a-connection-in-azure-portal).
- [ ] If a card is not visible, open the channel directly. Cards do not @mention members and channels may be hidden, so an absent toast is not proof that posting failed. Follow the troubleshooting steps in [Teams Notifications](notifications.md#troubleshooting).

## 4. Find the runbook, dashboards, and health endpoint

A support responder needs one known path from an alert to the health signal, logs, and dashboard evidence.

- [ ] Open the [operations runbook](runbook.md) and confirm the **First 5 minutes of any incident** section is reachable.
- [ ] In Azure Monitor, locate the environment's **Operations Overview** workbook, the one dashboard for the platform. Display names carry the environment prefix and an em dash, such as `IBP-AUE-TST — Operations Overview`.
- [ ] Locate the Log Analytics workspace for the environment. The name follows `IBP-AUE-{ENV}-LAW-APP01`, using `DEV`, `TST`, or `PRD` for `{ENV}`. Confirm that function application warnings and errors are available.
- [ ] Check the anonymous health endpoint for the environment:

  ```text
  https://ibp-aue-{env}-fna-app01.azurewebsites.net/api/health
  ```

  Replace `{env}` with `dev`, `tst`, or `prd`. Record the HTTP status and named check results. Use the [monitoring dashboard guide](dashboard-guide.md) for workbook locations and interpretation.
- [ ] If the health check is degraded or unavailable, stop treating new generation as healthy and follow the runbook's first response steps before trying to generate another bulletin.
- [ ] Treat a quiet or unreachable production health check as an incident, but account for the actual posture: production runs with schedules disabled and no station armed. Park and resume remain available procedures for a deliberate, announced pause; if someone has parked the environment the Function App is **Stopped** and the plan is **B1**. Confirm which of these states you are looking at before escalating, then follow the runbook's first response steps.

## 5. Trace one bulletin's delivery end to end (read-only)

Day one is a read-only trace of a bulletin that already exists. Approving, publishing, or triggering a bulletin is a write and does not belong on day one; the full arm-to-air publish rehearsal is a supervised first-week exercise with an authorised operator. Station arming changes over time, so check the live schedule before assuming any station will produce a bulletin: `GET /api/schedules` is the authority and needs the **Operator** role to read. As verified 2026-09-03 no station is armed for playout anywhere: both test stations generate on schedule with playout disabled, both production schedules are disabled, and Dev never delivers. Confirm it live rather than relying on this note.

- [ ] Read the live `GET /api/schedules` for the Test environment and record which stations are armed. If the call returns `Forbidden`, the account lacks the **Operator** role; route that through the ARN technology lead or platform support before continuing.
- [ ] In the console, open **On air** and then **History** and select a bulletin that already exists, preferably one that reached **Waiting approval** or beyond. If the queue is empty, use **History**; an empty queue does not prove a generation failure. Do not trigger, approve, or publish anything to fill the slot.
- [ ] Open the bulletin and trace its path: script, source panel, Candidate board, map, slot details, and complete audio. Check that the Candidate board's Google Maps links, selected corridors, and evidence match the bulletin.
- [ ] Follow the bulletin into its status history and delivery entry. A delivery entry confirms that the approved WAV was written to the shared playout share; it does **not** confirm Zetta ingest or on-air playback. If the trail ends without a delivery entry, record the bulletin ID, station, slot, and timestamp and escalate rather than retrying.
- [ ] Use the [operator guide](operator-guide.md) for the review, approval, publish, and delivery sequence as reading, not as actions to perform. If the traced bulletin ended in fallback, record the fallback outcome rather than treating fallback as a successful fresh generation.
- [ ] Book the supervised first-week exercise: with an authorised operator, watch a scheduled Test bulletin from scheduler creation through approval and publish to the delivery entry, against the live schedule checked above.

## 6. Know who to call

Routing an issue to the right tier protects the broadcast window and avoids changing content when the platform needs platform support.

- [ ] Use **Tier 1, ARN operator self-service** for wording, local editorial guidance, writer-prompt sections, pronunciation verdicts, filler lines, priority roads, and review decisions.
- [ ] Use **Tier 2, ARN platform operations** for health failures, stopped hosts, queue failures, repeated failed bulletins, AI throttling, delivery failures, missing fallback coverage, access problems, deploy or rollback issues, and secret or Teams wiring problems. Start with the [operations runbook](runbook.md).
- [ ] Use **Tier 3, vendor or Azure escalation** when Tier 2 has confirmed a sustained HERE, ElevenLabs, Azure OpenAI, Functions, Storage, Key Vault, networking, or quota fault that persists beyond the runbook's first response.
- [ ] Confirm the contact matrix identifies the ARN operator lead, at least two platform-support contacts, the technology lead, and the relevant business owners. Do not invent a phone number, channel, or support hour.
- [ ] Use the ARN operations Teams channels for defect triage, deployment support, and alert interpretation.
- [ ] Route defects and operational incidents to ARN maintenance through Tier 2. Escalate vendor or Azure faults through the support model.
- [ ] Include the environment, station, licence area, bulletin ID, scheduled slot and local time, current status, listener-facing outcome, exact console or Teams message, and action already taken in every escalation.

## What to do in your first week

The first week turns the ownership checks into repeatable maintenance.

- [ ] Read [Known Limitations](known-limitations.md) and confirm which active limitation affects your environment or station.
- [ ] Confirm ARN owners for the Azure subscription Owner or Contributor path, each environment's deployment service principal, Entra app registrations, Key Vault data-plane access, Azure DevOps service connections, and environment approvers.
- [ ] Confirm the ARN owner for the HERE production account and API key, and name the ElevenLabs workspace owner with at least one ARN workspace administrator.
- [ ] Review the [ARN Self-Service Operations Guide](runbook.md#arn-self-service-operations-guide) and confirm the team can execute its procedures.
- [ ] Open the station's future-lessons view, review any Station Lessons candidates, and confirm an administrator understands the three-reinforcement activation and dismissal controls.
- [ ] Diarise the credential and ownership dates recorded in [Access and Accounts](access-and-accounts.md): deployment service-principal secret or certificate rotations, Azure DevOps service-connection renewal, Key Vault secret rotations, Entra app or client-secret changes, HERE and ElevenLabs key ownership, Teams OAuth re-consent, and Logic App trigger URL SAS regeneration.
- [ ] Rehearse the applicable rotation procedure in the [operations runbook](runbook.md), including the health check and one bulletin verification after a key change. Record the next review date and the ARN owner who will perform it.
- [ ] Set a recurring daily health, alert, and queue check, and a weekly cost and alert review using the [monitoring dashboard guide](dashboard-guide.md).
