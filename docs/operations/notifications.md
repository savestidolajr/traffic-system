---
title: "Teams Notifications"
description: "How the platform delivers review and failure notifications to Microsoft Teams, how to reauthorise the connection, and how to diagnose missing cards"
tags: [operations, notifications, teams, logic-app, webhook, runbook]
purpose: "runbook"
status: "current"
last_verified: 2026-09-04
review_after: 2026-11-27
volatility: "medium"
source_of_truth: "Azure Portal Logic Apps, Teams API connections, Function App settings, and this page"
verified_by:
  - "Azure Portal connection and workflow status"
  - "Operational notification procedure"
aliases:
  - notifications
  - teams cards
  - approval notifications
  - webhook notifications
common_queries:
  - "How do Teams notifications work?"
  - "How do I set up notifications in a new environment?"
  - "Why did a notification not arrive?"
  - "How do I reauthorise the Teams connection?"
see_also:
  - "docs/operations/runbook.md"
  - "docs/operations/as-built.md"
---

# Teams Notifications

The ARN Information Bulletin Platform sends review, approval, failure, and
alert cards to Microsoft Teams. The notification path uses one Logic App and
one Teams API connection per environment. Every environment's Azure Monitor
action group also still carries an email receiver addressed to the delivery
representative's own mailbox; the deployment parameter files now declare an
empty receiver list, so the next deployment removes that receiver and leaves
Teams as the only path.

## Architecture

| Stage | Azure resource or surface | Behaviour |
|---|---|---|
| Notification sender | The environment Function App | Sends one structured notification for a logical bulletin or alert transition when notifications are enabled. |
| Transport | `IBP-AUE-{ENV}-LOA-APP01`, a Logic App | Receives the HTTP request and chooses the card branch. |
| Teams connection | `IBP-AUE-{ENV}-LOA-APP01-teams`, a Teams API connection | Authenticates the post to Microsoft Teams. |
| Destination | The configured environment Teams channel | Receives an Adaptive Card from the Flow bot. |

Azure Monitor alerts ride the same transport: each environment's action group
carries a Microsoft Teams webhook receiver (common alert schema) pointing at
the same Logic App, whose first branch renders an alert Adaptive Card - so a
system-down alert lands in the same channel as the app's own cards. Enabled
and verified in all three environments (dev 31 Jul, test and prod 7 Aug 2026,
each via `az monitor action-group test-notifications` producing a Succeeded
run and a card). SMS and phone receivers are deferred at ARN's direction.

An email backstop is still deployed but is already deleted in source. Every
environment's action group `IBP-AUE-{ENV}-AIA-APP01`, including development,
still carries the delivery representative's own mailbox as an enabled email
receiver, verified 2026-09-04. The deployment parameter files now declare an
empty receiver list, so the next deployment of any environment removes that
receiver and leaves Microsoft Teams as the only configured path. That first
deploy silently ends the last alert path that does not depend on the
platform's own subscription, so nominate an ARN-owned address before it if an
email backstop is wanted, using the procedure below.

One caveat worth knowing: the Teams path runs through a Logic App inside the
same subscription as the platform, so it cannot be relied on to report that the
subscription itself is unavailable. An email or SMS receiver on an ARN-owned
address is the only route that does not share that fate.

## Change where alerts go

Both destinations are declared in infrastructure as code, so a change is a
parameter edit and a pipeline run rather than a support request.

**A different email address, or several.** Edit the `alertEmailReceivers` value in the
environment's deployment parameter file, giving each entry a `name` and an
`emailAddress`, then run the deployment pipeline for that environment. An empty list is
valid and leaves Teams as the only path. Editing the action group directly in
the Azure Portal takes effect immediately, but the next deployment restores
whatever the parameter file says, so the parameter file is the durable change.

