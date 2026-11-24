---
title: "ARN Azure Naming Convention"
description: "ARN's confirmed Azure naming convention for the Information Bulletin Platform (IBP): IBP-AUE-{ENV}-{TYPE}-APP01, resource group layout, action groups, DLs, cost alerting, and the open items still with ARN's technology lead."
tags:
  - "azure"
  - "naming"
  - "infrastructure"
  - "environments"
  - "convention"
aliases:
  - "ARN Azure Naming Convention"
  - "IBP Naming Convention"
purpose: "reference"
status: "current"
last_verified: 2026-06-19
review_after: 2026-09-09
volatility: "low"
source_of_truth: "IBP-AzureNamingConvention(Production).csv v2 (ARN Technology Lead, 11 Jun 2026) + Teams call decision 11 Jun (keep APP01 segment, 'go with what's in the doco')"
verified_by:
  - "IBP-AzureNamingConvention(Production).csv (ARN Technology Lead, received 10 Jun, revised 11 Jun 2026)"
  - "Teams call (delivery team and ARN technology lead) 11 Jun 2026 — APP01 segment retained, PEP RG rolled into NTW01"
related_code:
  - "deployment/main.bicep"
  - "deployment/main.dev.bicepparam"
common_queries:
  - "What is ARN's Azure naming convention?"
  - "What will the dev/test/prod resource groups be named?"
  - "What resource type codes does ARN use?"
  - "What are the IBP action groups and cost alert thresholds?"
see_also:
  - "deployment/main.bicep"
  - "docs/canonical/decisions.md"
---

# ARN Azure Naming Convention

Final convention for the Information Bulletin Platform (IBP), agreed with ARN's Technology Lead on 11 Jun 2026. Authoritative source: ARN's `IBP-AzureNamingConvention(Production).csv` (v2, 11 Jun) plus the Teams call decision to keep the `APP01` segment ("go with what's in the doco"). This supersedes the 4 Jun `ARN-AUE-[Env]-[Type]-IBP01` scheme (see History below). The PoC resources predate it and use a CAF-style scheme.

## Pattern

```
IBP-<REGION>-<ENV>-<TYPE>-APP01
```

CSV header form: `<Subscription>-<Location>-<Environment>-<Resource Type>-<Application/Use>##`.

| Segment | Meaning | Values |
|---|---|---|
| Subscription | Owning subscription/product code | `IBP` (this platform); `MGT` = ARN management subscription |
| Location | Azure region | `AUE` (Australia East) |
| Environment | Environment | `DEV`, `TST`, `PRD` (CSV examples use `PRD`) |
| Type | Resource type code | see table below |
| Application/Use | Workload + instance | `APP01` for all platform resources |

The `APP01` segment was questioned ("APP? Not sure") in CSV v1 and explicitly retained on the 11 Jun call. Resource groups follow the same shape with `RSG` as the type and the group's purpose as the final segment: `IBP-AUE-PRD-RSG-APP01`.

## Resource groups (per environment)

| Resource Group | Name | Contents |
|---|---|---|
| Application | `IBP-AUE-{ENV}-RSG-APP01` | Everything not listed below (Function App, plan, SWA, Logic App, App Insights, Foundry/AI, Search) |
| Network | `IBP-AUE-{ENV}-RSG-NTW01` | VNets, subnets, NSGs, DNS zones, **private endpoints** (PEP RG from CSV v1 was rolled into NTW01) |
| Key Vault | `IBP-AUE-{ENV}-RSG-KYV01` | Key Vault + its secrets/certs/keys (ARN keeps KV separate) |
| Storage | `IBP-AUE-{ENV}-RSG-STA01` | Storage accounts |
| Log Analytics | **open question** | CSV v2 names the workspace `MGT-AUE-PRD-LAW-APP01` in `MGT-AUE-PRD-RSG-LAW01` (ARN management subscription = central logging), but the RG list still carries `IBP-AUE-PRD-RSG-LAW01`. Question sent to ARN's technology lead 11 Jun. If MGT wins: 4 IBP RGs per env, we reference their workspace by resource ID instead of creating one. |

