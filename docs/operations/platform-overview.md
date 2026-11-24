---
title: "What This Platform Does"
description: "Read this first: what a traffic bulletin is, where the data comes from, who approves it, and what reaches air."
tags:
  - "handover"
  - "overview"
  - "operations"
aliases:
  - "what does this platform do"
  - "platform overview"
  - "how does it work"
purpose: "overview"
status: "current"
last_verified: 2026-09-01
review_after: 2026-12-01
volatility: "medium"
source_of_truth: "docs/canonical/architecture.md and the deployed platform"
verified_by:
  - "manual review"
common_queries:
  - "What does this platform actually do?"
  - "Where does the traffic data come from?"
  - "Who approves a bulletin before it airs?"
see_also:
  - "docs/canonical/architecture.md"
  - "docs/operations/operator-guide.md"
  - "docs/operations/glossary.md"
---

# What This Platform Does

Read this first. This page explains what a traffic bulletin is, how the platform produces it, and what an ARN listener can hear.

![System context](diagram:system-context.png)

The system context shows the operator console, the bulletin generation service, and the external services that supply data and deliver audio.

## What a bulletin is

A bulletin is a short piece of broadcast audio about current traffic conditions. The platform produces one bulletin for a station at each scheduled slot. A **slot** is the time in a station's schedule when a bulletin is expected, a **licence area** is the geographic service area associated with the station, and a **station** is the radio service whose voice, schedule, coverage, and editorial settings shape the bulletin. See the [glossary](glossary.md) for the full vocabulary.

The bulletin is prepared for the station and its slot, rather than being one generic recording for every ARN service. The station configuration determines which content sources are enabled and which voice and delivery settings apply.

## Where the data comes from

The traffic evidence comes from HERE live traffic services for the station's coverage area. HERE can provide incidents, traffic flow, congestion, routing information, and related location data. The platform can also include optional weather and fuel content when those sources are enabled for the station.

The platform never invents a road, an incident, or a delay. It reports what the traffic feed supplies. The operator console shows the source information used by the script, so the reviewer can check that spoken claims match the available information before approval.

## How a bulletin is made

Each bulletin follows the same broad path:

1. The station schedule becomes due. The scheduler resolves the station's current configuration and starts bulletin generation.
2. The platform collects current HERE traffic evidence and any enabled weather or fuel content for the station's coverage area.
3. A conversational workflow generates a script in the station's editorial style.
4. Deterministic slot fitting and content gates check the script against the supplied evidence. A gate either passes the script or sends the slot to the fallback path.
5. One content-safety exit checks the content before audio is prepared.
6. Audio is synthesised in the station's resolved voice. The station's delivery style and pronunciation dictionary are applied at this step.
7. The completed script, source information, and audio are presented in the operator console for a human review decision.

The console is the hand-off between automated production and broadcast operations.

## Who approves it and what reaches air

An ARN operator reviews the completed bulletin in the console. The review includes the script, the source information behind its factual claims, the expected duration, and the complete audio. If the bulletin is suitable, the operator selects **Approve**. If it needs changes, the operator can reject it with feedback and request regeneration. A rejected or regenerated draft is not the recording that should reach listeners.

Approval and delivery are separate steps. Approval places the recording on the publish path. The operator then selects **Publish**, after which the approved audio is delivered to the agreed playout system. The playout system uses that delivered recording at the scheduled slot. A draft that has not been approved and published is not sent to playout.

![The operator console home](screenshot:00-home.desktop.png)

The home page of the operator console is the starting point for current bulletin and review work.

## What happens when something goes wrong

The platform uses a two-part fallback chain as a safety property:

1. It uses the most recent usable same-day bulletin for the same station.
2. If no usable same-day bulletin exists, it uses the station's configured standby audio.

This prevents a failed fresh generation from sending false or empty audio to air. The fallback recording is identified in the bulletin history, so the outcome can be checked and escalated when needed. If neither fallback is available, the slot remains uncovered and requires intervention.

## Where it runs

Three environments run in the ARN tenant. Compute and the data plane are in Australia East; the console's Static Web Apps resources are hosted from a separate Azure location. Each environment has a distinct purpose; see the [as-built environment record](as-built.md) for current state.

| Environment | Purpose |
|---|---|
| Dev | Platform maintenance and change verification; it can never deliver audio. |
| Test | Live pilot environment; both stations generate on schedule with playout disabled, so nothing is delivered until ARN arms a station. |
| Prod | Running and held at the last known-good state as the fallback; both schedules are disabled, so it generates and airs nothing. |

Operators use the Test console for the live pilot. The Prod console is retained as the frozen fallback, and Dev is never used for delivery. The console addresses and sign-in steps are listed in the [first-time sign-in walkthrough](first-time-sign-in.md).

## Where to go next

- Read the [glossary](glossary.md) if a term such as slot, fallback, or playout is unfamiliar.
- Follow [First-time Sign-In](first-time-sign-in.md) to access the assigned console with an ARN account.
- Use the [operator guide](operator-guide.md) for review, approval, regeneration, and fallback tasks.
- Read the [architecture overview](../canonical/architecture.md) for system boundaries, runtime flow, and service responsibilities.
