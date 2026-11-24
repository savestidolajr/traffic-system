---
title: "Known Limitations"
description: "Operational constraints and missing capabilities that ARN must account for when running the platform."
tags:
  - "operations"
  - "limitations"
  - "handover"
aliases:
  - "known limitations"
  - "what it cannot do"
  - "constraints"
purpose: "reference"
status: "current"
last_verified: 2026-09-04
review_after: 2026-12-02
volatility: "medium"
source_of_truth: "The deployed platform and current operational records"
verified_by:
  - "manual review"
common_queries:
  - "Does the platform fail over to another region?"
  - "What is not covered by backup?"
  - "Are there known defects at handover?"
see_also:
  - "docs/operations/disaster-recovery.md"
  - "docs/operations/defect-log.md"
  - "docs/operations/support-model.md"
---

# Known Limitations

These are material constraints that ARN must account for when operating the ARN Information Bulletin Platform. A limitation is a capability the platform does not provide. Open findings from acceptance runs are recorded on the [defect register](defect-log.md).

## Security posture

The platform's deployed security controls are:

- **Fail-closed authentication.** The console and API require Microsoft Entra ID sign-in. Unauthenticated API requests are refused, every route is enforced by Operator and Admin roles, and storage is accessed by the platform's managed identity with shared-key access disabled.
- **Private network.** Test and production use private endpoints for storage, Key Vault and AI services, with public network access disabled and network access denied by default at the storage and Key Vault layers.
- **Data protection.** Blob soft delete (90 days in test and production, 30 days in development) and point-in-time Blob restore over the last 30 days in test and production, Key Vault soft delete with purge protection, and encryption at rest throughout.
- **Scanning in the delivery pipeline.** Dependency vulnerability scans for the backend and console fail the build on high and critical findings, and a secret scan runs over the repository before deployment. Static application analysis and infrastructure-as-code policy checks are recommended but not yet part of the pipeline.
- **Microsoft Defender for Cloud.** The subscription runs Defender for Cloud at the free tier. No paid Defender plan and no DDoS protection plan are deployed.

**An independent penetration test has not been performed against this platform.** The controls above are the measured posture; they are not a substitute for an external test. Whether to commission one is an ARN decision recorded on the [open items register](open-items.md), and the platform's published position stands until ARN acts on it.

## Single region, no automatic failover

The platform's compute and data plane run in Australia East only, with the console served from Static Web Apps in a separate Azure location. There is no secondary region and no automatic regional failover. If Australia East is unavailable, bulletin generation, the console backend, and the queue, table and blob data services are unavailable.

Station teams can continue airing fallback content held locally, but the platform cannot produce new bulletins during a regional outage. Local fallback audio is not an automatic platform failover. Use the [disaster recovery procedure](disaster-recovery.md) when regional recovery is required.

## Table Storage has no point-in-time restore

Azure Table Storage has no configured point-in-time restore. Storage redundancy does not provide point-in-time restore. Audit exports, where available, contain bulletin records and evidence, but they are not a complete restore source for schedules, synthesis history or other tables. Recovery point and recovery time objectives (RPO and RTO) for that data are undefined.

The [as-built reference](as-built.md) and [disaster recovery procedure](disaster-recovery.md) describe the current storage posture and recovery actions.

## Vendor single points of failure

HERE traffic data, Azure OpenAI script generation and ElevenLabs voice synthesis each rely on one provider. No alternate provider is configured. A sustained outage at any one of these providers can stop new bulletins that depend on that service. Existing fallback content can still be used, but it does not provide a substitute provider.

Six official authority feeds are wired alongside HERE, and the platform-wide
configuration switches on two of them. **Live Traffic NSW** answers reliably.
**QLDTraffic** answers on a shared public demonstration key that is rate-limited
at 100 requests per minute, and that key has been refused with `401` in
development since 2026-09-01; a dedicated authority key resolves both.

