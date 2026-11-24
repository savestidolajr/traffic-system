---
title: "Why the Platform Works This Way"
description: "The design choices that shape how the platform must be operated, and the reasoning behind each."
tags:
  - "architecture"
  - "decisions"
aliases:
  - "why"
  - "design decisions"
  - "rationale"
  - "why single region"
  - "why human approval"
purpose: "reference"
status: "current"
last_verified: 2026-09-01
review_after: 2026-12-01
volatility: "medium"
source_of_truth: "The deployed platform"
verified_by:
  - "manual review"
common_queries:
  - "Why does a person have to approve every bulletin?"
  - "Why is there only one region?"
  - "Why these vendors?"
see_also:
  - "docs/operations/solution-overview.md"
  - "docs/operations/known-limitations.md"
  - "docs/operations/dependencies.md"
---

# Why the Platform Works This Way

These are the operating decisions that shape the ARN Information Bulletin
Platform. They explain why a control exists and what an ARN technology person
must preserve when changing configuration, access or infrastructure. The
[solution overview](solution-overview.md) describes the resulting components;
this page explains the reasoning behind their boundaries.

### Why does a person have to approve every bulletin?

Every generated bulletin requires an authenticated operator to approve it
before fresh audio is delivered to Zetta. The approval records the person's
identity and the time of the decision. Deterministic slot fitting and content
gates check the workflow result against the target slot and supplied evidence.
One content-safety exit sends an unsafe or unsuccessful result to fallback, but
these controls cannot publish a fresh bulletin.

Keep a staffed review path for every scheduled slot. If an operator does not
approve a generated bulletin in time, the platform follows the fallback rules
rather than bypassing the control. Do not grant an account broader permissions
to avoid a review delay. The [operator guide](operator-guide.md) describes the
decision surface and the [support model](support-model.md) describes
escalation.

### Why does the fallback contain two steps?

The platform uses the most recent approved bulletin from the same day for the
same station as its first fallback. If no such bulletin exists, it uses
pre-recorded standby audio. The sequence is deliberately short: the first
option retains current station context, while the second covers the slot
without presenting old traffic as current. Where neither can be applied the
slot stays uncovered and needs operator intervention.

Keep the same-day boundary intact when investigating a fallback. A bulletin
from a previous day is not a safe substitute for live conditions. Confirm that
standby audio is present and playable for each station, then use the
[runbook](runbook.md) to trace why the normal path did not complete.

### Why does the platform use HERE, Azure OpenAI and ElevenLabs?

Each provider has one defined responsibility. HERE supplies traffic, weather
and fuel information. One conversational workflow backed by one centralized
Azure OpenAI model drafts the script in the station's editorial style, and
ElevenLabs synthesises the gated script into the station's voice. Keeping those
responsibilities explicit makes the source, language and audio boundaries easy
to audit.

There is no automatic second provider for any of those three responsibilities.
Substituting a different source, language service or voice service would
change data meaning, editorial behaviour or audio characteristics at the
moment an operator needs a predictable result. Monitor each dependency and
use the platform fallback when one is unavailable. Rotation, credential
ownership and vendor escalation are covered in [external service
dependencies](dependencies.md).

### Why is traffic data fetched close to playout?

The platform obtains source data when a bulletin starts, rather than relying
on a long-lived copy attached to the schedule. Traffic can change between
slots, and a cached incident can remain visible after a road has reopened.
Fetching close to playout gives the operator a current evidence set for the
approval decision.

When a bulletin is delayed, check the source timestamp and freshness result
before treating the script as current. Do not manually reuse an older
incident list to make generation faster. If the source cannot provide data
within the safe window, the fallback is safer than an apparently current
script built from stale facts.

### Why are station settings resolved when a bulletin starts?

Station settings are read at broadcast time, not copied into a future
scheduled request. This lets an authorised administrator change a schedule,
target duration, source selection, voice, delivery style or pronunciation
assignment for the next eligible bulletin without rebuilding the platform.
The active settings are captured with the bulletin so the approved result
remains understandable after a later change.

Apply configuration changes before the next slot that should use them, and
confirm the effective station assignment in the console. Do not expect a
change to rewrite a bulletin already under review or alter an audio file
already approved. The [per-station configuration guide](../reference/per-station-configuration.md)
explains the supported settings and their scope.

