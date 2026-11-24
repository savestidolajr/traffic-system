---
title: "Where the Platform Depends on ARN"
description: "Where platform behaviour depends on ARN-held access, ARN-owned scheduling or an ARN address, with the role, timing and consequence for each."
tags:
  - "handover"
  - "governance"
  - "actions"
aliases:
  - "open items"
  - "ARN actions"
  - "what does ARN need to do"
  - "outstanding items"
purpose: "reference"
status: "current"
last_verified: 2026-09-04
review_after: 2026-12-02
volatility: "medium"
source_of_truth: "The deployed feed configuration and the playout delivery boundary"
verified_by:
  - "Reviewed against the delivered scope"
common_queries:
  - "What does ARN still need to do?"
  - "Who owns the outstanding items?"
  - "What is blocking go-live?"
---

# Where the Platform Depends on ARN

Where the platform's behaviour depends on something only ARN holds: the
administrative access and release approvals that outlive the engagement, three
authority feeds that need ARN-held access or credentials, the emergency feed's
key, the station clocks that decide whether delivered audio airs, the address an
emailed alert goes to, and the content and compliance decisions that are ARN's
to make. Each row states the responsible role, the timing and what happens if it
stays as it is.

ARN's own staffing and governance arrangements are ARN's to make. The platform
behaviour they relate to is documented where it belongs: alert routing and the
deferred SMS and phone receivers in the
[notifications reference](notifications.md), the accepted constraints in
[known limitations](known-limitations.md).

## Administrative access and approvals

These rows are the ones that decay without anyone touching them. Every other
item on this page stays as it is until someone changes it; the rows below
change state when the engagement accounts are deprovisioned, when an unrecorded
credential expires, or when the first ARN-authored change meets a gate with
nobody named to open it.

| Action | Role | Timing | Consequence if incomplete |
|---|---|---|---|
| Add at least two named ARN administrators to the platform's own Owner and Contributor Entra groups, then remove the delivery representative's two accounts | ARN technology lead with the identity team | On or before the last engagement day | Verified 2026-09-03: the platform-scoped Owner group contains only the delivery representative's ARN and vendor accounts, and the Contributor group is empty. ARN's all-subscription Owner and Contributor groups still hold the platform team, so nobody is locked out, but no one is named as this platform's administrator and the audit trail for it ends with a departing account |
| Add a second ARN-named human to the `AZ - IBP - Owner` PIM group and prove a PIM activation as that second person | ARN subscription administrator | On or before the last engagement day | At handover the group holds one person under two accounts. When those accounts are removed the subscription has no administrator: nobody can activate Owner, approve access, or recover the subscription without going through tenant-level support, and an unproven second member is only a name on a list |
| Grant ARN-native identities the Key Vault and Storage data-plane roles currently held only by the delivery representative's account, and prove a test or production secret read from inside the virtual network | ARN technology lead with the identity team | On or before the last engagement day | At handover no ARN identity holds the data-plane grants for secret rotation or recovery. When the delivery account lapses, nobody at ARN can read or rotate a Key Vault secret or Storage key, including from inside the virtual network, and an unproven grant is only a role assignment until the read succeeds |
| Name at least two ARN approvers on the test and production release environments in Azure DevOps, keeping at least one required approver | ARN pipeline owner | On or before the last engagement day | Verified 2026-09-03: the sole approver on both environments is the delivery representative's ARN account. Once it lapses, a queued release waits at the approval gate for an approver who cannot respond, and no deployment, including an emergency fix, can complete |
| Add ARN engineers to the project's Contributors group in Azure DevOps, which today contains only the delivery representative's account | ARN pipeline owner | On or before the last engagement day | When that account lapses, no ARN engineer can push a branch or raise a pull request without an administrator overriding permissions first, so the first ARN-authored fix waits on access rather than on review |
| Add a branch policy on `master` with a minimum reviewer count and build validation on the release pipeline | ARN pipeline owner | Before the first ARN-authored change | At handover `master` carries no branch policy. An unreviewed or unbuilt change can land on master and flow straight to release, with no required reviewer and no build gate in the way |
| Re-authorise the Teams notification connection in each of the three environments while signed in as a lasting ARN account, then confirm a card arrives | ARN M365 service owner with platform support | On or before the last engagement day | Verified 2026-09-03: all three connections are consented as the delivery representative's ARN account and report Connected. When that account lapses the connection keeps reporting Connected and every post fails, so review, deadline and failure cards and every Azure Monitor alert card stop arriving while the alert rules keep firing |
| Record the expiry date of the sign-in secret on each environment's application registration and of the release pipeline's federated credentials, and set a renewal reminder ahead of each | ARN identity team with the pipeline owner | Within the first support cycle | No expiry date is recorded anywhere for any of these credentials, and a non-privileged reader cannot see them. An expired secret ends console sign-in and API authorisation for every operator at once, and an expired federated credential stops deployments, each with no alert and no gradual failure, and the deployment must be rerun after the new value is stored |

## Cost and subscription governance

