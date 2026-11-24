---
title: "Glossary"
description: "Definitions for every platform term the rest of the documentation assumes you already know."
tags:
  - "handover"
  - "reference"
  - "glossary"
aliases:
  - "glossary"
  - "what does this term mean"
  - "definitions"
  - "terminology"
purpose: "reference"
status: "current"
last_verified: 2026-09-01
review_after: 2026-12-01
volatility: "medium"
source_of_truth: "The platform code and the documents each term is defined in"
verified_by:
  - "manual review"
common_queries:
  - "What is standby audio?"
  - "What is a clear road?"
  - "What does jam factor mean?"
see_also:
  - "docs/operations/platform-overview.md"
  - "docs/operations/operator-guide.md"
---

# Glossary

Definitions for the ARN Information Bulletin Platform are listed in alphabetical order. Use the linked page in an entry when you need the full operating or configuration procedure.

**Automated audio check**

The automated audio check compares a transcription of the finished recording with the written script. It flags differences for a person to listen to before approval. See the [operator guide](operator-guide.md).

**Bulletin**

A bulletin is a short piece of broadcast audio about current traffic conditions for one station and one scheduled slot. The platform prepares the script and audio, presents both for review, and delivers only approved audio to playout. See [what this platform does](platform-overview.md).

**Bookend**

A bookend is a station's fixed opening line, closing line, or both, wrapped around every bulletin for that station. Pairs are authored once in a shared library and assigned to a station; the text can carry `{token}` placeholders such as `{city}` that are filled per station. If a pair cannot share a very short slot with the traffic read, it is dropped for that bulletin so the traffic always airs. See [per-station configuration](../reference/per-station-configuration.md).

**Cart**

In ARN broadcast language, a cart is a prerecorded audio item. In this platform, a configured cart can provide standby audio when a fresh bulletin and an earlier same-day bulletin are unavailable. See the [operations runbook](runbook.md).

**Clear road**

A clear road is a road that the traffic feed reports as free-flowing at the time of collection. The platform can use that evidence when a bulletin needs a useful traffic update without a significant incident, but a clear road is not a promise that conditions will remain unchanged. See [per-station configuration](../reference/per-station-configuration.md).

**Content priority order**

Content priority order is a station setting that determines which optional content is considered when traffic evidence does not fill the target duration. It lets ARN decide whether weather, fuel, or other enabled content is considered first. See [per-station configuration](../reference/per-station-configuration.md).

**Content-safety check**

A deterministic gate that checks a script for prohibited or unsafe wording before the recording is prepared. A gate either passes the script or sends the slot to the fallback path; it does not initiate an automated correction retry. The operator still reviews the source panel and audio.

**Coverage area**

A coverage area is the geographic area from which traffic evidence is collected for a station. Its shape and size are configured for the station's licence area and determine which roads and incidents can be considered. See [per-station configuration](../reference/per-station-configuration.md).

**Delivery style**

A delivery style is a versioned setting for how a bulletin is voiced, including tone, pace, and permitted vocal direction. It changes delivery rather than the traffic facts in the script. See [per-station configuration](../reference/per-station-configuration.md).

**Delivery tone**

A delivery tone is a per-line direction an operator places on a script in the review editor (for example `[excitedly]` or `[slow]`) to steer how the voice reads that line. Tones travel with the words when the read is re-voiced; if a line cannot fit with its tone, the tone is dropped so the words always air. See the [operator guide](operator-guide.md).

**Fallback**

Fallback is the safe-audio path used when a fresh bulletin cannot reach air safely or in time. The platform first uses the most recent usable same-day bulletin for the station, then configured standby audio; if neither exists, the slot needs intervention. See the [operator guide](operator-guide.md).

**Guidance**

Guidance is a standing editorial instruction for a station or the platform. It can describe preferred wording, ordering, or local context, but it cannot add unsupported facts. See [per-station configuration](../reference/per-station-configuration.md).

**Future lesson**

A future lesson is a station-scoped writer instruction learned from published operator edits: when an edited, re-voiced script is delivered to playout, the platform generalises the change into a lesson candidate, and a candidate becomes active after three independently published edits agree. It is separate from rejection audit notes, expires after 90 days, and can be promoted to standing guidance or dismissed by an administrator.

**Incident**

An incident is a traffic event supplied by HERE, such as a crash, closure, or hazard. Its location, severity, direction, and other available details appear in the source panel so an operator can compare them with the script. See the [operator guide](operator-guide.md).