**A different Teams channel.** In Teams, open the target channel, choose Get
link to channel, and take the group and channel identifiers from it. Set
`teamsGroupId` and `teamsChannelId` in the same parameter file and run the
deployment pipeline. The Logic App posts to the new channel on its next run; the app's own
cards and the Azure Monitor alerts both follow, because they share the one
transport. If the Teams API connection is new or has been recreated, authorise
it once with an ARN-native account using the reauthorisation steps below.

Confirm either change with `az monitor action-group test-notifications`, which
produces a Succeeded run and a card in the destination channel.

## What raises an incident, and who sees it

Every row below ends in the environment's Teams channel, via the
action group's Teams webhook receiver. Until the next deployment, the Azure
Monitor rows below additionally send email to the delivery representative's
own mailbox; SMS, phone and at-mentions are not configured.

| Incident | Detector | Sev | Verified end-to-end |
|---|---|---|---|
| Function App returning HTTP 5xx | `-errors-alert` metric alert, >=1 in 5m | 2 | Fired 8x on dev / 1x on test in the 30 days to 10 Aug; metric-alert card path re-proved 10 Aug with a shape-clone probe |
| Function App serving nothing for 15 min | `-no-executions` metric alert | 1 | Fired on parked PROD 10 Aug 14:32:16Z, card in the Prod channel 14:32:18Z |
| `/api/health` failing from >=2 probe locations | `-health-availability` metric alert on the web test | 1 | Fired 10 Aug 14:34:54Z from a deliberately-failing clone web test, card 14:34:56Z |
| Key Vault refusing an authenticated caller | `-kv-access-denied` log alert on the workspace AuditEvent stream | 2 | Fired 10 Aug 14:35:46Z from a real refused-by-role-assignment denial, card 14:35:47Z |
| Azure OpenAI throttling (429s) | `-ai-throttling` log alert, >=5 in 15m | 2 | Fired 10 Aug 13:53:18Z, card 13:53:20Z |
| Message in a `*-poison` queue | `-poison-queue` log alert | 1 | Fired 10 Aug 13:51:43Z, card 13:51:44Z |
| ElevenLabs synthesis degraded | `-synthesis-failures` log alert, >=3 in 1h | 2 | Fired 10 Aug 13:52:51Z, card 13:52:53Z |
| Nearly every bulletin failing | `-bulletin-failure-rate` log alert, >=90% in 30m | 1 | Fired 10 Aug 13:50:57Z, card 13:50:59Z |
| A dependency reachable but unusable | `-dependency-unhealthy` log alert | 1 | Fired 10 Aug 13:52:09Z, card 13:52:10Z |
| Bulletin failed with nothing safe to air | app card, failed and uncovered | - | 23 real cards in the retained run history (22 "nothing queued", 1 "filler will air") |
| Bulletin failed but a same-day re-air covers it | app card, failed but covered | - | 3 real cards, first 5 Aug 23:43Z |
| Deadline passed after an urgent escalation | app card, deadline closed out | - | Not yet seen live; covered by unit tests and shares the failed-but-covered card branch |
| CI/CD pipeline failure | the pipeline's failure-notification stage, posting to its own CI/CD webhook | - | Live, not a no-op: card observed 7 Aug 09:49Z |

The five log-search rules were verified by injecting synthetic telemetry into the
dev Application Insights ingestion endpoint (each item tagged
`synthetic_test=monitoring-audit-2026-08-10`), which is why 20 failed
`broadcast.bulletin.emitted` records for market `AUDIT` appear in dev telemetry
around 13:49Z on 10 Aug 2026. They are test fixtures, not a dev regression.

Four rules are latched (`autoMitigate: false`) so a sustained outage stays one
open incident until a human closes it. Latched rules also carry
`muteActionsDuration: PT1H`: without it they re-notify on every 5-minute
evaluation, which produced 12 cards in 17 minutes during the 10 Aug test.
Azure rejects action suppression together with auto-mitigation, so the Key Vault
rule auto-mitigates rather than mutes.

