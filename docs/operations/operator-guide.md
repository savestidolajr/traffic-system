---
title: "Operator Guide"
description: "Practical guide for ARN content operators who review, approve and manage daily bulletins"
tags:
    - "operations"
    - "operators"
    - "handover"
    - "bulletins"
    - "review"
aliases:
    - "operator guide"
    - "content-team guide"
    - "bulletin operator guide"
purpose: "runbook"
status: "current"
last_verified: 2026-09-01
review_after: 2026-11-27
volatility: "medium"
source_of_truth: "Console labels, bulletin review workflow and support procedures"
verified_by:
    - "Console labels and operator workflow reviewed"
    - "Review, fallback and escalation behaviour reviewed"
    - "Station settings and wording controls reviewed"
common_queries:
    - "How do I review and approve a bulletin?"
    - "What will listeners hear if I reject a bulletin?"
    - "How do I fix a suspect pronunciation?"
    - "Which console setting changes the next bulletin?"
    - "When should I escalate an issue?"
see_also:
    - "docs/operations/uat-plan.md"
    - "docs/operations/runbook.md"
    - "docs/operations/support-model.md"
    - "docs/reference/prompt-iteration-guide.md"
    - "docs/reference/per-station-configuration.md"
    - "docs/operations/first-day.md"
---

# Operator Guide

This guide shows ARN content operators how to take a bulletin from **Waiting
approval** to a safe decision and, when authorised, to playout. Open the
console, check the source panel and complete audio checks before approving.
Publishing is a separate, deliberate step you take after approving. For a
platform incident, use [the runbook](runbook.md). For ownership and contacts,
use [the support model](support-model.md).

## 1. Work the bulletin queue

At the start of a shift, work the waiting queue in deadline order:

1. Open **On air** and choose the waiting bulletin with the earliest deadline.
2. Read the complete script, check the source panel and listen to the complete
   audio.
3. Use **Adjust** when a road or incident needs to be included or removed,
   then regenerate and review the new result.
4. Choose **Approve**, **Revise**, a fallback decision or **Reject bulletin**
   according to the decision table in section 4.
5. Select **Publish** on the approved bulletin, then check the bulletin log for
   the write confirmation.

After **Approve**, the review workspace stays on that same job and refreshes to
show **Approved**. It does not advance to another bulletin. After **Publish**,
the console advances to the next bulletin waiting for approval when one exists;
when none remain, it returns to the **On air** jobs landing page.

Approving a bulletin does not deliver it to playout. The approved recording
reaches playout only after the separate publish step. The listener hears the
published recording at the scheduled slot, not an unapproved draft.

If a new bulletin cannot safely fill its slot, the platform uses:

1. the most recent usable bulletin from the same day for that station; or
2. the station's configured standby audio when no usable same-day recording is
   available.

If neither option is available, the slot is uncovered and needs immediate
escalation. A standby statement is different from standby audio: it is a short
station-approved line that can be added inside a new bulletin when the real
traffic, weather, fuel and minor-traffic content does not fill the slot. The
station's content priority order decides whether a standby statement can be
used, and a statement can be restricted to an availability window so it only
airs at the times it is meant for.

A station's bookends — its fixed opening and closing lines — are read as part
of the same recording as the traffic. If a pair is too long to share a very
short slot with the traffic read, the platform drops the bookend for that
bulletin and the job log records why, so a bulletin without its usual intro or
outro is expected in that case rather than an error.

## 2. Start a shift

### Sign in and choose the environment

