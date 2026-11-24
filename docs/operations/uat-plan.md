---
title: "Acceptance Testing"
description: "Acceptance testing scope, participant roles, entry and exit criteria, and scenarios for ARN operators using the test environment."
tags: [operations, acceptance, testing, test-environment]
purpose: "runbook"
status: "current"
last_verified: 2026-09-01
review_after: 2026-12-02
volatility: "medium"
source_of_truth: "The acceptance scenarios and the deployed test environment"
verified_by:
  - "manual review"
aliases:
  - "acceptance testing"
  - "acceptance test plan"
common_queries:
  - "What does acceptance testing cover?"
  - "What do acceptance participants do?"
  - "Is the test environment ready for acceptance testing?"
see_also:
  - "docs/operations/uat-scenario-sheet.md"
  - "docs/operations/integration-test-plan.md"
  - "docs/operations/runbook.md"
---

# Acceptance Testing

Acceptance testing is a repeatable capability that ARN reruns whenever a station, market or release changes. Each run confirms that ARN operators can run the daily bulletin workflow in the test environment. The workflow covers review, source checking, approval or rejection, station configuration and delivery without requiring a platform support person to drive the scenarios.

## Test environment

| Item | Value |
|---|---|
| Console | https://ain-test.arn.com.au |
| Backend resource | `IBP-AUE-TST-FNA-APP01`, with fail-closed Microsoft Entra ID access |
| Pilot stations | KIIS 90s Sydney (`sydney`) and Star 102.7 FM Cairns (`cairns-4ca`) |
| Station configuration | Two station schedules with drawn coverage, station voice profiles, delivery styles, pronunciation dictionaries, writer-prompt sections, content-safety terms, editorial guidance, priority roads and filler lines |
| External services | Live HERE, Azure OpenAI and ElevenLabs services |
| Playout | **Live** in TEST: the Function App writes approved WAV files with its managed identity to the shared Azure Files `ibpauetststaftp01/informationbulletins` share when a station is armed |
| Audio access | Audio is served through the authenticated console and can be played off-network |
TEST can write real playout files. Test carries new code while production is held frozen at the last known-good build, so TEST is the environment used to prove delivery for a pilot station. The Function App writes approved WAV files with its managed identity to the shared Azure Files `ibpauetststaftp01/informationbulletins` share. Zetta ingests the approved file over SMB and deletes it, so delivery is confirmed by the file disappearing. There is no on-air receipt. Any market other than KIIS 90s Sydney and Star 102.7 FM Cairns still cannot reach playout from any environment, because it has no playout code configured and delivery fails loudly instead of inventing a file name.

Whether a publish actually reaches a station is decided per station by the **Send to playout** switch on the **Stations** page. With it OFF the station generates, is reviewed and publishes normally while nothing leaves for the shared library, and the bulletin log records "Send to playout is off for this station". With it ON, a publish writes the approved WAV to the shared library; Zetta ingests and deletes it, and no on-air receipt is available.

**Before each run, read the live schedule flags and the Send to playout flags.** When intentionally proving delivery, use TEST with `cairns-4ca`; keep `sydney` held back. Never arm the same station in two environments. Production is running but frozen at the last known-good build and has no station armed. For a run that must not deliver, leave **Send to playout** OFF; for a delivery run, arm only the intended TEST station. Enable only the schedule needed for the planned slot, then restore the prior schedule and station flags afterwards and verify the restored state. Do not assume schedules are disabled or rely on a previous run's notes.

## Participant roles

| Role | Responsibility |
|---|---|
| Operators, two to three | Execute operator scenarios and judge script and audio quality from an on-air perspective |
| Administrator, one | Execute administration scenarios and confirm role-based access |
| Acceptance coordinator | Maintains the acceptance record, records outcomes and makes the pass or fail decision for each scenario |
| Platform support team | Makes the environment available, observes failures and provides technical triage without driving the scenarios |

## Entry criteria

| Criterion | Required outcome and minimum evidence |
|---|---|
| Test environment | The console at https://ain-test.arn.com.au loads and GET `/api/health` on the test Function App returns HTTP 200 with status `healthy`. Record the console address, HTTP status, status field value, and time observed |
| Station configuration | The pilot station schedules and their editorial settings are saved and usable. Record station names, schedule settings, and configuration review evidence |
| End-to-end trial | A test bulletin completes generation, review, audio synthesis, approval and delivery to the shared playout library; the file disappears when Zetta ingests it, which is the available delivery evidence. Record the bulletin reference and delivery evidence |
| Sign-in | Every participant role can sign in and sees only the surfaces permitted for that role. Record the role observed and access surfaces confirmed |
| Run readiness | The acceptance coordinator, operator roles and administrator role are assigned before the run begins. Record the coordinator role and participant roles |