A deploy re-arms alert rules on a **parked** environment. `park.sh` disables them
first by design, but any later module or full deploy re-enables them and the
parked app then trips `-no-executions` (observed on prod, 10 Aug). Re-run the
silencing step, or resume the environment, after deploying into a parked one.

The Function App setting Webhook__Enabled controls whether the sender is
active. The Key Vault secret Webhook--EndpointUrl holds the Logic App trigger
address. The app reads the vault at startup rather than through an app-setting
reference, so after changing that secret you only need to restart the Function
App — there is no app-settings re-save step any more.

The Logic App transports the card and does not rewrite the card content. A
standard Teams card uses a full-width action area and displays times in each
reader's local timezone.

## Card behaviour

| Card | Trigger | What the operator should do |
|---|---|---|
| Ready for review | Audio is ready and a bulletin is waiting for approval | Open the console and review the bulletin before its slot. |
| Approval reminder | The approval window is getting shorter | Open the console and complete the review. |
| Urgent approval | Playout is imminent and approval is still missing | Approve in the console or allow the documented fallback to cover the slot. |
| Failed and not covered | A failure has no safe same-day bulletin or standby audio queued | Open the console, inspect the failure, and choose the available recovery action. |
| Failed but covered | A same-day fallback has already covered the failure and no urgent escalation is needed | Treat the card as an audit notification unless the fallback repeats. |
| Missed-deadline closure | A same-day fallback covered a bulletin after an urgent card was sent | Confirm that the previous bulletin was re-aired and record the event if it recurs. |
| No card | Approved, published, rejected, and content-limited fallback transitions | Check the console and Activity page when an audit trail is needed. |

The notification rules are exactly once for each logical transition. For one
bulletin, the reminder and urgent escalation each occur at most once. A failure
card is sent once when the status changes to failed. A no-audio fallback
warning is also sent once, so repeated scheduler checks do not create a stream
of duplicate cards. If a Logic App run ends with an unknown result, inspect
**Runs history** before resending anything.

## Current environment wiring

| Environment | Logic App | Teams channel | Connection status | Operational note |
|---|---|---|---|---|
| DEV | `IBP-AUE-DEV-LOA-APP01` | `IBP Notifications (Dev)` | Connected | Review, failure, reminder, urgent, and release cards use this channel. |
| TEST | `IBP-AUE-TST-LOA-APP01` | `IBP Notifications (Test)` | Connected | Use this channel for pilot and acceptance checks. |
| PROD | `IBP-AUE-PRD-LOA-APP01` | `IBP Notifications (Prod)` | Connected | Production runs but is held frozen at the last known-good fallback with no station armed, so job cards are not expected from live playout. Azure Monitor alert cards still deliver through this channel. |

To find a channel, open Microsoft Teams and expand the team that contains the
environment channel. These are standard channels. Cards do not use @mentions,
so they do not create a toast or activity-feed entry. If a channel is hidden,
choose **Show** under hidden channels before investigating a missing card.

## The consent failure mode: re-consent before the delivery accounts lapse

Do this first, in all three environments, before the delivery
representative's accounts are removed. Every Teams API connection
(`IBP-AUE-{ENV}-LOA-APP01-teams` in dev, test and production) is consented by
that one account alone, verified 2026-09-04. When the account lapses, each
connection keeps reporting **Connected** while every Teams post fails, so
review, deadline and failure cards and every Azure Monitor alert card stop
arriving while the alert rules keep firing. That is a silent failure of the
only remaining alert path: after the next deployment removes the email
receiver, nothing else reports it.

Re-consent must be completed with an ARN-native account that can post to the
environment channel. A guest account from another tenant can make the
connection display **Connected**, but every later
Teams post can fail with HTTP 401 because the token belongs to the wrong home
tenant. Never trust the status label alone.

