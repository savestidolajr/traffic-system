---
title: "Integration Testing"
description: "Integration cases for the external services that support bulletin generation, review, audio synthesis, notifications, identity and Zetta playout."
tags: [operations, testing, integration, zetta, here, elevenlabs, teams]
purpose: "runbook"
status: "current"
last_verified: 2026-09-01
review_after: 2026-12-01
volatility: "medium"
source_of_truth: "The external service integration cases and the deployed test environment"
verified_by:
  - "manual review"
aliases:
  - "integration testing"
  - "integration test plan"
common_queries:
  - "What integration tests does ARN run?"
  - "How is the Zetta leg tested?"
see_also:
  - "docs/operations/uat-plan.md"
  - "docs/operations/defect-log.md"
  - "docs/operations/runbook.md"
---

# Integration Testing

Integration testing confirms that the ARN Information Bulletin Platform can exchange the required data with each external service. Each case uses a real counterpart service or a participant with access to the receiving system. Record the result, evidence and any defect on the [defect register](defect-log.md).

Cases can be rerun in each environment against its configured real providers whenever a station, market or release changes. HERE, Azure OpenAI and ElevenLabs are live integrations; the six authority traffic feeds are optional per-source integrations whose availability and failure state must be recorded. Test is the environment used for intentional proving. Production is running but held frozen at its last known-good build with no station armed, and development can never deliver.

## I1, HERE traffic

| Case | Steps | Expected outcome and evidence |
|---|---|---|
| Live fetch within station coverage | Trigger a bulletin for a station with live traffic coverage | The bulletin record shows a HERE traffic fetch. The source panel contains current incidents and the map shows their locations |
| Coverage boundary | Compare the incidents in the rundown with the station's drawn coverage on the map | No incident outside the station coverage is voiced. Retain the map and source-panel evidence |
| Data accuracy feedback | When an operator identifies an incorrect incident, record the observation with its location and time | The observation is available for traffic-provider review and does not prevent the rest of the integration case from being recorded |

## I1a, authority traffic feeds

The optional authority feeds are NSW, QLD, VIC, WA, SA and TAS. Run this case for the configured sources in the selected market and retain each source's status.

| Case | Steps | Expected outcome and evidence |
|---|---|---|
| Authority-feed corroboration and per-source disable behaviour | With the relevant authority sources enabled, trigger a bulletin and compare their records with HERE evidence. Repeat with one authority source switched OFF, then with a source left ON while its endpoint fails | Enabled sources contribute corroborating records where available. A source switched OFF reports **Skipped**. A source left ON that fails reports **Failed**, while HERE fallback carries the bulletin. Retain the per-source statuses, source-panel evidence and resulting script |

## I1b, emergency-services feed

Run this case whenever the emergency-services source is enabled and provisioned in the selected environment. If it is not available, record Blocked with the configuration evidence rather than treating the source as a successful integration.

| Case | Steps | Expected outcome and evidence |
|---|---|---|
| Console-only emergency records | Ingest a known emergency-services record, open the console's emergency-services panel and trigger a bulletin using the same market data | The known record appears in the console panel and is retained in the job evidence. Compare the resulting script and writer inputs with the emergency record to prove that the emergency record never reaches the script |

## I2, script generation with Azure OpenAI

| Case | Steps | Expected outcome and evidence |
|---|---|---|
| Complete bulletin generation | Trigger a bulletin using live station data | A draft is produced, deterministic content and slot-fit gates record their results, the single content-safety exit records its result, and the source panel shows the evidence used by the script |
| Content-safety term | Configure a prohibited term, then trigger a bulletin whose source data would use it | The term does not appear in an aired script and the content-safety review explains the decision |

## I3, speech synthesis with ElevenLabs

| Case | Steps | Expected outcome and evidence |
|---|---|---|
| Slot-length audio | Trigger a bulletin with a defined slot duration | The platform provides a WAV file at the configured duration and broadcast sample rate. The read sounds natural and is not artificially slowed |
| Voice and pronunciation | Use the voice and pronunciation cases in the [acceptance testing plan](uat-plan.md) | The assigned voice is used and configured respellings are heard in the audio |