## Scenario scripts

Record the result of every scenario as Pass, Fail or Blocked, together with the participant role, time, bulletin reference and evidence. The **Expected** column is the acceptance contract. Any other observable result is a defect and belongs on the [defect register](defect-log.md).

### O1 to O11, operator workflow

| Case | Steps | Expected |
|---|---|---|
| O1, review a live bulletin | Enable a pilot station schedule, wait for the scheduler to run, then open the bulletin from On air | The bulletin reaches Waiting approval with a script, playable audio, source panel showing cited, unused and below-cut evidence, and a map |
| O2, verify against evidence | In Review, open the cited source for each sentence and open the traffic map link from the Adjust rundown | Every scripted claim traces to a source row and the map opens the live traffic view at the correct location |
| O3, approve and publish | Approve the bulletin, then publish it | Status moves through Approved and Published, the activity record confirms the approved WAV was written for playout, and when the station is armed the shared file disappears as Zetta ingests it. No on-air receipt exists. The Microsoft Teams approval card was received at Waiting approval |
| O4, reject with a reason and audit notes | In Review, open Revise and expand This bulletin must not air. Choose a reason from the list, enter audit notes of at least 10 characters, then confirm Reject | Rejection is blocked until a reason is chosen and the notes meet the minimum. The bulletin is dropped from Waiting approval and the reason and notes are recorded in the audit history. A correctable reason offers a direct fix (pronunciation, sources or the script) instead of teaching the writer |
| O5, edit script and revoice | Edit the script text in Review, then revoice it | New audio reads the edited text verbatim and the edit remains present after a failed revoice and retry |
| O6, reject to fallback | Reject a waiting bulletin to fallback | The slot uses the most recent same-day bulletin, or the station's standby audio when no same-day bulletin exists. The panel explains which source was used and why. Filler lines are editorial padding during generation, not a fallback tier |
| O7, deadline pressure | Leave a bulletin unapproved into the notification window | A reminder escalation appears in the console and Microsoft Teams, and the urgency banner identifies the most urgent bulletin |
| O8, audio experience | Play the audio from start to finish, use word highlighting, click to seek, change speed and download it | Playback works off-network, highlighting tracks the read and the download provides the WAV file |
| O9, reject and record a station lesson | Reject a waiting bulletin whose issue is teachable, for example Unsuitable station wording or tone. In the rejection section, enable Teach this station for future bulletins, choose a lesson category and write an imperative instruction of at least 20 characters, then confirm Reject | The lesson is recorded for that station only: it appears in the station's future-lessons list, can be promoted or dismissed by an administrator, and guides later bulletins for that station. The reason and audit notes still go to the audit history; a lesson is never taught unless the operator explicitly enables it |
| O10, regenerate with a different voice | Open a Waiting approval bulletin in Review and open Revise. In the Voice section choose a different active voice from Change to, play the voice sample or use Audition with this voice to hear it read the current script, then confirm the apply | A voice-only change applies as a re-voice: the current script words are kept and the audio is recorded with the chosen voice. The override applies to this bulletin only; the station schedule keeps its configured voice. If the audition shows the read cannot fit the slot on the chosen voice, the console offers Regenerate instead and the escalation to a rewrite is explicit |
| O11, station bookends on air | Assign a bookend pair to a pilot station in the station studio Speech step and trigger a bulletin. In Review, read the script and play the audio. Then set the station's slot to a duration too short for the pair alongside a minimum viable read (for example 15 seconds), trigger again and restore the slot duration afterwards | The read opens with the pair's intro and closes with its outro: both lines are in the script and audible in the audio with no seam. On a slot too short for the pair, the bulletin airs the traffic without the bookend lines and the job log explains that the bookend was dropped because it could not fit alongside a minimum viable traffic read |

### A1 to A7, administrator workflow

