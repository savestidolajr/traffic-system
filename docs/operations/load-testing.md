---
title: "Load and Performance Testing (Backend)"
description: "The backend/system load-test harness under test/load/: scenarios, auth model, cost controls, repeatable execution, and measured baselines. Frontend perf is covered separately in performance-testing.md"
tags: [operations, load-testing, performance, k6, slo, testing]
purpose: "runbook"
status: "current"
last_verified: 2026-09-01
review_after: 2026-12-01
volatility: "medium"
source_of_truth: "test/load/*, docs/operations/slo.md, and this repeatable execution runbook"
verified_by:
  - "Harness command and scenarios reviewed against test/load/ on 2026-09-01"
  - "Measured dev and TEST baselines retained below for comparison"
aliases:
  - load testing
  - performance testing backend
  - k6
  - pipeline burst
common_queries:
  - "How do I load test the backend?"
  - "How do we validate the 4-minute generation SLO?"
  - "What does a load-test run cost?"
  - "Why can't I load test the Test environment API?"
see_also:
  - docs/operations/performance-testing.md
  - docs/operations/slo.md
  - docs/operations/e2e-testing.md
---

# Load and Performance Testing (Backend)

This runbook defines repeatable backend load scenarios and the controls for
running them safely. Each run uses an approved window and a reversible
authentication configuration where bearer access is required; restore that
configuration and verify the expected unauthorised response afterwards. UI
performance is covered separately in `docs/operations/performance-testing.md`.

## CLI and compatibility runner

The current entry point is the `arn` CLI:

```bash
python3 tools/arn/arn.py --env <env> perf load --scenario <scenario> [--jobs N]
```

It delegates to `test/load/run.sh`, which remains the underlying compatibility
runner. k6 auto-downloads to `test/load/.bin/` (pinned v0.57.0, no global
install). Results land in `test/load/results/` as timestamped JSON.

## Scenarios

| Scenario | What it measures | Auth | Vendor spend |
|---|---|---|---|
| `health` | Availability + latency envelope of `/api/health` (SLO 2 floor; exercises PEs + downstream probes) | none | none |
| `api-read` | Operator-console read mix: `/jobs` poll → newest job detail → `/schedules` + `/voices/profiles`, at N simulated operators (ramp/hold) | bearer | none |
| `soak` | Same mix, flat low concurrency for hours (broadcast-day ambient) | bearer | none |
| `pipeline-burst` | **The one that matters**: N concurrent `POST /broadcast` → poll to terminal state; reports P50/P95/max vs **SLO 3 (≤ 240 s P99)**. Default N=11 = every market's poller firing the same minute | bearer | **real** (HERE + Azure OpenAI + ElevenLabs per job) |

Thresholds encode the SLOs (`http_req_failed < 0.1%` = SLO 2;
`sloGenerationP99Under240s` = SLO 3). Smoke baseline: a single dev bulletin ran
**70.9 s** end-to-end (target 240 s).

The `api-read` jobs-list leg is expected to exercise the scalar `JobListItem`
projection rather than reconstructing complete `Job` aggregates. Keep its
response-equivalence checks in place when changing the repository read path;
the list endpoint's correctness contract is unchanged even when its storage
work is reduced.

## Pipeline-burst release gates

`pipeline-burst` is a release gate at both the normal shared-minute
population and a two-clock-tick burst. Run the exact commands below and retain
the JSON result, queue-age observations, retry/provider call counts, duplicate
claim count, and terminal outcomes with the release evidence:

```bash
python3 tools/arn/arn.py --env dev perf load --scenario pipeline-burst --jobs 11
python3 tools/arn/arn.py --env dev perf load --scenario pipeline-burst --jobs 22
```

Both runs must keep orchestration P99 at or below 240 seconds, with no
duplicate terminal output and bounded queue age. The 11-job run represents all
markets firing in one poller minute; 22 jobs exercises the adjacent-minute
admission and recovery shape. These are release gates, not an always-on CI
job, because the real-provider mode consumes HERE, Azure OpenAI, and
ElevenLabs quota.

The N=20 burst is the gate for the same-second multi-station shape and must show zero `[TtsBudget]`/`[AoaiBulkhead]` waits above 30s and zero budget cancellations.

For a storage, queue, and Functions capacity run without vendor spend, use
the deterministic fake-provider mode by setting the Function App application
setting `Providers__ExternalProviderMode=Fake` for the approved window,
restarting the app, and reverting it immediately afterwards. The same two
commands remain the gates in fake-provider mode; label the evidence as fake
provider and do not use it as the real-vendor latency SLO.

