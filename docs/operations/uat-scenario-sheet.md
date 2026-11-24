---
title: "Acceptance Scenario Template"
description: "Reusable template for recording acceptance scenarios, outcomes and evidence for the ARN Information Bulletin Platform."
tags:
  - "operations"
  - "acceptance"
  - "testing"
  - "evidence"
aliases:
  - "acceptance test record"
  - "acceptance scenario sheet"
  - "acceptance evidence"
purpose: "runbook"
status: "current"
last_verified: 2026-09-01
review_after: 2026-12-01
volatility: "high"
source_of_truth: "The acceptance scenarios and their recorded outcomes"
verified_by:
  - "manual review"
common_queries:
  - "How do I record an acceptance scenario result?"
  - "What evidence is required for acceptance testing?"
see_also:
  - "docs/operations/uat-plan.md"
  - "docs/operations/defect-log.md"
  - "docs/operations/first-time-sign-in.md"
---

# Acceptance Scenario Template

## Run cover — copy and complete for each run

Copy this template into ARN's own records and complete the run fields before the first case. Acceptance testing is rerun whenever a station, market or release changes. Use TEST at https://ain-test.arn.com.au. Playout delivery is **live** in TEST: the Function App writes approved WAV files with its managed identity to the shared Azure Files `ibpauetststaftp01/informationbulletins` share when a station is armed; Zetta ingests the file over SMB and deletes it, so disappearance is the delivery evidence and there is no on-air receipt.

Before each run, read the live schedule flags and the **Send to playout** flags. When intentionally proving delivery, use TEST with `cairns-4ca`; keep `sydney` held back. Never arm the same station in two environments. Production is running but held frozen at the last known-good build, with no station armed. For a run that must not deliver, leave **Send to playout** OFF. Enable only the schedule needed for the planned slot, then restore the prior schedule and station flags afterwards and verify the restored state.

