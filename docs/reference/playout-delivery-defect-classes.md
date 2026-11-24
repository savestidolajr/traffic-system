---
title: "Playout Delivery Defect Classes"
description: "The five recurring defect classes behind the 2026-08-11 dead-air findings: a status that changed while the file did not move, a stale revision passed to a guarded call, a promise nothing keeps, a null that disables a safety net, and an action the UI offers that the domain refuses. Each with the audited instances, what was fixed, and what remains open."
tags:
  - "delivery"
  - "playout"
  - "fallback"
  - "audit"
  - "dead-air"
aliases:
  - "dead air classes"
  - "delivery defect classes"
  - "why did the slot air nothing"
purpose: "reference"
status: "current"
last_verified: 2026-08-12
review_after: 2026-09-10
volatility: "medium"
source_of_truth: "Source audit of src/ and web/src on 2026-08-11, plus deployed containment verification on 2026-08-12"
verified_by:
  - "five read-only source audits, one per class"
  - "dev verification: sha256(drop file) == sha256(chosen audio) across five delivery paths"
  - "test verification: publish completed with delivery disabled before audio download or writer invocation"
related_code:
  - "src/ArnBulletinGenerator.Application/Jobs/JobService.cs"
  - "src/ArnBulletinGenerator.Application/Scheduling/JobWatchdog.cs"
  - "src/ArnBulletinGenerator.Application/Broadcasting/BroadcastOrchestrator.cs"
  - "web/src/features/onair/lib/air-plan.ts"
common_queries:
  - "Why did a covered slot air nothing?"
  - "Which paths deliver audio to playout?"
  - "What does an ad-hoc bulletin not get?"
  - "Why did approve not send the bulletin?"

# Playout Delivery Defect Classes

Five classes, each found by a defect that reached dev. They recur because the platform
separates a job's RECORD from the file that actually airs, and every seam between the two is
somewhere a status can change while nothing reaches the station.

Read this before touching the fallback chain, the deadline sweep, delivery, or any operator
action that changes what airs.

## A. A status changed while the file did not move

`JobService.PublishJobAsync` is the ONLY caller of `IFileDeliveryService.DeliverAsync`.
Anything that "covers a slot" without ending in a publish has changed a record and nothing
else. Three separate paths shipped this bug independently:

| Path | Symptom | State |
|---|---|---|
| `JobWatchdog.ProcessFallbacksAsync` | Deadline sweep marked `FallbackUsed`, drop folder stayed empty | Fixed 2026-08-11, proven on dev |
| `BroadcastOrchestrator.HandleUnairableAsync` | Content-limited and quality-gate slots "covered", never delivered, and unrecoverable because the sweep skips `FallbackUsed` | Fixed 2026-08-11 |
| `JobService.RejectToFallbackAsync` | Operator chose "air the fallback"; status moved, file did not | Fixed 2026-08-11 |

**The rule now:** every path that covers a slot finishes through
`IJobService.DeliverCoveredSlotAsync(jobId, what, by)`. It re-reads, signs off, re-reads,
publishes, and writes the outcome to the job's own log either way. Do not write a fourth
copy of that sequence — call it.

**Guard when adding a path:** if your code calls `TryApplyFallbackAsync` or
`ApplyFillerCartAsync`, the next thing it does must be `DeliverCoveredSlotAsync`.

### Age boundary: coverage expires with the slot

The delivery rule above has a hard upper bound. `MarkMissedExpiredSlotsAsync` runs before the
fallback sweep, and `ProcessFallbacksAsync` independently refuses a job once
`FallbackDeadlineUtc + MissedAfterDeadlineMinutes` has passed. Approval does not override this
boundary. This guard was added after a production restart on 2026-08-11 delivered 15 jobs
8-10 hours after their slots.

## B. A stale revision passed to a revision-guarded call

`TableStorageJobRepository.SaveJobAsync` does NOT write the new ETag back onto the instance
you passed it. So the moment you save a job, the reference you are holding is stale, and
handing its `ETag` to `ApproveJobAsync`/`PublishJobAsync`/`DeleteJobAsync` fails the
revision guard.

