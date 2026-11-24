---
title: "Changing What Bulletins Say"
description: "How to change bulletin wording through Bulletin rules, preview a station writer-prompt edit, choose the right scope and confirm the result on air"
tags: [reference, quality, guidance, writer-prompt, handover]
purpose: "reference"
status: "current"
last_verified: 2026-09-02
review_after: 2026-11-28
volatility: "medium"
source_of_truth: "Console guidance, station writer-prompt and bulletin review controls"
verified_by:
  - "Current Bulletin rules station picker and Writer prompt workflow reviewed"
  - "Preview and version-history behaviour reviewed"
  - "Station-specific scope and next-bulletin behaviour reviewed"
aliases:
  - "changing bulletin wording"
  - "bulletin style guide"
  - "quality changes"
common_queries:
  - "How do I change how bulletins are written?"
  - "Do I need a developer to change the tone?"
  - "How do I preview a wording change before it airs?"
  - "What is the difference between guidance and writer-prompt sections?"
see_also:
  - "docs/operations/operator-guide.md"
  - "docs/reference/per-station-configuration.md"
---

# Changing What Bulletins Say

The console is the normal place to change bulletin wording. Use **Editorial
guidance** for a standing instruction about local context or editorial policy,
and **Writer prompt** for a station's standing instructions about how the final
bulletin is written. Both are reached from **Bulletin rules**, whose header
selects one concrete station at a time.

A saved change creates a new version and is used for later bulletins. It does
not rewrite a bulletin already in review or change audio that has already been
delivered.

## Choose the smallest change

| Desired result | Use in the console | Scope |
|---|---|---|
| Change how one station describes a local road, suburb or recurring situation | **Bulletin rules → Editorial guidance** | Select that station |
| Change how one station's bulletin is structured or voiced | **Bulletin rules → Writer prompt** | Select that station |
| Change numeric caps, slot-fit values or enabled traffic sources | **Bulletin rules → Limits** | Select that station; administrator |
| Keep a term out of scripts | **Bulletin rules → Banned words** | Select that station; administrator |
| Change which roads receive extra attention | **Bulletin rules → Priority roads** | Select the station context |

Use Editorial guidance for a local editorial instruction. Use Writer prompt for
the presenter's standing voice, sentence shape and length behaviour. Use Limits,
Banned words and Priority roads for their respective deterministic controls;
do not try to express those settings as prose in the Writer prompt.

Every Bulletin rules tab uses the station selected in the page header. Inherited
global guidance may still be visible while looking at a station, but there is
no separate "All stations" Writer prompt state.

## Change editorial guidance

1. Open **Bulletin rules** and select the station in the page header.
2. Open **Editorial guidance**.
3. Add or edit the instruction.
4. Write one clear instruction for one behaviour. State the preferred wording,
   emphasis or local context in plain language.
5. Save the guidance and record the version shown by the console.

Guidance affects the next bulletin generated in the selected scope. It does
not change a waiting bulletin, because that bulletin retains the setting
versions resolved when it was created. To apply a saved change deliberately to
a bulletin in review, use **Revise** and regenerate it, then review the new
result.
The resolved directives are carried into the conversational workflow as plain
strings. The two writer desks and the composer receive them in a `STATION
GUIDANCE` block; the editor does not, because guidance directs selection and
realisation rather than editorial review of the finished read.

## Composer prompt layout

The shipped `compose2.md` prompt keeps its cache-friendly role, fidelity,
grounding, selection, no-advice, no-bookend and length rules in addressable
playbook blocks. Its final block is **What a good read sounds like**. At
runtime the `{{ATN_EXEMPLARS}}` token in that block is replaced with three
deterministically rotated ATN reads: nearest to the requested word target,
from the station's market class and daypart where available, with at least
two board densities. The examples are style reference only; their facts,
names, figures, sponsors and presenters are never available material for the
new bulletin.

When a same-day read exists, it follows the station-policy blocks as
**PREVIOUS READ (aired earlier today)**. That block contains the prior read
verbatim and tells the composer to continue unchanged facts without reusing
its opening, speed/state idioms or sentence shapes. The dynamic word-count
instruction remains the final line of the rendered request and is the only
composer word-count statement.


### Good guidance

| Better instruction | Why it helps |
|---|---|
| "Name the road and direction before describing the delay." | The operator can check the result in the script. |
| "Use the station's local suburb names when they are supported by the source panel." | The scope and evidence requirement are explicit. |
| "Keep the close concise and avoid repeating a road already named in the lead." | The intended change is observable in one bulletin. |

Avoid combining unrelated rules in one entry. If a guidance entry conflicts
with the standard editorial rules, narrow the instruction or ask the platform
support team to review the intended policy.

## Change the Writer prompt

1. Open **Bulletin rules** and select the station in the page header.
2. Open **Writer prompt**.
3. Select the agent role whose instructions you want to inspect or edit. The
   selector includes the writer, incident desk, flow desk, presenter and editor
   roles used by the platform.
4. Edit the relevant **Presenter**, **Voice** or **Length** card. Protected
   contract and selection sections remain visible but are not station-editable.
5. Select **Preview** to audition the proposed instructions against recent
   station data.
6. In the preview result, open **Rendered prompt** to inspect the exact resolved
   system prompt and, when that role has a rendered request, its user prompt.
   The disclosure is collapsed by default so the normal preview remains compact.
7. If the preview is suitable, save. Saving creates a new version; if it is
   not suitable, close the preview and revise the cards.

The preview is a dry run. It does not create a live bulletin, enqueue work,
or deliver audio. The saved Writer prompt applies to a later bulletin for the
selected station. Version history remains available from the Writer prompt
toolbar, and restoring an old version creates a new current version so the
change remains auditable.

The saved Writer prompt is versioned for the selected station. **Bulletin rules → Writer prompt** is the authoring and version-history surface.
## Confirm that the change worked

Use one bulletin from the affected station and compare it with a bulletin made
before the change:

1. Open **On air** or **History** and open the bulletin record.
2. Read the complete script. Check that the intended wording appears and that
   the change has not introduced repetition, unsupported claims or a missing
   road.
3. Use the source panel to confirm that factual claims still match live source
   rows.
4. Listen to the complete audio. Check the voice, pronunciation, pace and
   slot length separately from the written wording.
5. Open **Config & audit** and record the setting versions used by the
   bulletin.
6. If the result is not wanted, restore the previous Writer prompt version and
   check the next bulletin again.

A wording change is successful only when the intended style is present and the
facts, coverage, pronunciation and timing remain acceptable. If a change
improves tone but causes a source mismatch, restore it and treat the factual
problem as the priority.

## When to ask for support

If the desired behaviour cannot be expressed through **Editorial guidance** or
the editable **Writer prompt** cards, send the platform support team:

- the affected station;
- the exact wording or behaviour that should change;
- one example of the current wording and one example of the intended wording;
- the bulletin ID, slot and local time used for the comparison; and
- the setting version shown in **Config & audit**.

Do not edit platform prompt files or attempt to change an internal service. A
change that requires new platform behaviour needs a documented support decision
and the normal release checks before it can affect a station.
