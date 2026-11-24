---
title: "Station Settings"
description: "The settings an operator or administrator can use to control a station's voice, delivery, pronunciation, content, roads, editorial rules, schedule and licence area"
tags:
  - "configuration"
  - "schedules"
  - "delivery-style"
  - "pronunciation"
  - "guidance"
  - "safety"
  - "content-sources"
  - "per-station"
purpose: "reference"
status: "current"
last_verified: 2026-09-02
review_after: 2026-11-27
volatility: "medium"
source_of_truth: "Console station, voice, delivery, pronunciation and bulletin-rule settings"
verified_by:
  - "Console labels and station setting behaviour reviewed"
  - "Version history and next-bulletin behaviour reviewed"
  - "Operational examples checked against the station settings screens"
aliases:
  - "station settings"
  - "station configuration"
  - "voice, delivery and pronunciation settings"
  - "bulletin settings"
common_queries:
  - "What settings can be changed for a station?"
  - "Which station setting changes the next bulletin?"
  - "How do voice, delivery and pronunciation settings work?"
  - "How does a station choose its content and priority roads?"
  - "How can bulletin wording change without a code change?"
see_also:
  - "docs/canonical/architecture.md"
  - "docs/reference/prompt-iteration-guide.md"
  - "docs/operations/operator-guide.md"
---

# Station Settings

![Station settings](diagram:per-station-configuration.png)

*The station settings view links a station to the voice, delivery, pronunciation and editorial choices used for its bulletins.*

The console lets an operator or administrator set the choices that shape what a station's
listeners hear. The settings cover the voice, the way the read is delivered,
pronunciation, content, priority roads, editorial guidance, banned words,
schedule and licence area.

## How versions work

Settings are versioned. Saving an edit creates a new version instead of
overwriting the version that was previously in force. A bulletin records the
exact version of each setting it used. An audit can therefore reconstruct the
choices that were active when the audio was made, even after a newer version is
saved.

A setting saved after a bulletin has entered review does not rewrite that
bulletin. The saved version is resolved for a later bulletin. To apply a
change to a bulletin already in the queue, use the review workspace to
regenerate it after checking the effect of the change.

## The voice

| Setting | What it does | Set it in the console | When it takes effect | If it is not set |
|---|---|---|---|---|
| Voice | Chooses the ElevenLabs voice used for the spoken bulletin. Voice settings control the voice's character and delivery pace. | Open **Voices** to preview and save a voice version, then open **Stations** to assign it. | The next bulletin generated after the assignment is saved. Existing audio is unchanged. | The station uses its configured default voice. If no usable voice is available, the bulletin cannot be produced safely and needs support. |
| Voice rotation | Selects one voice for every bulletin, or rotates through an ordered set of voices. Time windows can select a voice for a station-local period. | Set the rotation on the station in **Stations**. Preview each voice in **Voices** first. | The next bulletin generated after the rotation is saved. The rotation position is retained between bulletins. | The assigned voice is used for every bulletin. |

The primary assigned voice remains the fallback when a voice in a rotation is
unavailable. A voice edit creates a new version, so a later change does not
alter recordings that already exist.

## How the bulletin is read

| Setting | What it does | Set it in the console | When it takes effect | If it is not set |
|---|---|---|---|---|
| Delivery style | Describes the intended tone and energy of the read. It can add supported delivery cues without changing the written words. | Open **Delivery**, preview and save a style version, then assign it to the station in **Stations**. | The next bulletin generated with the new assignment. A bulletin already generated keeps its resolved style. | The bulletin uses the neutral read. |
| Delivery pace | Adjusts how much spoken content can fit the slot while retaining an intelligible read. The boost is capped at 60 words per minute: it is realised by targeting more words for the slot and compressing the read to fit, never by making the voice speak faster. | Edit the pace when saving a voice version in **Voices**. | The next bulletin generated with that voice version. Existing audio is unchanged. | The voice's normal pace and the slot's standard duration target are used. |

If a delivery style is unavailable or cannot be applied safely, the platform
keeps the plain written script and uses a neutral read. Delivery controls do
not change the words in a bulletin.

