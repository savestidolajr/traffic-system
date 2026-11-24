---
title: "Delivery Device Map — What Measurably Changes an ElevenLabs v3 Read"
description: "Measured effect of every text-level delivery device on an ElevenLabs v3 station voice: 50 devices and 364 generations tested against an 8-run unchanged control with Welch t-tests. Establishes the audibility rule (a device is audible only if it inserts non-speech, shifts pitch, or changes duration), the confirmed-working tag list, the tag-placement rule, and the ceiling of text-only delivery control."
tags:
  - "delivery"
  - "audio-tags"
  - "elevenlabs"
  - "measurement"
  - "prompt"
  - "voice"
aliases:
  - "audio tag map"
  - "tag effectiveness"
  - "delivery devices"
purpose: "reference"
status: "current"
last_verified: 2026-08-05
review_after: 2026-09-04
volatility: "medium"
source_of_truth: "Measurement run 2026-08-05 against the deployed dev voice; raw dataset delivery-device-map.json"
verified_by:
  - "364 generations"
  - "Welch t-test vs 8-run control; operator ear-confirmation of 23 devices"
related_code:
  - "prompts/delivery-writer.md"
  - "web/src/features/delivery/cues.ts"
  - "src/ArnBulletinGenerator.Domain/Models/DeliveryStyle.cs"
common_queries:
  - "Which ElevenLabs audio tags actually work?"
  - "Why does the delivery style have no audible effect?"
  - "Where should an audio tag be placed in the script?"
---

# Delivery Device Map

## Why this exists

The delivery-style palettes and the writer prompt were both built on tags that do nothing. An
operator asking to energise a bulletin got a single `[urgently]` tag, which is measurably inert. This
document is the measured replacement for that guesswork.

## Method

The generator's own variance is large, so listening to one clip proves nothing:

| metric | control spread (8 identical runs) |
|---|---|
| duration | ±4.9% |
| pause count | ±19.2% |
| median pitch | ±3.4% |
| pitch span | ±7.1% |
| terminal slope | ±20.0% |

Procedure: fix voice, model, settings and script. Run the unchanged input 8x for a noise floor. Run
each device 4x. Compare per metric with a Welch t-test. 50 devices, 364 generations.

## The audibility rule

A text device is audible only if it does one of three things:

1. **inserts non-speech** — voiced fraction drops ≥6%
2. **shifts pitch** — ≥10%
3. **changes duration** — ≥7%

Validated 18/19 against operator ear-confirmation with zero false positives. **Pitch-span and
velocity changes are inaudible** — and span is inversely correlated with what an operator can hear,
which is why `[urgent]`, `[professional]` and `[calm]` measured as "effects" while sounding identical.

## Confirmed working

Ear-confirmed by the operator, in the two families that pass the rule:

| device | effect |
|---|---|
| `[sighs]` | inserts non-speech, span +40% |
| `[exhales]` | non-speech −7%, duration +11% |
| `[exhales sharply]` | non-speech −10%, span +38% |
| `[clears throat]` | non-speech −14% |
| `[frustrated sigh]` | duration +8% |
| `[snorts]` | non-speech −13% (audible, but wrong for a traffic desk) |
| `[happy gasp]` | non-speech −16% (reads as a plain gasp) |
| `[whispering]` | pitch −15% — **mid-script only** |
| `[excitedly]` | pitch +8% (slight) |
| `[delighted]` | non-speech −6% (slight) |

Structure works regardless of position and carries the widest range:

| device | effect |
|---|---|
| sentence splitting | duration +12%, pauses +78% |
| ellipsis | pauses +33% |
| em dash | pauses +33%, duration +8% |
| `[short pause]` | duration +13% |
| CAPS | emphasis +7%, terminal inflection +31% |
| terminal punctuation | inflection −37 → −6 st/s — the strongest single device measured |
| maximum stack | duration +39%, pauses +133% |

## Confirmed inaudible

`[calm]` `[bright]` `[urgently]` `[warmly]` `[happily]` `[booming]` `[assertive]` `[direct]`
`[curious]` `[professional]` `[alarmed]` `[dramatically]` `[reassuring]` `[amazed]` `[impressed]`
`[emphatic]` `[upbeat]` `[matter-of-fact]` `[gulps]` `[long pause]` `[fast-paced]`
`[strong australian accent]` `[Australian accent]`, exclamation marks, line breaks, SSML `<break>`,
all-lowercase, comma-heavy punctuation, quotation marks, mid-clause tag placement, stability 0.0.

## Two rules that matter more than tag choice

**Placement.** A tag on the opening line is the weakest possible position — there is no earlier
speech for it to contrast against. `[whispering]` at the start of the script measured as nothing; the
same tag before the second sentence drops pitch 22.7% and loudness 17.1%. `[professional]`, inert at
the start, narrows span 35.6% before the third sentence. The writer prompt enforces this.

**Exact strings.** The vocabulary is a lookup, not semantics. There is no morphology rule:

| works | does nothing |
|---|---|
| `[sighs]` | `[sigh]`, `[sighing]` |
| `[brightly]` | `[bright]` |
| `[excitedly]` | `[excited]` |
| `[urgent]` (weak) | `[urgently]` |

A wrong form fails silently and is indistinguishable from a tag with no effect.

## The ceiling

Text-only control on this voice reaches **+39% duration, −15% pitch, −33% voiced fraction**. There is
**no reliable way to add energy upward** — every audible device either subtracts energy or inserts a
breath. Energy in a bulletin comes from sentence structure, not from the palette.

## None of the tags exist on a non-v3 voice

Every tag in this document is an **eleven_v3-only** device. Any other model reads the tag out as
words: the same text on `eleven_multilingual_v2` airs as *"Excitedly, the Monash Freeway is closed
city bound. Exhales sharply. Burke Road is clear?"* (measured against the live dev app, 2026-08-05).

So for a station whose voice profile is not v3 the palette does not exist, and the pipeline enforces
that rather than trusting the prompt: the tag strip happens at the synthesis boundary
(`ElevenLabsAudioSynthesizer`), which every path converges on. See
`docs/canonical/decisions.md` — "Audio tags are gated at the synthesis boundary, not per caller".

This costs less than it sounds. The two strongest devices measured here are **sentence splitting
(+12.3% duration)** and **clause-ending punctuation**, and both are model-agnostic, as are ellipses
and CAPS. A v2 voice still gets a real delivery rewrite; it just gets it entirely from structure.
The `eleven_v3` column of this document is the only part that needs a v3 voice.

## Caveats

- One voice, one 8-second script, n=4 per device. A device with a true sub-1σ effect reads as inert
  here; "no detectable effect" is not "proven zero".
- The metrics do not capture artefacting, which is why stability 0.0 measures inert while an operator
  correctly hears it as degraded audio.
- Numbers are voice-specific and will drift when ElevenLabs ships a model. Re-measure per voice.
