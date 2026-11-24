---
title: "Solution Overview"
description: "How the platform is put together, what each part is responsible for, and the boundaries of what it does."
tags:
  - "architecture"
  - "overview"
aliases:
  - "solution overview"
  - "how is it built"
  - "system design"
  - "architecture summary"
purpose: "overview"
status: "current"
last_verified: 2026-09-01
review_after: 2026-12-01
volatility: "medium"
source_of_truth: "The deployed platform"
verified_by:
  - "manual review"
common_queries:
  - "How is the platform put together?"
  - "What is each component responsible for?"
  - "What is in and out of scope?"
see_also:
  - "docs/canonical/architecture.md"
  - "docs/reference/architecture-diagrams.md"
  - "docs/operations/known-limitations.md"
---

# Solution Overview

The ARN Information Bulletin Platform turns live traffic information into
approved radio audio for each configured station. It combines a schedule,
traffic and related data from HERE, script generation in Azure OpenAI, speech
synthesis in ElevenLabs, a human approval step in the console, and delivery to
Zetta. The platform is designed to produce a useful bulletin on every
scheduled occasion while preventing unsupported information or empty audio
from reaching air.

## The end-to-end flow

![Platform architecture](diagram:platform-architecture.png)

The platform follows the same sequence for a scheduled or manually requested
bulletin:

1. **The scheduler starts the work.** Each station has a local schedule with
   service hours, slots, a target duration and a station-specific licence area.
   At the scheduled time, the scheduler creates a request for that station.
   An authorised administrator can also start a request when an operational
   check or recovery requires one.
2. **The platform obtains current source data.** It asks HERE for the traffic
   incidents and traffic flow relevant to the station's coverage area. The
   platform can also obtain weather and fuel information where the station has
   those sources enabled. Data outside the configured freshness window is
   discarded before it can shape a script.
3. **Bulletin generation selects and orders facts.** The platform ranks the
   available incidents and clear roads according to the station's settings,
   then sends the selected evidence to one conversational workflow backed by
   one centralized Azure OpenAI model. The workflow drafts a concise script in
   the station's editorial style, constrained by the selected source data, the
   target duration and the station's content priority order.
4. **Deterministic fitting and content gates check the result.** Slot fitting
   checks the workflow result against the target slot, while content gates
   check it against the evidence supplied for that bulletin. One
   content-safety exit sends an unsafe or unsuccessful result to the fallback
   path. There is no separate reviewer, refiner or whole-script retry loop.
5. **Audio is synthesised and checked.** ElevenLabs converts the gated script
   to audio using the station's assigned voice and pronunciation settings. The
   automated audio check compares the spoken audio with the gated script,
   checks duration and confirms that the recording is usable. The platform
   records the audio and the deterministic gate results.
6. **An operator makes the publication decision.** The console presents the
   script, selected incidents, source details and audio to an authorised
   operator. The operator can approve, reject, or edit and regenerate within
   the permitted workflow. Approval records the person's identity and time.
7. **Approved audio reaches playout.** When a person publishes an approved
   bulletin, the platform checks the recording against the record it approved
   and writes it to the station's playout drop share under the file name ARN
   supplied. The playout system collects the file itself and removes it once
   ingested, so a file left in the share means the delivery was not consumed.
   A failed write leaves the bulletin approved so it can be published again.
8. **A safe result covers an unsuccessful attempt.** If a bulletin cannot be
   produced safely, the platform uses the most recent approved bulletin from
   the same day for that station. If none exists, it uses the station's
   pre-recorded standby audio. Where neither is available the slot stays
   uncovered and needs operator intervention. This two-step fallback prevents
   stale information from being presented as current and keeps an empty result
   out of playout.

The operator remains the final decision-maker. Deterministic slot fitting and
content gates constrain the workflow result, and one content-safety exit sends
an unsafe or unsuccessful result to fallback. These controls do not publish a
bulletin without an approval decision. The [operator guide](operator-guide.md)
describes the review surface, while the [runbook](runbook.md) covers recovery
when a step does not complete.

## Responsibility by platform part

| Part | Responsibility | What it does not decide |
| --- | --- | --- |
| **The scheduler** | Starts a bulletin for each station at the configured local time, keeps requests separate by station and records missed or delayed schedule activity. | It does not choose words, approve content or bypass the review step. |
| **Bulletin generation** | Fetches current source data, selects the most useful facts, sends them to one conversational workflow backed by one centralized Azure OpenAI model, applies deterministic slot fitting and content gates, and uses one content-safety exit for unsafe or unsuccessful results. | It does not invent a cause, delay, route, closure or weather condition that is absent from the source data. |
| **The console** | Gives operators the source panel, script, audio, status, feedback and approval controls. It limits actions according to the person's role and station scope. | It does not grant access, replace the server-side approval rules or publish an unapproved recording. |
| **Audio delivery** | Converts the approved script to station voice audio, checks the recording and writes it to the playout drop share under ARN's file name for the station. | It does not operate Zetta's clock, collect the file from the share, manage commercial credits or decide which bulletin is editorially correct. |
| **Monitoring** | Captures health, latency, queue activity, external dependency results, cost signals and audit events. It presents the signals through Azure Monitor workbooks and alerts. | It does not repair a vendor outage or turn an alert into an approval decision. |

