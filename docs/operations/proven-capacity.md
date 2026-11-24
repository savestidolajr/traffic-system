---
title: "Proven Capacity and Performance"
description: "The load the platform has been measured at, the response times it achieves, and the limits it has been tested to."
tags:
  - "performance"
  - "capacity"
  - "assurance"
aliases:
  - "capacity"
  - "performance"
  - "load"
  - "how many stations"
  - "scale"
purpose: "reference"
status: "current"
last_verified: 2026-09-01
review_after: 2026-12-01
volatility: "medium"
source_of_truth: "Recorded load-test output and the bulletin records for each measured run"
verified_by:
  - "Retained k6 output for the health and console-read scenarios"
  - "Bulletin records for the scheduler-tick timings"
common_queries:
  - "How many stations can it handle?"
  - "How fast is a bulletin produced?"
  - "Has it been load tested?"
see_also:
  - "docs/operations/slo.md"
  - "docs/operations/known-limitations.md"
  - "docs/operations/dashboard-guide.md"
---

# Proven Capacity and Performance

The platform has been measured with real external services in the development
and test environments. The measurements cover health requests, concurrent
console reads and a full scheduler tick in which all configured station
schedules start together. They show the time taken to reach a usable terminal
result, including a safe standby outcome where current source information is
too limited for a fresh bulletin.

The results below are evidence from the previous generation pipeline, not a
promise that the current conversational workflow or every future station count
will behave identically. The [SLIs and SLOs](slo.md) page defines the live
service objectives. The figures on this page describe the largest measured
shape and the response thresholds used for that measurement.

The health and console-read figures come from retained load-test output. The
scheduler-tick timings were read back from the bulletin records after the
harness lost its polling loop mid-run on both occasions, so they carry no
separate artefact; repeat the tick if an auditor needs one.

> **Scope of this evidence:** Every figure below was measured against the previous generation pipeline, before the current conversational workflow. These figures do not establish the current workflow's capacity. Repeat the full scheduler-tick run on the current workflow before treating its capacity as proven.

## Measured results

| Scenario | Load applied | Result | Threshold measured against |
| --- | --- | --- | --- |
| Development health | 5 concurrent health users for one minute | 224 of 224 requests completed, 0.00% failed, p95 response 413 ms | Less than 0.1% failed requests |
| Development console reads | 10 concurrent operators reading the console | 1,036 of 1,036 checks passed, 0.00% failed, p95 response 382 ms | p95 response below 3,000 ms |
| Development full scheduler tick | 11 station schedule invocations released together | 11 of 11 reached a usable terminal result; p50 75 s, maximum 117.1 s | End-to-end production time below 240 s at the 99th percentile |
| Test health | 20 concurrent health users for two minutes | 3,538 of 3,538 checks passed, 0.00% failed, p95 response 425 ms | Less than 0.1% failed requests |
| Test console reads | 25 concurrent operators across the list, detail, schedule and voice surfaces | 3,272 of 3,272 checks passed, 0.00% failed, p95 response 353 ms | p95 response below 3,000 ms |
| Test full scheduler tick | 11 station schedule invocations released together, using a 25-second target slot | 10 of 11 reached a usable terminal result; p50 72 s, p95 115 s, maximum 128 s. One failed a content-quality gate | End-to-end production time below 240 s at the 99th percentile |

The test tick produced six standby outcomes and four fresh bulletins, and one
request failed a content-quality gate. That mix does not change the timing
result. The window was the quietest traffic hour of the week, and a quiet
station can have too few supported facts to fill its target slot, so the
platform correctly chooses standby audio rather than stretching an unsupported
script. The capacity measure therefore asks whether the request reaches a safe
terminal result within the time objective, while the broadcast success measure
records whether a fresh bulletin was produced. The [runbook](runbook.md)
explains how to identify the outcome shown for an individual bulletin.

## A full scheduler tick across all stations

At a full tick, the scheduler starts one request for each configured station
schedule rather than waiting for one station to finish before starting the
next. Each request obtains its own HERE data, creates its own script through
the conversational workflow, applies deterministic slot fitting and content
gates, passes through one content-safety exit, synthesises audio and waits
for the operator decision. Queue capacity and controlled concurrency prevent
the simultaneous requests from exhausting the platform or an external
provider.

The development measurement completed the eleven-request tick with a maximum
time of 117.1 seconds. In test, ten of the eleven requests reached a usable
terminal outcome with a maximum of 128 seconds, even while an external provider
applied rate limits that the platform absorbed with retry. The eleventh failed a
content-quality gate and terminated without covering its slot at generation
time; that routing was corrected the same day, so a repeat measurement is needed
before the eleven-request shape can be called proven in test. Both maxima are
well below the four-minute service objective. The measured result includes the
work required for source retrieval, conversational script generation,
deterministic slot fitting and content gates, one content-safety exit, audio
synthesis and persistence, not just the time to open a console record.

The dominant variable is external service response time and the amount of
source data that must be reviewed, not the number of words displayed in the
console. A station count above eleven, overlapping ticks or a sustained
broadcast day can change that balance. Use the live pipeline latency and
dependency panels in **Operations Overview** before increasing the station set.

## Console response times

The console read measurement used the operations an operator performs while
reviewing current work: reading the bulletin list, opening a bulletin,
reading schedules and opening voice settings. Ten concurrent operators in
development produced a p95 response of 382 ms. The larger test measurement
used 25 concurrent operators across the same four surfaces and produced a
p95 response of 353 ms, with no failed checks.

These numbers describe API response time for the measured read mix. They do
not guarantee the same result for a newly introduced configuration surface,
an unusually large history view or a client network with high latency. After
a console release, check both the user-visible page and the API response
signals in the [dashboard guide](dashboard-guide.md).

## What the previous-pipeline measurements establish

Under the measured conditions, the previous generation pipeline was known to
support:

| Capability | Evidence |
| --- | --- |
| Health and API availability under concurrent access | 0.00% failed requests in both measured environments, against a 0.1% failure threshold |
| Routine console use | 25 concurrent operators in test with p95 reads of 353 ms |
| A simultaneous station tick | 10 of 11 schedule invocations reached a usable terminal outcome within 128 seconds in test; 11 of 11 within 117.1 seconds in development |
| External rate limiting during a tick | Provider throttling was retried without pushing the recorded test timings beyond 240 seconds |

The measurements do not establish automatic cross-region recovery, capacity
for an unlimited station count, or a sustained multi-hour broadcast day with
overlapping ticks. They also do not establish that every external provider
will maintain the same response time or quota. A production outage, a new
station configuration, a larger operator population or a change to target
duration can alter the result. The [known limitations](known-limitations.md)
page records the boundaries that require operational planning.

## Re-measuring after a station-count change

Before adding stations, repeat the measurement in the test environment with
the intended station schedules enabled. Use one full scheduler tick, then
observe the following tick with the normal operator population. Compare
terminal completion time, fresh versus standby outcomes, queue depth, failed
requests, source response time and external-provider throttling with the
thresholds in the table above.

After promotion, monitor the live figures rather than relying on the earlier
capacity result. The **Operations Overview** workbook provides the operational
view, including the pipeline stage latency, dependency health and queue depth
panels. The [dashboard guide](dashboard-guide.md) explains where to find each
signal. If the 99th-percentile production time approaches four minutes, pause
further station expansion, inspect the dependency and queue signals, and
repeat the controlled test after the constraint is addressed.
