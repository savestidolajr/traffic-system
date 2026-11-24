---
title: "Monitoring Dashboard Guide"
description: "How ARN support engineers reach Azure Monitor workbooks, interpret health signals, and respond to alert rules"
tags:
  - "operations"
  - "monitoring"
  - "dashboards"
  - "azure-monitor"
  - "slo"
  - "kql"
  - "telemetry"
  - "alerts"
aliases:
  - "dashboard guide"
  - "monitoring guide"
  - "Azure Monitor workbook guide"
purpose: "runbook"
status: "current"
last_verified: 2026-09-02
review_after: 2026-12-04
volatility: "medium"
source_of_truth: "Azure Monitor workbooks, alert rules, Log Analytics, and this guide"
verified_by:
  - "Azure Portal workbook and alert inventory"
  - "SLO and first-response procedures"
  - "Health endpoint response contract"
  - "Every published query executed against the development Application Insights resource, 2026-08-05"
  - "Telemetry catalogue read from the metrics implementation and confirmed against emitted event names"
related_code: []
common_queries:
  - "Where are the ARN monitoring dashboards?"
  - "What does a healthy dashboard look like?"
  - "Which alert fired and what does it mean?"
  - "How do I read the health endpoint?"
  - "What should I check first when a dashboard looks wrong?"
  - "How do I trace one bulletin through the logs?"
  - "Which log queries should I run when a bulletin goes wrong?"
  - "What telemetry does the platform emit?"
  - "Why does grouping by station return nothing?"
see_also:
  - "docs/operations/slo.md"
  - "docs/operations/runbook.md"
  - "docs/operations/support-model.md"
---

# Monitoring Dashboard Guide

This guide is for ARN operations and support staff deciding whether the
platform is healthy, what a reading means, and where to start. Use the
[operations runbook](runbook.md) for incident response and recovery actions.

## 1. Reach the dashboard

The Azure subscription is **IBP Sub**
(`5e5f8695-2207-4572-8e80-1065a1b0c135`). In Azure Portal, open **Monitor**,
**Workbooks**, or open **Resource groups**, choose
`IBP-AUE-{ENV}-RSG-APP01`, and select **Workbooks**.

Each environment has one workbook, **IBP-AUE-{ENV} &mdash; Operations
Overview**. Until 2026-08-10 each environment also had five focused workbooks
(Broadcast Health, Pipeline Latency, Queue Status, External Dependencies, and
Cost Tracking); every panel they carried now lives in Operations Overview, and
the separate workbooks no longer exist. The deployed inventory is one
consolidated Operations Overview workbook per environment, containing the 13
panels listed in section 3:

| Environment | Resource group | Function App | Application Insights | Workbook display name |
|---|---|---|---|---|
| DEV | `IBP-AUE-DEV-RSG-APP01` | `IBP-AUE-DEV-FNA-APP01` | `IBP-AUE-DEV-AIN-APP01` | IBP-AUE-DEV &mdash; Operations Overview |
| TEST | `IBP-AUE-TST-RSG-APP01` | `IBP-AUE-TST-FNA-APP01` | `IBP-AUE-TST-AIN-APP01` | IBP-AUE-TST &mdash; Operations Overview |
| PROD | `IBP-AUE-PRD-RSG-APP01` | `IBP-AUE-PRD-FNA-APP01` | `IBP-AUE-PRD-AIN-APP01` | IBP-AUE-PRD &mdash; Operations Overview |

The workbook resource identifiers are generated values. Search by the
environment prefix and the exact display name rather than guessing an
identifier. From the current operating posture, production runs with schedules
disabled and no station armed. A quiet production window is therefore expected
by policy, while a quiet development or test
window may reflect a deliberate park. Park and resume remain available for an
announced pause; a parked environment emits no new telemetry because its
Function App is stopped and its availability test is disabled, so the latest
bins go quiet and the health-test panel stops recording. Telemetry recorded
before a park remains visible within the retention window. Establish which
posture you are looking at before reading the quiet as either normal or a fault.

Selecting **Workbooks** rather than the default **All** tab hides Azure's own
template galleries and leaves only the deployed workbook for this platform. The
screenshots throughout this page are taken from the development environment;
test and production look the same with their own prefix.

