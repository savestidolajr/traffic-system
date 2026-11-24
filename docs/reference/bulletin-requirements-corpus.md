---
title: "Bulletin Requirements Corpus"
description: "The single accumulated source of truth for what a good Arn traffic bulletin is — every requirement, rule, ATN-style target, and lesson learned from ARN feedback (BFN/WALK3) and every pipeline iteration. Written so a prompt author or a workflow-lab experiment can make ONE agent aware of everything the current pipeline only enforces downstream via deterministic gates and the refine loop."
tags:
  - "script-generation"
  - "requirements"
  - "atn"
  - "style"
  - "prompt-engineering"
  - "corpus"
aliases:
  - "bulletin requirements"
  - "what good looks like"
  - "bulletin rules corpus"
  - "everything the draft must consider"
purpose: "reference"
status: "current"
last_verified: 2026-08-10
review_after: 2026-09-30
volatility: "medium"
source_of_truth: "Synthesis of prompts/*.md, the 13 deterministic gates in Domain/Services + Application/Validation, ARN editorial feedback (BFN-001..022, WALK3-001..010), and the ATN corpus fingerprints in docs/reference/atn-bulletin-style-samples.md. Provenance cited inline."
verified_by:
  - "Direct read of prompts/*.md and the 13 gates in src/ArnBulletinGenerator.Domain/Services + Application/Validation, 2026-07-09"
  - "ARN feedback docs BFN-001..022 (2026-07-01) and WALK3-001..010; ATN fingerprints in docs/reference/atn-bulletin-style-samples.md"
common_queries:
  - "what must a bulletin consider"
  - "what rules is the draft judged on"
  - "how should an ARN bulletin sound"
  - "what does the draft agent not know"
  - "everything we've learned about bulletin quality"
see_also:
  - "docs/reference/atn-bulletin-style-samples.md"
  - "prompts/system.md"
  - "prompts/draft.md"
---

# Bulletin Requirements Corpus

The consolidated answer to "what must be factored into these bulletins." Every point
below came from ARN feedback or a pipeline iteration. Provenance is cited inline:
`BFN-nnn` = ARN editorial feedback notes (1 Jul 2026); `WALK3-nnn` = app-walkthrough
action points; a `ClassName` = a deterministic gate that enforces the rule downstream;
`ATN n=62` = a measured hit-rate in the ATN ground-truth corpus.

> **Why this document exists (the core problem).** The current pipeline pushed most of
> these rules OUT of the Draft prompt and INTO downstream enforcement — 13 deterministic
> detectors, the review→refine loop, and mechanical normalizers. The Draft agent is
> therefore *judged* on rules it was never *told*. That is why a single well-briefed
> Draft call can match the 6-role pipeline (see `experiments/`): the knowledge was always
> the moat, not the extra agents. §20 lists exactly what the Draft is currently not told
> but is scored on — the highest-leverage material to fold into any new workflow's prompt.

---

## 1. What a bulletin IS

