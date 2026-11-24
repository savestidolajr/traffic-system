# Dev-bulletin history corpus (`test/corpus/dev-bulletins/`)

A snapshot of the persisted bulletin-generation job history from the **dev** environment
(`ibpauedevstaapp01`), captured so every historical script can be replayed through the REAL
deterministic gate suite as of today's code. This is a **diagnostic and regression-net asset,
not a correctness fixture**: the scripts are historical LLM output and will not reproduce, but
each job's `GeneratorInputJson` is the exact writer-input snapshot, so "would this bulletin trip
gate X today?" is answerable deterministically for every persisted job.

## Layout

- `MANIFEST.json` (tracked): snapshot metadata, the era table below, and one row per job
  (jobId, era, market, station, status, timings, fallback flags, replayable marker).
- `jobs/<jobId>.json` (**gitignored**): the full Table Storage row for each job, with every
  `gz:`-prefixed column already decoded to plain JSON text. Relevant columns: `ScriptText`,
  `GeneratorInputJson`, `LogsJson`, `ContentSelectionJson`, `WeatherJson`, `FuelJson`,
  `GoodRunRoadsJson`, `TargetDurationSeconds`, `MeasuredDurationSec`, `Market`, `Status`.
- `jobs/<jobId>.logs.txt` (gitignored): the rendered `LogsJson` timeline.

`jobs/` is 8 MB of operational data and is gitignored; only `MANIFEST.json` and this README
are version-controlled.

## Refreshing

```bash
python3 scripts/snapshot-dev-bulletins.py
```

Re-run after any significant dev deployment wave to extend the history. The script regenerates
`MANIFEST.json` (including the era table, which maps UTC ranges to code eras) and the
`jobs/` payload. A replayable job is one with both `ScriptText` and `GeneratorInputJson`
present.

## Eras

From `MANIFEST.json` (`snapshotUtc: 2026-08-10T09:39:32Z`, `environment: dev`):

| Era | From (UTC) | Meaning |
|---|---|---|
| E1-current | 2026-08-10T07:40 | Always-include weather/fuel + per-station section order, on top of the 10 Aug duration work. Comparable to deployed dev code. |
| E2-10aug-pre-feature | 2026-08-10T00:00 | 10 Aug duration/pace work only (compression ceiling 1.60, pace boost 60, draft item-aim floor, filler pace-match, inhale cut). No always-include. |
| E3-release-9d5217e | 2026-08-07T00:00 | 5-7 Aug wave (voice swap, future lessons, filler windows, alerts to Teams). Old duration physics: compression 1.35, pace boost 40. |
| E4-delivery-styles | 2026-08-03T00:00 | Delivery styles, director worked examples, predicted-overrun gate, stacked pronunciation dictionaries. |
| E5-editorial-blocks | 2026-07-30T00:00 | ARN-editable editorial blocks and numeric editorial settings. Pre delivery-style. |
| E6-archive | 2026-01-01T00:00 | Pre-editorial. Historical pattern only, NOT comparable to current behaviour. |

219 jobs captured (2026-07-22 to 2026-08-10); **203 are replayable**.

## Running the scan

```bash
ARN_CORPUS_SCAN=1 dotnet test test/ArnBulletinGenerator.Domain.Tests --filter FullyQualifiedName~DevBulletinGateScan
```

The scan runs every deterministic Detect gate (mirroring `ScriptReviewLoop.ComputeViolationCounts`
plus the fidelity gates of the final grounding hard gate, enumerated explicitly, no reflection)
over every replayable script and writes `output/corpus-scan/gate-scan.md` (gitignored) with a
gate-by-era matrix, the first 5 offenders per gate, and the RollCall/SectionOrder facts. Without
the env var both facts no-op, so the normal suite is unaffected.

One known context limitation: the corpus does not persist each station's fill order, so
`SectionOrderDetector` resolves the model default weather-then-fuel order for every historical
job (called out next to that gate's number in the report).