The other four are disabled at the global editorial scope, so a bulletin
records them as skipped rather than attempted. Enabling one for a market or a
station is a setting in **Bulletin rules**, not a code change, and what each
needs differs:

- **TasAlert** answers and parses when enabled, so Tasmanian coverage needs
  only the setting.
- **Main Roads WA** answers when enabled, so Western Australian coverage needs
  only the setting.
- **Traffic SA** is refused with `403` by South Australia's edge, which does not
  accept the platform's Azure egress address. That address must be allow-listed
  before the feed can contribute.
- **VicTraffic** would contribute nothing if enabled: its configured address
  serves the dataset's API specification rather than its data. The dataset's
  live endpoints require a DataVic API key, so the key must exist before the
  address can be changed.

When an authority feed is skipped, fails or returns nothing, the bulletin
carries HERE traffic facts only. That is a narrower evidence base rather than
an outage.


## Emergency services are console-only

The DataQuoll emergency-services integration is Phase 1 and display-only. Events are persisted and shown in the Adjust tab's Emergency panel, but are structurally excluded from bulletin script generation and cannot reach air. This is an intentional product boundary, not a feed-quality fallback; any on-air use requires an approved Phase 2 design and deployment.

## Playout requires one armed environment per station
Delivery requires an environment-level playout setting and a station-level playout flag. Only one environment may arm a station at a time because test and production share the Azure Files drop. As verified 2026-09-03, no station is armed in any environment. Both test stations (cairns-4ca and sydney) are enabled for scheduled generation but have playout disabled, so test generates bulletins on its schedule and delivers none to playout. Both production stations (cairns-4ca and sydney) are disabled for generation and playout, so production generates nothing and airs nothing until ARN enables a station. Development has playout disabled and cannot deliver.

## Playout ingest is confirmed indirectly

The delivery service writes the approved WAV through the Azure Files REST API with the Function App managed identity to ARN's shared `ibpauetststaftp01/informationbulletins` share. Zetta provides no delivery receipt or on-air timestamp; the operational ingest signal is that Zetta consumes and deletes the file. A lingering file or a station reporting no fresh bulletin therefore requires the playout-drop, tray and Zetta checks in the [support model](support-model.md), not an assumption that generation failed.

Bulletins also do not go to air until ARN's content teams schedule them into the station clock. Delivery and scheduling are separate ARN decisions.

Follow the [support model](support-model.md) and [external service dependencies](dependencies.md) when a provider outage or quota issue persists.

## Regulatory review of generated broadcast content is incomplete

A formal regulatory review of AI-generated broadcast content has not been completed. The automated content-safety check and operator approval are operational controls, but they are not a regulatory determination. ARN must establish the applicable review obligations, approval evidence and ongoing controls before treating generated content as regulatory assurance.

## Station content-safety word lists are unpopulated

Station-specific content-safety word lists are unpopulated. The content-safety check cannot block a local term that is absent from its lists. ARN content leads must supply and maintain the words, names and subjects that each station must not air, then verify the list with a controlled test bulletin.


## Route ranking can omit a road closure in busy conditions

A bulletin's rundown holds a fixed number of items, derived from the slot length: between six and fifteen. When more higher-delay major incidents are present than the slot can hold, a closed road can be omitted from the script even though the source incident data contains it. The route ranking gives the higher-delay incidents priority, and the closed road can fall outside the retained set.

An operator can therefore see a bulletin that reports other major delays while omitting a closure. Review the source panel for closures during busy periods, and reject or correct any bulletin whose script omits a closure the source data contains.

## Unattended fallback can end without standby audio

When a bulletin is handled without an operator and the selected same-day replacement has no playable audio, or its state changes while the platform is applying it, that attempt can finish with no new bulletin rather than falling back to the station's standby recording. The listener receives nothing new from that attempt.