| Action | Role | Timing | Consequence if incomplete |
|---|---|---|---|
| Review the monthly subscription budget amount and confirm who acts on its alerts | ARN subscription administrator | At the first review after handover | A monthly budget of A$1,800 was created on 2026-09-04 and notifies the Owner and Contributor contact roles at eighty per cent, one hundred per cent and forecast one hundred per cent of the month. It replaced having no cost guardrail at all. Measured August consumption was A$1,277 across the three environments, of which production was about A$260. Script generation and speech synthesis are consumption-priced, so the ceiling needs revisiting whenever scheduling changes |
| Decide whether development and test stay running between pilot changes, and park them if not | ARN technology lead | At the first review after handover | Development and test account for about three quarters of the monthly spend. Both can be parked and resumed with the documented procedure without losing configuration or history |

## Content and compliance decisions

| Action | Role | Timing | Consequence if incomplete |
|---|---|---|---|
| Accept the residual penetration-test risk in writing, or commission an independent test before wider rollout | ARN technology lead with the security owner | Before rollout beyond the pilot stations | No independent penetration test has been performed. The platform runs on the proven Azure security baseline of role-based access, managed identity and private endpoints, with no third-party assurance over that baseline. This decision has no default: leaving it unmade leaves the risk unaccepted and unassured |
| Supply the content-safety word lists for the markets in scope, or confirm the shipped defaults are accepted | ARN content leads | Before a station relies on automated content screening | The safety terms list carries only the delivered defaults, so screening reflects our judgement of what must never air rather than ARN's |
| Complete ARN's own regulatory review of AI-generated broadcast content and record the outcome | ARN legal stakeholders | Before wider rollout | The platform enforces the editorial and safety controls we were asked for. Whether those controls satisfy ARN's obligations for automated broadcast content is a determination only ARN can make |
| Set the recovery point and recovery time targets for bulletin state, and confirm whether the current protections meet them | ARN technology lead | Within the first support cycle | Bulletin state is protected by soft delete and versioning with a 90 day retention window in test and production, but no target has been stated for how much history may be lost or how quickly service must return, so nobody can say whether the protection is sufficient |
| Decide whether static application and infrastructure policy scanning is required in the release pipeline | ARN pipeline owner | Within the first support cycle | The pipeline scans dependencies for published vulnerabilities and the repository for secrets, and blocks a release on either. It does not scan the application's own code or the infrastructure definitions against a policy baseline |

## Authority feed access

| Action | Role | Timing | Consequence if incomplete |
|---|---|---|---|
| Request a dedicated QLDTraffic authority key from `qldtraffic@tmr.qld.gov.au` and store it in each environment's Key Vault | ARN technology lead with the vendor account owner | Before relying on Queensland authority coverage | The shared demonstration key is throttled with 429 responses at its 100-per-minute cap, and has been refused with 401 in development since 2026-09-01, leaving Queensland bulletins with HERE-only evidence |
| Register for a DataVic API key, then switch VicTraffic to the authenticated `api.opendata.transport.vic.gov.au` endpoint | ARN technology lead | Before relying on Victorian authority coverage | The configured address answers 200 but serves the dataset's API specification rather than its data, so VicTraffic parses zero records. The dataset's live v2 and v3 endpoints refuse an unauthenticated caller, so the key is required before the address can change. Victorian markets carry HERE evidence only |
| Ask South Australia's road-operations team at `DIT.RoadOperations@sa.gov.au` to allowlist Azure egress address `20.92.68.208` | ARN technology lead | Before relying on Traffic SA for South Australian authority coverage | With the feed enabled, South Australia's edge refuses the platform's address with 403. South Australian bulletins fall back to HERE until the address is allow-listed |
| Enable TasAlert and Main Roads WA in **Bulletin rules** for any Tasmanian or Western Australian market that should consult them | ARN content team with platform support | Before a station in those states relies on authority corroboration | Both feeds answer and parse but are disabled at the global scope, so bulletins in those states are built from HERE evidence alone. No key or vendor request is involved: it is a setting |

## Playout scheduling

| Action | Role | Timing | Consequence if incomplete |
|---|---|---|---|
| Schedule the delivered WAV drops in the Zetta station clocks for each pilot station | ARN broadcast engineering with the station content teams | Before each station's next scheduled slot, and whenever a station clock changes | Audio can be delivered successfully but remain unscheduled, so no bulletin airs at the intended slot |

## Emergency services feed

| Action | Role | Timing | Consequence if incomplete |
|---|---|---|---|
| Obtain a DataQuoll API key, store it as the `EmergencyFeeds--ApiKey` Key Vault secret, and confirm the state-agency licensing position | ARN technology lead with the vendor account owner | Before the console's Emergency panel is expected to show anything | The panel stays empty. Nothing else changes: the integration is read-only by design, so emergency records are displayed and never reach a bulletin or air, and on-air behaviour is identical whether the key exists or not |

## Alert routing

| Action | Role | Timing | Consequence if incomplete |
|---|---|---|---|
| Nominate an on-call email or SMS receiver on an ARN-owned address and add it to `alertEmailReceivers` in the environment's deployment parameter file | ARN technology lead | Before the next deployment of any environment | Verified 2026-09-04: every environment's action group, including development, still sends every alert to the delivery representative's own mailbox as an enabled email receiver, while the deployment parameter files declare an empty receiver list. The next deployment removes that mailbox and leaves Microsoft Teams, which runs through a Logic App in the same subscription as the platform, as the only path, so it cannot report that the subscription itself is unavailable |
