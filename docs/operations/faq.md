---
title: "Frequently Asked Questions"
description: "Short answers to the questions operators and support engineers ask most, each pointing at the page that covers it in depth."
tags:
  - "operations"
  - "faq"
  - "support"
aliases:
  - "faq"
  - "frequently asked questions"
  - "common questions"
  - "how do I"
purpose: "reference"
status: "current"
last_verified: 2026-09-01
review_after: 2026-11-27
volatility: "medium"
source_of_truth: "Published operations procedures and live console behaviour"
verified_by:
  - "manual review"
common_queries:
  - "Why did this bulletin fall back?"
  - "How do I fix a mispronounced road name?"
  - "Why can I not sign in?"
see_also:
  - "docs/operations/operator-guide.md"
  - "docs/operations/runbook.md"
  - "docs/operations/glossary.md"
---

# Frequently Asked Questions

This FAQ is for ARN content-team operators and the support engineers who keep the platform running. Answers are deliberately short and route you to the published page with the full procedure or explanation.

## Using the console

### How do I find the next bulletin to review?

Open **On air** and select a bulletin marked **Waiting approval**, starting with the earliest deadline. If the list is empty, check **History** before assuming generation failed. See the [operator guide](operator-guide.md) for the review workflow.

### What should I check before I approve a bulletin?

Read the complete script, check the slot, duration, coverage and voice, listen to all audio, and trace factual claims to the source panel. Do not approve a contradiction or unsupported claim. See the [operator guide](operator-guide.md) for the evidence and audio checks.

### What does approving a bulletin do?

**Approve** marks the bulletin approved, but it does not deliver audio to playout. Publishing is a second, separate action: select **Publish**, confirm it, then check the delivery history. See the [operator guide](operator-guide.md) for the decision states.

### What do I do when a bulletin should not air?

Open **Revise**, record a reason and useful notes, then choose fallback audio when safe fallback coverage is wanted. Use **Reject bulletin** only when fallback must not be used and a replacement can be generated in time. See the [operator guide](operator-guide.md) for the destructive decisions.

### Does every rejection teach the writer?

No. Rejection is an audit record for that bulletin only: reason and notes are both optional and neither enters writer memory. Pronunciation, source and script defects should be fixed through the action shown in **Revise**. The writer learns automatically instead: when an operator edits a script, re-voices it and the revised bulletin is delivered to playout, the platform generalises the edit into a station-scoped lesson candidate. Candidates activate only after three independently published edits agree, expire after 90 days, and can be promoted or dismissed by an administrator under **Bulletin rules → Future lessons**.

### Where can I find audio for a past bulletin, and how long is it kept?

Open the bulletin in **History** and use its audio player or download control. In test and production, lifecycle rules delete audio automatically: published audio after 90 days, never-aired audio after 30 days, and audit exports after 1095 days, with audio tiering to cool storage after 30 days. Development has no broadcast delete rule, so its recordings remain until someone removes them (its LLM-call captures are deleted after 14 days). Deleted audio can be recovered within the soft-delete window, which is 90 days in test and production and 30 days in development; test and production also support point-in-time restore for the last 30 days. See the [operations runbook](runbook.md) for retrieval and retention details.

### Why is a saved setting not visible in the bulletin I have open?


Each bulletin keeps the configuration resolved when it was created, so a saved change does not rewrite an existing bulletin. Check the next scheduler-created bulletin, or regenerate when the change is an intentional review decision. See the [operator guide](operator-guide.md) for configuration timing.

## Bulletin content and quality

### Why did this bulletin fall back to old content?

The fresh bulletin may have failed, been content-limited, missed its window, or been rejected. The platform first uses the most recent usable same-day bulletin for that station, then configured standby audio if no same-day audio exists. See the [operations runbook](runbook.md) for fallback reasons and listener impact.

### How do I fix a mispronounced road or suburb name?

If the recording is wrong, hover the suspect word and pause. When the bulletin's traffic data identifies a complete road name, the **Say it differently** panel selects that whole phrase (for example, **Gillies Highway**) rather than one token. Check or edit the target, enter how it should sound, and save the dictionary rule for future bulletins. If the road is not in the bulletin data, widen the target manually in the panel. Re-voice the current script when that recording must be corrected; if it sounded right, record **Sounded fine** instead of adding an alias. See the [operator guide](operator-guide.md) for pronunciation review.