## Resource names (PRD examples from the CSV; swap `PRD` for `DEV`/`TST`)

| Resource | Code | Name | Resource Group |
|---|---|---|---|
| Virtual Network | VNT | `IBP-AUE-PRD-VNT-APP01-10-10-10-0_24` (CIDR appended) | NTW01 |
| Subnet | SNT | `IBP-AUE-PRD-SNT-APP01-10-10-10-128_25` (CIDR appended) | NTW01 |
| Private Endpoint | PEP | `IBP-AUE-PRD-PEP-APP01` | NTW01 |
| Network Security Group | NSG | `IBP-AUE-PRD-NSG-APP01` | NTW01 |
| Application Insights | AIN | `IBP-AUE-PRD-AIN-APP01` | APP01 |
| App Insights Action Group | AIA | `IBP-AUE-PRD-AIA-APP01` (code changed from `AGP` in v1) | APP01 |
| Smart Detector Rule | SDR | `IBP-AUE-PRD-SDR-APP01` | APP01 |
| Foundry (AI Services / Azure OpenAI) | FDY | `IBP-AUE-PRD-FDY-APP01` (v1's `FDY-COG01` resolved; COG = Cognitive Services from the PoC) | APP01 |
| Function App | FNA | `IBP-AUE-PRD-FNA-APP01` | APP01 |
| Logic App | LOA | `IBP-AUE-PRD-LOA-APP01` | APP01 |
| App Service Plan | PLN | `IBP-AUE-PRD-PLN-APP01` | APP01 |
| Search Service (Foundry IQ) | FIQ | `IBP-AUE-PRD-FIQ-APP01` | APP01 |
| Static Web App | SWA | `IBP-AUE-PRD-SWA-APP01` (name keeps `AUE` even though SWA deploys to East Asia) | APP01 |
| Storage account | STA | `ibpaueprdstaapp01` (lowercase, no hyphens, 17/24 chars) | STA01 |
| Key Vault | KYV | `IBP-AUE-PRD-KYV-APP01` | KYV01 |
| Key Vault Secret / Certificate / Key | KVS / KVC / KVK | `IBP-AUE-PRD-KVS-APP01` etc. | inside the vault |
| Log Analytics Workspace | LAW | `MGT-AUE-PRD-LAW-APP01` (**MGT sub — pending confirmation**) | MGT-AUE-PRD-RSG-LAW01 |

Note on KVS/KVC/KVK: our existing secrets use functional names (`ElevenLabsApiKey` etc.). Whether ARN expects the KVS-prefixed pattern for individual secret names is unconfirmed; treat the rows as the doc template rather than a mandate until clarified.

## Action groups, DLs and cost alerting

Action groups use a separate lowercase ARN management convention, not the IBP resource pattern:

| Action Group | DL | Audience |
|---|---|---|
| `mgt-aue-ibp-agp-PlatformAvailability` | `AZ-IBP-PlatformAvailability@arn.com.au` | Infra & platform availability |
| `mgt-aue-ibp-agp-SecurityIncidents` | `AZ-IBP-SecurityIncidents@arn.com.au` | Security incidents |
| `mgt-aue-ibp-agp-BudgetEscalation` | `AZ-IBP-BudgetEscalation@arn.com.au` | Budget escalation |

DL membership (all groups): the CSV names individual Non-Prod members (removed here — use the role distribution lists above); prod equivalents undefined.

Cost alert thresholds: 25/50/75% → `mgt-aue-ibp-agp-DataAnalytics`, 90/100% → `mgt-aue-ibp-agp-BudgetEscalation`. **Inconsistency:** the DataAnalytics action group was removed from the v2 action-group list but the thresholds still reference it.

## Open items (with ARN's technology lead, raised 11 Jun)

1. **LAW subscription**: MGT (central) vs IBP — the CSV contradicts itself (see above). If MGT, ARN supplies the workspace resource ID per environment and grants our deployment identity rights to link App Insights + diagnostic settings.
2. **DataAnalytics action group** dangling from cost thresholds.
3. **Missing type codes** for resources our Bicep deploys: metric/scheduled-query alert rules (only SDR exists), the Logic App's Teams API connection (`Microsoft.Web/connections`), Azure Monitor workbooks, route tables/UDRs (hub egress), budgets.
4. **Prod action groups/DLs** undefined (everything marked Non-Prod).
5. CSV typos: `AZ-IPB-PlatformAvailability` (IPB→IBP), `AZ-DPN-SecurityIncidents` (DPN looks pasted from another product), "Foundary"→"Foundry".
6. **Dedicated IBP subscription — confirmed (ARN technology lead, 18/19 Jun).** ARN is provisioning a dedicated IBP subscription and has granted the Arinco deploy operator PIM-eligible Owner (permanent Reader → PIM-activate to Owner). Subscription ID + Azure DevOps service connection pending delivery. Resolves the prior fallback question (IBP-named RGs inside the ARN sub no longer needed).

## Deployment alignment (our side) — implemented and sandbox-validated 11 Jun 2026

`deployment/main.bicep` now deploys at **subscription scope** across the confirmed RG layout (APP01 / NTW01 / KYV01 / STA01, plus LAW01 only while logging stays IBP-side). All names are computed centrally from `environment` (`dev`/`test`/`prod`/`sbx` → `DEV`/`TST`/`PRD`/`SBX`):

- Storage is deterministic `ibpaue{env}staapp01` (the `uniqueString` suffix is gone); Azure AI Search is lowercased (`ibp-aue-{env}-fiq-app01` — the service rejects uppercase); the FDY custom subdomain is lowercased (DNS label).
- Private endpoints deploy into NTW01 as `IBP-AUE-{ENV}-PEP-APP01..05`, targeting Storage/KV/AI by resource ID across RGs; role assignments split per target RG (`roles-storage|key-vault|ai-services.bicep`).
- Unconfirmed items are **documented placeholder parameters** (see `deployment/README.md`): `logAnalyticsWorkspaceResourceId` (empty → IBP-side workspace; set ARN's MGT workspace ID when confirmed), `actionGroupId` (empty → local `IBP-AUE-{ENV}-AIA-APP01`; swap for `mgt-aue-ibp-agp-*` IDs), `createResourceGroups` (false when ARN pre-creates), PE subnet + DNS zone IDs (pending ARN network team), AAD IDs (pending identity team).
- `deploy.sh` and `azure-pipelines.yml` use `az deployment sub create`; new environments target `australiaeast`. The PoC (CAF names, single RG, `australiasoutheast`) is frozen — its template lives in git history.
- **Sandbox-validated 11 Jun 2026**: the full template deployed end-to-end into the delivery team's sandbox subscription (`IBP-AUE-SBX-*`, four RGs + conditional skips) and every deployed name matched this convention; a real bulletin ran through the whole pipeline before teardown. Findings + fixes recorded in `deployment/README.md` ("Sandbox validation").

## History

- **4 Jun 2026**: ARN's technology lead supplied ARN's `AzureNamingConvention.docx` scheme `ARN-AUE-[Env]-[Type]-IBP01`; recorded in `docs/canonical/decisions.md` (2026-06-04).
- **11 Jun 2026**: ARN's technology lead returned the CSV adopting the IBP pattern with ARN's RG layout and type codes; call confirmed keeping `APP01` and rolling the PEP RG into NTW01. This doc reflects that final state.

## Multi-tenant / on-sell (future)

ARN's technology lead flagged (4 Jun, pending architecture-team review) that if the platform is on-sold and internally hosted for a client, each client gets its own subscription, swapping the leading product segment for the client's 3-letter code. The rest of the convention is unchanged.