| Case | Steps | Expected |
|---|---|---|
| A1, station schedule change | Edit a pilot station schedule, duration and content toggles, then save with a change note | The change applies, the activity record shows who changed what and when, and the next scheduler run uses the new configuration |
| A2, voice management | Preview voices, assign a different voice profile to a station and trigger a bulletin | The next bulletin uses the assigned voice and the voice gallery shows its airing statistics |
| A3, pronunciation | Add a respelling for a local road or suburb and trigger a bulletin that mentions it | The word is pronounced according to the rule when the audio is played |
| A4, station writer playbook and limits | Open **Bulletin rules** for the test station. Edit a Writer prompt card in the card grid, run its preview, and save it. As an administrator, open **Limits** and verify that numeric editorial settings are available there | The preview reflects the proposed station wording instruction; the next generated bulletin uses the saved playbook. Limits exposes controls for numeric caps, slot fit and enabled traffic sources |
| A5, content-safety terms | Add a term and trigger a bulletin whose source data would use it | The term does not reach the aired script and the content-safety review notes are visible |
| A6, configuration transfer | Export all station configuration and re-import it unchanged | The round trip reports that all items are unchanged and no setting is altered |
| A7, operators and access | Open the operators page and confirm that an operator account cannot see administration surfaces | Console routes and API access match the assigned role |

### R1 to R2, resilience

These scenarios test operator-visible behaviour without deliberately disrupting a provider or infrastructure service. Run them on TEST only. The coordinator records the schedule, slot time, source-data snapshot and reset evidence for each run.

| Case | Preconditions and arrangement on TEST | Participant observes | Reset |
|---|---|---|---|
| R1, quiet-station fill | Use the Cairns (`cairns-4ca`) schedule for an off-peak slot. Before enabling it, read the live schedule and **Send to playout** flags, confirm that `sydney` is held back and no environment also arms `cairns-4ca`, then check that the TEST traffic snapshot has no material incidents and that the station's weather, fuel and filler configuration is present. Trigger the slot. This arrangement cannot guarantee a content-limited read because HERE data and generation vary; if the slot is not quiet enough, wait for a naturally quiet Cairns slot and capture the same evidence, or record Blocked with the data snapshot. | The generated read uses the configured content priority order of minor traffic, weather, fuel and filler lines. Where the traffic-only read leaves a measured gap, station filler audio is appended and the final read remains within the slot without stretching. If the pipeline cannot cover the slot, observe the documented same-day or standby fallback instead and record which source was used. | Restore the Cairns schedule and all station/environment flags to their prior values, remove any temporary schedule change or filler/configuration test data, and confirm no test bulletin remains awaiting approval unless it is part of another run. Do not change production. |
| R2, missed window | On TEST, read the live schedule and **Send to playout** flags, confirm `sydney` is held back and no environment also arms the selected station, then enable a pilot schedule with a slot due soon enough for the coordinator to observe it. Allow the resulting bulletin to remain Waiting approval and do not approve it through its playout/fallback deadline. The watchdog treats a non-active job past its fallback deadline as missed and applies the same-day bulletin or, if none exists, the station's standby audio; do not stop a provider or infrastructure service. If no suitable waiting bulletin can be created during the run, wait for a naturally missed window and capture the logs and notification evidence. | The fallback source is recorded on the bulletin, the activity or notification evidence describes the closure, and the participant can see the missed-window outcome rather than an approved publication. A same-day source produces the missed-deadline closure notification when the job had been escalated; a quiet covered fallback may produce the covered notification instead. | Restore the schedule and all station/environment flags to their prior values, approve or otherwise close any unrelated waiting test jobs according to the coordinator's run record, and confirm TEST is back to its pre-run schedule and configuration. Never perform this arrangement on production. |


## Defect handling

Record every failure with its scenario, bulletin reference, time, environment, observable result and evidence. Use these severity levels:

- **Blocker:** the workflow cannot complete.
- **Major:** the listener may hear a wrong on-air outcome, or an essential operational control does not work.
- **Minor:** the issue is cosmetic or affects wording without changing the on-air outcome.
Record script and audio quality observations as improvement feedback. Do not count an opinion about style as a defect unless it violates an explicit scenario expectation.

## Exit criteria

- All operator and administrator scenarios pass, and the resilience scenarios behave as described.
- No Blocker or Major defect remains open. A Major carried into a run must be fixed or have its identifier, rationale and scope recorded as a written waiver on the [defect register](defect-log.md).
- Minor defects and quality feedback may remain recorded without blocking completion when the coordinator documents their disposition in the run record.
- The coordinator records the completed scenario outcomes and retains the run record in ARN's own records.