### Is a pronunciation warning proof that the voice said the word incorrectly?

No. The automated audio check compares the transcript with the written script, so names whose spelling and sound differ can be flagged even when they sounded correct. Listen to the word and record the appropriate verdict. See the [operator guide](operator-guide.md) for suspect-word handling.

### How do I change bulletin wording or style without a developer?

Open **Bulletin rules → Writer prompt** for the station, edit the relevant
playbook card, and use its preview before saving. The station playbook governs
wording over the ARN house style; **Station lessons** are also read by the
conversational writer. For numeric editorial controls such as corridor caps,
tier/continuity limits, slot-fit values or traffic sources, an administrator
must use **Bulletin rules → Limits**. A saved change affects a later bulletin,
not an existing recording. See the [bulletin quality iteration guide](../reference/prompt-iteration-guide.md).

### What is the difference between a standby statement and a fallback?

A standby statement is a short ARN-authored line added inside a fresh bulletin when its real content leaves a duration gap. A fallback is standby audio used when a fresh bulletin cannot safely air, so a standby statement is not a third fallback tier. See the [operator guide](operator-guide.md) for content priority order and fallback behaviour.

### Can a standby statement be restricted to certain times or days?

Yes. A standby statement can carry an availability window — an eligible-from and until date, days of the week, and a start and end time, all evaluated in the station's local time. A statement outside its window never airs and nothing substitutes for it; the console warns when no enabled statement for a station is currently eligible. See [per-station configuration](../reference/per-station-configuration.md).

### Why did a bulletin air without its usual intro or outro?

A station's bookend pair — its fixed opening and closing lines — is dropped for a single bulletin when the pair cannot share the slot with the traffic read; the traffic always wins and the job log records the reason. It is expected in that case, not a failure. See [per-station configuration](../reference/per-station-configuration.md) for bookends.

### Can the platform invent a road or incident?

No. Script claims must trace to the collected source information, and the source check rejects unsupported content. Do not approve a claim that has no matching source row. See the [operator guide](operator-guide.md) for sentence-level source checks.

### Why is my bulletin too short or too long?

The available evidence, slot target, and current changes may not fit the script. Use **Edit** to adjust the final words inside the displayed slot guidance and re-voice them. Regenerate only after changing the selected source information, or when a voice-fit warning says the words cannot fit and you explicitly accept a rewrite. Standby statements are sized automatically. See the [operator guide](operator-guide.md) for length and regeneration decisions.

## When something goes wrong

### Why will the audio not play?

The console may need an authenticated session, or the audio request may have failed. Sign in again, reload the bulletin, and try **Download audio**; if it remains unavailable, do not approve it and escalate with the bulletin details. See the [operator guide](operator-guide.md) for the audio failure response.

### Why is no bulletin waiting for me?

The schedule may not be due, the station may be disabled, or the bulletin may already be in **History**. Check the environment and schedule with the platform support team, and do not create repeated manual bulletins to compensate. See the [operator guide](operator-guide.md) for this troubleshooting case.

### How can I tell whether an alert is real?

Check the anonymous `/api/health` endpoint, the console, and recent Log Analytics warnings or errors. Allow for the documented one-to-five-minute ingestion delay, and remember that a no-executions alert on a parked environment is expected only when park has disabled its alerts. See the [operations runbook](runbook.md) for the alert catalogue and first response.

### What does the platform do during a vendor outage?

Transient calls may be retried, but sustained HERE, Azure OpenAI, or ElevenLabs failure makes generation fail safe. The platform uses the same-day fallback or standby audio where available, so stale safe content can air rather than false or empty content; escalate the service issue instead of restarting repeatedly. See the [operations runbook](runbook.md) for failure effects and escalation.

### What should I do if a bulletin keeps failing?

