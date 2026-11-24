---
title: "External Service Dependencies"
description: "The external services used by the ARN Information Bulletin Platform, their operational effect, credential location, rotation path and ARN owner."
tags:
  - "operations"
  - "dependencies"
  - "vendors"
aliases:
  - "external dependencies"
  - "vendors"
  - "third party services"
  - "what breaks if"
purpose: "reference"
status: "current"
last_verified: 2026-09-01
review_after: 2026-11-01
volatility: "medium"
source_of_truth: "Azure resource inventory, Key Vault configuration and service ownership records"
verified_by:
  - "Environment dependency review"
common_queries:
  - "What happens if HERE goes down?"
  - "Where is the ElevenLabs key stored?"
  - "Who escalates a vendor outage?"
see_also:
  - "docs/operations/runbook.md"
  - "docs/operations/support-model.md"
  - "docs/operations/known-limitations.md"
---

# External Service Dependencies

The ARN Information Bulletin Platform fails safe when a dependency prevents a
new bulletin from completing. It uses the most recent same-day bulletin, then
standby audio, rather than sending unverified content. Where neither is
available the slot stays uncovered and needs operator intervention. The
register below identifies the service owner and the action that restores each
path.

## Dependency register

| Service | What it does | What breaks without it | Credential and rotation | ARN owner and escalation |
|---|---|---|---|---|
| **HERE** | Supplies traffic incidents, flow, congestion, routing, geocoding and the optional weather and fuel sources used for bulletin evidence, plus every console map (station coverage editor, card thumbnails, evidence map) and the address search | No fresh traffic evidence is available. Bulletin generation falls back to the most recent same-day bulletin or standby audio. Without `Here--MapApiKey` the console maps show **Map unavailable** and address search is disabled; coverage settings still save and generation is unaffected | The runtime keys are `Here--ApiKey` and `Here--MapApiKey` in `IBP-AUE-{ENV}-KYV-APP01`. Update the affected Key Vault secret, restart the Function App so it reloads Key Vault, check the health endpoint and complete one controlled generation | **HERE vendor account owner** records the environment, request time, response status and bulletin ID, then opens HERE vendor support for persistent errors or quota issues |
| **Azure OpenAI** | Turns traffic evidence and station settings into a reviewed script for audio synthesis | No new script reaches operator review. The same-day fallback or standby audio path is used | All environments use one centralized Azure OpenAI model with no additional reasoning effort. Authentication is the Function App managed identity with the Cognitive Services User role; there is no runtime API key to rotate | **Subscription administrator** checks the Azure AI service and quota, then opens Azure support with the environment, deployment name, timestamp and correlation ID |
| **ElevenLabs** | Synthesises the reviewed script into broadcast audio using the station voice and pronunciation configuration | A script may complete but no usable broadcast audio is produced. The same-day audio or standby audio path is used | The key is `AiSettings--ElevenLabs--ApiKey` in `IBP-AUE-{ENV}-KYV-APP01`. Update the secret, restart the Function App so it re-reads the vault, check the health endpoint and complete one controlled generation | **ElevenLabs vendor account owner** retains at least two ARN workspace administrators, checks subscription and character quota, and opens ElevenLabs support for synthesis or voice faults |
| **Azure AI Speech** | Transcribes completed audio so the automated audio check can compare spoken words, numbers and pronunciation with the script | The automated audio check has no transcription evidence. Audio still reaches operator approval, but this safeguard is weaker | Speech uses the shared Azure AI Services identity and does not have a separate runtime secret. Retain the Cognitive Services User role; if the API-key path is used, rotate the shared Azure AI key | **Subscription administrator** checks the Azure AI resource and opens Azure support for persistent transcription failures |
| **Microsoft Teams through Power Automate** | Sends review, deadline, failure and selected platform alert cards to the station operations channel | Bulletin generation and delivery can continue, but operators may miss a review window or deadline. The console remains the operational path | The Logic App trigger URL is `Webhook--EndpointUrl` in `IBP-AUE-{ENV}-KYV-APP01`. The Teams OAuth credential is held by the environment connection resource. Re-authorise that connection with an ARN identity, refresh the trigger URL if it changes, and prove one card | **Microsoft 365 service owner** owns the channel and connection. Escalate tenant or channel issues through ARN collaboration support, and Azure resource faults through Azure support |
| **Microsoft Entra ID** | Provides sign-in and the Operator and Admin roles for the console and protected API | Operators cannot sign in, review, approve or publish a bulletin. Scheduled processing uses the fallback path for the affected slot | The custom console provider uses the public `aadClientId` value from the environment bicepparam plus the `AadClientSecret` secret in `IBP-AUE-{ENV}-KYV-APP01`. The Entra application owner issues a new secret, updates Key Vault and redeploys the Static Web App settings | **Entra application owner** manages app roles, redirect URLs, consent and secret renewal. Escalate tenant identity faults through ARN identity support |
| **Zetta playout** | Receives the approved WAV through the `informationbulletins` share for use at the scheduled slot | Generation, review and approval can complete, but the new audio does not reach playout | Production and TEST write through the Azure Files Drop transport (Files REST) to storage account `ibpauetststaftp01`, file share `informationbulletins`. The platform uses its managed identity, so there is no delivery secret to rotate. Only one environment may arm a station at a time; verify the station's **Send to playout** switch before a delivery test | **Playout integration owner** confirms the share, managed-identity data-plane role, file name and ingest status with ARN/Zetta support |
| **Azure Storage** | Holds bulletin records, schedules, station configuration and incident state in Table Storage, generation work in Queue Storage, and WAV and audit exports in Blob Storage | The Function App cannot read or write state, consume work or retrieve audio. New bulletins stop progressing and no new recording is delivered | The Function App uses managed identity roles. There is no application storage connection string or storage secret to rotate. Check managed identity assignments, private endpoints and private DNS before restarting | **Subscription administrator** owns storage roles and network access. Escalate persistent Storage or private endpoint faults through Azure support |
| **Azure Monitor and Log Analytics** | Collects worker logs, metrics and availability checks, and provides one operations workbook per environment (three in total, verified 2026-08-12) and the alert rules | Bulletin generation can continue, but operators may lose timely alerts and dashboard evidence | No application secret is used. Check diagnostic settings, workspace access and managed identity permissions; there is no monitoring credential to rotate | **Platform operations owner** checks alert history and workbook access, then escalates persistent Azure Monitor or Log Analytics faults through Azure support |

