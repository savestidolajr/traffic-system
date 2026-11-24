---
title: "Run Costs: Steady-State Envelope"
description: "Measured Azure run costs by environment, the cost shape of running or parking an environment, vendor commercial arrangements and practical cost controls."
tags:
  - "operations"
  - "cost"
  - "azure"
  - "budget"
aliases:
  - "run costs"
  - "cost envelope"
  - "azure costs"
  - "steady-state costs"
purpose: "reference"
status: "current"
last_verified: 2026-09-04
review_after: 2026-11-27
volatility: "medium"
source_of_truth: "Azure Cost Management and environment operations records"
verified_by:
  - "Cost Management actual-cost query"
  - "Environment state review"
common_queries:
  - "What does the platform cost to run?"
  - "What does a parked environment cost?"
  - "What vendor spend sits outside Azure?"
  - "How do I reduce run costs?"
see_also:
  - "docs/operations/runbook.md"
  - "docs/operations/as-built.md"
---

# Run Costs: Steady-State Envelope

The figures below describe Azure spend for subscription
`5e5f8695-2207-4572-8e80-1065a1b0c135`. They are point-in-time measurements,
not a live forecast. Currency is AUD.

## Measured Azure cost by environment

### August 2026, full month (current, verified 2026-09-04)

August 2026 actuals are the current figure, verified 2026-09-04.

| Environment | Resource group | Cost (AUD) |
|---|---|---:|
| dev | `ibp-aue-dev-rsg-app01` | 203.62 |
| dev | `ibp-aue-dev-rsg-law01` | 109.38 |
| dev | `ibp-aue-dev-rsg-sta01` | 7.59 |
| **dev total (excluding vaults)** | | **320.59** |
| test | `ibp-aue-tst-rsg-sta01` | 354.84 |
| test | `ibp-aue-tst-rsg-app01` | 162.97 |
| test | `ibp-aue-tst-rsg-law01` | 88.37 |
| test | `ibp-aue-tst-rsg-ntw01` | 84.04 |
| **test total (excluding vaults)** | | **690.22** |
| prod | `ibp-aue-prd-rsg-app01` | 136.51 |
| prod | `ibp-aue-prd-rsg-ntw01` | 83.93 |
| prod | `ibp-aue-prd-rsg-law01` | 40.83 |
| prod | `ibp-aue-prd-rsg-sta01` | 5.32 |
| **prod total (excluding vaults)** | | **266.59** |
| shared | `ibp-aue-dev-rsg-kyv01`, `ibp-aue-tst-rsg-kyv01` and `ibp-aue-prd-rsg-kyv01` combined | 0.02 |
| **August total** | | **1,277.42** |

Production accounts for about A$260 of the A$1,277 total. Development and test together are about three quarters of the spend. Key vault spend is negligible: each vault rounds to about A$0.01 and the combined actual is A$0.02.

A monthly subscription budget of A$1,800 was created on 2026-09-04, about forty per cent above the August actual so ordinary variation does not alert. It notifies the Owner and Contributor contact roles, not a named mailbox, so it keeps working through any staff change: at eighty per cent of the month's actual, at one hundred per cent, and when the forecast for the month exceeds one hundred per cent. Review the amount once ARN has decided whether development and test stay running, because they carry about three quarters of the spend and parking them changes the sensible ceiling.

### Month to date, 1 to 27 July 2026 (historical)

| Environment | Resource group | Cost (AUD) |
|---|---|---:|
| dev | `ibp-aue-dev-rsg-app01` | 378.20 |
| dev | `ibp-aue-dev-rsg-law01` | 84.54 |
| dev | `ibp-aue-dev-rsg-sta01` | 4.22 |
| dev | `ibp-aue-dev-rsg-kyv01` | 0.02 |
| **dev total** | | **466.98** |
| test | `ibp-aue-tst-rsg-sta01` | 138.91 |
| test | `ibp-aue-tst-rsg-app01` | 102.68 |
| test | `ibp-aue-tst-rsg-ntw01` | 61.65 |
| test | `ibp-aue-tst-rsg-law01` | 53.32 |
| test | `ibp-aue-tst-rsg-kyv01` | 0.02 |
| **test total** | | **356.59** |
| prod | `ibp-aue-prd-rsg-ntw01` | 44.77 |
| prod | `ibp-aue-prd-rsg-app01` | 23.84 |
| prod | `ibp-aue-prd-rsg-law01` | 0.35 |
| prod | `ibp-aue-prd-rsg-sta01` | 0.02 |
| prod | `ibp-aue-prd-rsg-kyv01` | 0.01 |
| **prod total, parked** | | **68.99** |

July was an unusually heavy month in dev and test: bulletin-generation
experiments, two load-test executions and the resulting Log Analytics ingestion
all land in these figures. The June table below is the better guide to a
low-volume steady state. Cost Management backfills for several days after a
period closes, so re-run the query rather than reusing a figure captured
mid-month.