## 2. Read the dashboard

Open **IBP-AUE-{ENV} &mdash; Operations Overview**. A single **Time range**
selector rescopes every query on the dashboard. It offers 1 hour, 4 hours,
12 hours, 1 day, 3 days, and 7 days; the default is 1 day. Widen the window
before concluding that a quiet period or sparse chart is a fault.

![Operations Overview, showing bulletin health and the market breakdown](portal:workbook-operations-overview.png)

### Bulletin generation health

The overview shows successful and failed emissions in 30-minute bins, a
per-market breakdown with totals and success percentage, stage latency
percentiles, and fallback activity by reason. Healthy operation means
successful emissions dominate and fallback activity is occasional and
explainable by a missed window, limited content, or an operator decision.

These panels show trends over the selected window. They do not calculate the
rolling 30-day broadcast success objective. Run SLO 1 on the [SLO
page](slo.md) for that ratio.

### Delivery to playout

The overview shows Zetta delivery outcomes in 30-minute bins and lists delivery
failures by market and reason. Healthy operation means successful outcomes
continue and a failure does not recur for the same market or reason. If a
published bulletin does not have the console log entry **Audio delivered to
playout system**, investigate before submitting the same bulletin again. Use
the [operations runbook](runbook.md) for the response sequence.

### Queues

The queue panel charts broadcast and regeneration queue depth, and both poison
queues, every five minutes. A healthy queue drains after work is dispatched.
Persistent growth means workers are not keeping up or are failing. A queue
whose name ends in `poison` above zero is an immediate action signal.

Queue depth is an operational signal, not one of the three service-level
formulas. Treat a rising queue as an early warning for generation latency or
missed slots, even before a fallback appears.

### External dependencies

The overview shows call count, success percentage, average latency, and P95
latency for HERE, ElevenLabs, and Azure OpenAI. It also shows Azure OpenAI
429 responses in 15-minute bins. Healthy operation means successful calls,
stable latency, and no sustained 429 series. Use the dependency checks from
the [operations runbook](runbook.md), not this panel alone, to determine
whether a critical dependency is reachable.

### Availability and errors

The overview shows the health-test success percentage and the most frequent
exception patterns. A health-test failure needs a direct health-endpoint check:
the endpoint can return 503 for a critical dependency while the Function App
still responds. The health-test panel is a diagnostic web test and does not
calculate the API availability objective. Run SLO 2 on the [SLO
page](slo.md) for the exact ratio.

### Cost

The overview shows estimated ElevenLabs synthesis cost and synthesised
character totals by market. Cost is an operational trend, not a service
level. Compare a sudden increase with bulletin volume, the selected time
range, and any retry or vendor incident before escalating.

## 3. Panel reference

Operations Overview is organised into six sections, and every signal the
platform emits has a home panel. There are no separate workbooks to open for
latency, queues, dependencies, or cost.

