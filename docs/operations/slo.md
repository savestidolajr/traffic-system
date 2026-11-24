---
title: "SLIs and SLOs"
description: "Service-level indicators, objectives, targets, and executable KQL for the ARN Information Bulletin Platform"
tags: [slo, sli, monitoring, observability]
purpose: "reference"
status: "current"
last_verified: 2026-09-04
review_after: 2026-12-01
volatility: "medium"
source_of_truth: "The service-level targets and the Log Analytics queries on this page"
verified_by:
  - "Operational KQL procedure"
aliases:
  - slo
  - sli
  - service level objectives
related_code: []
common_queries:
  - "What are the SLOs for ARN?"
  - "How do I measure broadcast success?"
  - "Which KQL drives the dashboards?"
see_also:
  - "docs/canonical/architecture.md"
  - "docs/operations/disaster-recovery.md"
  - "docs/operations/dashboard-guide.md"
---

# SLIs and SLOs

Three service-level objectives anchor production support. Each objective has a
measurable signal, a target, and a KQL query against the environment's Log
Analytics workspace. The Operations Overview workbook charts these signals through
the Application Insights compatibility view. Alert rules use related but
coarser thresholds, and none implements the error-budget burn-rate formulas.

When querying through the Log Analytics workspace, use the AppEvents, AppMetrics,
and AppRequests tables shown below. The Application Insights resource's own
**Logs** blade and the workbook's queries expose the compatibility names
customEvents, customMetrics, and requests. Select the correct Logs scope before
deciding that telemetry is missing.

## Measurement status at handover

No bulletin has generated in any environment in the 30 days to 2026-09-04, so
each query on this page that reads `broadcast.bulletin.emitted` or
`broadcast.stage.*` currently returns no data. The objectives below are
unmeasured rather than met: an empty result is not evidence of a 99.5 percent
success rate or a P99 below four minutes, and the targets below are unchanged.
After controlled generation resumes, re-run each query over a window that
covers real traffic and confirm it returns rows before treating any objective
as met. The release gates below already report this state as `NO DATA` rather
than a pass.

## SLO 1: Broadcast success rate at least 99.5 percent

**SLI:** the ratio of successful bulletins emitted to total bulletins
attempted over a rolling 30-day window.

A bulletin is successful when an AppEvents row has Name equal to
`broadcast.bulletin.emitted` and Properties.success equal to `true`.

Run this query in **Monitor**, **Logs**, against the environment's Log
Analytics workspace:

```kusto
let window = ago(30d);
AppEvents
| where Name == "broadcast.bulletin.emitted" and TimeGenerated >= window
| summarize
    success = countif(tostring(Properties.success) == "true"),
    total   = count()
| extend slo = success * 1.0 / total
```

No alert implements the 14.4 times or 6 times burn-rate calculations in the
error-budget table below. The closest deployed signal is the
`IBP-AUE-{ENV}-FNA-APP01-synthesis-failures` alert, which counts failed audio
synthesis stages and is only one contributor to the full broadcast-success
signal. There is no no-bulletins heartbeat alert because a station can have
long broadcast-free gaps in its schedule. Silence alone is not an outage.

## SLO 2: API availability at least 99.9 percent

**SLI:** the ratio of API responses with status below 500 to total API
responses, excluding `/api/health`, over a rolling 30-day window.

```kusto
let window = ago(30d);
AppRequests
| where TimeGenerated >= window and Url has "/api/" and Url !has "/api/health"
| summarize
    healthy = countif(toint(ResultCode) < 500),
    total   = count()
| extend slo = healthy * 1.0 / total
```

The health endpoint is excluded because it can legitimately return 503 during
a downstream outage without counting as Function App unavailability.

The deployed HTTP error alert
`IBP-AUE-{ENV}-FNA-APP01-errors-alert` counts raw 5xx responses in a short
window. It is a coarse incident signal, not the 99.9 percent API ratio above.
Run SLO 2 for the rolling objective.

## SLO 3: End-to-end generation P99 below four minutes

**SLI:** the 99th percentile of
`broadcast.stage.orchestration_total.duration_seconds` over a rolling
seven-day window. The target is below 240 seconds.

```kusto
let window = ago(7d);
AppMetrics
| where Name == "broadcast.stage.orchestration_total.duration_seconds"
  and TimeGenerated >= window
| extend value = Sum / ItemCount
| summarize p99 = percentile(value, 99)
```

No alert fires on the latency objective. After any change to bulletin
generation, open the **Operations Overview** workbook, check the **Pipeline
stage latency (P50 / P95 / P99 seconds)** panel, set a window that covers the
change, and run this query. A P99 above 240 seconds is an objective breach
signal even when every alert is quiet.

## Performance release gates

The live baseline exporter (the `perf-baseline` maintenance script in the
engineering toolkit; see the load-testing runbook) runs the standard KQL set
for each environment and records raw JSON plus rendered tables as a dated
per-environment baseline page.

The exporter preserves empty or unsupported telemetry as **no data**. It does
not treat missing observations as zero and reports a gate as `NO DATA` until a
real observation is available. A release must meet all applicable gates:

| Gate | Threshold |
|---|---|
| Health availability | Failed responses `<1%` and p95 `<2,000 ms` |
| API-read / soak | Failed responses `<0.1%` and p95 `<3,000 ms` |
| Pipeline orchestration | P99 `≤240 seconds` |

The pipeline gate is exercised at both the standard 11-job burst and the
22-job two-clock-tick burst. Retry call-count and total-delay caps are a
separate deterministic guardrail; they must pass before provider or pipeline
tuning is accepted.

## Error budgets

| SLO | 30-day budget | Fast burn-rate threshold | Slow burn-rate threshold |
|---|---:|---:|---:|
| Broadcast success at least 99.5 percent | 0.5 percent, about 3.6 hours of failed broadcasts per month | 14.4 times over 1 hour | 6 times over 6 hours |
| API availability at least 99.9 percent | 0.1 percent, about 43 minutes per month | 14.4 times over 1 hour | 6 times over 6 hours |
| Generation below four minutes P99 | Not applicable, latency objective | 240 seconds for 10 minutes | 240 seconds for 1 hour |

These thresholds are measurement targets. The deployed alert catalogue is
coarser: no alert calculates burn rate for either ratio, and no alert
evaluates the generation P99. Use the queries and the Operations Overview panels when
deciding whether to spend an error budget.

## Where the numbers come from

The platform emits one `broadcast.bulletin.emitted` event for each generation
attempt, with a success property. The event lands in AppEvents and carries the
station dimension. The broadcast duration metric is emitted at the end of each
orchestration, regardless of outcome, and lands in AppMetrics.

The direct workspace tables expose the following shapes:

| Signal | Workspace table | Important fields |
|---|---|---|
| Broadcast outcome | AppEvents | Name, TimeGenerated, and Properties.success |
| API response | AppRequests | Url, ResultCode, and TimeGenerated |
| Generation duration | AppMetrics | Name, Sum, ItemCount, and TimeGenerated |

Events and metrics both carry a station dimension. To split an objective by
station, query the metric stream, which exposes the station in
`Properties.market` and the measured value in `Sum / ItemCount`. For
example, per-station generation latency over seven days:

```kusto
let window = ago(7d);
AppMetrics
| where Name == "broadcast.stage.orchestration_total.duration_seconds"
  and TimeGenerated >= window
| extend station = tostring(Properties.market), value = Sum / ItemCount
| summarize p99 = percentile(value, 99), bulletins = sum(ItemCount) by station
| order by p99 desc
```