### Why are station settings versioned and append-only?

Voice, delivery style and pronunciation rules are reusable station libraries,
and each edit creates a new version. Earlier versions remain available rather
than being overwritten. This preserves the settings used for an approved
bulletin and gives an administrator a known point to restore when a new
version produces an unwanted result.

Publish a new version for a change, assign it to the intended station and
verify it with a preview or the next review surface. Do not delete or mutate a
version that an existing bulletin references. When an issue appears, return
the station assignment to a known earlier version and record the reason in
the console activity history.

### Why is audio served through an authenticated proxy?

In test and production the console requests audio through an authenticated
platform endpoint. The endpoint checks the caller's identity and station scope
before streaming the recording, instead of exposing a direct storage link. This
keeps access control in one place and gives monitoring a consistent record of
playback requests. Development serves audio directly, because it holds no
broadcast content.

Use the console for playback and treat a playback failure as an access,
network, storage or endpoint issue to diagnose. Do not copy a storage URL
into a message or make a recording public to work around a failed request.
Check Entra ID, private network resolution, the endpoint response and the
relevant [monitoring workbooks](dashboard-guide.md) in that order.

### Why does the platform never invent traffic content?

Deterministic content gates compare claims in the workflow result with the
source information provided for that bulletin, while slot fitting checks the
result against the target duration. A statement about a road, incident, delay,
closure, weather condition or fuel fact must have a matching source basis.
When the supplied data is insufficient, an unsafe or unsuccessful result takes
the single content-safety exit to fallback. The platform is designed to leave
out an unsupported fact, not to make a plausible sentence fill the slot.

Operators should correct a source selection or wording through the console,
not add an unverified fact to make a script sound fuller. If HERE lacks the
detail needed for a safe statement, leave it out and allow the next supported
fact to take its place. This rule is also why a content-limited result can
legitimately use standby audio.

### Why are secrets managed-identity only?

Azure resources communicate using managed identity and role assignments. The
Function App obtains access to storage, Key Vault and Azure AI services
without a storage, database or vendor connection string in application
settings. External provider keys are held in the Key Vault for the relevant
environment and are read at runtime.

Grant the required data and secret permissions to the environment identity,
then rotate provider keys in Key Vault without placing them in tickets,
scripts or chat. A new integration must use the same identity and secret
boundary. If access fails, inspect the identity role assignment, Key Vault
access logs and private endpoint path before changing application code.

### Why does the scheduler poll instead of waiting for events?

Broadcast slots are driven by station clocks, while traffic providers do not
emit one dependable event for every station's next bulletin. The scheduler
therefore checks the configured schedules at regular intervals, starts due
work and records missed-window information. This makes the schedule the
authoritative trigger and keeps timing predictable when external data changes.

Keep the scheduler running and monitor its heartbeat and missed-window
signals. A provider webhook is not a substitute for a station schedule.
Use a manual trigger only for an explicit operational recovery or check, and
confirm that it targets the intended station and slot before approval.

### Why are there only Operator and Admin roles?

The platform uses two roles with a clear separation. An Operator can read
station bulletins and perform the review actions required to approve, reject
or regenerate them. An Admin can also change schedules, station settings and
other operational configuration. Station scoping limits each operator to the
stations assigned to that person.

Assign the least powerful role that supports a person's duties. Use Admin for
configuration ownership, not for routine review convenience. When a person
changes responsibilities, update the role or station scope and confirm that
the old access no longer appears in the console. The [access and accounts
page](access-and-accounts.md) covers the sign-in and role process.

### Why is production in one Azure region without automatic failover?

Development, test and production run in the dedicated ARN Azure subscription,
with compute and the data plane in Australia East. A single region keeps the
network path to ARN services, the Zetta destination and private endpoints
straightforward, and it keeps the environment model easy to operate. Automatic
cross-region failover is not configured for the platform.

Treat a regional outage as a recovery event, not as an automatic transition.
Use the fallback audio and the [disaster recovery
page](disaster-recovery.md) while the subscription and network owners assess
the service. The [known limitations](known-limitations.md) page records this
boundary and the other conditions that affect capacity or continuity.