| Section | Panel | What it shows | Healthy reading | When to escalate |
|---|---|---|---|---|
| Bulletin pipeline health | **Bulletins emitted over time** | Successful and failed emissions in 30-minute bins. | Successful emissions dominate and follow the expected schedule. | A cluster of failures or a drop in emissions. Check **Fallback activations by reason**, then run SLO 1 for the rolling ratio. |
| Bulletin pipeline health | **Bulletins by market** | Emission totals and success percentage per market. | Every market keeps emitting with stable success. | A market stops emitting or its success collapses. Read the market-dimension caveat in section 5 before reporting a rate. |
| Bulletin pipeline health | **Pipeline stage latency (P50 / P95 / P99 seconds)** | Latency percentiles per generation stage. | P99 stays below the generation target and does not trend upward. | P99 approaches or exceeds 240 seconds, or one stage dominates. No alert evaluates this objective, so inspect this panel manually after any change to generation. |
| Bulletin pipeline health | **Fallback activations by reason** | Fallback activity grouped by reason. | Fallback is occasional and explainable by a missed window, limited content, or an operator decision. | A cluster of quality-gate reasons in one hour points at generation rather than at traffic. |
| Delivery to playout (Zetta) | **Zetta delivery outcomes over time** | Successful Zetta deliveries in 30-minute bins. | Successful outcomes continue on schedule. | A published bulletin has no console log entry **Audio delivered to playout system**; investigate before submitting the same bulletin again. |
| Delivery to playout (Zetta) | **Zetta delivery failures (market / reason)** | Failed deliveries grouped by market and reason. | A failure does not recur for the same market or reason. | Recurring failures for the same market or reason. Use the [operations runbook](runbook.md) response sequence. |
| Queues | **Queue depth over time (sampled every 5 min)** | Broadcast, regeneration, and both poison queue depths, sampled every five minutes. | Work queues drain after dispatch and poison queues sit at zero. | A work queue that only rises means workers are failing or too few. Treat any poison queue depth as an alert and inspect the affected bulletin's logs and status history. |
| External dependencies | **Dependency health (HERE / ElevenLabs / Azure OpenAI)** | Call count, success percentage, average latency, and P95 latency per dependency. | Calls succeed with stable latency and no sustained throttling. | Repeated failed calls or high latency. Check vendor status and route the incident to the support team. |
| External dependencies | **Azure OpenAI 429 throttling** | Azure OpenAI 429 responses in 15-minute bins. | No sustained 429 series. | Sustained 429s: check Azure OpenAI service health and route to the vendor. |
| Availability & errors | **Health endpoint availability** | Health-test success percentage over time. | Health checks pass. | A health-test failure needs a direct health-endpoint check: the endpoint can return 503 for a critical dependency while the Function App still responds. The health test is not the API-availability objective; run SLO 2 for that ratio. |
| Availability & errors | **Top exceptions** | The most frequent exception patterns. | No exception pattern grows. | A single recurring fault usually accounts for most of the volume; group and read before assuming an incident. |
| Cost (TTS usage) | **Estimated TTS cost (USD) by market** | Estimated ElevenLabs synthesis cost per market. | Cost follows bulletin volume and the expected schedule. | Cost rises without a matching volume or retry explanation. |
| Cost (TTS usage) | **TTS characters synthesised by market** | Synthesised character totals per market. | Characters track bulletin volume. | Compare a rise against bulletin volume, the selected time range, and any retry or vendor incident before escalating. |

![Operations Overview, lower sections: Zetta delivery, queues, dependencies, availability and cost](portal:workbook-operations-overview-lower.png)

## 4. Relate panels to the service levels

| Objective | Target | Dashboard evidence | Limitation and action |
|---|---|---|---|
| Broadcast success | At least 99.5 percent over a rolling 30 days | **Bulletins emitted over time**, **Bulletins by market**, and **Fallback activations by reason** | The dashboard selector is at most 7 days. Run SLO 1 for the 30-day ratio. |
| API availability | At least 99.9 percent for API responses below 500, excluding the health endpoint, over 30 days | **Health endpoint availability**, exception patterns, and the HTTP error alert | The health test is deliberately excluded from this objective. Run SLO 2 for the ratio. |
| End-to-end generation | P99 below 240 seconds over 7 days | **Pipeline stage latency (P50 / P95 / P99 seconds)** | No alert fires on the latency objective. Inspect the **Pipeline stage latency (P50 / P95 / P99 seconds)** panel manually after any change to generation and run SLO 3. |

The Application Insights **Logs** blade and the workbook use the classic
telemetry names `customEvents`, `customMetrics`, and `requests`. Direct
workspace queries use AppEvents, AppMetrics, and AppRequests. Select the
correct Logs scope before deciding that telemetry is missing.

## 5. Query the telemetry directly

The dashboard answers *is the platform healthy*. It cannot answer *what
happened to this bulletin*, because no panel filters to a single bulletin.
That question needs a query.

Open the environment's Application Insights resource, choose **Logs**, and run
the queries below. They use the classic names (`customEvents`, `customMetrics`,
`traces`, `exceptions`); if you are querying the Log Analytics workspace
instead, substitute the names from the table at the end of section 4.

### What the platform records

Broadcast measurements are emitted as one aggregate `customMetrics` series per
logical metric. The metric's `valueSum` and `valueCount` retain the aggregate
sum/count needed for latency means and totals. The workbook and operations
queries that still read `customEvents` (bulletin, stage failures, fallback and
delivery) retain a temporary compatibility event stream while their queries
migrate. LLM attempts remain dependency rows, with operation-ID correlation
intact.

