---
title: "Platform Architecture"
description: "How the ARN Information Bulletin Platform runs, integrates with external services and protects operational data"
tags: [architecture, azure, productionisation, traffic-bulletins]
purpose: "reference"
status: "current"
last_verified: 2026-09-01
review_after: 2026-12-01
volatility: "medium"
source_of_truth: "The production codebase and its deployed configuration"
verified_by:
  - "manual source review"
aliases:
  - architecture
  - clean architecture
  - system design
  - azure functions architecture
common_queries:
  - "What is the current Arn architecture?"
  - "Which backend layers make up the platform?"
  - "Where should architecture changes be documented?"
see_also:
  - docs/operations/solution-structure.md
---

# Platform Architecture

The ARN Information Bulletin Platform turns live traffic conditions into
approved radio bulletins for each station. It combines traffic data, language
generation, speech synthesis, operator approval and radio playout in one
controlled flow. The [solution overview](../operations/solution-overview.md)
explains the operating model; this page describes the platform boundaries and
responsibilities.

![System context](diagram:system-context.png)

## Runtime flow

1. The scheduler starts a bulletin for a station when its configured slot is due. It reads the station's coverage, monitored roads and editorial settings.
2. The traffic integration retrieves live incidents, traffic flow, route travel times and, where enabled, weather or fuel information from HERE. The corridor and incident-road enrichment task is shared within the configured three-minute bucket, while final corridor results are cached in memory for that bucket. Static map segment attributes use a two-level memory/Table Storage cache, and forward and reverse geocoded locations use 24-hour memory caches.
3. Bulletin generation selects relevant facts, carries forward same-day context
   where available, and sends the evidence to one conversational workflow
   backed by one centralized Azure OpenAI model. The workflow drafts the script
   in the station's editorial style. Deterministic slot fitting checks the
   workflow result against the slot, and deterministic content gates check it
   against the supplied traffic evidence. One content-safety exit sends an
   unsafe or unsuccessful result to fallback. There is no separate reviewer,
   refiner or whole-script retry loop.
4. ElevenLabs synthesises the gated script using the station's selected voice.
   The delivery style and pronunciation dictionary guide how the read sounds
   without changing the factual content. The automated audio check compares
   the spoken audio with the gated script.
5. The platform stores the audio and bulletin record, then presents both to an operator in the console. The operator can approve, reject or request a new attempt.
6. Approved audio is delivered to the Zetta playout system. Notification messages keep the operational channel informed of important status changes.
7. If a fresh bulletin cannot be produced safely, the platform uses the most recent approved bulletin from the same day. If no same-day bulletin is available, it uses standby audio. Where neither is available the slot stays uncovered and needs operator intervention. This fallback keeps false or empty audio off air.

## Component responsibilities

| Component | Reader-facing responsibility |
|---|---|
| Console | Web application used by operators and administrators to review bulletins, manage station settings and inspect activity. |
| API and processing services | Apply bulletin rules, coordinate external calls, track status and enforce server-side permissions. |
| Scheduler | Starts scheduled bulletins from each station's configured slots. |
| Queue processor | Runs asynchronous bulletin work and keeps long-running generation separate from interactive console requests. |
| Script generation | Combines traffic evidence, station settings and same-day context, sends them to one conversational workflow backed by one centralized Azure OpenAI model, applies deterministic slot fitting and content gates, and uses one content-safety exit for unsafe or unsuccessful results. |
| Audio services | Call ElevenLabs, apply voice, delivery and pronunciation settings, and run the automated audio check. |
| Delivery and notifications | Send approved audio to Zetta and send status notifications through Microsoft Teams. |
| Monitoring | Records health, latency, failures and costs in Azure Monitor and Application Insights. |

## External services

| Service | Use in the platform | Effect of an outage |
|---|---|---|
| HERE | Supplies traffic incidents, traffic flow, route travel times, geocoding and optional weather or fuel information. | Fresh traffic evidence is unavailable, so the platform uses its fallback path. |
| Azure OpenAI | Backs the single conversational workflow that drafts the script in the station's editorial style. | Its outage means that no fresh script can be produced. |
| ElevenLabs | Synthesises the script into speech using the station's selected voice. | A fresh audio bulletin cannot be prepared. |
| Azure AI Speech | Compares a transcription of generated audio with the script during the automated audio check. | The audio check cannot complete. |
| Microsoft Entra ID | Authenticates console users and supplies their platform role. | Console access fails closed. |
| Azure Storage | Persists bulletin records, station settings, traffic state, static map segment attributes, audio and asynchronous work. | New bulletins cannot be tracked, stored or processed reliably. |
| Azure Key Vault | Holds per-environment credentials and other secrets. | External service calls cannot obtain their credentials. |
| Microsoft Teams through Power Automate | Sends operational notifications and reminders. | The bulletin can continue, but notification delivery is unavailable. |
| Azure Static Web Apps | Hosts the console. | Operators cannot use the web console. |
| Azure Monitor and Application Insights | Collects logs, metrics, health information and operational workbooks. | The platform has reduced diagnostic visibility. |
| Zetta playout | Receives approved audio for broadcast. | Approved audio cannot reach on-air playout. |

## Identity and network posture

Microsoft Entra ID is required for console access, and access fails closed when sign-in cannot be established. Roles map to Operator or Admin, with optional station scoping. Server-side authorisation protects API actions as well as console controls.

Secrets live in a separate Key Vault for each environment and are read through managed identity. No storage, database or vendor connection string is held in application settings. Development, test and production are separate environments in a dedicated Azure subscription. Compute and the data plane run in Australia East; the console's Static Web Apps resources are hosted from a separate Azure location.

The console is hosted by Azure Static Web Apps and the API and processing services run on Azure Functions. In test and production, protected Azure data services are reached over private endpoints and their public data plane is disabled; development runs the same services on a public data plane. Vendor calls use HTTPS and the playout connection follows the controlled ARN network route.

![Network topology](diagram:network-topology.png)

Use this figure when investigating a sign-in path, private endpoint, DNS, firewall or playout-network issue.

## Data stores

| Store | What it contains |
|---|---|
| Azure Table Storage | Bulletin records and history, station schedules and settings, traffic incidents and same-day traffic state, and the persistent map-attributes cache. |
| Azure Blob Storage | Generated bulletin audio and standby audio. |
| Azure Queue Storage | Asynchronous work waiting for processing. |
| Azure Key Vault | Credentials and other secrets for one environment. |
| Azure Monitor and Application Insights | Logs, metrics, health signals and operational workbooks. |

## Station settings

Each station has versioned settings for voice, optional voice rotation, delivery style, pronunciation dictionary, content sources, priority roads, monitored routes and editorial guidance. The scheduler resolves the settings for each slot, applying any station or licence-area scope before generation. See [per-station configuration](../reference/per-station-configuration.md) for the fields an ARN administrator can change and how they affect a bulletin.