After consent, open the Teams API connection in Azure Portal and check the
authenticated account in **API connection** details. The account must be an
ARN-native identity. Then open the Logic App, choose **Runs history**, and
confirm a successful Teams action. Repeat the reauthorisation steps below once
per environment.

## Reauthorise a connection in Azure Portal

Use these steps when the connection is **Unauthorized**, a Teams action returns
HTTP 401, or the account shown in the connection details is not ARN-native.
Run them once per environment before the delivery representative's accounts
are removed, even while every connection still reports **Connected**.

1. In Azure Portal, open **Resource groups** and select
   `IBP-AUE-{ENV}-RSG-APP01`.
2. Open `IBP-AUE-{ENV}-LOA-APP01-teams`, choose **Edit API connection**, and
   select **Authorise**.
3. In the account picker, choose an ARN-native account. If the browser
   automatically selects a guest identity, choose **Use another account** or
   sign out of the guest session first.
4. Complete the Microsoft sign-in and consent prompts, then choose **Save**.
5. Return to the connection **Overview** and confirm **Connected** plus the
   ARN-native authenticated account.
6. Open the Logic App `IBP-AUE-{ENV}-LOA-APP01`, choose **Runs history**, and
   confirm the next notification run succeeds through the Teams action.

If the connection remains connected but the Teams action still returns 401,
repeat the procedure with a different ARN-native account that can post to the
channel. Do not authorise with a guest identity.

## Bring up notifications in an environment

1. In Microsoft Teams, create or choose a standard channel that the
   ARN-native authorising account can post to. Record the channel display name.
2. In Azure Portal, open the environment resource group and the Logic App.
   Use **Logic App Designer** to confirm the HTTP trigger and Teams post action
   are enabled. Use **Overview**, **Enable** if the workflow is disabled.
3. Open the Teams API connection from the resource group and complete the
   reauthorisation steps above.
4. Open the Function App, **Configuration**, **Application settings**, and
   confirm Webhook__Enabled is `true`.
5. In the environment Key Vault, **Secrets**, confirm Webhook--EndpointUrl has
   a current version, then restart the Function App so it re-reads the vault.
6. Submit one test bulletin through the console. In the Logic App **Runs
   history**, open the corresponding run and confirm that the Teams action
   succeeded. Confirm the card appears in the intended channel.

## Troubleshooting

| Symptom | Portal checks |
|---|---|
| No Teams card and no Logic App run | Open the Function App **Configuration**, confirm `Webhook__Enabled`; open Key Vault and check `Webhook--EndpointUrl` has a current version; then restart the Function App so it re-reads the vault. |
| Logic App run failed before the Teams action | Open the run, expand the failed action, and read its inputs and outputs. A stale trigger URL or a disabled workflow is the usual cause. |
| Logic App run failed with HTTP 401 | Reauthorise the Teams API connection with an ARN-native account and verify the authenticated account. |
| Logic App run succeeded but no card is visible | Confirm the channel display name and that the authorising account can post there. Expand hidden channels in Teams. |
| Cards arrive more than once | Inspect **Runs history** and the console Activity page for repeated status transitions. Do not resend while a run has an unknown result. Escalate repeated logical transitions with the bulletin ID. |
| Email alert did not arrive | Open **Monitor**, **Alerts**, **Action groups**, inspect the receiver list and **Test action group**. Until the next deployment, every action group still sends to the delivery representative's own mailbox; after that deployment no email receiver is configured. SMS and phone receivers are not configured. |
| Alert fired but no Teams card | Check the Logic App **Runs history** for a failed `Is_azure_monitor_alert` branch. If the run failed in `Compose_card` instead, the workflow definition predates the alert branch (31 Jul 2026) - redeploy the notification module or re-apply the current definition. |

For the incident response sequence around a missing card or failed bulletin,
use the [operations runbook](runbook.md). For release failure notifications,
inspect the Azure DevOps release run and the Logic App **Runs history** without
changing the notification connection during the incident.