The canonical custom-metric dimensions are:

| Dimension | Meaning and bound |
|---|---|
| `market` | Normalized market key: trimmed and lower-case invariant. |
| `queue` | Lower-case queue name; queue topology values only. |
| `stage` | Lower-case pipeline stage. Stage names are also retained in the metric name for compatibility. |
| `reason` | Lower-case finite decision/failure category. |
| `model` | Lower-case model deployment identifier. |
| `role` | Lower-case workflow role (dependency telemetry). |
| `route` | Lower-case raw-model or agent route (dependency telemetry). |

`success` remains a bounded boolean dimension on emission and stage metrics
because the deployed failure-rate and synthesis alerts filter on it. Standard
`gen_ai.*` dependency properties and bounded attempt/stall flags remain
available for LLM diagnosis. Market values are no longer grouped under mixed
station/location spellings.

| Metric name | What it records | Dimensions |
|---|---|---|
| `broadcast.bulletin.emitted` | One aggregate per generation attempt, successful or not | market, success |
| `broadcast.stage.<stage>.duration_seconds` | Aggregate seconds and count for one stage | market, success |
| `broadcast.fallback.activated` | A fallback was used instead of a fresh bulletin | market, reason |
| `broadcast.tts.characters` | Characters sent for speech synthesis | market |
| `broadcast.tts.estimated_cost_usd` | Estimated synthesis cost in US dollars | market |
| `broadcast.queue.depth` | Sampled depth of one work queue | queue |
| `zetta.delivery.success` | Approved audio reached the playout system | market |
| `zetta.delivery.failure` | Delivery to the playout system failed | market, reason |

### Workbook event migration

`Telemetry:LegacyEventsEnabled` defaults to `true` so the deployed workbook
continues to render its four existing `customEvents` panels. Set it to `false`
only after those panels have been migrated to the equivalent `customMetrics`
queries. The exact current workbook queries are:

