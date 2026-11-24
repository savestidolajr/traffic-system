---
title: "Support Model"
description: "Three support tiers, vendor escalation paths, contact ownership and the operating workload for the ARN Information Bulletin Platform."
tags:
  - "operations"
  - "support"
  - "escalation"
  - "handover"
aliases:
  - "support model"
  - "escalation matrix"
  - "support contacts"
purpose: "runbook"
status: "current"
last_verified: 2026-09-01
review_after: 2026-11-27
volatility: "medium"
source_of_truth: "The support roles, escalation paths and operating workload"
verified_by:
  - "manual review"
common_queries:
  - "Who do I contact when something breaks?"
  - "What can operators fix themselves?"
  - "Who covers broadcast hours?"
see_also:
  - "docs/operations/runbook.md"
  - "docs/operations/access-and-accounts.md"
  - "docs/operations/dependencies.md"
  - "docs/operations/known-limitations.md"
---

# Support Model

Support is organised in three tiers. Start with the lowest tier that can resolve the issue, then escalate with the environment, bulletin reference, time, observed behaviour and evidence. Bulletin generation uses centralized Azure OpenAI script generation in Dev, Test and Production; deterministic post-workflow slot fitting and content/safety gates decide what can proceed. The [runbook](runbook.md), including its [ARN Self-Service Operations Guide](runbook.md#arn-self-service-operations-guide), contains the first-response checks and recovery actions.

## Support tiers

| Tier | Role | Responsibility | Escalation trigger |
|---|---|---|---|
| **1, operator support** | ARN operators and content staff | Review the console, approve or reject bulletins, correct station content and configuration, and use the operator guidance for pronunciation, style and delivery settings | Escalate when the console, a control or an on-air outcome cannot be corrected through the documented operator actions |
| **2, platform operations** | ARN platform support | Check health, alerts, queues, deployments, access, secrets and service configuration. Coordinate the incident record and protect on-air continuity | Escalate when the fault persists after the [first-response procedure](runbook.md), requires a provider action or affects more than one environment |
| **3, vendor and platform escalation** | ARN platform support with the relevant provider or playout support role | Raise and manage provider incidents, quota requests, platform support requests and Zetta delivery issues until service is restored | Close when the provider or playout support role confirms recovery and the affected bulletin path has been witnessed |

## Diagnostic triage

Record the environment, station, job ID, scheduled slot, UTC timestamps and correlation IDs before changing configuration. For traffic, inspect the job's source diagnostics and review the coverage and unknown-authority diagnostics for per-source availability, coverage trends and unrecognised authority labels. Compare the affected authority feed with the HERE fallback; a source marked `Failed` or `Skipped` is not evidence that all traffic coverage is unavailable.

For emergency services, inspect the emergency-services source diagnostic and the Adjust tab's Emergency panel. Phase 1 events are persisted and displayed only; they are structurally excluded from the writer and cannot reach a bulletin. Do not treat an absent emergency item as a generation defect. Escalate authentication or provider failures with the source ID, response status and time, and do not enable an on-air path without an approved Phase 2 change.

For Zetta, confirm the station's live Send to playout setting and the environment's playout enablement flag, then inspect the playout tray and Function App delivery logs. The delivery service writes the approved WAV through the Azure Files REST API using the Function App managed identity to `ibpauetststaftp01/informationbulletins`; verify the write, expected Zetta deletion after ingest and the one-environment-per-station arming rule before escalating to broadcast engineering.

## Escalation contacts and hours

Broadcast hours are 5 am to 10 pm, seven days a week, and the tiers above assume a trained responder is available throughout them. ARN maintains the named support contacts, agreed channels and coverage hours in its controlled support register. The register covers these roles:

- Platform support owner
- Content and operations owner
- After-hours contact
- Azure platform and Azure OpenAI escalation
- HERE vendor escalation
- ElevenLabs vendor escalation
- Microsoft Teams and Power Automate escalation
- Microsoft Entra ID escalation
- Zetta playout escalation

### Handover contacts

ARN completes this table at the handover session. Until it is completed, escalation has no route.

| Role | Name | Contact | Hours |
|---|---|---|---|
| Technology lead | | | |
| Platform support | | | |
| Subscription administrator | | | |
| Pipeline owner | | | |
| Identity team | | | |
| Broadcast engineering | | | |
| Content team | | | |
| Security owner | | | |
| M365 service owner | | | |


Outside broadcast hours there are no scheduled bulletin slots, so a fault discovered overnight is triaged when the next broadcast window approaches unless it is already affecting on-air continuity. In that case the after-hours contact uses the same escalation path as during hours; anything that cannot wait for the next window is raised immediately rather than queued.

## Vendor escalation paths

| Service | Escalate when | Escalation path | On-air response while the issue continues |
|---|---|---|---|
| Azure platform and Azure OpenAI | Functions, storage, Key Vault, networking or script-generation errors persist after the documented retries | Open an Azure Portal support request against the affected resource and include the environment, time, correlation evidence and service impact | New bulletin generation may stop. Use the existing same-day fallback and station standby audio while the provider recovers |
| HERE | Traffic fetch errors, quota exhaustion or sustained incorrect traffic responses | Raise a case through the HERE developer support portal with the station, coverage and incident evidence | Bulletins may contain less traffic detail. Do not approve a bulletin whose claims cannot be traced to its source panel |
| ElevenLabs | Audio synthesis failures, quota exhaustion or voice service faults | Raise a case through the ElevenLabs support portal with the voice, bulletin reference and failure evidence | The platform falls back to same-day audio or station standby audio rather than airing unverified audio. Escalate immediately if a slot is reported uncovered |
| Microsoft Teams and Power Automate | Approval, reminder or failure notifications stop arriving or links fail | Check the configured connection and raise a Microsoft support request if the service remains unavailable | Operators continue to use the console and the activity record. Escalate missed deadlines through the platform operations tier |
| Microsoft Entra ID | Sign-in, role mapping or group access fails | Check the user's group assignment, then raise an Azure Portal identity support request with the environment and role | Users without access must not share accounts. The platform remains fail-closed |
| Zetta playout | A published WAV cannot be retrieved, ingested, played or removed | ARN broadcast engineering raises the issue with the Zetta supplier support role and supplies the playout-drop and console evidence | Keep the station's fallback path available and confirm the next playable source before the slot |

## Operating workload

The workload varies with the number of stations, schedules and bulletin slots. The following cadence describes the ongoing work without assuming a particular staffing structure.

| Area | Normal workload |
|---|---|
| Platform operations | A short daily health, alert and queue review, a weekly cost and alert review, and occasional deployments or secret rotations. Incident days require additional investigation and coordination |
| Operator review | Each bulletin requires a short console review before approval. The time rises when a station has more incidents, a source claim needs checking or a bulletin requires regeneration |
| Broadcast coverage | Broadcast hours are 5 am to 10 pm, seven days a week. At least two trained platform responders provide practical coverage so early and late shifts do not depend on one person |
| Required capability | Azure Functions, Storage, Key Vault, Monitor and KQL; identity and access administration; service-provider support; and working familiarity with broadcast playout |
| Training | Maintain capability through a supervised deployment and park-or-resume exercise in Test, a runbook walkthrough, basic query practice and periodic review of the HERE, ElevenLabs, Zetta and Azure support portals |

## Transition into ARN support

ARN owns routine operation, access, vendor contacts and service review. Any later assistance is arranged separately.

## Broadcast-hours coverage

The platform is intended to support broadcast hours from 5 am to 10 pm, seven days a week. The operator tier covers content decisions, the platform operations tier covers service health and delivery, and the vendor escalation tier handles provider or playout faults. Keep a trained responder available throughout those hours and retain the fallback path for any unresolved issue.
