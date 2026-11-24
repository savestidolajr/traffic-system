---
title: "As-Built: Deployed Environments"
description: "The deployed ARN Information Bulletin Platform across development, test and production, including resource inventory, hostnames, network topology and identity model."
tags: [operations, as-built, inventory, environments, deployment]
purpose: "reference"
status: "current"
last_verified: 2026-09-01
review_after: 2026-11-17
volatility: "high"
source_of_truth: "Azure resource inventory, environment health checks and platform configuration"
verified_by:
  - "Azure resource inventory"
  - "Environment health and smoke checks"
aliases:
  - as-built
  - environment inventory
  - deployed resources
  - what is deployed
common_queries:
  - "What is deployed in each environment?"
  - "What are the environment URLs?"
  - "How do the environments differ?"
see_also:
  - "docs/operations/runbook.md"
  - "docs/operations/notifications.md"
  - "docs/operations/disaster-recovery.md"
---

# As-Built: Deployed Environments

This page records the deployed state of the ARN Information Bulletin Platform.
The three environments share one Azure subscription in Australia East. Use the
resource names and hostnames below when working in the Azure portal or the
console.

## Environment matrix

| | Development | Test | Production |
|---|---|---|---|
| Subscription | `5e5f8695-2207-4572-8e80-1065a1b0c135` in the ARN tenant | Same subscription | Same subscription |
| State | LIVE public data plane, but cannot deliver to playout | DEPLOYED, generating bulletins on schedule but delivering none to playout (verified 2026-09-03) | DEPLOYED with schedules currently disabled, generating and airing nothing until ARN enables a station (verified 2026-09-03) |
| API | `https://ibp-aue-dev-fna-app01.azurewebsites.net/api` | `https://ibp-aue-tst-fna-app01.azurewebsites.net/api` | `https://ibp-aue-prd-fna-app01.azurewebsites.net/api` |
| Console | `https://ain-dev.arn.com.au` | `https://ain-test.arn.com.au` | `https://ain.arn.com.au` |
| Network | Public data plane, no virtual network or private endpoints | ARN hub-connected virtual network `10.241.91.0/24`, five private endpoints, data plane disabled | Dedicated virtual network `10.61.0.0/24`, five private endpoints, data plane disabled |
| Authentication | Microsoft Entra ID with a development-only administrative fallback | Microsoft Entra ID, role resolution from the API, no fallback | Microsoft Entra ID through the ARN production application registration |
| Storage | Standard_LRS, 30-day blob soft delete | Standard_ZRS, 90-day blob soft delete, 30-day point-in-time Blob restore | Standard_ZRS, 90-day blob soft delete, 30-day point-in-time Blob restore |
| Notifications | Logic App and Microsoft Teams channel for development alerts | Logic App and Microsoft Teams channel for test alerts | Logic App and Microsoft Teams channel for production alerts |
| Station configuration | Seeded configuration; `Zetta__Enabled=false`, so Dev cannot deliver | `cairns-4ca` (Star 102.7) and `sydney` (KIIS 90s) both with `Enabled=true` and `PlayoutEnabled=false` (verified 2026-09-03) | Both schedules disabled (`Enabled=false`, `PlayoutEnabled=false`) (verified 2026-09-03) |
| Deployment | Azure DevOps deployment | Controlled deployment by the deployment approver | Controlled deployment by the deployment approver |

The station flags and schedule assignments above are a point-in-time description of
live behaviour, verified 2026-09-03. Test and production share the Azure Files drop, so confirm which station is armed in which environment before changing schedules. Treat `GET /api/schedules` in the
relevant environment as the source of truth before arming, holding back, or
diagnosing a station, and check it before relying on any written state in this
page. Test has both `cairns-4ca` and `sydney` enabled with `PlayoutEnabled=false` on both, so test generates bulletins on its schedule and delivers none to playout. An operator set `PlayoutEnabled=false` on both test stations at 04:58Z on 2026-09-03.
Production has both schedules disabled (`Enabled=false`, `PlayoutEnabled=false`), so production generates nothing and airs nothing until ARN enables a station. Dev has `Zetta__Enabled=false` and
cannot deliver.