```kusto
customEvents | where name == 'broadcast.bulletin.emitted' | extend Outcome = iff(tostring(customDimensions.success) == 'true', 'Succeeded', 'Failed') | summarize Bulletins = count() by Outcome, bin(timestamp, 30m)
customEvents | where name == 'broadcast.bulletin.emitted' | extend market = tostring(customDimensions.market), success = tostring(customDimensions.success) | summarize Total = count(), Succeeded = countif(success == 'true'), Failed = countif(success != 'true') by Market = market | extend ['Success %'] = round(100.0 * Succeeded / Total, 1) | order by Total desc
customEvents | where name startswith "broadcast.stage." and timestamp > ago(24h) | where tostring(customDimensions.success) == "false" | summarize failures = count() by name, station = tostring(customDimensions.market) | order by failures desc
customEvents | where name == 'broadcast.fallback.activated' | summarize Count = count() by Reason = tostring(customDimensions.reason) | order by Count desc
customEvents | where name in ('zetta.delivery.success', 'zetta.delivery.failure') | extend Result = iff(name == 'zetta.delivery.success', 'Delivered', 'Failed') | summarize Count = count() by Result, bin(timestamp, 30m)

The stage failure query is included because the operations guide uses it to
diagnose failed stages; the latency panel itself already reads the aggregate
`customMetrics` series. Migrate the four event panels and this stage-failure
diagnostic to `customMetrics`, using `sum(valueCount)` for counts and
`sum(valueSum)` where the value itself is meaningful, while retaining the
`market`, `success` and `reason` dimensions shown above. Until that deployment
is complete, leave the flag enabled; otherwise those event panels become empty.

The stage names that appear in practice are `traffic_fetch`, `weather_fetch`,
`fuel_fetch`, `script_generate`, `tts_synthesize` and `orchestration_total`,
which spans the whole attempt. Others appear only on the paths that emit them,
including `missed_window_lateness`, `content_limited_no_fallback` and
`fallback_none`.

### Two things that will mislead you

**The market dimension is canonical.** Filtering or grouping on
`customDimensions.station` still returns nothing; use
`customDimensions.market`. New rows normalize the supplied market key by
trimming and lower-casing it, so equivalent station/location spellings now
form one series. Historical rows retain their original values until they age
out of retention.

The remaining dimensions are bounded categorical values: `queue`, `stage`,
`reason`, `model`, `role` and `route` (with the alert-required boolean
`success`). Do not introduce a new free-form dimension; put diagnostic detail
in the telemetry message or a non-dimension property. Whole-environment ratios
on the [service levels page](slo.md) remain the authoritative rates.

On development you should also expect `zetta.delivery.success` and
`zetta.delivery.failure` to be absent: development writes audio to storage
rather than to playout, so the delivery panels are empty there by design, not
broken.

### Trace one bulletin end to end

The bulletin identifier from the console is attached to every log line the
attempt produced. This is the first query to run when an operator reports a
specific bulletin.

```kusto
traces
| where customDimensions.JobId == "<bulletin id>"
| project timestamp, severityLevel, message
| order by timestamp asc
```

A completed attempt produces on the order of a hundred rows. If it produces
none, the identifier is wrong or the attempt never started; check the console's
activity view before assuming an outage.

To read the shape of the run rather than every line, keep only the status
changes:

```kusto
traces
| where customDimensions.JobId == "<bulletin id>"
| where message startswith "[StatusTransition]"
| project timestamp, message
| order by timestamp asc
```

![The Logs blade showing the status changes for one bulletin](portal:logs-trace-one-bulletin.png)

That is the query above run against the development environment: two status
changes for one bulletin, oldest first.

The last transition reached is where the attempt stopped. Compare it with the
lifecycle figure on the [diagrams page](/how-it-works/architecture-diagrams/).

### Why bulletins fell back

```kusto
customEvents
| where name == "broadcast.fallback.activated" and timestamp > ago(7d)
| extend reason = tostring(customDimensions.reason)
| summarize activations = count() by reason, bin(timestamp, 1d)
| order by timestamp desc
```

The reason names the decision, and the two halves matter separately. The prefix
is why a fresh bulletin was not used: `content_limited` means there was not
enough to say, `quality_gate` means the draft failed a check, `operator_rejected`
means a person declined it, and `missed_window` means the slot passed. The
suffix is what aired instead: `same_day` reused that day's last approved
bulletin, `filler` or `filler_cart` used standby audio.

A steady trickle of `content_limited_same_day` on quiet roads is normal
operation. A cluster of `quality_gate` reasons in one hour is not, and points at
generation rather than at traffic.

### Which stage is failing, and which is slow

```kusto
customEvents
| where name startswith "broadcast.stage." and timestamp > ago(24h)
| where tostring(customDimensions.success) == "false"
| summarize failures = count() by name, station = tostring(customDimensions.market)
| order by failures desc
```

Failures concentrated in one stage name point at that stage's external service.
Failures spread evenly across stages point at the host or the network.

```kusto
customMetrics
| where name startswith "broadcast.stage." and timestamp > ago(7d)
| extend seconds = todouble(valueSum) / todouble(valueCount)
| summarize p50 = round(percentile(seconds, 50), 1),
            p95 = round(percentile(seconds, 95), 1),
            runs = sum(valueCount)
  by name
| order by p95 desc
```

`orchestration_total` is the figure to compare against the 240-second
generation objective; the rest are its parts. On a healthy environment speech
synthesis is the largest single stage and traffic retrieval is a few seconds.
If `orchestration_total` grows while every part stays flat, the time is being
spent waiting between stages, so check queue depth next.

### Queue depth and poison queues

```kusto
customMetrics
| where name == "broadcast.queue.depth" and timestamp > ago(4h)
| extend queue = tostring(customDimensions.queue),
         depth = todouble(value)