Delivery pace is a boost in words per minute, capped at 60. The platform
realises the boost by aiming the script at more words for the slot and
compressing the finished recording to fit — the voice is never told to speak
faster, and the words an operator reviews are the words that air. A read is not
compressed past 1.60 times its natural rate; the voice studio shows the
effective reading pace for the requested boost and warns when the boost tops
out at that ceiling, so a pace the pipeline cannot deliver is visible before
you save. After a pace change, listen to the next bulletin: the compressed read
must stay intelligible on the station's voice.

![The delivery style gallery](screenshot:20-delivery.desktop.png)

*Delivery (`/delivery`): preview and save the styles that shape how a bulletin is read.*

## Station bookends

| Setting | What it does | Set it in the console | When it takes effect | If it is not set |
|---|---|---|---|---|
| Bookend | A station's fixed opening line, closing line, or both, wrapped around every bulletin for that station. One library entry is a pair: the intro and outro are authored and assigned together. | Operators and administrators both author and version the pairs in **Bookends** — create, edit, duplicate, preview, archive and restore. Only deleting a pair outright is administrator-only; archive is the reversible way to retire one. Then open **Stations**, select the station, and choose a pair for its schedule. | The next bulletin generated after the assignment is saved. The bulletin records the bookend version it used. | The bulletin opens on its first traffic sentence and ends on its last line of content. |

A pair is written once in the shared **Bookends** library, so stations on a
brand share the same wording without each retyping it. A template can carry
`{token}` placeholders: `{city}`, `{market}` and `{station}` are filled
automatically from the station's schedule, and the station can override any of
them — or supply its own, such as a sponsor name — when the pair is assigned.
For an administrator, the station studio shows the fully resolved sentences
before you save. An operator sees the same assignment controls and a warning
naming any token that has no value, but not the resolved lines — resolving a
template is an administrator call. Either way an assignment whose tokens cannot
all be filled is rejected, so a placeholder never reaches the air as a raw word.

The pair is read as part of the same recording as the traffic — the intro
first, the outro last, with no audible seam — so its words are reserved from
the slot before the body is written. If a pair is too long to share the slot
with a minimum viable traffic read, the platform drops the bookend for that
bulletin and airs the traffic on its own, and the job log records why. On a
very short slot a station may therefore hear a bulletin without its usual intro
or outro; the traffic always wins.

![The bookends library](screenshot:26-bookends.desktop.png)

*Bookends: the shared library of intro and outro pairs, with token chips and the stations using each pair.*

![The bookend studio](screenshot:27-bookend-studio.desktop.png)

*The bookend studio: name the pair, write the intro and outro, and watch the token count as you type.*

## Pronunciation

| Setting | What it does | Set it in the console | When it takes effect | If it is not set |
|---|---|---|---|---|
| Pronunciation dictionary | Stores respellings and pronunciation rules for local suburbs, roads, names and other terms. | Open **Pronunciation**, preview a rule, save a dictionary version, then assign it to the station in **Stations**. | The next bulletin that resolves the saved dictionary. Existing recordings keep the dictionary version they used. | The platform-wide dictionary applies on its own, if one is set. Otherwise the voice provider's standard pronunciation is used. |
| Platform-wide dictionary | One dictionary that applies to every station, underneath whatever that station carries. Use it for names that read the same in every market, such as national motorway brands. | Open **Pronunciation**, open a dictionary and set **Applies to** to "Every station, platform-wide". Only one dictionary can be platform-wide, and it cannot be assigned to a single station. | The next bulletin for every station. | Only the dictionaries stations choose for themselves apply. |

A station's own rule wins any word the platform-wide dictionary also covers, and every other
platform-wide rule still applies. So a station can correct one name without losing the shared
ones, and a name that reads the same everywhere only has to be fixed once.

Keep one active rule for a word and rule type. If a pronunciation is wrong,
save a corrected rule as a new dictionary version and listen to the next
affected bulletin. The written script remains the canonical text shown to the
operator, while the saved rule controls how the voice says the term.

A correction made from a bulletin in review always lands on that station's own dictionary, never
on the platform-wide one, so fixing a word for one market cannot change what the others say.
Changing the platform-wide dictionary is an administrator action in **Pronunciation**.

When auditioning a station dictionary, leave **Include the platform-wide layer** on. With it off
you hear that dictionary alone, which is not what airs.

![The pronunciation studio](screenshot:22-pronunciation.desktop.png)

*Pronunciation (`/pronunciation`): add, preview and save the rules used for local terms.*

## What content is included

