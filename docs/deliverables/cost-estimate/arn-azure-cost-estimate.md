---
title: "ARN Information Bulletin Platform — Azure Cost Estimate"
description: "Full production-solution Azure cost estimate (DEV + TEST + PROD) built via the official Azure Pricing Calculator, exported programmatically"
tags: [deliverables, cost-estimate, azure, finance]
purpose: "reference"
status: "current"
last_verified: 2026-09-03
review_after: 2026-12-03
volatility: "medium"
source_of_truth: "Azure Pricing Calculator export (ExportedEstimate-ARN-full-solution.xlsx)"
verified_by:
  - "Native Azure Pricing Calculator export, 2026-06-25"
aliases:
  - "cost estimate"
  - "azure cost estimate"
  - "run cost projection"
common_queries:
  - "What will this solution cost on Azure?"
  - "Where is the Azure cost estimate for ARN?"
see_also:
  - "docs/operations/run-costs.md"
related_code: []
---

# ARN Information Bulletin Platform — Azure Cost Estimate

**Full production solution · all environments (DEV + TEST + PROD)**

| | |
|---|---|
| **Estimated total** | **$1,487.14 / month · $17,845.68 / year (USD)** |
| Region | Australia East |
| Currency | USD |
| Pricing | Azure **retail / pay-as-you-go**, captured 2026-06-25 (Azure Retail Prices API) |
| Scope | Productionised solution (not the PoC), modelled from the deployed Bicep IaC |
| Excludes | External SaaS (HERE, ElevenLabs) and deferred items (WAF, multi-region DR, Defender) — see below |

Built the right way: prices are live Azure retail rates and SKUs come straight from the deployment templates (`deployment/main.bicep` + `main.{dev,test,prod}.bicepparam`) — the total is genuine architecture, **not** padded. The headline figure includes the production RAG **AI Search** line (a real platform feature — `StyleExamplesService` + `ai-services.bicep`); with that one line removed the Azure total is **$13,956.24/yr**. Note too that the external **ElevenLabs / HERE** SaaS spend (§4) dwarfs the Azure bill regardless.

Companion files in this folder:
- **`arn-azure-cost-estimate.xlsx`** — live calculator (edit any monthly value or assumption → totals recalc). Sheets: Summary, Line Items, Unit Prices, Assumptions, External SaaS.
- **`arn-azure-cost-estimate.csv`** — flat line items.

---

## 1. Per-environment summary

| Environment | Purpose | Monthly | Annual |
|---|---|--:|--:|
| **PRODUCTION** | Live broadcast pipeline — HA compute, private endpoints, ZRS | $1,161.13 | $13,933.56 |
| **TEST** | Pre-prod soak / staging — prod-parity infrastructure | $198.90 | $2,386.80 |
| **DEV** | Integration testing — minimal, public networking | $127.11 | $1,525.32 |
| **TOTAL** | | **$1,487.14** | **$17,845.68** |

Production is ~78% of spend; dev/test are deliberately lean (single instance, lower log volume, light generation).

> **Model note (2026-07-16, revised 2026-09-03):** the deployed generation model has moved from `gpt-5.4-mini` to **`gpt-5.6-luna`** (Foundry prompt agents on the IBP dev/test/prod projects). The Azure OpenAI line items below still use the `gpt-5.4-mini` GlobalStandard **token rate** as the pricing basis — luna's per-token rate is not yet in the public Azure pricing calculator, so the calculator export uses the mini rate as a proxy. Re-cost the AI line at luna's published rate once available. **This remains the largest single uncertainty in the estimate and it is a rate assumption, not a volume one.**
>
> **Pipeline basis corrected 2026-09-03.** The line below was derived from the retired deterministic pipeline (Draft → Review → Refine → Safety → ScriptDirector), which no longer exists. The delivered pipeline is `conversational-v1`. Measured over live test bulletins, the per-bulletin call shape is: `writeIncidents` ×1, `writeFlow` ×1, `compose2` ×5 (three candidates plus duration-driven revisions), `edit` ×2.3, `Safety` ×1, plus `DeliveryWriter` only when an operator requests a register change — about **10 model calls per bulletin against the 5–6 originally assumed**. Prompt sizes are known from the repository (`writeIncidents` 8.2k chars, `writeFlow` 6.6k, `compose2` 9.1k, `edit` 4.2k) and, with evidence payloads, put input volume in the same ~20-25k tokens/bulletin band the estimate already assumed, while measured output is well below the assumed 2k. The dollar figure is therefore still the right order of magnitude, but **the stated derivation was wrong and the call count is roughly double**. Re-derive properly alongside the luna rate before this estimate is used for a budget commitment rather than a planning range.
>
> Note also that the 2026-09-03 model-capacity rebalance (dev 1000 / test 4000 / prod 4000 units, where one unit is 1000 TPM) does **not** change cost. GlobalStandard is billed per token consumed; capacity is a rate limit, not a reservation.

