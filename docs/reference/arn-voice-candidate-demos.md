---
title: "ARN Voice Candidate Demos (trained ElevenLabs voices)"
description: "Four ARN-produced demo renders of ONE shared generated Sydney traffic+weather bulletin script, each voiced with a different ElevenLabs trained voice (Original MVO, Younger FVO, Younger MVO, plus one unlabelled render). A voice-selection A/B reel of our OWN pipeline output — NOT ATN on-air ground truth. Audio committed under assets/voice-candidates/."
tags:
  - "voice"
  - "elevenlabs"
  - "voice-candidates"
  - "voice-selection"
  - "audio"
  - "samples"
aliases:
  - "voice candidate demos"
  - "voice demos"
  - "trained voice samples"
  - "elevenlabs voice candidates"
  - "voice ab reel"
purpose: "reference"
status: "current"
last_verified: 2026-06-26
review_after: 2026-09-26
volatility: "low"
source_of_truth: "Four ARN-produced MP3 renders (Adobe Audition 25.6 Windows, dated 2026-06-25) of one shared generated Sydney traffic+weather script, voiced with ElevenLabs trained voices; supplied via Downloads, committed 2026-06-26 under docs/reference/assets/voice-candidates/"
verified_by:
  - "faster-whisper medium.en transcription of all four files confirming an identical shared script, 2026-06-26"
  - "ffprobe XMP/CreatorTool metadata inspection (Adobe Audition 25.6, dated 2026-06-25), 2026-06-26"
common_queries:
  - "what do our candidate voices sound like"
  - "which trained voices is ARN considering"
  - "voice demo a b comparison"
  - "where are the elevenlabs voice candidate renders"
see_also:
  - "docs/reference/atn-bulletin-style-samples.md"
---

# ARN Voice Candidate Demos

ARN produced four MP3 demo renders (supplied 2026-06-26) to compare
candidate broadcast voices. **All four read the *same* script** — one
generated Sydney traffic-and-weather bulletin — each voiced with a
different **ElevenLabs trained voice**. This is a voice-selection A/B
reel, not a set of distinct bulletins.

> **Not ATN ground truth.** These are renders of *our own* generated
> pipeline output, not real on-air ATN reads. They must **not** be added
> to or confused with the ATN aircheck corpus
> (`atn-bulletin-style-samples.md`), which is the ground-truth *target*
> the prompts and eval judge imitate. Treating our own output as the
> reference would create a self-referential grounding loop. Keep the two
> corpora separate: that file = what we copy; this file = how we sound.

## What these are for

The point of these files is the **voice**, not the copy. The script is
held constant so the listener can compare timbre, age, gender, and pace
across candidates and pick what to ship. This feeds the voice-selection
decision tracked in `voice-strategy-options.md` and the voice-library
work in `voice-audio-config-ui-plan.md` (ARN produces, tunes, previews
and versions their own voice profiles).

## Renders

Audio lives in [`assets/voice-candidates/`](assets/voice-candidates/).
All four are 48 kHz stereo MP3, 192 kbps, produced in **Adobe Audition
25.6 (Windows)**, file-dated 2026-06-25.

| File | Voice candidate | Length | Words | Pace |
|---|---|---|---|---|
| `assets/voice-candidates/original-mvo-v2.mp3` | Original male VO ("Through V2") | 31s | ~84 | 2.7 w/s |
| `assets/voice-candidates/younger-fvo.mp3` | Younger female VO | 36s | ~84 | 2.3 w/s |
| `assets/voice-candidates/younger-mvo.mp3` | Younger male VO | 35s | ~84 | 2.4 w/s |
| `assets/voice-candidates/render-ade33ebe.mp3` | Unlabelled render (filename is a job/blob GUID) | 37s | ~84 | 2.3 w/s |

Original source filenames (as supplied): `Traffic Demo Original MVO
Through V2.mp3`, `Traffic Demo Younger FVO.mp3`, `Traffic Demo Younger
MVO.mp3`, `ade33ebe-42c4-4be4-b778-2f89b7423f53.mp3`.

The four renders differ only in voice and delivery speed — the
"Original" male read is the tightest (31s, ~2.7 w/s); the younger voices
read slower (35–37s, ~2.3–2.4 w/s). Word count is identical across all
four; pace differences are the voice, not the script.

## Shared script (transcribed)

Transcribed with `faster-whisper medium.en` (2026-06-26) and
hand-corrected for Sydney place names (Whisper variously heard
"Warunga" → **Wahroonga**, "Barraura"/"Bararra" → **Berowra**,
"Dromoyne" → **Drummoyne**, "KISS 1065" → **KIIS 106.5**):

> Sydney traffic. Sydney traffic. Traffic is on the brakes on the M1
> heading past Wahroonga with stop-start patches toward Berowra. The
> Harbour Bridge and Eastern Distributor are crawling both ways, and the
> M4 near Parramatta is tough. The M5 East is slow toward the airport
> tunnels, Anzac Bridge is bumper-to-bumper westbound, and Victoria
> Road's dragging through Drummoyne. Sydney stays clear and cool this
> arvo with sunshine, mid-teen temps and a cold night on the way. That's
> your traffic and weather together on KIIS 106.5.

This is exactly the *generalised* traffic+weather shape our pipeline
produces (incident sweep + weather one-liner + station sign-off), which
is why it is useful as a voice carrier but is **not** an ATN-style read
(no embedded sponsor copy, no presenter wrapper — contrast the ATN
corpus).

## Re-listening / A/B comparison

Play the four files back-to-back, or build a per-sentence round-robin
comparison page seeded with these four renders, to evaluate them
sentence-by-sentence before committing a voice.