**Jam factor**

Jam factor is the traffic feed's congestion score, 0 clear to 10 blocked. The platform uses it with other traffic evidence when selecting and describing roads, and it records the value used for a bulletin. See [per-station configuration](../reference/per-station-configuration.md).

**Licence area**

A licence area is the geographic service area associated with a station. It provides the broadcast context for the station's coverage area, schedule, and local editorial choices. See [per-station configuration](../reference/per-station-configuration.md).

**On air**

On air is the console view for current and recent bulletins. It is the place to open a bulletin for review, follow its decision, and check the information needed before it reaches listeners. See the [operator guide](operator-guide.md).

**Park and resume**

Park and resume are the environment availability and cost-control operations. Park stops an environment's application and reduces its running resources, while resume restores the service and checks its health. See the [operations runbook](runbook.md).

**Playout**

Playout is the downstream broadcast system that uses delivered audio at the scheduled slot. In this platform, an authorised person publishes an approved bulletin before its recording is delivered to the configured Zetta playout destination. See the [operator guide](operator-guide.md).

**Priority roads**

Priority roads are routes that matter particularly to listeners in a station's licence area. The setting can increase the chance that matching incidents or clear-road updates are considered, but it cannot guarantee that a route is mentioned when more significant information competes for time. See [per-station configuration](../reference/per-station-configuration.md).

**Pronunciation dictionary**

A pronunciation dictionary is a versioned set of rules for how road and place names are spoken. The selected rules are applied when audio is synthesised, and the bulletin keeps the version used for its recording. See [per-station configuration](../reference/per-station-configuration.md).

**Regeneration**

Regeneration is a fresh bulletin attempt after an operator requests changes or an earlier attempt fails. It creates a new script and recording while retaining the earlier decision history, and it can use the operator's feedback. See the [operator guide](operator-guide.md).

**Rundown**

A rundown is an ARN broadcast term for the ordered content planned for a read. In this platform, the rundown shows the traffic items and optional content selected for a bulletin so the operator can see what the script is expected to cover. See the [operator guide](operator-guide.md).

**Scheduler**

The scheduler watches each station's schedule and starts bulletin generation when a slot is due. It resolves the current station settings at that point, so a saved configuration normally affects a later bulletin rather than one already being reviewed. See [per-station configuration](../reference/per-station-configuration.md).

**Slot**

A slot is the scheduled place in a station's clock where a bulletin is expected, together with the target duration it must fit. The platform sizes the script and recording to the slot and uses fallback when fresh content cannot safely cover it. See the [operator guide](operator-guide.md).

**Source check**

A deterministic content gate that checks whether factual claims in a script are supported by the traffic and other source information collected for that bulletin. A gate either passes the script or sends the slot to the fallback path; it does not initiate an automated correction retry.

**Source panel**

The source panel is the console area that shows the traffic, weather, and other source information used for a bulletin. Compare each important script claim with the relevant source row before approval. See the [operator guide](operator-guide.md).

**Standby audio**

Standby audio is a prerecorded, approved recording used when a fresh bulletin cannot safely reach air and no usable same-day bulletin is available. It has no newly generated script to review, because the recording itself is the fallback content. See the [operations runbook](runbook.md).

**Standby statement**

A standby statement is a short, station-approved line the platform can add inside a bulletin when the traffic, weather, fuel and minor-traffic content still leaves room in the slot. A statement can carry an availability window — a date range, days of the week, and a time of day, evaluated in the station's local time — and a statement outside its window never airs. See [per-station configuration](../reference/per-station-configuration.md).

**Station**

A station is the configured radio service whose schedule, licence area, voice, editorial settings, and playout destination shape each bulletin. Settings are resolved for the station's later scheduled slots, rather than changing a bulletin that is already in progress. See [per-station configuration](../reference/per-station-configuration.md).

**Voice profile**

A voice profile is a named, versioned configuration for the ElevenLabs voice used by a station. It records the voice and delivery settings needed for synthesis, while each bulletin keeps the resolved profile used for its recording. See [per-station configuration](../reference/per-station-configuration.md).

**Writer prompt**

The station's card-grid playbook of writer instructions on the **Writer prompt**
tab. It governs wording and presentation for that station over the ARN house
style, without changing source facts. Cards can be previewed before saving, and
a saved version applies to later conversational-pipeline bulletins. **Station
lessons** are also read by the conversational writer as standing preferences.
Numeric editorial limits belong on **Bulletin rules → Limits**, not in the
writer prompt.