| Setting | What it does | Set it in the console | When it takes effect | If it is not set |
|---|---|---|---|---|
| Content selection | Chooses which available sources can contribute to a bulletin, including traffic, weather and fuel. | Open **Stations**, select the station, and change its content options. | The next scheduled bulletin after the options are saved. A bulletin already in review keeps its original selection. | Traffic is used as the default source. At least one source must remain enabled. |

The platform uses the enabled sources that are available for the slot. If an
optional source is unavailable, generation can continue with the other enabled
sources. If the remaining information cannot support a safe bulletin, the
platform uses the normal fallback outcome rather than inventing a claim.

## Standby statements

| Setting | What it does | Set it in the console | When it takes effect | If it is not set |
|---|---|---|---|---|
| Standby statements | Short, station-approved lines the platform can add inside a bulletin when the traffic, weather, fuel and minor-traffic content still leaves room in the slot. Each statement can carry an availability window. | Open **Bulletin rules**, select the station, and open its standby statements. Add and enable lines, and set a window on any of them. | The next bulletin that needs a content gap. A statement outside its window never airs. | The bulletin ends when the available content ends; no extra line is added. |

A standby statement is a short line such as "Please stay alert out there and
stick to the speed limit." The platform sizes it from its word count and the
voice's pace and places it in the read when the day's content leaves room.

An availability window restricts when a statement can air. Each part is
optional, and a window with everything blank means the statement is always
eligible:

- **Eligible from** and **Until** — an inclusive date range.
- **Days of week** — Monday to Sunday; no days selected means every day.
- **Start time** and **End time** — a time-of-day window; an end time earlier
  than the start time runs past midnight.

The whole window is evaluated in the station's local time. The rule is strict:
a statement outside its window never airs and nothing else is substituted in
its place, so a station whose windows have lapsed simply airs without padding.
The statement list warns when no enabled statement for the station is currently
eligible, so the gap is visible before it reaches the air.

A standby statement is not standby audio: standby audio is the prerecorded
fallback used when a fresh bulletin cannot be produced at all, while a standby
statement rides inside a new bulletin that is being reviewed and approved like
any other.

## Slot-fit envelope

These settings control how the finished read is brought to its scheduled slot
when spoken content is short or long. An administrator opens **Bulletin rules →
Limits** for the station; the Limits tab is admin-only and contains live numeric
editorial controls, including slot-fit bounds, the corridor cap, the cleared-item
continuity cap, ranking thresholds, and enabled traffic sources.

The Limits tab also retains three older continuity caps, **New**, **Still going**
and **Updated**, for compatibility with existing records. They are read-only
because the live conversational pipeline does not read them. Configure bulletin
wording in the station's **Writer prompt** Playbook studio instead.

Each field is optional: an empty box inherits that field from the next wider
scope, resolved **Station > Market > Global** independently. The greyed number
shows the value the current environment will use.

| Setting | What it does | Bounds | When it takes effect | If it is not set |
|---|---|---|---|---|
| Stretch a short read | Stretches a short spoken read, preserving pitch, to close an underrun. `1.0` disables slow-fill. | `1.0`–`1.25` | The next bulletin generated after save. | The environment's deployed speech-duration value is inherited. |
| Compress a long read | Compresses a read that runs long so it can fit the slot. | `1.0`–`1.30` | The next bulletin generated after save. | The environment's deployed speech-duration value is inherited. |

At **Stretch a short read = 1.0**, a read that cannot reach its slot through
content growth and other allowed fitting steps is failed to the standby cart
rather than stretched. Silence pad and safe overrun trim are not settable.

## Which roads matter most

| Setting | What it does | Set it in the console | When it takes effect | If it is not set |
|---|---|---|---|---|
| Priority roads | Gives important roads extra weight when the platform chooses incidents and clear roads for the read. | Open **Bulletin rules**, choose the relevant station or licence area, then open **Priority roads**. | The next bulletin generated after the road list or priority level is saved. Use **Adjust** and regenerate to apply it intentionally to a bulletin already in review. | The traffic feed's normal ranking is used. |

Use a road name that matches how the traffic feed names it — the road names shown
in a bulletin's rundown are the reliable guide. A name the feed never uses simply
has no effect. Set priority for roads that should be considered first, rather than
adding every road in the licence area. The station's coverage area still limits
which live conditions can be selected.