- A short Australian commercial-FM traffic bulletin, read aloud, ~30 seconds (also 15s/45s slots).
- A **traffic desk reporting live data** — plain, direct, factual. NOT a presenter persona, NOT a personality. (`system.md`; BFN-001 tone reversal.)
- Net traffic copy runs **~25–35s for metro, ~15–25s for regional** (regional slots are mostly local sponsor copy). (ATN corpus.)
- Pace target: **~3.0–3.5 words/sec** metro; word target ≈ `targetSeconds × wordsPerMinute/60` (150 wpm default). (ATN metro corpus.)
- Delivered as **traffic copy only** — no station wrapper/sign-on/sign-off in OUR output (ATN inserts branding downstream). This is a deliberate PoC decision; the station-wrapper gap (§ATN gap #6) needs an explicit ARN product decision. (ATN gap assessment.)

## 2. The one rule that matters — actionability

- **Every sentence names a specific thing a listener can act on: a road, where on it, and what's happening there.** If you can't name the road, the spot, and the condition, don't write the sentence. (`system.md`.)
- A short, sharp bulletin of real facts beats a full-length one padded with vagueness. **Dead air is acceptable; a sentence that says nothing is not.** (`system.md`.)
- **Actionable consequence belongs on a closure/blockage** — "…steer around it", "…avoid it", "…take {named alternative}". ATN does this ("stick to Ballarat Road instead", "take Mount Alexander Road"). **This is under-emphasised in the current Draft prompt** — the detour/"avoid it" steer appears in `system.md` examples but is not a first-class instruction, and true detour advice (a named alternative route) is an open gap needing routing data (§ATN gap #4). Fold an explicit "close a closure with an avoid-it / reroute steer when one is safely derivable" instruction into any new workflow. (User observation 2026-07-09; ATN gap #4.)

## 3. Content selection & ordering

- **Worst first, by placement not announcement.** Lead with the closure or biggest delay by putting it first — never by saying "the big one" or "first up". (`draft.md`; anti-scaffolding.)
- Give **each incident its own short sentence**; never chain two incidents with "and". Voice each item **once**. (`draft.md`.)
- **~3–4 significant items** for a 30s read (global guidance); the data layer estimates `maxIncidents` that realistically fit and hands it to the Draft as a pacing gauge. ATN averages ~5.5 anchored items across 4–11. (Guidance; `DraftWordBudget.MaxVoicedIncidents`; ATN n=62.)
- When the read hits the ceiling, **STOP** — remaining lower-severity items are dropped this read, never hinted at vaguely ("a couple more spots" is banned). (`draft.md`.)
- Fill order after the incident story is **per-station configurable**: an ordered list over {MinorTraffic, Weather, Fuel, Filler}. Weather/fuel/minor items are FILL by default, voiced only when the major story + flowing roads leave the read short; a station that marks weather or fuel always-include promotes it to a FIXTURE that must air whenever its data was fetched. The weather/fuel relative order in the list is their on-air read order after the traffic body; minor traffic reads with the traffic body, and filler always closes. (BFN-003; `FillOrder`, `RequiredSourceDetector`, `ContentSectionOrder`.)
- On a **busy** read, a clear-run mention only earns airtime when it's genuinely news (a usual hotspot that's moving). On a **quiet/all-clear** read, the clear roads ARE the story — name them. (`draft.md`.)
- Never pad a sparse read by inventing content; fill honestly with named flowing roads, then weather/fuel/filler, else leave it short and let audio slot-fill + fallback handle length. (`draft.md`; slot-fill design.)

## 4. Anchoring & geography (the ATN fingerprint)

- **Direction on nearly every item** — citybound / outbound / eastbound / both ways. **ATN n=62: 94%.** Voice a direction ONLY where the data states it; a route stating its own run ("Parramatta to the city" = citybound) is derivable, not invented. (`system.md`; ATN.)
- **Cross-street / landmark anchor on ~87% of items** — "approaching Coleman Street", "after Christies Road", "near the airport", "back to the Rozelle Interchange". Pin each item to its spot ONCE, using only an anchor the data gives. (ATN n=62: 87%.)
- **Suburb retention:** every corridor with a resolved suburb already carries it in the source data — the risk is the model DROPPING it while condensing. Keep the suburb for roads that need placing. Enforced by `SuburbRetentionDetector` (replaced the removed WALK3-002 curated "ambiguous roads" list). The "3-second mental map" test: a listener must place the incident within 3s from the road name alone. (WALK3-002 → `SuburbRetentionDetector`.)
- **Magnitude as spatial EXTENT, not minutes** — "from A to B", "back to {place}", "between X and Y". **ATN n=62: 56% use extent.** `TrafficCorridor.Extent` is populated by revgeocoding corridor endpoints. (ATN; extent enrichment 2026-06-30.)
- Regional markets anchor on **landmarks/suburbs over cross-streets** ("around the KFC", "Sturt Reserve") — sparser junction data. (ATN regional corpus.)

## 5. Numbers discipline — the headline finding

- **Minutes are a 3% exception, never the lead.** ATN quotes a drive-time minute count in only 2 of 62 reads, always as a single monitored-route item. Our drafts historically quoted one in 70% (sometimes stacked) — **this is the single largest remaining style gap.** (ATN n=62: 3%; look-and-feel review.)
- **The only minute figure allowed is one commuter drive-time run, said whole:** "the M4, Parramatta to the city, about 22 minutes." No per-incident minute counts, no stacked figures, no "X min, normally Y" comparisons beyond that one. (`review.md` check 4; `TrafficScriptStyleValidator`: fails at >3 numeric matches / ≤2 voiced.)
- **Distance rounds to the nearest 5** — "about 26 kays" → "about 25 kays". (BFN-011; `review.md`.)
- Prices and temperatures are exempt from the number cap. (`review.md`.)
- No imperial units ever ("a mile-and-a-half" was a live defect). (2026-06-14 units fix.)

## 6. Incident-type fidelity

- **Say how the road DRIVES, not a category label** — "bumper to bumper", "stop-start", "slow through the works" — never "congestion, moderate delays". Pick the ONE most telling descriptor per item; never stack type + impact + detail in one breath. (`draft.md`; `ImpactStackDetector`, `IncidentTypeStackDetector`.)
- **A closure is "closed", not "shut"** as the headline verb ("the M1 is closed at Kent Road"). Keep "shut" only as occasional non-lead variety. (BFN-017; `ClosureVerbNormalizer`.)
- **Never change what the data says happened** — a "road closure" is not a "crash"; a closure leads and reads as "closed / avoid it", never deflated to a small delay. (`system.md`, `review.md` check 3.)
- **Roadworks ≠ jam.** Never escalate a roadworks/minor item to a congestion verb ("crawling", "at a standstill") the data doesn't state for that road. (reviewer-loop cycle 2.)
- **Never say congestion is building/easing/clearing** without a trend line in the data. (`system.md`.)
- Say it as spoken English with the road as subject and the incident as something ON it — "X is closed", "there's a breakdown on X" — never the data's label as a predicate ("X is road closed", "X is a road hazard"). (`styleRules`; `IncidentCommentNormalizer`.)
- Voice the **cause/event** when the data names it ("ASICS Gold Coast Marathon 2026", "Torrens To Darlington – Civil Works") — the reason is what a listener wants. Never invent a cause. (`draft.md`.)

## 7. Vagueness & fabrication — the #1 and #2 failures

- **VAGUENESS is the top failure.** A sentence naming no specific road/spot/condition — "a few more hold-ups around town", "minor congestion in the suburbs", "keeping things busy", "the rest of the run's clear" (with no named road behind it) — tells a listener nothing and worries them about roads that are fine. CUT the vague line; don't reword it. (`review.md` check 1.)
- **FABRICATION:** never a road, cross-street, direction, lane count, cause, time, weather condition, or price not in the source data. (`review.md` check 2.)
- A **knock-on effect** on another road ("Y is taking the extra load", "delays building on the approaches to Z") is fabrication unless the data states it for THAT road — a closure does not license inventing consequences elsewhere. (`review.md` check 2.)
- An **all-clear claim** ("the rest of the main runs are clear") with no flowing-roads data behind it is fabricated reassurance. A clear-road claim is a fact about a NAMED road or it is not said — no "no other dramas", "everything else is moving" sweeps. (`review.md`; `RollCallDetector`.)
- **Style pressure without a hard data-only rule makes the model fabricate cross-streets** — the anchoring push must always be subordinate to "anchors/directions only from data". (2026-06-10 eval finding.)
- A **fake location from an incident TYPE** ("closed at a road closure", "shut near the road closure point") when the data carries no anchor — "road closure" is WHAT happened, never WHERE. (`FakeLocationDetector`.)

## 8. Repetition & variety

- **Turn-of-phrase variety = restructure the sentence, not swap a synonym.** "M1 has a breakdown" / "breakdown on the M1" — vary HOW the same fact is said. (BFN-009.)
- **No cloned sentence template** down the bulletin ("King Street is moving freely through the CBD. Collins Street is moving freely through the CBD…"). (`SentenceCloneDetector`.)
- **No road-name roll-call** — three or more bare road names in a row, or 3+ distinct clear roads dumped. Sweep into one area phrase instead, but only over NAMED roads. (`RollCallDetector`.)
- **Consecutive closures must not repeat "[Road] is closed at [Location]" three times** — drop the "is" on later ones, or join two with a comma (elliptical/compressed list). (BFN-018.)
- **No duplicate anchor within one sentence/item** — "Over at Smith Street… at Smith Street Motorway" names Smith Street twice. Name a road once per item; refer back implicitly ("the closure"). (BFN-006/BFN-020; `DuplicateAnchorDetector`.)
- **No repeated preposition listing two spots** — "shut at Brook Street, at the ramp to Alfred Street North". Two separate incidents on one road get varied phrasing or only the worst voiced. (`RepeatedPrepositionDetector`.)
- **No restatement padding** — the same item voiced twice in different words. Everything after the first complete statement is padding; reach for an unused source instead. (`review.md` check 6b.)
- **No stacked severity synonyms** in a trailing clause — "congested at X, with heavy traffic and moderate delays" says "bad" three times. One honest magnitude per corridor. (`ImpactStackDetector`.)
- **No trailing incident-type dump** — "…for upgrades, including closures and speed reductions". Type words are notes, not copy. (BFN-019; `IncidentTypeStackDetector`.)
- **When two roads share one condition, say the condition ONCE** — "Barclay Road and the M2 are both at a standstill", not the clause repeated per road. Two roads with DIFFERENT states can share a sentence, each keeping its own short state. (`styleRules`.)
- **Redundant clause:** "shut at the ramp to the Pacific Motorway" — "ramp" already implies the connecting road; drop the destination clause ("shut at the ramp"). (BFN-005.)
- **Connect related facts into one clause** — "roadworks between Greensborough and Rosanna Road, with a lane blocked" — not two comma-spliced bolted-on facts. (BFN-008.)
- **Two sequential bulletins on the same data must not read the same** — a new opening (different lead road or sentence shape), fresh connectors, no near-verbatim repeat. A per-read random "variety angle" seeds this. (anti-repetition Phase 1.)

## 9. Meta-leaks & scaffolding (AI-tells)

- **Never read internal labels aloud** — "corridor", "monitored route", "good run", "the data", "logged", "the listed roads", "the current feed". Say what a listener hears. (`system.md`; `MetaLeakDetector`.)
- **Never narrate what's missing** — "no fuel data", "weather isn't reported", "nothing else to report". If a source isn't there, stay silent and stop on the last real item. (`system.md`; BOUNDARY.)
- **No meta-scaffolding / narration phrases** — "the worst of it", "the worst of the rest", "on the brighter side", "off that", "first up", "moving on", "in the mix", "in the same boat", "the pinch point", "keeping things a bit busy". (anti-scaffolding rule; reviewer-loop cycles 2–3.)
- **No ranking labels** — "the big one", "the good news is". No invented ranking between equal items. (`review.md` check 5; reviewer-loop cycle 4.)
- **No intro, sign-off, or generic advice** — "that's the traffic for…", "take care", "allow extra time", "plan ahead", "stay safe", "motorists warned". Report it; don't react to it. (`system.md`; `TrafficScriptStyleValidator` banned-closer.)
- **No subjective interpretation / editorialising** — "it's gonna be a wet one", "in a bit of strife", "knocking around a few minutes" → replace with a concrete verb ("adding a few minutes"). If we're open about it being AI, there's no need to perform being human. (BFN-001, BFN-010.)

## 10. Time-of-day discipline

- If a market daypart is known (from the schedule timezone), any time reference must fit it, phrased naturally — "this morning/afternoon/evening", or "overnight"/"around midday" alone. Never "this overnight", never a different daypart. (`draft.md`.)
- If the airing time is NOT known, **never state a time of day** — a wrong daypart on air destroys trust. (`draft.md`.)
- The model habitually opens all-clear reads with "…this morning" regardless of the clock; this is mechanically corrected after generation (`TimeOfDayNormalizer` rewrites the mismatched daypart, or to "right now" when unknown) because routing it through the reviewer gutted scripts. (`TimeOfDayNormalizer`; Port Lincoln replay.)

## 11. Weather (only when selected)

- One weather line only: **condition + temperature**, nothing more — no forecast, no wind speed, no rain-chance %, no stacked sky labels. (`draft.md`, `review.md` check 7.)
- A weather **ALERT** is safety content — it always reaches the writer and opens the read in one plain sentence, then into traffic. (`draft.md`; `weatherLeads`.)
- **Naturalise HERE's raw condition vocabulary** — "Sprinkles" → "chance of a shower". Cover the full variant space (showers, patches of dry, partly cloudy/sunny, etc.) via a translation table, not verbatim pass-through. (BFN-022; `ContentFormatters.FormatWeather`.)
- If weather was NOT selected, ANY weather mention (temperature, sky, wind, rain) is fabrication and must not appear. (`review.md`; `UnselectedSourceDetector`.)
- If weather was selected AND the station marks it always-include (`WeatherRequired`), it must air whenever its data was fetched: a required source is a FIXTURE, not fill. (`RequiredSourceDetector`.)
- An always-include weather section also survives the duration trim: `BulletinDurationFitter.FitByTrimming` sheds traffic down to its `minSentences` floor rather than drop a required close, and returns no trim at all when traffic is already at the floor, leaving the residue to compression and the post-synthesis pass. The honest limit: a slot too short to hold traffic plus the required sections squeezes the traffic body hard, because the word target does not reserve room for a required section. (`BulletinDurationFitter`.)
- Fail-soft: a required source whose fetch FAILED cannot air, and the bulletin still goes out without it: a HERE outage must never take a station off air. (provider fail-soft.)
- On-air placement: weather reads after the traffic body, in the station's fill-order slot, before fuel when weather sits earlier in `FillOrder`. (`ContentSectionOrder`, `SectionOrderDetector`.)

## 12. Fuel (only when selected)

- One line after the traffic body, positioned by the station's fill order (before weather when fuel sits earlier in `FillOrder`): cheapest unleaded **and its suburb**, never a bare market-wide price ("$1.45 a litre", where?). When the data lists diesel, say it too, each price said ONCE. (BFN-012; `draft.md`, `review.md` check 8; `ContentSectionOrder`.)
- Only Unleaded and Diesel — never an average, station count, price board, or premium types. (BFN-012; `FuelOptions.FuelTypes`.)
- If fuel was NOT selected, ANY fuel/price mention is fabrication. (`UnselectedSourceDetector`.)
- If fuel was selected AND the station marks it always-include (`FuelRequired`), it must air whenever its data was fetched: a required source is a FIXTURE, not fill. (`RequiredSourceDetector`.)
- An always-include fuel section survives the duration trim the same way: `BulletinDurationFitter.FitByTrimming` sheds traffic to its `minSentences` floor rather than drop the required close, and returns no trim at all when traffic is already at the floor. The honest limit mirrors weather: a slot too short to hold traffic plus the required sections squeezes the traffic body, because the word target does not reserve room for a required section. (`BulletinDurationFitter`.)
- Fail-soft: a required source whose fetch FAILED cannot air, and the bulletin still goes out without it: a HERE outage must never take a station off air. (provider fail-soft.)

## 13. Continuity (re-reads within a day)

- A re-read must sound **clearly different** from the prior same-day bulletin (new opening, fresh connectors), while building on it as an update. (`draft.md`.)
- An item **still ongoing** carries ONE continuation word ("still", "no change there", "that earlier…") — and only once across the whole read. A **new** or **cleared** item is NEVER called "still". (`draft.md`, `review.md` check 9.)
- A **cleared** item is closed off in a phrase ("that earlier breakdown has cleared"), not silently dropped. (`system.md` example.)
- Continuity delta comes from the `incidentChanges` block (New / Still ongoing / Cleared labels). (`IncidentChangeFormatter`.)

## 14. Names & spelling

- Every road, suburb, and place name spelled **exactly as the source data spells it** ("Scottsdale", never "Scottdale") — a misspelling airs as a mispronunciation. (`review.md` check 6c.)
- HERE sometimes emits SHOUTING all-caps names ("SMITH STREET MOTORWAY") — title-cased before reaching the writer so they never air as caps. (`RoadNameFormatter`.)
- A road with SEPARATE location spots has one incident per spot: voice them separately ("…and further along at…") or voice only the worst — never blend two locations into one compound place. (`draft.md`.)
- Slash-separated anchors ("Todd Road / Cook Street") are normalised to "and" before the writer, and route codes expanded, so nothing reads a literal slash aloud. (`RoadNameFormatter.ToSpeakable`; slash-leak fix 2026-07-09.)

## 15. Length & slot-fill

- Length is a **soft aim** (~`targetWords`), not a hard gate for the writer. Short and sharp (all real facts) is good; only OVER-length (past the ceiling) or vague-padding is a problem. (`review.md` length note.)
- The slot is exactly `targetSeconds`; a read past the ceiling can't air. On a busy market there's almost always more traffic than fits — that's normal, voice the worst and stop. (`draft.md`.)
- Downstream, the audio pipeline guarantees an exactly-slot-length file: content-first fill, then bounded pitch-preserving speed-up (never slow), else fallback. Never slow-fill. (slot-fill design; `SpeechDurationOptions`.)
- If underfilled and unfixable within the cap → **content-limited → fallback chain** (most-recent same-day bulletin, else generic filler cart), not dead air. (fallback design.)

## 16. Filler statements (ARN-authored, never LLM-touched)

- Per-market admin-authored standing road facts, appended **deterministically after** generation/review — never shown to the writer or reviewer, never critiqued or reworded. (2026-07-06 decision.)
- Selected by word-count fit to the remaining gap; must **not repeat** the same statement in a station's next same-day bulletin. (BFN-002; `FillerStatementSelector`.)
- A station whose `FillOrder` omits Filler has opted out of the cart. (`FillOrder`.)

## 17. Precedence / chain of command (higher wins)

1. **Accuracy and safety** — never overridden.
2. **Output contract** — return only the bulletin (or the workflow's specified format).
3. **Producer feedback** (`<producer-feedback>`) — authoritative real-world context and style for THIS read; outranks style defaults, never accuracy.
4. **Standing guidance / house-style** (`<guidance>` / `<house-style>`) — narrower scope wins (station > market > global).
5. **The style defaults** — used unless 3 or 4 steer otherwise.

(`system.md`.) Guidance can be date/time-windowed (WALK3-007, spec'd not built).

## 18. Delivery / voice (the Director stage + BFN tone)

- Delivery tags steer HOW it sounds, never WHAT it says — byte-identical spoken words (word-immutability guard); fail-safe to the plain script. (`director.md`, `IScriptDirector`.)
- **Remove breath / conversational-fluff tags** (`[sighs]`, `[exhales]`) and script-level filler ("look", "so", trailing asides) — attempts at human-like naturalness read as MORE synthetic. Over-formulaic "naturalness" (breaths, upward inflections) is the AI-tell, not the fix. (BFN-004, BFN-013.)
- v3 audio tags only for v3 models; non-v3 speaks the tags aloud, so they're gated. (`director.md`.)
- Per-station **voice rotation** / same-voice-same-time consistency is a shipped feature — validated by ARN as correct. (BFN-016; `VoiceRotationPolicy`.)
- Per-voice `PaceBoostWpm` sizes the pre-synthesis script only (works on v2 AND v3, unlike ElevenLabs' capped/ignored native Speed). (Tim's speed-limitation ask.)

## 19. Positive exemplars (target style)

- "The Pacific Highway's shut both ways at Hexham after a crash — steer around it. The New England Highway is bumper to bumper on the approach, and the run in from Maitland is sitting a touch over the usual twenty minutes. Cessnock Road and Nelson Bay Road are both clear." (`system.md` — worst-first, anchored, one whole drive-time, avoid-it steer, traffic-only close.)
- "Tulla inbound's only a touch slow, and the Monash inbound is running close to normal." (BFN-015 — clean factual/objective target: concrete, no colour.)
- "Roadworks from Plenty Road to the M80, between the Eastern Freeway and Bridge Street." (extent-over-minutes: magnitude as span, no minute count.)
- Real ATN: "the M2 chewing up citybound North Ryde to the Lane Cove Tunnel. Very heavy M4 motorway citybound at Eastern Creek — stacks of traffic at the M7/M2 merger." (dense, anchored, directional, severity-by-feel.)

## 20. What the Draft agent is currently NOT told but IS judged on

> This is the actionable heart of the corpus and the user's 2026-07-09 observation. These
> rules live only in downstream gates / the refine loop / mechanical normalizers, so the
> Draft never learns them and re-offends every run. A new single-agent workflow should
> fold ALL of these into its system/draft prompt so it gets them right the FIRST time.

- **Actionability close** — "so avoid it" / "steer around it" on a closure is only in examples, not an explicit instruction. (§2.)
- **Roll-call ban** — enforced by `RollCallDetector` only; Draft is not told "≤2 named clear roads, then sweep an area phrase". (§8.)
- **Duplicate anchor / repeated preposition / fake location** — all code-gated (`DuplicateAnchorDetector`, `RepeatedPrepositionDetector`, `FakeLocationDetector`); Draft gets no explicit rule. (§7, §8.)
- **Cloned sentence shape** — `SentenceCloneDetector` only. (§8.)
- **Stacked type / impact words** — `ImpactStackDetector`, `IncidentTypeStackDetector` only; Draft's ban is a single line it ignores under sampling. (§6, §8.)
- **Meta-leak vocabulary** — `MetaLeakDetector` catches "data"/"logged"/"listed roads"; Draft's prose ban is unreliable. (§9.)
- **"Closed" not "shut"** — `ClosureVerbNormalizer` rewrites it post-hoc; Draft still defaults to "shut". (§6.)
- **Time-of-day** — `TimeOfDayNormalizer` rewrites it; Draft still guesses "this morning". (§10.)
- **Unselected weather/fuel** — `UnselectedSourceDetector` catches leaks; Draft is told once. (§11, §12.)
- **Required weather/fuel dropped**: `RequiredSourceDetector` blocks a required-and-fetched source absent from the script, with no mechanical Repair (a missing fact cannot be invented); the Draft is told via the `requiredSections` prompt argument. (§11, §12.)
- **Suburb retention** — `SuburbRetentionDetector` re-injects dropped suburbs; Draft isn't told which roads need placing. (§4.)
- **Distance rounding to 5 / minute discipline (3% target)** — split between prompt and `TrafficScriptStyleValidator`; the "minutes are a 3% exception" framing is NOT in the Draft prompt strongly enough (still voiced in ~70% of drafts). (§5.)

**Design implication (grounded in `experiments/` results):** the review→refine loop was
observed *adding* fabrication and churning while these rules stayed unlearned. A single
Draft agent briefed with §2–§14 up front — plus the deterministic gates kept as a safety
net, NOT as the primary teacher — is the shape to trial. Keep the deterministic gates
(they catch model-variance misses reliably); stop relying on them to teach the model what
good looks like.

---

## Provenance index

- **Prompts:** `prompts/{system,draft,review,refine,safety,director}.md`
- **Deterministic gates** (`src/ArnBulletinGenerator.Domain/Services/` + `Application/Validation/`): `RollCallDetector`, `DuplicateAnchorDetector`, `RepeatedPrepositionDetector`, `FakeLocationDetector`, `SentenceCloneDetector`, `ImpactStackDetector`, `IncidentTypeStackDetector`, `MetaLeakDetector`, `UnselectedSourceDetector`, `RequiredSourceDetector`, `SectionOrderDetector`, `ContentSourceSignals`, `SuburbRetentionDetector`, `TrafficScriptStyleValidator`; normalizers `ClosureVerbNormalizer`, `TimeOfDayNormalizer`, `IncidentCommentNormalizer`, `RoadNameFormatter`.
- **ARN feedback:** BFN-001 to BFN-016, BFN-017 to BFN-022, and WALK3-001 to WALK3-010.
- **ATN ground truth + gap history:** `docs/reference/atn-bulletin-style-samples.md`.