| Field | Record for this run |
|---|---|
| Run date and time | [enter the run start and finish] |
| Environment | TEST at https://ain-test.arn.com.au |
| Build or release under test | [enter the deployed build or release identifier] |
| Acceptance coordinator | [enter name and role] |
| Participants | [enter each participant's name and role] |

Use this record before, during and after each run. For every case, record Pass, Fail or Blocked, the participant role, the time, the relevant bulletin reference and the evidence location. If the observed result differs from **Expected**, record the issue on the [defect register](defect-log.md) and include its defect number in the case record.

Use [Acceptance Testing](uat-plan.md) for environment preparation and the operating sequence. This published page is a reusable template; copy it for each run and retain the completed copy in ARN's own records. It is not itself the completed acceptance record.

## Run details

Use these fields to make the copied record unambiguous for the run.

| Field | What to record |
|---|---|
| Environment | TEST at https://ain-test.arn.com.au |
| Build or release | The deployed build or release identifier |
| Run window | The start and finish time for this run |
| Acceptance coordinator | The role responsible for maintaining this record |
| Participant roles | The operator, administrator and platform support roles present |

## Participant roles

| Role | Responsibility |
|---|---|
| Operators, two to three | Run operator cases and judge script and audio quality |
| Administrator, one | Run administration cases and confirm role-based access |
| Acceptance coordinator | Record outcomes, evidence and defect references |
| Platform support team | Prepare the environment and observe technical failures without driving the cases |

## Pre-flight checks

Complete these checks before the first scenario. The sign-in prerequisite is described in the [first-time sign-in walkthrough](first-time-sign-in.md).

| Entry criterion | Evidence to record |
|---|---|
| Test environment is deployed and healthy: the console at https://ain-test.arn.com.au loads and GET `/api/health` on the test Function App returns HTTP 200 with status `healthy` | Console address, HTTP status, status field value, and time observed |
| Pilot station configuration is available | Station names, schedule settings and configuration review evidence |
| End-to-end bulletin trial completes | Bulletin reference and delivery evidence (shared file disappearance) |
| Participant roles can sign in | Role observed and access surfaces confirmed |
| Run readiness | Coordinator role and participant roles recorded |

The **Expected** line for each case is the acceptance contract. Any other observable result is a defect. Record each failure on the [defect register](defect-log.md); integration outcomes use the same record.

## O, operator workflow

### O1, review a live bulletin

**Steps:** Enable a pilot station schedule, wait for the scheduler to run, then open the bulletin from On air.

**Expected:** The bulletin reaches Waiting approval with a script, playable audio, source panel showing cited, unused and below-cut evidence, and a map.

**Record:** Outcome, operator role, time, bulletin reference, evidence and any defect number.

### O2, verify against evidence

**Steps:** In Review, open the cited source for each sentence and open the traffic map link from the Adjust rundown.

**Expected:** Every scripted claim traces to a source row and the map opens the live traffic view at the correct location.

**Record:** Outcome, operator role, time, bulletin reference, evidence and any defect number.

### O3, approve and publish

**Steps:** Approve the bulletin, then publish it.

**Expected:** Status moves through Approved and Published, the activity record confirms the approved WAV was written for playout, and when the station is armed the shared file disappears as Zetta ingests it. No on-air receipt exists. The Microsoft Teams approval card was received at Waiting approval.

**Record:** Outcome, operator role, time, bulletin reference, status evidence, notification evidence and any defect number.

### O4, reject with a reason and audit notes

**Steps:** In Review, open Revise and expand This bulletin must not air. Choose a reason, enter audit notes of at least 10 characters, then confirm Reject.

**Expected:** Rejection is blocked until a reason is chosen and the notes meet the minimum length. The bulletin is dropped from Waiting approval, and the reason and notes are recorded in the audit history. A correctable reason offers a direct fix (pronunciation, sources or the script) instead of teaching the writer.

**Record:** Outcome, operator role, time, bulletin reference, reason and notes evidence, audit evidence and any defect number.

### O5, edit script and revoice

**Steps:** Edit the script text in Review, then revoice it.

**Expected:** New audio reads the edited text verbatim and the edit remains present after a failed revoice and retry.

**Record:** Outcome, operator role, time, bulletin reference, edited text evidence, audio evidence and any defect number.

### O6, reject to fallback

**Steps:** Reject a waiting bulletin to fallback.

**Expected:** The slot uses the most recent same-day bulletin, or the station's standby audio when no same-day bulletin exists. The panel explains which source was used and why. Filler lines are editorial padding during generation, not a fallback tier.

**Record:** Outcome, operator role, time, bulletin reference, source used, audio evidence and any defect number.

### O7, deadline pressure

**Steps:** Leave a bulletin unapproved into the notification window.

**Expected:** A reminder escalation appears in the console and Microsoft Teams, and the urgency banner identifies the most urgent bulletin.

**Record:** Outcome, operator role, time, bulletin reference, console evidence, notification evidence and any defect number.

### O8, audio experience

**Steps:** Play the audio from start to finish, use word highlighting, click to seek, change speed and download it.

**Expected:** Playback works off-network, highlighting tracks the read and the download provides the WAV file.

**Record:** Outcome, operator role, time, bulletin reference, playback evidence and any defect number.

### O9, reject and record a station lesson

**Steps:** Reject a waiting bulletin whose issue is teachable, for example "Unsuitable station wording or tone". In the rejection section, enable **Teach this station for future bulletins**, choose a lesson category, write an imperative instruction of at least 20 characters, then confirm **Reject**.

**Expected:** The lesson is recorded for that station only, appears in the station's future-lessons list and can be promoted or dismissed by an administrator. The reason and audit notes still enter the audit history; no lesson is taught unless the operator explicitly enables it.

**Record:** Outcome, operator role, time, bulletin reference, lesson category and text, evidence of the lesson in the future-lessons list and any defect number.

### O10, regenerate with a different voice

**Steps:** Open a Waiting approval bulletin in Review and open **Revise**. In the **Voice** section, choose a different active voice from **Change to**. Play the voice sample or use **Audition with this voice** to hear the staged voice read the current script and check its fit, then confirm the apply.

**Expected:** A voice-only change applies as a re-voice: the current script words are kept and the audio is recorded with the chosen voice. The override applies to this bulletin only; the station schedule keeps its configured voice. If the audition shows the read cannot fit the slot on the chosen voice, the console offers **Regenerate instead**, and the escalation to a rewrite is explicit.

**Record:** Outcome, operator role, time, bulletin reference, original and chosen voice, audition verdict, audio evidence and any defect number.

### O11, station bookends on air

**Steps:** In the station studio **Speech** step, assign a bookend pair to a pilot station and save. Trigger a bulletin for that station, then read the script and play the audio in Review. To test the short-slot case, set the station's slot to a duration too short for the pair alongside a minimum viable read (for example 15 seconds), trigger again and restore the original slot duration afterwards.

**Expected:** The read opens with the pair's intro and closes with its outro: both lines are in the script and audible in the audio with no seam. When the slot cannot fit the pair alongside a minimum viable traffic read, the bulletin airs the traffic without the bookend lines, and the job log explains that the bookend was dropped because it could not fit.

**Record:** Outcome, operator role, time, bulletin references, bookend pair, slot duration, script and audio evidence, job-log evidence and any defect number.

## A, administrator workflow

### A1, station schedule change

**Steps:** Edit a pilot station schedule, duration and content toggles, then save with a change note.

**Expected:** The change applies, the activity record shows who changed what and when, and the next scheduler run uses the new configuration.

**Record:** Outcome, administrator role, time, station, configuration evidence and any defect number.

### A2, voice management

**Steps:** Preview voices, assign a different voice profile to a station and trigger a bulletin.

**Expected:** The next bulletin uses the assigned voice and the voice gallery shows its airing statistics.

**Record:** Outcome, administrator role, time, station, bulletin reference, audio evidence and any defect number.

### A3, pronunciation

**Steps:** Add a respelling for a local road or suburb and trigger a bulletin that mentions it.

**Expected:** The word is pronounced according to the rule when the audio is played.

**Record:** Outcome, administrator role, time, station, bulletin reference, pronunciation rule and audio evidence.

### A4, station writer playbook and limits

**Steps:** Open **Bulletin rules** for the test station. Edit a Writer prompt
card in the card grid, run its preview, and save it. As an administrator,
open **Limits** and verify that numeric editorial settings are available there.

**Expected:** The preview reflects the proposed station wording instruction;
the next generated bulletin uses the saved playbook. Limits exposes controls
for numeric caps, slot fit and enabled traffic sources.

**Record:** Outcome, role, station, preview reference, before and after evidence,
limit values and any defect number.

### A5, content-safety terms

**Steps:** Add a term and trigger a bulletin whose source data would use it.

**Expected:** The term does not reach the aired script and the content-safety review notes are visible.

**Record:** Outcome, administrator role, time, station, bulletin reference, review evidence and any defect number.

### A6, configuration transfer

**Steps:** Export all station configuration and re-import it unchanged.

**Expected:** The round trip reports that all items are unchanged and no setting is altered.

**Record:** Outcome, administrator role, time, transfer reference, result evidence and any defect number.

### A7, operators and access

**Steps:** Open the operators page and confirm that an operator account cannot see administration surfaces.

**Expected:** Console routes and API access match the assigned role.

**Record:** Outcome, administrator role, time, account role, route evidence and any defect number.

## R, resilience

These cases test operator-visible behaviour without deliberately disrupting a provider or infrastructure service.

### R1, quiet-station fill

**Steps:** Before each run, read the live schedule and **Send to playout** flags. Use TEST with `cairns-4ca` for an intentional delivery proof, keep `sydney` held back and confirm no other environment arms the selected station. Set up an off-peak slot, record the prior schedule and flags, then trigger the bulletin. Restore the prior schedule and flags after the case and verify the restored state.

**Expected:** The slot is filled using the configured content priority order of minor traffic, weather, fuel and filler lines. If there is not enough source content, the most recent same-day bulletin is used, followed by station standby audio. Filler lines pad a generated bulletin and are not a fallback tier. The read is not stretched.

**Record:** Outcome, operator role, time, bulletin reference, selected source, audio evidence and any defect number.

### R2, missed window

**Steps:** Use TEST and read the live schedule and **Send to playout** flags before setup. Keep `sydney` held back and confirm no other environment arms the selected station. Set up a pilot schedule with a slot due soon enough to observe, record the prior schedule and flags, and create a waiting bulletin. This is a deadline-boundary test: leave the bulletin unapproved through its playout/fallback deadline. Do not stop a provider or infrastructure service.

**Expected:** The scheduler applies the fallback chain, the bulletin explains what aired instead and the notification card describes the closure behaviour.

**Record:** Outcome, operator role, time, bulletin reference, fallback evidence, notification evidence and any defect number.

**Reset:** Restore the prior schedule and all station/environment flags, close any unrelated waiting test jobs according to the run record, and verify TEST is back to its pre-run state. Never perform this arrangement on production.

## Run-outcome summary

After all cases, record the counts of Pass, Fail and Blocked outcomes, together with the number of open Blocker and Major defects. The run record is complete when:

- all operator and administrator cases pass, and the resilience cases behave as described;
- no Blocker or Major defect remains open; and
- the coordinator records the outcome and confirms the evidence set.

| Field | Record for this run |
|---|---|
| Overall outcome | [enter Pass / Pass with conditions / Not accepted] |
| Conditions, exclusions or defect references | [enter any applicable details] |
| Evidence location | [enter the retained evidence location] |
| Coordinator completion date | [enter date] |

The completed copy, including outcomes, evidence and defect references, is retained in ARN's own records. This published page is a reusable record template, not the completed acceptance record.