Production's disabled state was confirmed against telemetry on 2026-09-03, not
only against configuration. Its scheduler is running and checked for due
stations 20,154 times in the preceding fourteen days, because a station is
armed individually rather than per environment: the scheduler finds nothing
armed and creates no bulletin. Production last generated a scheduled bulletin
on 2026-08-27, and the two it generated on 2026-09-03 were the deliberate
verification run after the release was promoted. A resumption of scheduled
generation in production is therefore visible as
`broadcast.stage.script_generate.duration_seconds` events in
`IBP-AUE-PRD-AIN-APP01`, and is the signal to check what has been armed.

The custom domains are live and validated: `ain.arn.com.au`, `ain-test.arn.com.au` and `ain-dev.arn.com.au` are bound to the production, test and development Static Web Apps with SWA-managed TLS.
Dev has enterprise edge enabled. Test and production use SWA-managed TLS without enterprise edge until ARN approves that deployment; those environments may still land on a non-local stamp.

## Identity model

- The Function App rejects unauthenticated requests. Microsoft Entra app roles
  map signed-in principals to Operator or Admin access.
- The console creates an operator record when a signed-in principal has no
  station assignment. An empty assignment means access to every station. A
  non-empty assignment limits an Operator to the listed stations.
- The console can enrich the Operators view from Microsoft Graph. Graph consent
  is optional. Without consent, signed-in operators can still use the console.
- Test and production use the Entra-backed console provider and the API's role
  resolution. Development uses its built-in provider and development-only
  fallback behaviour.
- The Function App and its staging slot use managed identity for Azure Storage,
  Key Vault and Azure AI Services. The identities hold Storage Blob, Queue and
  Table Data roles, Key Vault Secrets User and Cognitive Services User.
- Three deployment service principals, one per environment, currently hold
  standing subscription Owner and Contributor. Data-plane permissions are
  assigned separately on each environment's Key Vault and storage account.
  Reducing these to just-in-time access is a hardening step ARN may choose to take; it is not required to operate the platform.

## Secrets and protected configuration

Secrets are held in the per-environment Key Vault named
`IBP-AUE-{ENV}-KYV-APP01`. At runtime the Function App uses the
Key Vault integration with its managed identity over the Key Vault private
endpoint. Double hyphens in a vault name map to configuration separators
(`Here--ApiKey` becomes `Here:ApiKey`).

| Runtime Key Vault secret | Source or purpose | Consumer | Status |
|---|---|---|---|
| `AiSettings--AzureAI--ApiKey` | Azure AI key placeholder | Azure AI Services | Inert and must stay empty, because Azure AI is reached by managed identity |
| `AiSettings--ElevenLabs--ApiKey` | ElevenLabs account key | Audio synthesis | Load-bearing |
| `EmergencyFeeds--ApiKey` | Emergency services feed key placeholder | Emergency feed | Placeholder until ARN obtains a DataQuoll key |
| `Here--ApiKey` | HERE account key | Traffic and route data | Load-bearing |
| `Webhook--EndpointUrl` | Logic App trigger URL | Notification delivery | Load-bearing |

The five names above are the complete runtime secret inventory. Verified 2026-09-03 against the development vault, only `Here--ApiKey`, `AiSettings--ElevenLabs--ApiKey` and
`Webhook--EndpointUrl` are load-bearing today. Do not rotate the inert or placeholder entries as routine work.

| Deployment-time secret (not in runtime Key Vault inventory) | Source or purpose | Consumer |
|---|---|---|
| `AadClientSecret` | Entra application-registration secret supplied during deployment | Static Web App custom identity provider |

`AadClientSecret` is not a Function App runtime secret. It is the Entra
application-registration secret supplied to the Static Web App custom identity
provider during deployment. The inventory above lists the deployed runtime
secrets. No App Service Key Vault-reference settings are used; the Function App
reads Key Vault directly at startup.

Test and production Key Vaults have public network access disabled. A Key Vault
data-plane operator must use the private network path, or use a controlled
public-access window for a secret update and disable public access immediately
afterwards. ARM deployment writes to the notification trigger do not require
that window.

## External services and AI model