This is what made the first fix to class A silently do nothing: approve was refused with
"the bulletin changed after you loaded it", and only App Insights knew.

**The rule:** re-read between a save and any guarded call. `DeliverCoveredSlotAsync` does it
twice, deliberately (apply bumps the revision, then approve bumps it again).

**Test shape that catches it:** model storage as authoritative — the re-read stub must hand
back a DISTINCT instance carrying the advanced ETag. A stub that mutates the caller's own
object passes with the bug present (verified: it did).

## C. A promise nothing keeps

User-visible copy that asserts an automatic future action, where the code can only deliver it
under conditions the copy does not state.

The instance: the on-air board said an approved bulletin "sends at the cutoff". True for a
poller-created job; false after the cutoff (it sends within a minute of the next sweep tick);
and false for an ad-hoc bulletin, where no deadline exists and nothing will ever send it. The
board now says `Sends 21:12` / `Sending now` / `Approved` + "publish it to send it".

**The rule:** name the moment, not the mechanism. If the mechanism only applies to some jobs,
the copy must branch on the same condition the code does.

**Never claimable:** that a bulletin AIRED. Zetta returns no receipt; it deletes the drop file
on ingest. "Delivered to the drop" and "collected by playout" are the strongest true claims.

## D. A null that disables a safety net

A Table Storage filter comparing a NULL property never matches, so an unset optional field
silently removes a job from a sweep rather than failing loudly.

`FallbackDeadlineUtc` is the load-bearing one — `ListJobsNeedingFallbackAsync` keys on it, and
it is written ONLY by `SchedulePollerService`. Consequences found:

- **Ad-hoc bulletins** (manual `POST /api/broadcast`) get no deadline auto-delivery, no
  same-day repeat, no standby cart, no no-fallback alert, and no approval escalation
  (`EscalateOverdueApprovalsAsync` filters on `ScheduledForUtc.HasValue`). They rely entirely
  on a human pressing Publish. **Open** — acceptable only while ad-hoc means "an operator is
  driving", and the console must never imply otherwise.
- **Regeneration used to null it** (`Job.RequestRegeneration`), so a rejected-then-regenerated
  SCHEDULED bulletin dropped out of the sweep while the board still promised delivery. Fixed
  2026-08-11: the deadline belongs to the slot, not the attempt, and an in-flight worker is
  protected by `IsActivelyGenerating` instead.
- **`ApplyFillerFallback` nulls `AudioSha256`**, so `ZettaFileDeliveryService`'s integrity
  re-hash is skipped for cart deliveries. **Open** (MED): the cart is our own audio, but the
  check silently does not run.

## E. The UI offers an action the domain refuses

Audited as a matrix of every console action against every job status. All mismatches were in
the offered-but-refused direction; nothing the backend accepts is hidden.

- **Air-source override on a delivered slot** — the board's own "Air this" is enabled on a row
  labelled `In playout`, and `ApplyFallback` refuses a terminal job: 409 every time. **Open**;
  either gate the button on the slot job's status or model a late swap properly (it needs the
  tray state, since a swap is only safe while our file is still in the drop).
- **Delete bulletin** offered on 8 statuses `IsDeletable` refuses, after a "cannot be undone"
  confirm. **Open** (MED).
- **The `R` key** opens the revise/reject dialog with no status guard (the `A` key is guarded),
  so its Reject and "use fallback audio" buttons are live on jobs the backend will refuse.
  **Open** (MED).
- `ApplyFallback`'s guard blocks only `Published`/`FallbackUsed` while its doc claims "any
  terminal state": `Failed` and `Missed` slip through. Widen the guard together with the UI
  gate, not before.

## How to audit for these again

One read-only pass per class, each with its own paths, is what surfaced these; the classes are
disjoint enough that five parallel passes do not duplicate work. The prompts are recorded in
`docs/canonical/decisions.md` (2026-08-11). Findings must cite `path:line`, state the
observable consequence for an operator or a listener, and name the smallest fix — a finding
without a citation cost more to check than it saved.