| summarize maxDepth = max(depth) by queue, bin(timestamp, 15m)
| order by timestamp desc
```

Four queues report: the broadcast and regeneration work queues and one poison
queue for each. A work queue rising and falling is normal. A work queue that
only rises means workers are failing or too few. **Any** non-zero reading on a
queue whose name ends in `poison` is an action signal, and the matching alert
should already have fired.

### Errors and exceptions

```kusto
exceptions
| where timestamp > ago(24h)
| summarize occurrences = count(), latest = max(timestamp) by type, outerMessage
| order by occurrences desc
```

Select a row in the Portal to open the full detail, including the failing
operation and stack. Group first and read second: a single recurring fault
usually accounts for most of the volume, and the count tells you whether you
are looking at one incident or at background noise.

### What synthesis is costing

```kusto
customMetrics
| where name == "broadcast.tts.estimated_cost_usd" and timestamp > ago(30d)
| summarize usd = round(sum(valueSum), 2)
  by market = tostring(customDimensions.market)
| order by usd desc
```

This is the same estimate the **Estimated TTS cost (USD) by market** panel
charts, at the level of detail the panel does not offer. Read it using the
canonical `market` dimension above, and compare a rise against bulletin volume
before treating it as a cost problem. The [running costs page](run-costs.md)
covers the commercial picture.


## 6. Map alerts to dashboard readings

Alert rule names are prefixed by the Function App name. Open **Monitor**,
**Alerts**, **Alert rules**, then select the rule to inspect its condition,
scope, evaluation frequency, severity, and action group.

| Alert suffix | Environments | Trigger | What to inspect |
|---|---|---|---|
| `-errors-alert` | DEV, TEST, PROD | At least one Function App HTTP 5xx in five minutes; severity 2 | **Top exceptions**, FunctionAppLogs in **Logs**, and the failing route. This is a coarse 5xx signal, not the 99.9 percent API ratio. |
| `-no-executions` | DEV, TEST, PROD | No Function App requests in 15 minutes; checked every five minutes; severity 1 | Function App state, scheduler activity, and the health endpoint. A parked environment is expected to be quiet and its metric alerts are disabled. |
| `-health-availability` | DEV, TEST, PROD | The `/api/health` web test fails from 2 or more of its 3 probe locations; checked every five minutes; severity 1. New on 2026-08-10: before it, no rule watched the availability web test at all. | **Health endpoint availability**, the health endpoint itself, and Function App state. It overlaps `-errors-alert` only when `/api/health` returns 503; TLS, DNS, and stopped-host failures have no other detector. |
| `-ai-throttling` | DEV, TEST, PROD | At least five Azure OpenAI or Cognitive Services 429 responses in 15 minutes; severity 2 | **Azure OpenAI 429 throttling**, **Dependency health (HERE / ElevenLabs / Azure OpenAI)**, and Azure OpenAI service health. |
| `-poison-queue` | DEV, TEST, PROD | A queue ending in `poison` has depth above zero in a five-minute window; severity 1 | **Queue depth over time (sampled every 5 min)**, the affected bulletin's logs and status history, and the runbook poison-queue response. |
| `-synthesis-failures` | DEV, TEST, PROD | At least three failed audio-synthesis stages in one hour; severity 2. The window is deliberately an hour, not fifteen minutes: scheduled generation runs every 10 to 30 minutes, so a shorter window empties between runs and a continuous outage arrives as intermittent noise. | **Pipeline stage latency (P50 / P95 / P99 seconds)**, ElevenLabs dependency health, and the runbook synthesis response. |
| `-bulletin-failure-rate` | DEV, TEST, PROD | At least 90 percent of bulletins emitted in a 30-minute window failed; severity 1. The total-outage signal: listeners get fallback content, not traffic. | **Bulletins emitted over time**, **Fallback activations by reason**, and the failing stages. A window with zero bulletins cannot alert; `-no-executions` covers a silent host. |
| `-dependency-unhealthy` | DEV, TEST, PROD | The health endpoint logged a dependency-unhealthy warning at least once in 30 minutes; severity 1. Fires when a dependency is reachable but unusable, such as an API key the provider rejects. | **Dependency health (HERE / ElevenLabs / Azure OpenAI)** and the vendor's status. The health endpoint stays 200 for this case, so no other rule fires. |
| `-kv-access-denied` | DEV, TEST, PROD | A Key Vault AuditEvent on the Log Analytics diagnostics stream shows an authenticated caller refused (`Forbidden`, or `Unauthorized` outside the Authentication operation); severity 2. Rebuilt on 2026-08-10: the old Activity Log rule watched Security/Warning events Key Vault never emits and could never fire. | The health `keyVault` check, Function App identity and Key Vault IAM. If the role assignment is present, confirm the caller reaches the vault over the approved private link, because the rule also fires on a network-rule refusal. |

The four latched rules (`-poison-queue`, `-synthesis-failures`,
`-bulletin-failure-rate`, `-dependency-unhealthy`) stay open until a human
closes them, and suppress repeat notifications for an hour so one sustained
outage does not fill the channel.

An alert rule existing in the resource group does not prove that an email,
SMS, or Teams card was delivered. Check **Alert history** and the Teams
notification path separately.

Alert coverage is intentionally coarser than objective measurement:

1. The HTTP error rule counts raw 5xx occurrences, not the rolling API ratio.
2. The synthesis rule covers one contributor to broadcast failure, not the
   complete broadcast-success signal.
3. No deployed rule calculates burn rate for the error budgets.
4. No deployed rule evaluates the generation P99.

Do not describe service-level targets as alert rules. Run the KQL on the [SLO
page](slo.md) and inspect the **Pipeline stage latency (P50 / P95 / P99
seconds)** panel manually after any generation change.

## 7. Read the health endpoint

The endpoint is anonymous and safe to query without console credentials:

```bash
curl -sS https://ibp-aue-{env}-fna-app01.azurewebsites.net/api/health
```

Replace `{env}` with `dev`, `tst`, or `prd`. The response includes `status`,
`timestamp`, `duration`, and a `checks` array with a name, health flag,
generic detail, and critical flag.

The critical checks are:

1. `storage`, the Blob Storage data-plane check.
2. `keyVault`, whether the configured vault is reachable by the application.

The non-critical reachability checks are hereApi, elevenLabs, and
azureOpenAI. HTTP 200 means all critical checks are healthy. HTTP 503 means
one or more critical checks are unhealthy. A non-critical vendor check can be
unhealthy while the endpoint still reports healthy.

The availability test deliberately stays on `/api/health`: it is the constant-time
liveness signal for the Function App. Use `/api/health/dependencies` for the
readiness diagnostic and its cached provider classifications; it may refresh provider
probes and is not the availability-test target.

| Reading | Meaning | Next action |
|---|---|---|
| HTTP 200 and all checks healthy | Critical dependencies and vendor probes respond. | Continue with the workbook trend and console check. |
| HTTP 200 and a non-critical check unhealthy | The application can run, but a vendor path may degrade generation. | Check **Dependency health (HERE / ElevenLabs / Azure OpenAI)** and recent fallback activity. |
| HTTP 503 with storage or Key Vault unhealthy | A critical dependency prevents normal operation. | Stop treating new generation as healthy and follow the first five minutes in the [operations runbook](runbook.md). |
| No response or Function App unavailable | The host or network path may be down. | Check Function App state, alert history, and the stopped-host response in the runbook. |

The health endpoint is also the availability web-test signal. A 503 can be the
correct degraded response during a downstream outage and is not, by itself,
proof that the Function App process has crashed.

## 8. First look when something feels wrong

Use this order, then hand over to the runbook rather than repeating response
actions:

1. Query the health endpoint for the affected environment and record the HTTP
   status and named check details.
2. Confirm that the console loads. Record the affected bulletin, station,
   status, scheduled slot, and listener-facing outcome.
3. Open **Operations Overview**, set a window that includes the event, and
   check generation success and fallback activity first. Then check Zetta
   delivery, queues, dependencies, availability, exceptions, and cost.
4. Match the alert suffix to the alert table above. Check alert history rather
   than assuming an action group delivered a notification.
5. If a specific bulletin is involved, trace it by identifier using section 5.
   Otherwise select the environment Log Analytics workspace in **Monitor**,
   **Logs**, and check FunctionAppLogs for recent Error or Warning rows. Allow
   one to five minutes for ingestion.
6. Follow **First five minutes of any incident** and **Alert catalogue and first
   response** in the [operations runbook](runbook.md).

Capture the environment, resource group, alert name, workbook, time range,
health response, bulletin ID, and UTC timestamps in the escalation. Use the
[support model](support-model.md) to select the response role.