### Last full month, June 2026 (historical)

| Environment | Resource group | Cost (AUD) |
|---|---|---:|
| dev | `ibp-aue-dev-rsg-app01` | 54.01 |
| dev | `ibp-aue-dev-rsg-law01` | 18.48 |
| dev | `ibp-aue-dev-rsg-sta01` | 1.35 |
| dev | `ibp-aue-dev-rsg-kyv01` | 0.00 |
| **dev total** | | **73.8** |
| test | `ibp-aue-tst-rsg-app01` | 5.37 |
| test | `ibp-aue-tst-rsg-ntw01` | 3.42 |
| test | `ibp-aue-tst-rsg-law01` | 1.75 |
| test | `ibp-aue-tst-rsg-sta01` | 0.12 |
| test | `ibp-aue-tst-rsg-kyv01` | 0.00 |
| **test total, mostly parked** | | **10.7** |
| prod | Not deployed in June | 0 |

June predates production deployment and most active test use. The August figures above are the current running-state signal. The July and June tables are retained as historical, while June development spend remains a useful long-running single-environment baseline.

To refresh these figures, open Azure Portal, go to **Cost Management + Billing
> Cost analysis**, select the IBP subscription, choose **Actual cost**, set the
period, and group by **Resource group**. Export the result as a CSV and retain
the measurement period with the figures.

## State-dependent cost shape

The App Service plan is the main state-dependent cost:

| State | Compute configuration | Cost behaviour |
|---|---|---|
| Running | P0v3 plan, Function App active and normal telemetry ingestion | Full operating cost |
| Parked | B1 plan, Function App stopped and alert rules disabled | Reduced, not zero |

Parking pulls two levers: it scales the App Service plan from P0v3 to B1,
which removes roughly 85 percent of the compute charge, and it stops the
Function App, so the scheduler never runs and there is no pay-per-use Azure
AI, HERE or ElevenLabs spend. A parked environment still costs roughly half
of a running one, for the reasons below.

Private endpoints, Static Web Apps, Key Vault, storage and the B1 plan continue
to incur charges while an environment is parked. Parked production cost 68.99
for 1 to 27 July, most of it private endpoints, but that figure is not a
ratio you can apply to a running environment: the two differ in generation
volume and telemetry ingestion as well as plan size. Measure a specific
environment before and after a park if you need its own saving.

As verified 2026-09-03, development is running but unable to deliver, test generates bulletins on its schedule for both stations but delivers none to playout because playout is disabled for both stations, and production generates nothing and airs nothing because both stations are disabled. Only one environment may arm a given station. The parked production figure for 1 to 27 July above is retained as a historical parked-period measurement, not as the expected steady state.

## Vendor arrangements outside Azure

| Vendor | Commercial arrangement | Cost treatment |
|---|---|---|
| ElevenLabs | ARN-held subscription with per-character audio usage | Cost varies with bulletin synthesis and regeneration volume. The vendor account owner retains the current plan and invoice details |
| HERE | ARN-held production traffic data entitlement | Cost is governed by the ARN vendor account and usage terms. The vendor account owner retains the current commercial record |
| Azure OpenAI | Consumption through the Azure AI Services resources | Included in the Azure figures above and varies with bulletin volume |

## Cost controls

| Lever | Action | Operational trade-off |
|---|---|---|
| Park and resume | Use the [operations runbook](runbook.md) procedure to reduce an environment to the B1 plan when it is not required | The console and retained resources remain available, but scheduled bulletins do not run while the Function App is stopped |
| Scheduler schedule | Disable schedules in development or test when no bulletins are required | No generation or audio synthesis spend, but no scheduled output is produced |
| App Service plan | Use P0v3 only for environments that require continuous generation and scale the other environments down | Lower compute capacity can increase latency when an environment resumes |
| Log Analytics retention | Retention is 30 days in every environment. Set a daily ingestion cap to match the operational evidence requirement | A cap protects the bill but drops telemetry once it is reached |
| Blob lifecycle | A tiering rule moves `broadcasts/` to cool storage after 30 days. Test and prod also delete published audio after 90 days, unpublished candidate audio after 30 days, and audit exports after 1095 days through lifecycle delete rules | Retention limits storage growth while preserving the published-audio floor and audit evidence window |
| Generation volume | Avoid repeated regeneration when the existing audio meets the approved script, and monitor Azure AI and ElevenLabs usage | Fewer regenerations reduce cost but leave less room for experimentation |

## Monthly review

The subscription administrator should review Cost Management monthly and record:

- actual cost by resource group for each environment
- the running or parked state during the measurement period
- Azure AI, storage, network and Log Analytics changes
- ElevenLabs and HERE invoices held by the vendor account owners
- any cost-control change that could affect bulletin availability or recovery

Use the [deployed environment inventory](as-built.md) to explain resource-group
changes before treating a cost movement as an anomaly.