These boundaries are useful when diagnosing an incident. For example, an
empty console may be an access or service problem, a late script may be a
source or generation problem, and an approved recording that does not appear
in playout is a delivery or Zetta problem. The [dashboard guide](dashboard-guide.md)
maps each symptom to the monitoring view that can confirm it.

## Where the data comes from

HERE is the primary external source for traffic information. The platform
receives structured incidents and flow information, including road names,
direction where available, closure state, incident type, descriptive details
and measured congestion. A jam factor is the traffic feed's congestion score,
from 0 clear to 10 blocked. The platform uses that score as one signal when
deciding which roads deserve attention, rather than presenting the score as
an unsupported claim about a listener's journey.

Weather and fuel are optional station sources supplied through HERE. Their
inclusion is controlled by station configuration. Where enabled, selected
weather or fuel facts are included in the bulletin, with traffic still
providing the primary road information unless a significant weather condition
changes the editorial priority.

The platform does not treat raw source text as an instruction. Source fields
are normalised, stale information is removed, and only the selected facts are
made available to bulletin generation. The resulting script is retained with
the source evidence that supported it. This gives an operator enough context
to check a claim quickly and gives support staff a way to distinguish a
source-data issue from a generation or delivery issue.

Audio and operational records are stored in Azure Storage. The console reads
them through authenticated platform endpoints rather than exposing storage
locations directly. Approval, rejection, regeneration, delivery and fallback
events are recorded so that an operator can explain what was aired and
support staff can trace the decision.

For external service ownership, credentials and recovery actions, see
[external service dependencies](dependencies.md). For live success, latency
and availability figures, see [SLIs and SLOs](slo.md).

## How station settings are applied

Station identity is configuration, not a separate software build. A station
record identifies its licence area, schedule, service hours, target duration,
content sources, priority routes, voice, delivery style, pronunciation
dictionary and playout destination. The settings also determine which
operators can review that station's bulletins.

The scheduler resolves the active settings when it starts each bulletin.
This means a change made in the console is applied to the next eligible
bulletin without rebuilding a release or editing a scheduled task. The
resolved settings are kept with the bulletin, so a later configuration
change cannot rewrite the record of what an operator reviewed and approved.
Reusable station libraries, including voice, delivery style and pronunciation
rules, are versioned. An administrator can assign a new version for a future
bulletin while preserving the previous version for traceability and rollback.
The [per-station configuration guide](../reference/per-station-configuration.md)
explains how those settings are maintained.

## Scope boundaries

The platform produces traffic bulletin audio and delivers it to Zetta. It does
not replace Zetta's playout clock, manage emergency broadcast suppression,
manage commercial credits or provide a general-purpose newsroom. It does not
offer native mobile applications, migrate historical traffic records, train a
new language model or publish without a human approval decision.

The platform also does not turn incomplete source information into advice. It
does not tell a listener what to do, infer a diversion that HERE has not
provided or state a delay that has not been measured. When source information
is too thin for a safe bulletin, the fallback rules apply. Known deployment,
dependency, coverage and recovery boundaries are listed in [known
limitations](known-limitations.md).

## Deployment and updates

The platform runs in a dedicated Azure subscription for ARN with three
environments. Compute and the data plane run in Australia East; the console's
Static Web Apps resources are hosted from a separate Azure location. Each
environment has a distinct purpose; see the [as-built environment record](as-built.md)
for current state:

| Environment | Purpose |
| --- | --- |
| **Development** | Change verification and operational investigation. |
| **Test** | Pilot and acceptance testing for KIIS 90s Sydney and Star 102.7 FM Cairns. |
| **Production** | Authorised production bulletin operations. |

Each environment has its own Function App, storage, monitoring workspace and
Key Vault. Microsoft Entra ID protects the console and API, and managed
identity is used for platform access to storage and secrets. No storage,
database or vendor connection string is held in application settings. External
service credentials are kept in the Key Vault belonging to the relevant
environment.

![Delivery pipeline](diagram:cicd-pipeline.png)

A change is built once, checked in development, exercised in test and then
promoted to production through the controlled deployment process. The process
uses the same versioned release through each environment, runs a health check
after deployment and can roll back when the post-deployment check fails.
Production promotion requires explicit operational approval. Infrastructure
and application configuration are kept separate from station content, so
changing a voice, route list or pronunciation rule does not require a
software release.

The [solution architecture](../canonical/architecture.md) gives the
component-level view. The [architecture diagrams](../reference/architecture-diagrams.md)
provide the visual views used when investigating network, security or
delivery paths. For day-to-day changes, follow the [runbook](runbook.md) and
check the relevant [service levels](slo.md) after the release.