Use **Retry** once when it is appropriate, retain the bulletin ID and exact failure detail, then escalate the repeated failure. Repeated clicks do not resolve a persistent generation, vendor, content-safety, or synthesis problem. See the [operator guide](operator-guide.md) for the escalation threshold.

### Why can I not see a Teams notification card?

The environment channel may be hidden, and cards do not create @mentions or normal toast notifications. Open the correct operations channel and inspect the console directly; if the Logic App or webhook is suspected, escalate to platform support. See [Teams notifications](notifications.md) for the channel and troubleshooting details.

### How do I know a published bulletin reached playout?

Open the bulletin history after publishing and look for the delivery entry. It confirms that the approved WAV was written to the shared playout share; it does **not** confirm that Zetta ingested the file or that it played on air. If the entry is missing, record the bulletin ID, station, slot, and timestamp and escalate. See the [operator guide](operator-guide.md) for the publish check.

### Why does the map show a different road from the bulletin?

The evidence row may have no coordinates, or the live map may have changed since the evidence snapshot was fetched. Compare the reported time and source fields, and reject with notes if the claim cannot be verified. See the [operator guide](operator-guide.md) for map and evidence checks.

### Where do I report a defect or a quality opinion?

Record a failure in the shared defect log with its scenario, environment, bulletin ID, severity, status, and evidence. Script or audio opinions belong in the quality-feedback table, not in the defect-severity count. See the [defect log](defect-log.md) for the required fields.

## Access and sign-in

### Why do I get a 403 after authenticating?

Authentication succeeded, but the ARN account or its group does not have the matching Operator or Admin app role. Contact the ARN technology lead or platform support to confirm the ARN account, environment group, and app-role assignment, and do not keep changing the password. See [First-Time Sign-In](first-time-sign-in.md) for the exact role check.

### Why am I stuck in a sign-in loop?

The browser may be reusing a guest account, the wrong Microsoft session or stale Entra cookies. Sign out, open the exact environment URL in a private window, and use the ARN-native `@arn.com.au` account; report the URL and time if the loop continues. See [First-Time Sign-In](first-time-sign-in.md) for sign-in troubleshooting.

### Which console role do I need?

An **Operator** can review, approve and reject within the assigned station scope, and maintain the station's own bulletin rules: editorial guidance, priority roads, standby statements and pronunciation verdicts. An **Admin** also manages schedules, voices, delivery, pronunciation dictionaries, banned words, configuration transfer, the activity log and operator access. See [First-Time Sign-In](first-time-sign-in.md) for role visibility and scope.

## Operating and cost

### How do I park or resume an environment, and what does it save?

Parking stops the Function App, scales the plan to B1, and disables alerts and autoscale; resuming starts the app, restores the P0v3 plan, health-checks it, and re-enables autoscale and alerts. Parking reduces compute cost but is not free, because private endpoints, Static Web Apps, Key Vault, storage, and the B1 plan still bill. See the [operations runbook](runbook.md) and [run costs](run-costs.md) for the controlled commands and measured figures.

### How do I rotate a platform secret?

Write the new value as a new version of the environment Key Vault secret, restart the Function App, then verify health and one generation. Secret names map double hyphens (`--`) to colons (`:`) in configuration. Teams OAuth and Logic App webhook changes have their own re-consent or configuration-touch steps. See the [operations runbook](runbook.md) for each secret type.

### How can I run a load test without vendor spend?

Use the documented test mode that prevents HERE, Azure OpenAI, and ElevenLabs calls during a load test. Keep that mode for testing only and verify the environment setting before returning to live services. See the [command reference](quick-reference.md) for the setting and the commands.

## Operations and support

### Who do I call at each support tier?

Tier 1 is ARN operator self-service for editorial and review decisions. Tier 2 is ARN platform operations for health, access, queues, delivery, deploys, and secrets. Tier 3 is vendor or Azure support reached through Tier 2. See the [support model](support-model.md) for the contact matrix.

### Who owns ongoing operation?

ARN owns ongoing maintenance and Tier 2 triage. Route vendor or Azure faults through the support model, with the environment, station, bulletin ID, and listener-facing outcome included in the report. See the [support model](support-model.md) and [access and accounts](access-and-accounts.md) for ownership and access procedures.