Use the operator reject-to-fallback action rather than relying on the unattended path when a slot matters, confirm the station has standby audio configured, and check the next scheduled bulletin. Keep the bulletin reference and audio evidence for any occurrence.

## Summary counts can disagree with the source rows on one bulletin

A single bulletin can show different totals for covered roads, cited incidents, unused evidence and items below the cut across its header, its Review surface and its Adjust surface.

Treat the individual source rows and the script claims as the authority when reviewing, rather than comparing the summary figures. Record any script claim that cannot be traced to a source row.

## Per-station telemetry can split one station across several names

Different parts of the platform report a station on this telemetry signal using either the station key or the station's location name, and both reach the same dimension. A dashboard panel or query that groups by station can therefore show one station as several rows, so a per-station total or success rate reads lower than the real figure. Whole-environment figures are unaffected.

Read per-station panels as a way of finding a problem, not as a source of a rate. Take any figure that must be correct from the whole-environment service-level queries, which do not group by station.

## A bulletin can close on a standing statement rather than on running roads

When a station's available traffic, weather and fuel content does not fill its target slot, the platform adds a standing statement to reach the slot length, so a bulletin can end on a generic road-safety line instead of on the roads that are running well.

The standing statement is filling a real content shortfall. Measured on one station with the standing lines switched off, four consecutive attempts produced 27.2, 25.6, 26.8 and 28.5 seconds of speech against a thirty-second slot, each falling back to the standby recording and ending with 2.6 seconds of dead air, so disabling the lines alone worsens the on-air outcome. Keep them enabled until ARN chooses between a shorter slot for that station and a wider set of clear-road content for the platform to draw on. Both are configurable per station in **Bulletin rules**.

## Coverage uses drawn shapes, not exact route geometry

Station coverage supports circles, bounding boxes, routes and polygons. It is
not an exact road-network geometry. For a polygon, the platform searches the
enclosing area and then keeps incidents whose points fall inside the drawn
shape.

A polygon describes an area and the incident points inside it, not an
authoritative list of road segments. Check the drawn coverage shape when a
boundary road is missing or unexpectedly included. If a job has no complete
coverage geometry snapshot, the evidence map deliberately shows the incident
points without inventing a large enclosing circle. The [per-station
configuration reference](../reference/per-station-configuration.md) describes
the supported shapes.

## No enterprise edge on the console

The three ARN console domains are live and validated. Enterprise edge is enabled for development only; on test and production the console may be served from a non-local stamp, which can add latency for Australian operators.

Enabling it for test and production is a platform infrastructure change rather than an ARN DNS dependency. The console addresses are listed in the [first-time sign-in walkthrough](first-time-sign-in.md).

## Monitoring is coarser than the service objectives

The platform defines an end-to-end generation objective of four minutes at the 99th percentile over a rolling seven-day window. No deployed alert evaluates that percentile directly. Existing alerts use coarser signals, such as HTTP 5xx occurrences and synthesis-failure counts.

An absent alert does not prove that the generation-time objective is being met. Review the [service objectives](slo.md) and the **Pipeline stage latency (P50 / P95 / P99 seconds)** panel in the [dashboard guide](dashboard-guide.md) during service review.

## Two failure alerts have no live telemetry behind them

Verified 2026-09-04: `broadcast.bulletin.emitted` and `broadcast.stage.*` have zero rows in 30 days in development and test, because no bulletin has generated there in that window. The `-synthesis-failures` and `-bulletin-failure-rate` rules therefore cannot fire on real data: their queries are deployed and were proved with synthetic fixtures, but against live telemetry they return nothing. The host-level rules (HTTP 5xx, no executions, availability, poison queue, dependency health, Key Vault denial) are the only live coverage.

Operationally, a run of synthesis or bulletin failures would pass without an alert until generation traffic resumes. What makes this go away is controlled generation resuming in the environment, then re-running the [service objective queries](slo.md) over a window that covers real traffic and confirming they return rows.
