---
title: "ARN Pronunciation Guides — Index"
description: "Index of the per-market place-name pronunciation guides that seed each ARN station's ElevenLabs eleven_v3 alias pronunciation dictionary."
tags: [pronunciation, reference, tts, index]
purpose: "reference"
status: "current"
last_verified: 2026-06-26
review_after: 2026-09-24
volatility: "low"
source_of_truth: "Per-market guides in this directory; web research cited per entry; respellings auditioned in the ElevenLabs studio before air"
verified_by:
  - "web research (sources cited per entry in each market guide)"
  - "orchestrator spot-check verification (2026-06-26)"
aliases:
  - "pronunciation guides"
  - "place name pronunciation"
  - "pronunciation dictionary research"
common_queries:
  - "Where is the researched pronunciation data for each ARN market?"
  - "What suburb and road respellings should a market's pronunciation dictionary contain?"
  - "How do we decide ElevenLabs alias respellings for a station?"
see_also:
  - docs/reference/pronunciation/sydney.md
  - docs/reference/pronunciation/melbourne.md
  - docs/reference/per-station-configuration.md
---

# ARN Pronunciation Guides — Index

Per-market research on the local place, road and landmark names that a generic
text-to-speech (TTS) reader gets wrong. Each guide seeds that station's
ElevenLabs **`eleven_v3`** pronunciation dictionary (alias rules). The value is
**niche local knowledge** — names locals say differently from how they are
spelled or how an outsider/TTS would guess (Aboriginal-derived names, silent
letters, counter-intuitive stress, anglicised forms).

Researched 2026-06-26 by one area-expert pass per market (web research, sources
cited per entry), then spot-check verified by the maintainer. **393 entries**
across 11 markets.

## Guides

| Market | Station | State | Entries | Guide |
| --- | --- | --- | --- | --- |
| Sydney | KIIS 90s SYD | NSW | 35 | [sydney.md](sydney.md) |
| Melbourne | KIIS 101.1 | VIC | 72 | [melbourne.md](melbourne.md) |
| Brisbane | KIIS 97.3 | QLD | 30 | [brisbane.md](brisbane.md) |
| Adelaide | KIIS 102.3 | SA | 37 | [adelaide.md](adelaide.md) |
| Perth | Gold 96FM | WA | 49 | [perth.md](perth.md) |
| Gold Coast | Hot Tomato 102.9 | QLD | 37 | [goldcoast.md](goldcoast.md) |
| Darwin | Hot 100 100.1 | NT | 29 | [darwin.md](darwin.md) |
| Mildura | River 1467 | VIC | 34 | [mildura.md](mildura.md) |
| Port Augusta | 5AU 97.9 | SA | 22 | [portaugusta.md](portaugusta.md) |
| Port Lincoln | Magic FM 89.9 | SA | 32 | [portlincoln.md](portlincoln.md) |
| Queenstown | 7XS 92.1 | TAS | 16 | [queenstown.md](queenstown.md) |

## How these feed the product

- The respellings become **alias** rules in a per-station ElevenLabs
  `eleven_v3` pronunciation dictionary. v3 applies alias (plain-text
  substitution) rules and **ignores** phoneme/IPA rules — so every "Alias
  respelling" column is plain ASCII, never IPA.
- Guide seed rows replace only the proper-noun token (e.g. `Monash`, never
  `Monash Freeway`); generic words like Road/Freeway/Highway read fine. The
  Review surface also supports a data-backed **whole-road phrase** rule when
  the local correction changes the road's name (for example, hovering
  `Gillies Highway` selects that complete phrase so it can be taught as
  `Gillies Range Road`).
- Replacements are a single fluid token where possible (`Nepean` -> `Nepeen`);
  a hyphen forces a syllable break (`Mordialloc` -> `Mordy-allock`).
- These are **seed** respellings. An operator auditions and trims each one in
  the ElevenLabs studio before air; a respelling that sounds worse on the
  station's actual voice/model is dropped.
- To load or update a station's dictionary from a guide, use the pronunciation
  studio in the operator console, or the `/api/pronunciation-dictionaries` API
  directly.

## Maintenance

- Each guide cites a source URL per entry; entries the researcher could not
  source formally are flagged "low confidence" (mostly common-local-usage
  items) — confirm these by ear in the studio before relying on them.
- When a market's coverage area or road network changes materially, refresh the
  matching guide and re-audition any new respellings.