## 2. Key cost drivers (production)

1. **Azure OpenAI — gpt-5.6-luna** (GlobalStandard): ~$5,256/yr — 600 bulletins/day at roughly 20-25k input and sub-2k output tokens across the ~10-call `conversational-v1` pipeline. *(Pricing basis: gpt-5.4-mini rate; derivation revised — see model note.)*
2. **AI Search Standard S1** (RAG style examples): ~$3,889/yr — *feature-flagged*; remove if `AzureSearch:Enabled` stays off.
3. **App Service Plan P0v3 × 2** (HA, Always On): ~$1,612/yr.
4. **Log Analytics + App Insights ingestion** (~50 GB/mo): ~$2,004/yr.
5. **5 × Private Endpoints** + data: ~$510/yr.

## 3. Line items by environment

### Production (IBP-AUE-PRD)
| Service | SKU / tier | Basis | Monthly | Annual |
|---|---|---|--:|--:|
| Azure AI Search | Standard S1, 1 unit *(flagged)* | RAG style-examples (feature-flagged) | $324.12 | $3,889.44 |
| Azure OpenAI (Foundry) | gpt-5.6-luna GlobalStandard | 600 bulletins/day @ 20k in / 2k out | $438.00 | $5,256.00 |
| App Service Plan | Premium v3 P0v3 (Linux), Always On | 2 instances (HA) × 730h | $134.32 | $1,611.84 |
| Logic App (Consumption) | Teams Adaptive-Card notifications | ~600 notif/day | $2.37 | $28.47 |
| Bandwidth (egress) | Internet egress (console + API) | ~150 GB/mo | $15.00 | $180.00 |
| Private Endpoints | 5 × Standard PE (blob/table/queue/KV/AI) | 5 × 730h + ~250GB data | $39.62 | $475.50 |
| Azure Monitor alerts | 7 rules + synthetic availability test | metric (free tier) + 2 log-search | $3.00 | $36.00 |
| Log Analytics + App Insights | Analytics Logs ingestion | ~50 GB/mo (diag + APM + AI RequestResponse) | $167.00 | $2,004.00 |
| Key Vault | Standard, MI references | ~few hundred K ops/mo | $1.00 | $12.00 |
| Storage Account | Hot ZRS + 90d soft-delete + 30d PITR | ~700 GB avg stored | $22.75 | $273.00 |
| Storage transactions | Hot ZRS read/write ops | ~0.4M write + ~5M read /mo | $4.95 | $59.40 |
| Static Web Apps | Standard (linked backend /api proxy) | 1 app | $9.00 | $108.00 |
| **Subtotal** | | | **$1,161.13** | **$13,933.56** |

### Test (IBP-AUE-TST)
| Service | SKU / tier | Basis | Monthly | Annual |
|---|---|---|--:|--:|
| Azure OpenAI (Foundry) | gpt-5.6-luna GlobalStandard | 30 bulletins/day (soak) | $21.90 | $262.80 |
| App Service Plan | Premium v3 P0v3 (Linux), Always On | 1 instance × 730h | $67.16 | $805.92 |
| Logic App (Consumption) | Teams notifications (light) | occasional | $1.00 | $12.00 |
| Bandwidth (egress) | Internet egress | ~20 GB/mo | $3.00 | $36.00 |
| Private Endpoints | 5 × Standard PE (prod-parity) | 5 × 730h + ~50GB | $37.12 | $445.50 |
| Azure Monitor alerts | parity alert rules | metric + log-search | $3.00 | $36.00 |
| Log Analytics + App Insights | Analytics Logs ingestion | ~15 GB/mo | $50.10 | $601.20 |
| Key Vault | Standard | low ops | $1.00 | $12.00 |
| Storage Account | Hot ZRS (prod-parity) | ~50 GB avg + transactions | $5.62 | $67.50 |
| Static Web Apps | Standard | 1 app | $9.00 | $108.00 |
| **Subtotal** | | | **$198.90** | **$2,386.80** |

