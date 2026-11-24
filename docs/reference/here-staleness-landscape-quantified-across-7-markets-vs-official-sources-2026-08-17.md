---
title: "HERE staleness landscape — quantified across 7 markets vs official sources (2026-08-17)"
description: "Measurement study backing the Wave-3 corroboration decision: window-class distribution, closure staleness, 12-hour persistence, bidirectional official-feed corroboration (NSW+QLD), the entry-freshness re-entry loophole, and flow self-contradiction rates."
tags:
  - "here"
  - "data-quality"
  - "staleness"
  - "corroboration"
  - "measurement"
aliases:
  - "HERE staleness landscape — quantified across 7 markets vs official sources (2026-08-17)"
purpose: "reference"
status: "current"
last_verified: 2026-08-17
review_after: 2026-09-16
volatility: "medium"
source_of_truth: "Live HERE Traffic API v7 + Live Traffic NSW open feeds + QLDTraffic GeoJSON API, captured 2026-08-17T05:55Z"
verified_by:
  - "manual review"
common_queries:
  - "When should I read HERE staleness landscape — quantified across 7 markets vs official sources (2026-08-17)?"
---

# HERE staleness landscape — quantified across 7 markets vs official sources (2026-08-17)

Measurement study backing the Wave-3 corroboration decision: window-class distribution, closure staleness, 12-hour persistence, bidirectional official-feed corroboration (NSW+QLD), the entry-freshness re-entry loophole, and flow self-contradiction rates.

Method: live HERE Traffic v7 incidents per market circle (the platform's own request shape); Live
Traffic NSW open feeds (incident + roadwork, `ended` excluded); QLDTraffic GeoJSON v2 (public key).
Geometry matching at 500 m point-set distance; window classes recomputed with `IncidentWindow`'s
exact rules. Twelve-hour persistence compares the 2026-08-16T17:55Z capture (now the golden-corpus
test asset) against 2026-08-17T05:55Z. Flow contradiction: HERE flow fetched in a 300 m circle at
each sampled closure's midpoint, matched within 120 m of the closure's own shape.

## A. Window-class landscape (all seven markets, 2026-08-17T05:55Z)

| market | records | Fresh | Ongoing | Scheduled | closures | scheduled closures | fresh closures | median entry age |
|---|---|---|---|---|---|---|---|---|
| sydney | 72 | 16 | 1 | **55 (76%)** | 59 | **49 (83%)** | 9 | 327 h |
| melbourne | 220 | 37 | 25 | **158 (72%)** | 75 | 30 | 20 | 90 h |
| brisbane | 43 | 14 | 8 | 21 (49%) | 28 | 11 | 9 | 62 h |
| goldcoast | 80 | 38 | 19 | 23 (29%) | 16 | 0 | 14 | 33 h |
| cairns | 27 | 7 | 4 | 16 (59%) | 0 | 0 | 0 | 183 h |
| perth | 88 | 33 | 19 | 36 (41%) | 34 | 6 | 9 | 68 h |
| adelaide | 252 | 227 | 0 | 25 (10%) | 10 | 0 | 10 | 0 h |

- `Unknown` was zero everywhere: `startTime`/`endTime` remain 100% populated across 782 records.
- The shipped boundary demotion (decision 2026-08-17) neutralises the scheduled-closure share per
  market: Sydney 83% of closure records, Melbourne 40%, Brisbane 39%, Perth 18%.
- Fresh closures exist in every big market (Sydney 9, Melbourne 20, Gold Coast 14, Adelaide 10) —
  the gate's keep-path carries real news; blanket closure suppression would be wrong.

## B. Twelve-hour persistence — HERE is a worksite register, not an incident stream

- Sydney: 68 of 71 records survived 12 h (**96%**); 53 survivors were already >36 h old.
- Cairns: 27 of 27 survived (**100%**); 18 already >36 h old.
- Consequence: "it was in the feed an hour ago" carries almost no information about currency;
  record lifetime is weeks. Any staleness detector must use the window, corroboration, or flow —
  not presence/absence between fetches.

## C. Bidirectional corroboration vs official sources (four ground-truth markets)

| market | HERE closures | uncorroborated ≤500 m | official incident-class events | absent from HERE ≤500 m |
|---|---|---|---|---|
| sydney (NSW) | 59 | **45 (76%)** | 14 | **10 (71%)** |
| brisbane (QLD) | 28 | 13 (46%) | 4 | 2 |
| goldcoast (QLD) | 16 | 5 (31%) | 2 | 1 |
| cairns (QLD) | 0 | – | 2 | 1 |

- Uncorroborated ≠ false: NSW/QLD model long-term project sub-closures differently (or not at all),
  which is exactly why corroboration must downrank + flag, never silently suppress.
- The reverse direction is the missing-incident complaint quantified: most officially-listed
  incident-class events (crashes, breakdowns, hazards, police ops) have NO HERE record. The
  2026-08-17 morning probe caught a live 3-vehicle crash, a truck breakdown and a bus breakdown
  all absent from HERE while listed by NSW.

## D. The entry-freshness re-entry loophole (new finding, Adelaide)

Adelaide's 227 "Fresh" records: only 104 genuinely started within 24 h; **123 are long-window
records HERE (re)issued with a new `entryTime`** (median window 2 days, max 373). Of those, 56
would otherwise classify Scheduled — the entry-freshness clause (`entryTime` ≤3 h ⇒ Fresh) lets
them escape demotion. Today **zero of the 56 are closures**, so the dangerous claim class is
unaffected — but the loophole is real under bulk re-issue. Design candidate when it matters:
honour entry-freshness only for `originalId`s not previously seen (needs the cross-fetch seen-set;
cheap once the nightly monitor exists). Not urgent on today's evidence.

## E. Flow self-contradiction (Sydney closures, n=20 random sample)

- **4/20 (20%)** had traffic flowing at near free-flow within 120 m of the "closed" geometry
  (jamFactor 1.0–1.6, e.g. 46 km/h against a 58 km/h free-flow) — physically contradicted records.
- 7/20 showed real friction (consistent with active works); 9/20 had no adjacent flow coverage at
  all, so the detector can adjudicate only ~55% of closures.
- A production detector should match on the closure's own `segmentRef`s (not radius) to avoid
  parallel-street pollution, and treat ramp-described records as unadjudicable by mainline flow.

## F. What this says about the remediation order

1. The shipped boundary demotion already neutralises the dominant class (49/59 Sydney closures).
2. **Corroboration (NSW first) is the highest-value next detector** — it adjudicates the 45
   uncorroborated Sydney closures AND recovers the 71% of official incident-class events HERE
   misses; one integration serves both directions.
3. Flow contradiction is a worthwhile secondary signal (catches ~20% of closures) but only
   adjudicates roughly half of them — ship it as a downrank flag, segmentRef-matched.
4. The re-entry loophole is monitored, not fixed, until it appears on closure records.
5. The nightly monitor should publish exactly the tables above so these rates become trends.