```text
Providers__ExternalProviderMode=Fake
```

Restore the setting after the run and verify the environment's normal
provider mode before any scheduled station can air.

## Cost control (pipeline-burst)

Each job uses one conversational workflow call set against the centralized
Azure OpenAI endpoint (dev and production reach it through the raw-model
route), then deterministic slot fitting and content gates, followed by one
content-safety exit. The exact number of model calls is implementation-
dependent. Each job also uses one HERE incident/flow fetch set and one
ElevenLabs synthesis (~25 s audio ≈ 400–700 characters), plus retries on
duration misses. Keep bursts to low tens of jobs; `SMOKE=1` forces one job.
For infra-scale runs without vendor spend, flip the deployed app to the
deterministic fake providers for the window:
`Providers__ExternalProviderMode=Fake` app setting (+ restart), run the burst,
revert. That loads Functions/queue/storage/table paths at any N for ~$0.

## Repeatable execution

1. Obtain approval for the environment and test window. For production, keep
   the environment held frozen at last known-good as the fallback and do not
   arm a station.
2. Preflight the selected environment, authentication configuration, provider
   mode, and available vendor quotas. Use `health` first when the environment
   or harness has changed.
3. Choose the virtual-user count and duration for the selected scenario.
4. Run `health`, `api-read`, `pipeline-burst`, or `soak` as appropriate. The
   overlapping two-clock-tick burst is available by setting the job count for
   the run; `soak` requires bearer authentication.
5. Archive the JSON output and the resulting job identifiers in a run
   directory of the operator's choosing, alongside the environment, scenario,
   virtual-user count, duration, and timestamp.
6. Restore any temporary authentication configuration immediately and verify
   that unauthorised access is rejected. Never leave temporary elevation open
   unattended; run `soak` only with durable approved authentication or
   supervised reversible authentication.

## Authentication controls

- **dev**: the Azure CLI ARM-audience token is accepted (dev-only Easy Auth
  audience `https://management.azure.com` — note: **slash-less**; the
  slash-suffixed form does not match `az account get-access-token`'s `aud`).
  `Dev__AuthorizationFallbackRole=Admin` authorises it. This lever is dev-only
  by policy and is wiped by any full Bicep redeploy; re-add it only for the
  approved run and restore it afterwards.
- **test/prod**: bearer scenarios use a **temporary, supervised reversible
  lever**. For the exact approved window, widen `allowedAudiences` by adding
  `https://management.azure.com` (slash-less, matching the token claim) and
  set `Auth__DevFallbackRole=Admin` on that environment's Function App.
  Restore both settings immediately after the run and verify a 401. This
  temporarily gives any ARN-tenant principal with an ARM token full app Admin
  access, so never leave it open unattended and do not use it outside the
  approved test window.
- **prod**: production remains held frozen at last known-good as the fallback.
  Obtain approval before any load run and do not arm a station.

## Extending

- New read endpoint in the console mix → add to `api-read.js` `group('config surfaces')`.
- Different burst shape (e.g. staggered clock ticks) → `pipeline_burst.py`
  `--jobs N`, or duplicate `MARKETS` entries.
- CI wiring is deliberately absent: load tests are windowed, human-triggered
  events against shared environments, not merge gates.

## Measured baselines

- **2026-07-16, dev:** `health` 224/224 requests with 0.00% failed and p95
  413 ms; `api-read` 1036/1036 checks with 0.00% failed and p95 382 ms;
  `pipeline-burst` 11/11 terminal jobs within SLO-3, P50 75 s and maximum
  117.1 s against the 240 s ceiling.
- **2026-08-26, DEV:** low-volume read-only sample: `ListJobs` 131 requests
  averaging 226.5 ms (p95 469.3 ms, max 1,052.1 ms), `GetJobDelta` 3
  averaging 82.2 ms, and `GetLedger` 3 averaging 424.7 ms (p95/max
  1,086.0 ms); this sample is not a production percentile.
- **2026-07-26, TEST:** `health` 3538/3538 checks with 0.00% failed and p95
  425 ms; `api-read` 3272/3272 checks with 0.00% failed and p95 353 ms;
  `pipeline-burst` 10/10 terminal jobs inside SLO-3, P50 72 s, p95 115 s,
  and maximum 128 s against the 240 s ceiling, with one failed job recorded.