### Dev (IBP-AUE-DEV)
| Service | SKU / tier | Basis | Monthly | Annual |
|---|---|---|--:|--:|
| Azure OpenAI (Foundry) | gpt-5.6-luna GlobalStandard | 15 bulletins/day (integration) | $10.95 | $131.40 |
| App Service Plan | Premium v3 P0v3 (Linux), Always On | 1 instance × 730h | $67.16 | $805.92 |
| Bandwidth (egress) | Internet egress | ~15 GB/mo | $2.00 | $24.00 |
| Azure Monitor alerts | basic alert rules | metric + log-search | $1.00 | $12.00 |
| Log Analytics + App Insights | Analytics Logs ingestion | ~10 GB/mo | $33.40 | $400.80 |
| Key Vault | Standard | low ops | $1.00 | $12.00 |
| Storage Account | Hot LRS | ~30 GB avg + transactions | $2.60 | $31.20 |
| Static Web Apps | Standard | 1 app | $9.00 | $108.00 |
| **Subtotal** | | | **$127.11** | **$1,525.32** |

*Lines marked* ***(flagged)*** *are feature-gated; included in the headline total but optional.*

## 4. External SaaS — billed separately (NOT on the Azure calculator)

These are real operating costs but third-party, so they do **not** appear in the Azure total:

| Provider | Item | Note |
|---|---|---|
| **ElevenLabs** | `eleven_v3` TTS (broadcast WAV) | **Dominant external run cost.** ~600 bulletins/day × ~30s. Per the SDD, generation cost ~10–30¢/bulletin (mostly TTS) → order **~$25k–$50k/yr** at full network scale. Confirm against ARN's ElevenLabs contract tier. |
| **HERE** | Traffic v7 / Routing / Geocoding | A few transactions per bulletin → order ~$1k–$5k/yr; ARN already holds a HERE agreement. |
| Microsoft 365 / Teams | Adaptive-Card notifications | Existing tenant licensing — no incremental cost (Logic App connector actions are already in the Azure total). |

## 5. Excluded (deferred per the risk register)

WAF, multi-region DR / GZRS cross-region replication, Microsoft Defender for Cloud, and reserved-instance/savings-plan discounts. Adding 1-year reserved instances or an App Service savings plan would *reduce* the compute lines by ~30–40%.

## 6. Reproduce on the Azure Pricing Calculator

<https://azure.microsoft.com/pricing/calculator/> — add these products, set **Region = Australia East**, **Currency = USD**. Quantities below are the production figures; repeat with the leaner dev/test values for those environments.

| Calculator product | Configuration (production) |
|---|---|
| **App Service** | Premium v3 tier, **P0 V3**, **Linux**, 2 instances |
| **Azure AI Foundry / Azure OpenAI** | Model **gpt-5.6-luna** (unlisted in the calculator — pick the GPT-5 mini / GPT-4o-mini line as a proxy), **Global Standard**; ~4,380 M input + ~438 M output tokens / yr (≈ 365 M + 36.5 M per month) |
| **Azure AI Search** | **Standard S1**, 1 unit *(omit if RAG stays disabled)* |
| **Static Web Apps** | **Standard** plan, 1 app |
| **Storage Accounts** | Block blob, **GPv2**, **ZRS**, **Hot**; ~700 GB + ~0.4 M write & ~5 M read ops / mo (use **LRS** for dev) |
| **Azure Monitor** | Log Analytics **pay-as-you-go ingestion ≈ 50 GB/mo**; + ~7 alert rules; synthetic availability test |
| **Azure Private Link** | **5** standard private endpoints + ~250 GB data processed / mo |
| **Key Vault** | Standard, operations only |
| **Logic Apps** | **Consumption**, ~18k standard-connector (Teams) actions / mo |
| **Bandwidth** | Internet egress ~150 GB/mo |

## 7. Assumptions & sensitivity

- **Volume anchor:** ATN serviced ~27 stations, ~3,091 reports/week (~442/day). Production modelled at **600 generations/day** (network anchor + regeneration overhead + growth); one approved script/audio is shared across co-located stations per slot.
- **Token sizing:** 20k input / 2k output per bulletin across the multi-role pipeline (RAG + prior-bulletin continuity context). All assumptions are editable on the **Assumptions** sheet of the workbook and flow through to the totals.
- **Sensitivity:** OpenAI scales linearly with bulletins/day and tokens — at 1,000 bulletins/day prod OpenAI ≈ $8,760/yr. Prompt caching (system prompts repeated per role) could cut input cost ~70% (cached input is $0.075 vs $0.75 / 1M).
- **Storage** grows over time (audit audio currently retained indefinitely, CC-010 pending); year-1 average modelled.

*Generated 2026-06-25 from `deployment/` IaC and live Azure retail prices.*