## Official traffic feeds

Six authority feeds are wired. The platform-wide configuration switches on two
of them, Live Traffic NSW and QLDTraffic; VicTraffic, Main Roads WA, Traffic SA
and TasAlert are disabled at the global editorial scope, so a bulletin records
them as skipped rather than attempted. Enabling a feed for a market or a
station is a setting in **Bulletin rules**, not a code change.

Feed credentials are held in Key Vault where required. A failed authority feed
is recorded as failed or skipped and HERE remains the fallback source. The
status column states each feed's current position and what it needs from ARN.

| Feed | Endpoint and credential | Status and what it needs | Failure mode and fallback |
|---|---|---|---|
| `livetraffic-nsw` | `https://www.livetraffic.com/traffic/hazards` (the service appends `incident-open.json` and `roadwork-open.json`); keyless | Enabled and answering. Nothing required | Authority outage or malformed response; use HERE |
| `qldtraffic` | `https://api.qldtraffic.qld.gov.au/v2/events?apikey=...`; shared public demonstration key | Enabled. The shared demonstration key is throttled with `429` at its 100-per-minute cap, and has been refused with `401` in development since 2026-09-01 | **ARN needs a dedicated QLDTraffic authority key**, which removes both the throttling and the refusal. Queensland bulletins fall back to HERE meanwhile |
| `victraffic` | `https://opendata.transport.vic.gov.au/.../download/unplanned_disruptions.json`; keyless | Disabled, and would contribute nothing if enabled: the configured address serves the dataset's OpenAPI specification rather than its data, so the feed parses zero records | The dataset's live endpoints are v2 and v3 under `api.opendata.transport.vic.gov.au`, and both refuse an unauthenticated caller. **ARN needs a DataVic API key**, after which the address changes to the authenticated endpoint. Victorian markets carry HERE evidence only until then |
| `mainroads-wa` | `https://gisservices.mainroads.wa.gov.au/arcgis/rest/services/TravelInformation/MapServer`; keyless | Disabled, and answers when enabled | **ARN enables it** for any Western Australian market that should consult it. Authority outage or ArcGIS failure falls back to HERE |
| `trafficsa` | `https://dit-cms-prod.geohub.sa.gov.au/arcgis/rest/services/TrafficSATime/TrafficSATime/MapServer/1/query?where=1%3D1&outFields=*&returnGeometry=false&f=pjson`; keyless | Disabled, and blocked when enabled: South Australia's edge refuses the platform's Azure egress address with `403` | **ARN needs the egress address allow-listed** by South Australia's road-operations team. South Australian bulletins carry HERE evidence only until then |
| `tasalert` | `https://www.police.tas.gov.au/news-events/media-releases/feed/`; keyless RSS/prose feed | Disabled, and works when enabled: it answers and parses against a Tasmanian market | **ARN enables it** for any Tasmanian market that should consult it. RSS outage or records without coordinates falls back to HERE |

## DataQuoll emergency feed

`dataquoll` is configured with BaseUrl `https://dataquoll.io/api/v1` and the
Bearer credential in Key Vault secret `EmergencyFeeds--ApiKey`. Phase 1 is
disabled by default and persists and displays emergency records in the
console only; it is excluded from script generation and cannot affect what
airs. Enable it only after ARN approves the data and licensing path.

## Rotation and incident sequence

For a secret-backed dependency, use this sequence:

1. Confirm the affected environment and capture the service error, timestamp and
   bulletin or correlation ID.
2. Rotate the vendor or application credential at its source, then update the
   matching Key Vault secret.
3. Restart Function App after Key Vault update.
4. Check the environment health endpoint and complete one controlled generation
   or delivery test.
5. Record the result in the incident record and notify the service owner above.

For a managed-identity dependency, do not create a replacement secret. Check the
role assignment, private endpoint and DNS path first. The [support model](support-model.md)
sets out the escalation path when the service remains unavailable.