| Service | Deployed configuration |
|---|---|
| Azure OpenAI / Azure AI Services | One centralized Azure OpenAI model across Dev, Test and Production, with no additional reasoning effort. |
| Official traffic feeds | `livetraffic-nsw` (Live Traffic NSW), `qldtraffic` (QLDTraffic), `victraffic` (VicTraffic/DataVic), `mainroads-wa` (Main Roads WA), `trafficsa` (TrafficSA), and `tasalert` (TasALERT). HERE remains the fallback source. |
| DataQuoll emergency feed | Not provisioned; the phase 1 console-only feature is disabled, the vault holds only a placeholder until ARN obtains a DataQuoll key, and events do not reach the writer or air. |
| Zetta playout | Both test and production write the approved WAV over the Azure Files REST API using the Function App managed identity to the shared `ibpauetststaftp01` storage account, `informationbulletins` Azure Files share (verified from the live app settings of both Function Apps on 2026-09-03). There is no separate production drop account. Zetta ingests over SMB and deletes the file. There is no delivery credential and no on-air receipt; ingest is confirmed only by the file disappearing. |

The six authority feeds are evidence rails alongside HERE; availability and
coverage can differ by market. DataQuoll is deliberately not an on-air
dependency until a later phase changes the writer boundary and deployment
configuration.

## Data plane

Each environment has one storage account named `ibpaue{env}staapp01`.

- Table Storage holds bulletin records, station schedules, station voice and
  delivery configuration, operator assignments, editorial guidance, safety
  terms, priority roads, feedback and incident state.
- Queue Storage carries bulletin generation and regeneration messages. Poison
  queues retain messages that need operator investigation.
- Blob Storage holds generated WAV files and daily audit exports in the
  `broadcasts` and `audit-exports` containers.
- The Function App uses managed identity roles rather than a storage connection
  string. Private endpoints and private DNS zones protect test and production
  storage.

## Observability

Each environment has an Application Insights component, a Log Analytics
workspace and one Azure Monitor workbook: **Operations Overview**. It is the
single operational view, with sections for bulletin pipeline health, delivery
to playout, queues, external dependencies, availability and errors, and TTS
cost. Open it in Azure Portal from **Monitor**, **Workbooks**, or from the
resource group `IBP-AUE-{ENV}-RSG-APP01`, **Workbooks**. The display name is
`IBP-AUE-{ENV} — Operations Overview`; the resource name is a GUID, because
Azure requires that for `Microsoft.Insights/workbooks`.

Nine alert rules per environment, 27 in total and all enabled (verified 2026-09-03), cover Function App errors, missing executions,
health-endpoint availability, Azure AI throttling, poison queues, synthesis
failures, total bulletin failure, unusable dependencies and Key Vault access
denials. Three are metric alerts (errors, health availability, no executions) and six are log alerts (AI throttling, bulletin failure rate, dependency unhealthy, Key Vault access denied, poison queue, synthesis failures). The availability test calls
`https://ibp-aue-{env}-fna-app01.azurewebsites.net/api/health`.
Action groups route alerts to the configured environment Teams channel. Verified 2026-09-03, each live action group carries one email receiver belonging to the delivery representative plus the Teams webhook. The committed deployment parameters set `alertEmailReceivers` to empty, so the next deployment of any environment removes that email receiver and leaves Teams as the only path.

## Resource inventory: development