Use the console URL for the environment assigned to the shift and sign in with
the ARN account. For Test shifts, use
[https://ain-test.arn.com.au](https://ain-test.arn.com.au); for authorised
production operations, use [https://ain.arn.com.au](https://ain.arn.com.au).
See [First-Time Sign-In](first-time-sign-in.md) for the full address set.

The main console areas are:

- **On air** (`/jobs`): current queue and review work.
- **History** (`/history`): completed and earlier bulletin records.
- **Bulletin rules** (`/guidance`): wording and coverage controls.
- **Stations** (`/stations`): the configuration of each station assigned to the
  account — schedule times, voice, delivery style, pronunciation dictionary,
  coverage and content. An operator can open a station, edit it and use
  **Trigger now** to generate an ad-hoc bulletin for it. A station outside the
  assigned markets is shown greyed under **Outside your markets** with its
  trigger disabled. Adding, duplicating and deleting a station remain with
  administrators, so those controls are not shown.

Administrator accounts also see **Voices** (`/voices`), **Delivery**
(`/delivery`), **Pronunciation Studio** (`/pronunciation`), **Operators**
(`/operators`), **Data transfer** (`/config-transfer`) and **Activity**
(`/activity`). In **Pronunciation Studio**, listen to the spoken preview before
saving a dictionary rule. Use the labels in the console rather than relying
on a saved browser route.

### Check which environment is armed for playout

`GET /api/schedules` is the authority on which environment arms which station; check it live (it needs the **Operator** role) before waiting on a station. As verified 2026-09-03, **no station is armed for playout in any environment**: both test stations (`cairns-4ca` and `sydney`) are enabled for scheduled generation with playout disabled, so test generates bulletins and delivers none; both production schedules are disabled, so production generates and airs nothing; and development can generate and review bulletins but can never deliver to playout.

Each station carries its own **Send to playout** control on the **Stations**
page (cards and table, plus the footer of the station editor). Only ONE
environment may arm a given station: every armed environment writes the
identical delivered file name to the same playout share, so two armed
environments would race and there is no on-air receipt to identify a winner.

- **Send to playout ON** — approving and publishing a bulletin for that station
  sends the audio to playout from this environment.
- **Send to playout OFF** — the station still generates, is still reviewed and
  still publishes normally, but nothing reaches playout. The bulletin's own log
  records "Send to playout is off for this station".

To move a station between environments, switch **Send to playout** OFF in the
environment that currently airs it, then ON in the other. It takes effect
immediately and needs no deployment. Pausing a station is a different control:
**Paused** stops it generating at all.

On **development** the control is present but inert: that environment has never
been able to send to playout, so every station reads "This environment never
sends to playout" whatever its own switch says.

### Find the next bulletin

1. Open **On air**.
2. Select a bulletin in **Waiting approval**. Use the countdown in the
   decision dock and work the earliest deadline first.
3. If several bulletins are waiting, return to **On air** and choose the one with the earliest deadline.
4. If the queue is empty, open **History** to confirm whether the station has
   already completed a bulletin. An empty queue does not by itself prove a
   generation failure.

![The On air queue](screenshot:01-onair.desktop.png)

_On air (`/jobs`): the shift queue, grouped by state, with waiting approvals at the top._

## 3. Review a bulletin

![A bulletin waiting for approval](screenshot:02-review-waiting.desktop.png)

_The review workspace (`/jobs/{id}`) for a bulletin in Waiting approval: script, player, source panel and fixed decision dock. The chips at the start of some lines are delivery tones, covered later in this section._

### Check the Review tab

The review workspace has **Review**, **Adjust** and **Config & audit** tabs.
Start on **Review**:

1. Read the script from the first sentence to the close. The script is shown
   as sentence-based teleprompter lines.
2. Check the vitals for the air slot, duration, coverage and voice. A script
   that is too short, too long or missing expected coverage needs a revise or
   regeneration decision, not a blind approval.
3. Listen to the complete audio. Use **Play**, **Pause**, **Back 5 seconds**,
   **Forward 5 seconds**, the seek slider, playback speed and **Download audio**
   as needed. Press space to play or pause. Left and right arrows move five
   seconds when focus is on the player.
4. Follow the karaoke highlight. It tracks the spoken word and sentence while
   the audio plays. Clicking a word in the script or a suspect-word chip seeks
   to that point in the audio.

### Verify the source panel

The source panel is the source check for what the script says:

- **Cited** shows source rows used by the bulletin.
- **Unused** shows selected source information not cited in the script.
- **Below cut** shows available information left out when the read was fitted
  to the slot.

Use the panel per sentence, not just as a traffic summary:

1. Hover a sentence to highlight its matched source row. Pin a row when the
   highlight needs to remain while scrolling. Karaoke also moves the focus as
   the recording plays.
2. Confirm that each factual claim has a matching source row. A red
   contradiction marker means the script and source data disagree.
3. If a claim is wrong or unsupported, do not approve. Open **Revise**, record
   the issue and regenerate, or use the destructive fallback decision when the
   bulletin must not air.
4. An **Unused** or **Below cut** row is not automatically an error. If a
   missed road matters, move it into the rundown before regenerating.

If the script still has a contradiction, the approval confirmation warns that
approving sends it to air as-is. Use that option only after checking the source
and making a deliberate editorial decision.

### Use Adjust to check roads and maps

1. Open **Adjust**.
2. In the rundown, read the road, direction, **Editorial rank**, **Magnitude
   tier**, lanes, authority note, road class, length and reported time where
   present. **Editorial rank** orders the rundown with blockage first, then the
   worst pace or delay. **Magnitude tier** sets emphasis and is pace-blind.
   They are deliberately distinct and can legitimately diverge; do not use one
   as a substitute for the other.
3. Select **Mention this incident** or **Drop this incident** to curate a
   traffic item. Drag a route into or out of the bulletin when the action is
   available. These choices are queued for the next regeneration; they do not
   rewrite the current audio.
4. On each evidence row, select **Open traffic view in Google Maps** to
   cross-check the live view at the reported location.
5. Open the **Emergency services** panel when present. It is display-only:
   emergency records are shown for operator awareness and never enter the
   script. They cannot be added through traffic curation.
6. Use **Expand the coverage map** when the compact map is not enough. The HERE
   flow colouring and incident markers are a live visual overlay. Hovering an
   incident in the explorer's rail flies the map to it and opens its callout;
   click a row to pin it (persistent highlight and dossier). The source rows
   remain the fetched snapshot used for the script. Use both when checking
   a claim.
7. Open the map or a row dossier when the incident location is needed. A flow
   row without coordinates can still be source information, but it cannot
   provide a map pin.

![The Adjust tab](screenshot:03-review-adjust.desktop.png)

_Adjust: the rundown of roads and incidents behind the script, with the coverage map._

Use **Config & audit** to check the settings resolved for the bulletin and its
full status history. It is not a replacement for the source panel.

![The Config and audit tab](screenshot:04-review-audit.desktop.png)

_Config & audit: the settings used by the bulletin and its complete status history._

### Set a delivery tone on a line

On **Review**, select **Edit** to direct how a line is read. Place a delivery
tone at the point where the voice should change — pick one from the tone
palette (for example `[excitedly]`), or type a tag such as `[slow]` straight
into the line. Tags render inline as chips, and one **Re-voice** applies the
words and the tones together:

- The tones ride along as the tagged read. When tones are set, the station's
  delivery style is skipped for that read, so the voice is directed by the
  tones alone.
- A tone may cost time but never content. If the tagged line cannot fit the
  slot, the platform first flattens the pauses, then removes only the tones
  that slow the read, and only as a last step re-voices the words without any
  tones. The words you approved always air — possibly without the tone.
- Tones are available only on voices that act on audio tags. On a voice that
  does not, the tone controls are not shown, because the tags would be read
  aloud as words.

Tones appear in the script as inline chips at the start of the line they apply
to, as in the review workspace shown at the start of this section.

## 4. Choose the operator decision

All waiting bulletins use the fixed **Bulletin decision** dock. **Revise**
opens the review-change surface. **Approve** is the positive decision.

| Decision                         | What to do                                                                                                                                                                                                                                                                                                                                                                                                                         | What the listener hears                                                                                                                                                                                                            |
| -------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Approve                          | Resolve the source and audio checks, then select **Approve**. If a contradiction remains, read the confirmation before selecting **Approve anyway**.                                                                                                                                                                                                                                                                               | The approved audio is queued for publishing. No new audio is delivered merely by approving.                                                                                                                                        |
| Publish                          | On an **Approved** bulletin, select **Publish**, confirm **Publish bulletin?** and check the log for the write to the shared playout share.                                                                                                                                                                                                                                                                               | The Function App writes the approved recording to the shared playout share. Zetta ingest is evidenced only when the file disappears from that share; there is no on-air receipt.                                                                                                                               |
| Regenerate after a source change | Use **Adjust** to change included incidents, corridor order or flowing roads. Open **Revise**, confirm the queued source changes and select **Regenerate**.                                                                                                                                                                                                                                                                        | The current draft is replaced by a bulletin rebuilt from the selected source information. It returns to **Waiting approval** for a new review.                                                                                     |
| Edit script and revoice          | On **Review**, select **Edit**. Change the sentence-per-line text directly, or use **Ask for a change**, inspect the proposed diff and select **Apply**. Check the word and slot-length guidance, then select **Re-voice** and review the new audio.                                                                                                                                                                               | The new recording reads the final text shown in the editor. It is not published until it is approved and published. Once the edited bulletin is delivered to playout, the platform may generalise the wording change into a station lesson candidate, governed under **Bulletin rules → Future lessons**. |
| Change the voice                 | Open **Revise** and pick a profile under **Voice**. Select **Audition with this voice** to hear the current script and check the fit verdict. If that voice cannot fit the slot, choose **Regenerate instead** before applying; choose **Keep re-voice** only when preserving the exact words is more important than fitting the target. A voice-only change otherwise re-voices the same words; queued source changes regenerate. | The same reviewed script is read by the selected voice after a re-voice. **Regenerate instead** may rewrite the words to fit the slot. The change applies to this bulletin only — the station schedule keeps its configured voice. |
| Use fallback audio               | Open **Revise**, expand **This bulletin must not air**, optionally choose a reason and add audit notes. Select **Use fallback audio**.                                                                                                                                     | The platform uses the most recent usable same-day bulletin, otherwise standby audio. The reason and notes remain on this job; nothing from a rejection enters writer memory.                       |
| Reject bulletin                  | Use the same destructive area and select **Reject bulletin** only when fallback audio must not be used and a replacement can be generated in time. Pronunciation, source-selection and script issues expose their existing correction action; fix them there rather than teaching the writer.                                                                                                                                      | This bulletin is dropped. Nothing from it airs unless a new bulletin is generated and delivered in time.                                                                                                                           |

The **Revise** dialog applies changes already queued from **Adjust**, pronunciation
review or the voice picker. For wording, use **Edit**: change the script directly
or preview **Ask for a change**, then re-voice the final text shown there. A
failed bulletin may expose **Retry**. Repeated failures are an escalation, not
a reason to retry indefinitely.

![The Revise dialog](screenshot:19-revise-dialog.desktop.png)

While a re-voice or regeneration is being applied, the workspace shows
**Re-voicing** or **Regenerating** with nothing to press. Applying a revision
queues the updated bulletin for processing. It is not an operator rejection and
it does not teach station lessons; the page keeps working and shows the new
recording when it lands.

![A bulletin that used fallback audio](screenshot:09-fallback.desktop.png)

_Fallback Used: the reason is recorded so a later listener check can tell why the stored audio aired._

## 5. Resolve automated audio check warnings

The automated audio check line sits below the player. It compares the
transcript of the audio with the canonical script:

- **N words to verify by ear** means the transcript did not contain those
  written words. It is a review prompt, not proof that the voice was wrong.
  Names whose spelling and sound differ can be flagged even when they were
  spoken correctly.
- **N figures unconfirmed** is informational. The transcript could not confirm
  an aired figure, so listen and check the written claim.
- A green check and percentage means the transcript matches the script for the
  measured comparison. It does not replace the factual source check.

Flagged words are highlighted in the script itself: amber for a word still to
check, grey once someone has verified it and purple for a word rewritten by a
dictionary rule. The line under the player is a count only. For each
highlighted word:

1. Click the word to seek the audio to it. Rest the pointer on it to open
   **Pronunciation Studio**. Use its spoken preview to hear a proposed
   target before saving; the question mark beside a flagged word explains why
   it is marked.
2. If the pronunciation was acceptable, select **Sounded fine**. This records
   a verified-word result for the station, so the word stops counting on later
   bulletins. It does not alter the recording or teach the voice a new
   spelling. Select **Needs another listen** to reverse the result.
3. If the pronunciation was wrong, check the target under **Say it
   differently**. For a road name present in this bulletin's traffic data,
   hovering any word in that name selects the complete phrase (for example,
   **Gillies Highway**). Edit the target when the operator's local name is
   different, type a respelling, and select **Save**. This creates a dictionary
   rule for future bulletins. One rule is kept for each word or phrase, so
   saving again replaces the earlier rule. Listen to the next affected
   bulletin.

Keep these decisions separate. Do not add a dictionary rule merely to clear a
word that already sounded correct. A verified result is a reversible review
decision over the comparison, so the original flag remains discoverable.

## 6. Make an editorial change

Open **Bulletin rules** and select the station. Saved changes are versioned and
resolved for a later bulletin; they do not rewrite an existing bulletin.

| Control | Task | When it takes effect |
|---|---|---|
| **Writer prompt** | Edit the station's instruction cards in the title-only card grid (Presenter, Voice and Length). Open a card dialog to add, edit, preview and inspect version history. These per-station writer instructions take precedence over the ARN house style. | The next bulletin generated after the save. |
| **Limits** (admin-only) | Edit numeric editorial limits such as maximum corridors, tier and continuity caps, slot-fit bounds, and enabled traffic sources. These values control selection and fitting, not wording. | The next bulletin generated after the save. |
| **Verified words** | Review pronunciation results created by **Sounded fine**. | The review decision is used by later comparisons. |
| **Banned words** | Add a term that must not be used and any deliberate exception. Do not use this for a style preference. | The next generation in the selected scope. |
| **Priority roads** | Add or adjust roads and their priority level. | The next generation. For a current bulletin, use **Adjust** and then **Regenerate**. |
| **Standby statements** | Add, enable or remove a short station-approved line with an optional availability window. | The next bulletin that needs a content gap. |
| **Pronunciation Studio** | Add an alias or other dictionary rule, listen to the spoken preview, and save a dictionary version. | The next bulletin that resolves that dictionary. |
| **Delivery** / **Voices** | Edit presentation or voice settings and assign the saved version to the station. | The next bulletin resolved with the assignment. |

The conversational pipeline is the only script generator in every environment.
Do not edit platform files for a local wording issue; use the Writer prompt
cards and preview, or the Limits tab for numeric controls.

For a change that cannot be expressed by these controls, use [Changing what
bulletins say](../reference/prompt-iteration-guide.md) and escalate through [the
support model](support-model.md).


## 7. Read Teams escalation cards

Cards appear in the Microsoft Teams operations channel for the assigned
environment. They link to the affected bulletin when an action is available.
A channel can be hidden and cards may not mention individual members, so open
the channel when checking for a card. A missing toast is not proof that no card
was sent.

| Card                         | Meaning                                                                                            | Operator action                                                                                                                  |
| ---------------------------- | -------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| **Ready for review**         | A bulletin is waiting and shows **Review** plus the review deadline.                               | Open the bulletin and complete the source and audio checks.                                                                      |
| **Approval reminder**        | The waiting card has a reminder inside the notification window.                                    | Treat it as a deadline reminder and review or hand off immediately.                                                              |
| **Urgent approval**          | A red card shows the time by which approval is needed or the previous bulletin will air again.     | Open the console, finish the source and audio checks and approve when safe. Publishing still follows the normal publish step.    |
| **Failure covered**          | A failure was covered by a same-day bulletin and no urgent intervention is needed.                 | Open details when the audit trail is needed. No new approval is required for the fallback audio.                                 |
| **Failure without coverage** | A red intervention card says standby audio may air or no bulletin is queued.                       | Open the console, check the bulletin and fallback availability and follow the runbook. Escalate if the slot is not covered.      |
| **Missed deadline**          | The previous bulletin was aired again without review.                                              | Record the missed review and inspect the bulletin history. Improve shift coverage; do not treat the card as an approval request. |
| **Routine update**           | No card is sent for routine approved, published, rejected or content-limited fallback audit noise. | Use the console, bulletin history or Azure Monitor when an audit record is needed.                                               |

A critical alert can appear instead of a routine bulletin card. Treat it as a
platform incident and follow the first response in [the runbook](runbook.md).

## 8. Common operator troubleshooting

| What you see                                                                        | Likely cause                                                                                                                            | Action                                                                                                                                                                                                                                                                           |
| ----------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| No bulletin is waiting                                                              | The schedule may not be due, the station may be disabled or a bulletin may already be in **History**.                                   | Check the environment and station with the administrator. Check **History** before escalating. Do not trigger repeated manual runs.                                                                                                                                              |
| A waiting bulletin is close to its deadline                                         | Review has not completed inside the notification or escalation window.                                                                  | Use the decision dock countdown. Review now, approve when safe or use **Revise** and the documented fallback decision.                                                                                                                                                           |
| A script claim does not match a source row                                          | Source selection is wrong or the claim is unsupported.                                                                                  | Do not approve. Use the source panel, curate the rundown in **Adjust**, then **Revise** and **Regenerate**.                                                                                                                                                                      |
| The map or a Google Maps link does not show the expected road                       | The row may have no coordinates, or the live map may have changed since the source snapshot.                                            | Compare the reported time and source fields with the map. If the claim cannot be verified, reject with notes and include the bulletin ID in the escalation.                                                                                                                      |
| Audio will not play                                                                 | The player needs an authenticated console session or the audio request failed.                                                          | Sign in again and reload the bulletin. Try **Download audio**. If it still has no playable audio, escalate and do not approve.                                                                                                                                                   |
| Audio sounds wrong but the script is correct                                        | The voice or pronunciation needs review, or a transcript flag needs a human result.                                                     | Use the suspect chip: **Sounded fine** for an acceptable read, or **Say it differently** plus **Save** for a future pronunciation rule. Re-voice the current script only when the recording is wrong.                                                                            |
| A word remains flagged after a pronunciation rule                                   | The automated audio check compares the canonical written word with the transcript. A provider alias may not change that comparison.     | Listen first. Record **Sounded fine** if correct. Do not keep adding aliases to suppress a correct flag.                                                                                                                                                                         |
| The script is too short or too long                                                 | The available information and slot target do not fit the current script, or an edit is outside the fit guidance.                        | In **Edit**, keep the final text inside the displayed slot guidance and **Re-voice** it. Regenerate only after changing source information, or when a voice-fit warning says the approved words cannot fit and you accept a rewrite. Standby statements are sized automatically. |
| Reject validation will not submit                                                   | **Notes** exceeds the maximum length, or a chosen reason failed to send.                                                                | Reason and notes are both optional. Shorten overlong notes and retry; a useful audit record still helps whoever reviews the history.                                                                                               |
| A saved quality setting is not visible in the open bulletin                         | The bulletin carries the settings resolved when it was created.                                                                         | Treat the open bulletin as a point-in-time record. Confirm the setting on the next scheduler-created bulletin.                                                                                                                                                                   |
| **Publish** is not available                                                        | Publish appears only on an **Approved** bulletin, and only for a station you are assigned to.                                           | Confirm the bulletin is approved and that it belongs to your station. Do not approve the same bulletin repeatedly.                                                                                                                                                               |
| A published bulletin has no delivery log                                            | The write to the shared playout share may have failed.                                                                                 | Check the bulletin log for the write confirmation. This entry confirms only that the Function App wrote the approved WAV to the shared playout share; Zetta ingest is evidenced by the file disappearing from that share, and there is no on-air receipt. Copy the bulletin ID, station, slot and timestamp and escalate using the runbook. |
| A red failure-without-coverage card says standby audio may air or nothing is queued | No usable same-day audio was available, or standby audio is not configured.                                                             | Open the console, record what the card says and escalate immediately if the slot is not covered.                                                                                                                                                                                 |
| A Teams card is not visible                                                         | The environment channel may be hidden, or notification cards may not mention members.                                                   | Open the correct environment channel and inspect the bulletin directly. If notification delivery is suspected, use [the notifications guide](notifications.md) and the runbook.                                                                                                  |
| A bulletin is repeatedly **Failed**                                                 | A generation, vendor, content-safety or synthesis problem persists.                                                                     | Use **Retry** once when appropriate, retain the bulletin ID and failure detail, then escalate instead of retrying indefinitely.                                                                                                                                                  |

## 9. When to escalate

Use the support tiers in [the support model](support-model.md):

1. **Tier 1, ARN operator self-service:** wording, local editorial guidance,
   writer-prompt sections, pronunciation results, standby statements, priority roads
   and review decisions. Record the bulletin ID when the change concerns one
   bulletin.
2. **Tier 2, ARN platform operations:** health failures, stopped hosts,
   queues, repeated failed bulletins, provider throttling, delivery failures,
   missing fallback coverage, access problems, release or rollback issues and
   secret or Teams wiring problems. Start with [the runbook](runbook.md).
3. **Tier 3, vendor or Azure escalation:** Tier 2 handles sustained HERE,
   ElevenLabs, Azure OpenAI, Functions, Storage, Key Vault, networking or
   quota faults that persist after the runbook's first response.

For every escalation include the environment, station, licence area, bulletin
ID, scheduled slot and local time, current status, what the listener heard or
is expected to hear, the exact console or Teams message and the action already
taken. Use the environment's Microsoft Teams operations channel, and the
contacts and hours in the support model.