Each road on the list does one or both of two jobs, chosen on its own row:

| Job | What it does | Extra setting |
|---|---|---|
| **Incidents** | This road's incidents get picked over equally serious ones elsewhere. | None. |
| **Good runs** | The road can earn a mention for running well, but only when the traffic feed actually reports it as flowing. | A strength: **Normal** airs when the read is quiet, **High** beats minor incidents, **Critical** airs unless serious incidents stack up. |

Select neither job and the row is marked **Not in use** — it is on the list but
changes nothing. Either job competes on score, so neither is a guarantee: a road
can be selected for a job and still not reach a particular bulletin. A road is
never described as running well just because nothing was reported on it — if the
traffic feed does not report it as flowing, it is not mentioned that way.

![The stations list](screenshot:10-stations.desktop.png)

*Stations (`/stations`): assign a station's voice, delivery, pronunciation and content choices.*

## Writer prompt, limits and banned words

| Setting | What it does | Set it in the console | When it takes effect | If it is not set |
|---|---|---|---|---|
| Writer prompt | Controls the station's wording through Presenter, Voice and Length instruction cards, over the ARN house style. Active station lessons may also influence the generated wording. | Open **Bulletin rules**, select the station, then open **Writer prompt** and preview a card before saving. | The next bulletin generated after the station's Writer prompt version is saved. | The ARN house style and other resolved defaults apply. |
| Editorial limits | Controls numeric caps, slot fit and enabled traffic sources; it does not define wording. | An administrator opens **Bulletin rules → Limits**. | The next bulletin generated after the settings are saved. | The resolved environment defaults apply. |
| Banned words | Prevents a term from appearing in a script, with an exception only where an administrator has deliberately allowed one. | Open **Bulletin rules**, select the station, then open **Banned words**. | The next generation in that scope. | The baseline content-safety check still applies. |

Use the Writer prompt for a preferred station voice or phrasing, Limits for
numeric editorial controls, and banned words for terms that must not be read.

![Bulletin rules](screenshot:13-guidance.desktop.png)

*Bulletin rules (`/guidance`): set the selected station's guidance, Writer prompt and banned-word controls.*

## The schedule and coverage layers

| Setting | What it does | Set it in the console | When it takes effect | If it is not set |
|---|---|---|---|---|
| Schedule | Sets the days and times at which the station's bulletin slots become due. | Open **Stations**, select the station, and edit its schedule. | The next due slot after the saved schedule is active. A slot already being processed is not rescheduled. | A disabled or incomplete schedule produces no new bulletin until its schedule is corrected. |
| Traffic coverage | Defines the geography used to select live traffic conditions for the station. | Open **Stations**, select the station, choose **Traffic** in the Coverage step, and edit the map. | The next bulletin generated after the saved coverage is active. Existing evidence snapshots keep their original area. | No reliable local traffic evidence is available. The platform may use fallback audio rather than create a bulletin with unsupported locations. |
| Weather coverage | Optional circle used for the HERE weather location. | Choose **Weather** in the same Coverage step and edit the circle. | The next bulletin generated after the saved override is active. | Weather inherits the traffic coverage centre. |
| Fuel coverage | Optional circle used for the HERE fuel station search. | Choose **Fuel** in the same Coverage step and edit the circle. | The next bulletin generated after the saved override is active. | Fuel inherits the traffic coverage, subject to the platform fuel-radius cap. |

Traffic, weather and fuel coverage are separate layers in one map editor. Traffic retains
the full licence-area shape; weather and fuel overrides are circles because their HERE
providers query a single location. Removing a source-specific override returns that source
to the traffic coverage centre. This lets a regional station keep broad traffic coverage
without voicing the cheapest fuel station from an unrelated remote suburb.

![The station studio](screenshot:16-station-studio.desktop.png)

*Station studio (`/stations`): edit the schedule, licence area and source-specific coverage layers.*

## Checking a change

After saving a setting, record the version shown in the console and wait for
the next bulletin for the affected station. In the review workspace, confirm
the resolved settings, script and source panel, then listen to the complete
audio. The bulletin history records the versions used, so a later comparison
can distinguish a setting change from a change in live traffic conditions.

For wording changes, use [Changing what bulletins say](prompt-iteration-guide.md).
For the review and approval sequence, use [the operator guide](../operations/operator-guide.md).