The Azure Files playout path is live in TEST and available in production, using the shared `ibpauetststaftp01/informationbulletins` share. TEST carries new code while production is held frozen at the last known-good build. The current posture, verified 2026-09-03, is that no station is armed anywhere: both TEST stations generate with playout disabled and both production schedules are disabled. TEST is the environment that may arm `cairns-4ca` for an intentional delivery proof; `sydney` stays unarmed. Development can never deliver. Read the live schedule and **Send to playout** flags before each run; exactly one environment may have a station armed at a time, and restore the prior flags afterwards. The Function App writes the approved WAV with its managed identity; Zetta ingests it over SMB and deletes it, so disappearance is the delivery evidence and there is no on-air receipt.

| Case | Participant role | Steps | Expected outcome and evidence |
|---|---|---|---|
| Delivery from the armed environment | Acceptance operator | In TEST, with **Send to playout** ON for `cairns-4ca` and OFF for `sydney`, publish an approved current-slot bulletin after confirming no other environment arms the station | One file named for the station appears at the shared library root, then disappears as Zetta ingests it. Record the file appearance and disappearance; there is no on-air receipt to verify |
| Held-back station writes nothing | Acceptance operator | With **Send to playout** OFF, publish an approved bulletin | The bulletin reaches Published and its log records "Send to playout is off for this station". No file appears in the shared library |
| Unmapped market refuses | Acceptance operator | Publish an approved bulletin for any market other than the two pilot stations | Delivery fails loudly with "No Zetta market code is configured"; no file is written under a guessed name |
| Expired slot refuses | Platform support team | Attempt delivery for a slot whose deadline has passed | The delivery boundary refuses before downloading audio; nothing reaches the shared library |
| Handover between environments | Platform support team | Read the live flags, switch **Send to playout** OFF where the station is armed, verify it is off, then ON in the other environment and verify the new state. Restore the prior flags after the proof | Only the newly armed environment delivers for that station. Both are never armed at once: the file name is identical, so concurrent writes would race and there is no on-air receipt to audit it |


## I5, Microsoft Teams notifications

| Case | Steps | Expected outcome and evidence |
|---|---|---|
| Approval card | Trigger a bulletin and wait for Waiting approval | A card appears in the configured operations channel and its link opens the relevant console bulletin |
| Reminder and urgent escalation | Leave a bulletin unapproved into the notification window | One reminder card and one urgent card appear in the expected order |
| Failure card | Observe a controlled generation failure or a failed test bulletin | The failure card identifies the bulletin and gives a useful cause without exposing a secret |

## I6, Microsoft Entra ID access

| Case | Steps | Expected outcome and evidence |
|---|---|---|
| Operator sign-in | An ARN operator signs in to the test console | The operator sees operator surfaces only and cannot access administration actions |
| Administrator sign-in | An ARN administrator signs in | Administration navigation is present and the administrator can use the permitted station settings |
| No-role user | A user outside the assigned access groups signs in | The console shows that access is unavailable and protected API requests are denied |

## Sequencing and evidence

1. Rerun I1 to I3, I1a, I1b, I5 and I6 in the environment under test when each corresponding real service is available. If an optional source is not provisioned, record Blocked with the configuration evidence.
2. Run the I4 delivery and Zetta-ingest cases as an attended TEST proving exercise, arming `cairns-4ca` only for the duration of the proof, leaving `sydney` unarmed, and restoring the prior flags afterwards.
3. When an environment handover is required, run the I4 handover case: read the live flags, turn the source station OFF, verify it is off, turn it ON only in the destination environment, verify the new state, and restore the prior flags afterwards. Production remains unarmed unless ARN explicitly authorises a change; development cannot deliver.
4. Record every case result, including passes, in the copied acceptance template and link failures to the [defect register](defect-log.md).

A completed integration case includes the participant role, time, environment, bulletin reference, observed result and retained evidence. A failed case includes the operator-visible or listener-visible behaviour and the workaround used while it remains open.