| Resource | Type | Resource group | SKU |
|---|---|---|---|
| `IBP-AUE-DEV-FDY-APP01` | accounts | `IBP-AUE-DEV-RSG-APP01` | S0 |
| `IBP-AUE-DEV-FDY-APP01/bulletin-generator` | accounts/projects | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-AIA-APP01` | actiongroups | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-AIN-APP01` | components | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-FNA-APP01-errors-alert` | metricalerts | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-FNA-APP01-health-availability` | metricalerts | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-FNA-APP01-no-executions` | metricalerts | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-FNA-APP01-ai-throttling` | scheduledqueryrules | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-FNA-APP01-bulletin-failure-rate` | scheduledqueryrules | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-FNA-APP01-dependency-unhealthy` | scheduledqueryrules | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-FNA-APP01-kv-access-denied` | scheduledqueryrules | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-FNA-APP01-poison-queue` | scheduledqueryrules | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-FNA-APP01-synthesis-failures` | scheduledqueryrules | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-health` | webtests | `IBP-AUE-DEV-RSG-APP01` | |
| `c09eeffa-ad09-546d-9b69-e1d5cbdd38de` | workbooks | `IBP-AUE-DEV-RSG-APP01` | Operations Overview |
| `IBP-AUE-DEV-LOA-APP01` | workflows | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-LOA-APP01-teams` | connections | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-PLN-APP01` | serverFarms | `IBP-AUE-DEV-RSG-APP01` | P0v3 |
| `IBP-AUE-DEV-PLN-APP01-autoscale` | autoscalesettings | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-FNA-APP01` | sites | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-FNA-APP01/staging` | sites/slots | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-SWA-APP01` | staticSites | `IBP-AUE-DEV-RSG-APP01` | Standard |
| `IBP-AUE-DEV-SWA-DOC01` | staticSites | `IBP-AUE-DEV-RSG-APP01` | |
| `Application Insights Smart Detection` | actiongroups | `IBP-AUE-DEV-RSG-APP01` | |
| `IBP-AUE-DEV-KYV-APP01` | vaults | `IBP-AUE-DEV-RSG-KYV01` | |
| `IBP-AUE-DEV-LAW-APP01` | workspaces | `IBP-AUE-DEV-RSG-LAW01` | |
| `ibpauedevstaapp01` | storageAccounts | `IBP-AUE-DEV-RSG-STA01` | Standard_LRS |

`IBP-AUE-DEV-FDY-APP01/bulletin-generator` is the Azure AI project that hosts the versioned bulletin-generator agents used by the writer pipeline. `IBP-AUE-DEV-SWA-DOC01` serves the published documentation site; the pipeline deploys that site to development and production only, so there is no test twin.

## Resource inventory: test

| Resource | Type | Resource group | SKU |
|---|---|---|---|
| `IBP-AUE-TST-FDY-APP01` | accounts | `IBP-AUE-TST-RSG-APP01` | S0 |
| `IBP-AUE-TST-FDY-APP01/bulletin-generator` | accounts/projects | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-AIA-APP01` | actiongroups | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-AIN-APP01` | components | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-FNA-APP01-errors-alert` | metricalerts | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-FNA-APP01-health-availability` | metricalerts | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-FNA-APP01-no-executions` | metricalerts | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-FNA-APP01-ai-throttling` | scheduledqueryrules | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-FNA-APP01-bulletin-failure-rate` | scheduledqueryrules | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-FNA-APP01-dependency-unhealthy` | scheduledqueryrules | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-FNA-APP01-kv-access-denied` | scheduledqueryrules | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-FNA-APP01-poison-queue` | scheduledqueryrules | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-FNA-APP01-synthesis-failures` | scheduledqueryrules | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-health` | webtests | `IBP-AUE-TST-RSG-APP01` | |
| `386d5140-ad98-5ce2-953b-264b48e6abc6` | workbooks | `IBP-AUE-TST-RSG-APP01` | Operations Overview |
| `IBP-AUE-TST-LOA-APP01` | workflows | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-LOA-APP01-teams` | connections | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-PLN-APP01` | serverFarms | `IBP-AUE-TST-RSG-APP01` | P0v3 |
| `IBP-AUE-TST-PLN-APP01-autoscale` | autoscalesettings | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-FNA-APP01` | sites | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-FNA-APP01/staging` | sites/slots | `IBP-AUE-TST-RSG-APP01` | |
| `IBP-AUE-TST-SWA-APP01` | staticSites | `IBP-AUE-TST-RSG-APP01` | Standard |
| `IBP-AUE-TST-KYV-APP01` | vaults | `IBP-AUE-TST-RSG-KYV01` | |
| `IBP-AUE-TST-LAW-APP01` | workspaces | `IBP-AUE-TST-RSG-LAW01` | |
| `IBP-AUE-TST-PEP-APP01.nic.11fb76cc-3b22-4181-946b-50e1d3ccfab0` | networkInterfaces | `IBP-AUE-TST-RSG-NTW01` | |
| `IBP-AUE-TST-PEP-APP02.nic.7f5786c2-c488-435d-8a65-2461905016fd` | networkInterfaces | `IBP-AUE-TST-RSG-NTW01` | |
| `IBP-AUE-TST-PEP-APP03.nic.dcf7ee27-68e6-4bd6-a083-841f8489e8ea` | networkInterfaces | `IBP-AUE-TST-RSG-NTW01` | |
| `IBP-AUE-TST-PEP-APP04.nic.bf3639c7-c395-4418-8161-fbe032d972ed` | networkInterfaces | `IBP-AUE-TST-RSG-NTW01` | |
| `IBP-AUE-TST-PEP-APP05.nic.f656ed97-c365-4aaf-96d7-a18b21f6b223` | networkInterfaces | `IBP-AUE-TST-RSG-NTW01` | |
| `privatelink.blob.core.windows.net` | privateDnsZones | `IBP-AUE-TST-RSG-NTW01` | |
| `privatelink.cognitiveservices.azure.com` | privateDnsZones | `IBP-AUE-TST-RSG-NTW01` | |
| `privatelink.queue.core.windows.net` | privateDnsZones | `IBP-AUE-TST-RSG-NTW01` | |
| `privatelink.table.core.windows.net` | privateDnsZones | `IBP-AUE-TST-RSG-NTW01` | |
| `privatelink.vaultcore.azure.net` | privateDnsZones | `IBP-AUE-TST-RSG-NTW01` | |
| `privatelink.blob.core.windows.net/IBP-AUE-TST-VNT-APP01-link` | privateDnsZones/virtualNetworkLinks | `IBP-AUE-TST-RSG-NTW01` | |
| `privatelink.blob.core.windows.net/IBP-AUE-TST-VNT-NTW01-link` | privateDnsZones/virtualNetworkLinks | `IBP-AUE-TST-RSG-NTW01` | |
| `privatelink.cognitiveservices.azure.com/IBP-AUE-TST-VNT-APP01-link` | privateDnsZones/virtualNetworkLinks | `IBP-AUE-TST-RSG-NTW01` | |
| `privatelink.cognitiveservices.azure.com/IBP-AUE-TST-VNT-NTW01-link` | privateDnsZones/virtualNetworkLinks | `IBP-AUE-TST-RSG-NTW01` | |
| `privatelink.queue.core.windows.net/IBP-AUE-TST-VNT-APP01-link` | privateDnsZones/virtualNetworkLinks | `IBP-AUE-TST-RSG-NTW01` | |
| `privatelink.queue.core.windows.net/IBP-AUE-TST-VNT-NTW01-link` | privateDnsZones/virtualNetworkLinks | `IBP-AUE-TST-RSG-NTW01` | |
| `privatelink.table.core.windows.net/IBP-AUE-TST-VNT-APP01-link` | privateDnsZones/virtualNetworkLinks | `IBP-AUE-TST-RSG-NTW01` | |
| `privatelink.table.core.windows.net/IBP-AUE-TST-VNT-NTW01-link` | privateDnsZones/virtualNetworkLinks | `IBP-AUE-TST-RSG-NTW01` | |
| `privatelink.vaultcore.azure.net/IBP-AUE-TST-VNT-APP01-link` | privateDnsZones/virtualNetworkLinks | `IBP-AUE-TST-RSG-NTW01` | |
| `privatelink.vaultcore.azure.net/IBP-AUE-TST-VNT-NTW01-link` | privateDnsZones/virtualNetworkLinks | `IBP-AUE-TST-RSG-NTW01` | |
| `IBP-AUE-TST-PEP-APP01` | privateEndpoints | `IBP-AUE-TST-RSG-NTW01` | |
| `IBP-AUE-TST-PEP-APP02` | privateEndpoints | `IBP-AUE-TST-RSG-NTW01` | |
| `IBP-AUE-TST-PEP-APP03` | privateEndpoints | `IBP-AUE-TST-RSG-NTW01` | |
| `IBP-AUE-TST-PEP-APP04` | privateEndpoints | `IBP-AUE-TST-RSG-NTW01` | |
| `IBP-AUE-TST-PEP-APP05` | privateEndpoints | `IBP-AUE-TST-RSG-NTW01` | |
| `IBP-AUE-TST-VNT-APP01-10-241-91-0_24` | virtualNetworks | `IBP-AUE-TST-RSG-NTW01` | |
| `IBP-AUE-TST-VNT-NTW01` | virtualNetworks | `IBP-AUE-TST-RSG-NTW01` | |
| `ibpauetststaapp01` | storageAccounts | `IBP-AUE-TST-RSG-STA01` | Standard_ZRS |
| `ibpauetststaftp01` | storageAccounts | `IBP-AUE-TST-RSG-STA01` | Standard_LRS, shared delivery drop for test and production, `informationbulletins` Azure Files share (verified 2026-09-03) |

## Resource inventory: production

| Resource | Type | Resource group | SKU |
|---|---|---|---|
| `IBP-AUE-PRD-FDY-APP01` | accounts | `IBP-AUE-PRD-RSG-APP01` | S0 |
| `IBP-AUE-PRD-AIA-APP01` | actiongroups | `IBP-AUE-PRD-RSG-APP01` | |
| `IBP-AUE-PRD-AIN-APP01` | components | `IBP-AUE-PRD-RSG-APP01` | |
| `IBP-AUE-PRD-FNA-APP01-errors-alert` | metricalerts | `IBP-AUE-PRD-RSG-APP01` | |
| `IBP-AUE-PRD-FNA-APP01-health-availability` | metricalerts | `IBP-AUE-PRD-RSG-APP01` | |
| `IBP-AUE-PRD-FNA-APP01-no-executions` | metricalerts | `IBP-AUE-PRD-RSG-APP01` | |
| `IBP-AUE-PRD-FNA-APP01-ai-throttling` | scheduledqueryrules | `IBP-AUE-PRD-RSG-APP01` | |
| `IBP-AUE-PRD-FNA-APP01-bulletin-failure-rate` | scheduledqueryrules | `IBP-AUE-PRD-RSG-APP01` | |
| `IBP-AUE-PRD-FNA-APP01-dependency-unhealthy` | scheduledqueryrules | `IBP-AUE-PRD-RSG-APP01` | |
| `IBP-AUE-PRD-FNA-APP01-kv-access-denied` | scheduledqueryrules | `IBP-AUE-PRD-RSG-APP01` | |
| `IBP-AUE-PRD-FNA-APP01-poison-queue` | scheduledqueryrules | `IBP-AUE-PRD-RSG-APP01` | |
| `IBP-AUE-PRD-FNA-APP01-synthesis-failures` | scheduledqueryrules | `IBP-AUE-PRD-RSG-APP01` | |
| `IBP-AUE-PRD-health` | webtests | `IBP-AUE-PRD-RSG-APP01` | |
| `fde0ed99-567b-5f68-8e5d-88fb41853ab8` | workbooks | `IBP-AUE-PRD-RSG-APP01` | Operations Overview |
| `IBP-AUE-PRD-LOA-APP01` | workflows | `IBP-AUE-PRD-RSG-APP01` | |
| `IBP-AUE-PRD-LOA-APP01-teams` | connections | `IBP-AUE-PRD-RSG-APP01` | |
| `IBP-AUE-PRD-PLN-APP01` | serverFarms | `IBP-AUE-PRD-RSG-APP01` | P0v3 |
| `IBP-AUE-PRD-PLN-APP01-autoscale` | autoscalesettings | `IBP-AUE-PRD-RSG-APP01` | |
| `IBP-AUE-PRD-FNA-APP01` | sites | `IBP-AUE-PRD-RSG-APP01` | |
| `IBP-AUE-PRD-SWA-APP01` | staticSites | `IBP-AUE-PRD-RSG-APP01` | Standard |
| `IBP-AUE-PRD-KYV-APP01` | vaults | `IBP-AUE-PRD-RSG-KYV01` | |
| `IBP-AUE-PRD-LAW-APP01` | workspaces | `IBP-AUE-PRD-RSG-LAW01` | |
| `IBP-AUE-PRD-PEP-APP01.nic.ebfdc061-de5c-44df-aea8-e791c5e8b275` | networkInterfaces | `IBP-AUE-PRD-RSG-NTW01` | |
| `IBP-AUE-PRD-PEP-APP02.nic.2c9940ac-2f9f-4d94-80e3-0de97abf341f` | networkInterfaces | `IBP-AUE-PRD-RSG-NTW01` | |
| `IBP-AUE-PRD-PEP-APP03.nic.3fd5e985-5ed8-420a-b7e8-8232ec1eb26e` | networkInterfaces | `IBP-AUE-PRD-RSG-NTW01` | |
| `IBP-AUE-PRD-PEP-APP04.nic.e6a2fcc7-a6ab-463a-9b48-d7bd7ab3d98c` | networkInterfaces | `IBP-AUE-PRD-RSG-NTW01` | |
| `IBP-AUE-PRD-PEP-APP05.nic.e48f0708-c5fc-46d7-84d8-7ab8ca879e44` | networkInterfaces | `IBP-AUE-PRD-RSG-NTW01` | |
| `privatelink.blob.core.windows.net` | privateDnsZones | `IBP-AUE-PRD-RSG-NTW01` | |
| `privatelink.cognitiveservices.azure.com` | privateDnsZones | `IBP-AUE-PRD-RSG-NTW01` | |
| `privatelink.queue.core.windows.net` | privateDnsZones | `IBP-AUE-PRD-RSG-NTW01` | |
| `privatelink.table.core.windows.net` | privateDnsZones | `IBP-AUE-PRD-RSG-NTW01` | |
| `privatelink.vaultcore.azure.net` | privateDnsZones | `IBP-AUE-PRD-RSG-NTW01` | |
| `privatelink.blob.core.windows.net/IBP-AUE-PRD-VNT-NTW01-link` | privateDnsZones/virtualNetworkLinks | `IBP-AUE-PRD-RSG-NTW01` | |
| `privatelink.cognitiveservices.azure.com/IBP-AUE-PRD-VNT-NTW01-link` | privateDnsZones/virtualNetworkLinks | `IBP-AUE-PRD-RSG-NTW01` | |
| `privatelink.queue.core.windows.net/IBP-AUE-PRD-VNT-NTW01-link` | privateDnsZones/virtualNetworkLinks | `IBP-AUE-PRD-RSG-NTW01` | |
| `privatelink.table.core.windows.net/IBP-AUE-PRD-VNT-NTW01-link` | privateDnsZones/virtualNetworkLinks | `IBP-AUE-PRD-RSG-NTW01` | |
| `privatelink.vaultcore.azure.net/IBP-AUE-PRD-VNT-NTW01-link` | privateDnsZones/virtualNetworkLinks | `IBP-AUE-PRD-RSG-NTW01` | |
| `IBP-AUE-PRD-PEP-APP01` | privateEndpoints | `IBP-AUE-PRD-RSG-NTW01` | |
| `IBP-AUE-PRD-PEP-APP02` | privateEndpoints | `IBP-AUE-PRD-RSG-NTW01` | |
| `IBP-AUE-PRD-PEP-APP03` | privateEndpoints | `IBP-AUE-PRD-RSG-NTW01` | |
| `IBP-AUE-PRD-PEP-APP04` | privateEndpoints | `IBP-AUE-PRD-RSG-NTW01` | |
| `IBP-AUE-PRD-PEP-APP05` | privateEndpoints | `IBP-AUE-PRD-RSG-NTW01` | |
| `IBP-AUE-PRD-VNT-NTW01` | virtualNetworks | `IBP-AUE-PRD-RSG-NTW01` | |
| `ibpaueprdstaapp01` | storageAccounts | `IBP-AUE-PRD-RSG-STA01` | Standard_ZRS |
| `ibpauetststaftp01` | storageAccounts | `IBP-AUE-TST-RSG-STA01` | Standard_LRS, shared delivery drop for test and production, `informationbulletins` Azure Files share (verified 2026-09-03) |

Development storage uses LRS because it is a non-durable environment. Test and
production use ZRS. Production is deployed with schedules currently disabled;
do not infer its station or delivery state from this resource inventory.
Read `GET /api/schedules` before any operational action. Parking remains available
to control idle cost and is a documented procedure (see the [command
reference](quick-reference.md) and [operations runbook](runbook.md)): parking
stops the Function App, drops the plan to B1 and disables the alert rules and the
availability test, leaving storage, Key Vault, private endpoints and the console
in place. Resuming restores P0v3, starts the app and re-enables monitoring. While
parked the production API is expected not to respond, and that is the intended
shape of parking, not a fault of the environment. A deploy into a parked
environment re-arms the alert rules against a stopped app, so resume first: the
deployment script now refuses a parked target unless it is explicitly overridden.
Every environment keeps a staging deployment slot, and all three are empty -
each returns 404, because neither the normal deployment path nor the pipeline
ever publishes to a slot. The blue-green option publishes to the slot and then
swaps, so it is safe on its own; the risk is a MANUAL swap from the Portal,
which would promote an empty slot into production. Production's slot was created
on 2026-08-12; dev's and test's predate it.
