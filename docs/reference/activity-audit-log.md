---
title: "Activity / Change Audit Log"
description: "Cross-cutting middleware-based audit trail of admin/operator config changes: what it captures, the resource table, RBAC, retention, and known gaps"
tags:
  - "activity"
  - "audit"
  - "compliance"
  - "middleware"
  - "rbac"
aliases:
  - "activity log"
  - "change audit"
  - "who changed"
  - "audit trail"
purpose: "reference"
status: "current"
last_verified: 2026-09-02
review_after: 2026-11-07
volatility: "medium"
source_of_truth: "src/ArnBulletinGenerator.Functions/Middleware/ActivityLoggingMiddleware.cs, ActivityPathResolver.cs, ActivityHttpTrigger.cs, ActivityAuditQueueTrigger.cs; src/ArnBulletinGenerator.Infrastructure/Persistence/TableStorageActivityLogRepository.cs"
verified_by:
  - "./scripts/docs.py check"
related_code:
  - "src/ArnBulletinGenerator.Functions/Middleware/ActivityLoggingMiddleware.cs"
  - "src/ArnBulletinGenerator.Functions/Middleware/ActivityPathResolver.cs"
  - "src/ArnBulletinGenerator.Functions/Triggers/Http/ActivityHttpTrigger.cs"
  - "src/ArnBulletinGenerator.Infrastructure/Persistence/TableStorageActivityLogRepository.cs"
  - "src/ArnBulletinGenerator.Functions/Triggers/Queues/ActivityAuditQueueTrigger.cs"
  - "src/ArnBulletinGenerator.Functions/Triggers/Queues/ActivityAuditPoisonQueueTrigger.cs"
  - "src/ArnBulletinGenerator.Domain/Contracts/Messages/ActivityAuditMessage.cs"
  - "web/src/features/activity"
  - "web/src/pages/ActivityPage.tsx"
common_queries:
  - "How do I see who changed a station's config"
  - "What does the activity log capture"
  - "How is a change to VoiceProfileId or priority roads audited"
  - "Why does Voice have version history but Stations doesn't"
  - "Should every admin page show change history"
see_also:
  - "docs/canonical/decisions.md"
  - "docs/reference/per-station-configuration.md"
---

# Activity / Change Audit Log

An ARN operator asked for a way to see "who changed the VO in Adelaide" — who mutated a piece of
platform config, and when. Before this, `MarketSchedule` (station config, incl. `VoiceProfileId`)
carried no actor field at all, and even where an actor WAS captured (`PriorityRoad.UpdatedBy`) it
was last-write-wins with no history. Rather than instrument each of the ~20 admin HTTP triggers
individually, this is ONE cross-cutting middleware with zero per-trigger changes. See the
2026-07-07 decision entries (`docs/canonical/decisions.md`) for the full rationale, including the
same-day follow-up that extended it from Stations-only to every simple-CRUD admin surface.

## Not the same thing as Version History

Voice Profiles, Delivery Styles, Pronunciation Dictionaries, and Style Rules each carry their own
domain-level `Versions[]` / `CurrentVersion` / `RestoreVersion` and a "Version History" UI tab
(`VoiceVersionHistory.tsx` and siblings) — pre-existing, unrelated to this feature. That pattern
exists specifically for **shared library assets** referenced by many stations/markets at once,
where a bad edit fans out everywhere and being able to literally revert matters. The activity log
described here is a **read-only who/when trail with no restore**, applicable to every mutable
admin resource — including the shared-library ones (a voice profile's own creates/edits also show
up in `/activity?resource=voices/profiles`), but that is a DIFFERENT, complementary view, not a
replacement for its Version History tab.

## What it captures

`ActivityLoggingMiddleware` (`src/ArnBulletinGenerator.Functions/Middleware/ActivityLoggingMiddleware.cs`),
registered immediately after `AuthorizationMiddleware` in `Program.cs`, logs every request that is
**all** of:

- authenticated (a parsed `X-MS-CLIENT-PRINCIPAL`);
- a mutating verb (`PUT`/`POST`/`DELETE` — `GET`/`HEAD` short-circuit before any work);
- resolved to a known `(resource, entityId)` pair (see "Resolving the entity id" below);
- **successful** (2xx response). A rejected/failed request didn't change anything, so it is never
  logged — this is a change record, not an attempt log.

Captured per entry: UTC timestamp, actor name + AAD object id, HTTP method, absolute path, the
resolved resource + entity id, status code, and the submitted JSON request body (dropped, not
truncated, past 8,000 chars — table storage's 64 KB property cap divided across other columns;
truncating JSON mid-string would produce invalid JSON).

## Resolving the entity id

`ActivityPathResolver` is a static, ordered, first-match-wins regex table mapping
`HttpRequestData.Url.AbsolutePath` to a resource string (and an entity id when the **path** carries
one). It **fails closed**: a path matching no rule, or explicitly excluded, returns `null` and the
request is never logged — never guessed. Add a new row here whenever a new admin/operator route is
added; do not rely on a generic segment-index heuristic (existing routes are irregular — the entity
id is sometimes mid-path).

Several routes carry their natural key in the BODY, not the URL — a safety-term upsert
(`PUT /api/safety/terms`, no id/market segment at all) and every POST-create route (a brand-new
voice profile/delivery style/pronunciation dictionary/global guidance entry has no id yet when the
request lands). For these, `ActivityLoggingMiddleware` falls back to the **response body**: when
the path alone resolves no entity id, it parses the response JSON and takes the first non-empty
match of `ScopeId` (preferred — groups by market), then `Id`, then `MarketId`. The path-resolved id,
when present, is NEVER overridden by this fallback. `SafetyTermsHttpTrigger.Delete` and
`GuidanceHttpTrigger.DeleteGuidance` deliberately echo `Id`/`ScopeId` in their delete response
bodies so the same fallback resolves consistently for both their create/update AND delete routes.

| Resource | Example path | Entity id resolution |
|---|---|---|
| `schedules` | `/api/schedules/{market}` | market id, from the URL |
| `priority-roads` | `/api/markets/{market}/priority-roads` | market id, from the URL |
| `filler-statements` | `/api/markets/{market}/filler-statements` | market id, from the URL |
| `guidance` | `/api/markets/{market}/guidance`, `/api/guidance` | market id from the URL (market-scoped); the entry's own id via response `Id` fallback (Global scope — `ScopeId` is empty there) |
| `delivery-styles` | `/api/delivery-styles/{id}`, `/api/delivery-styles` (POST create) | style id, from the URL or response `Id` fallback on create |
| `pronunciation-dictionaries` | `/api/pronunciation-dictionaries/{id}`, `/api/pronunciation-dictionaries` (POST create) | dictionary id, from the URL or response `Id` fallback on create |
| `voices/profiles` | `/api/voices/profiles/{id}`, `/api/voices/profiles` (POST create) | profile id, from the URL or response `Id` fallback on create |
| `operators` | `/api/operators/{objectId}` | operator object id, from the URL |
| `safety-terms` | `/api/safety/terms`, `/api/safety/terms/{id}` (DELETE) | market id, via response `ScopeId` fallback for BOTH upsert and delete (never the term's own id — grouping by market is what admins actually browse; Global-scope rows fall through to the term's own `Id`) |
| `style-rules` | `/api/style-rules` | — (single global row, no per-item id) |
| `jobs` | `/api/jobs/{id}/approve`\|`reject`\|`publish`\|`regenerate` | job id, from the URL |
| `broadcast` | `/api/broadcast` | — |
| `config` | `/api/config/import` | — (whole-bundle action, not a single entity) |
| `config-imports` | `/api/config/imports/pending/{id}/approve`\|`reject` | import id, from the URL |

**Always excluded** (never logged, any method): any path ending `/preview`, `/remix`, `/calibrate`
(measurement/preview clicks, not persisted changes); `/api/synthesize`, `/api/health`,
`/api/GetRoles`, `/api/config/export`, `/api/config/schema`, `/api/config/imports/pending` (list),
and `/api/activity` itself (never audit-log reads of the audit log).

## Storage

`TableStorageActivityLogRepository` writes to a new Azure Table `activityLog`: `PartitionKey` is
the UTC date bucket (`yyyy-MM-dd`, same convention as `AuditExportTimerTrigger`), `RowKey` is the
inverted-ticks + GUID scheme for inline writes, or a deterministic SHA-256 idempotency suffix for
queue writes (ascending RowKey = newest-first **within** a partition). Reads walk date partitions
newest-first and apply bounded inverted-tick RowKey ranges, stopping when `limit` matching entries
have been collected; they do not materialize the complete multi-day range.

## Queued completion mode

`Audit:Queue:Enabled` is disabled by default, preserving the inline Table write. When enabled,
successful (2xx) mutating requests publish their same actor/path/status/entity/body metadata to the
durable `activity-audit` Storage Queue after the response is constructed. The
`ActivityAuditQueueProcessor` consumer records each message at least once using the idempotency key
`{requestId}|{actor}|{path}`, so redelivery upserts one row. Messages that exhaust retries remain
in `activity-audit-poison` and are logged and metered for operator action. Rejected or failed
requests are never enqueued.

## Reading it

`GET /api/activity` (Admin-only, enforced by `RouteRoleMatrix`) accepts `from`/`to` (ISO 8601,
default: last 180 days ending now), `resource`, `entityId`, and `limit` (default 200, clamped to
1–1000, never errors on an out-of-range value). Max range is 366 days.

Two shapes of web surface read it, both via `web/src/features/activity/`:

- **The global `/activity` page** (`ActivityPage.tsx`, sidebar "Activity", admin-only) — a resource
  `Select` + entity-id `Input` filter over the whole log. Consecutive same-entity entries are
  diffed (`diffable` on `ActivityFeed`) only once BOTH filters narrow to one entity — diffing
  across unrelated entities in the unfiltered feed would be meaningless.
- **`ScopedActivityHistory`** — a shared, collapsed-by-default disclosure (same visual shape as the
  Version History disclosures — a chevron + label, one-line description when collapsed) that always
  diffs, scoped to one fixed `resource`+`entityId` pair. Used in six places:
  - Per-station **"History" step** on both `ScheduleDetailStudio.tsx` (the read-only "view" panel)
    and `ScheduleStudio.tsx` (the actual editable "Edit station"/"New station" wizard, hidden while
    creating a new station) — the original ask, always expanded-as-a-step rather than a disclosure
    (a first-class view, not a rare escape hatch), matching how Voice/Delivery/Pronunciation embed
    their own History step directly in their single edit wizard rather than a separate view-only
    surface. Both call `ActivityFeed` directly rather than wrapping it in `ScopedActivityHistory` —
    they're already a dedicated step, not an inline disclosure below other content.
    Since 2026-08-06 the studios are reachable by Operators (the `/stations` route
    gate fell), so both gate the History step on `useIsAdmin` — the underlying
    `GET /api/activity` is Admin-only, and an operator would otherwise see a 403
    rendered as an empty "No activity recorded".
  - **Priority Roads / Filler Statements managers** — shown only when the market filter narrows to
    one market (both are naturally per-market resources; "all markets" has no single entity id).
  - **Safety Terms manager** — shown only for a specific market (not "all scopes", not "Global" —
    Global-scope terms don't share one entity id, see the resource table above).
  - **Guidance inventory** — same rule: shown only for a specific market, not "all scopes" or the
    Global group.
  - **Operator cards** — one disclosure per card, keyed by the operator's object id.

  Both `ScheduleDetailStudio` and `ScheduleStudio` use `useCoarseNow` (not `Date.now()` directly) to
  compute their 366-day range as a pure render read, satisfying `react-hooks/purity`; the query
  itself is gated on `open` (and, for `ScheduleStudio`, on an existing `schedule` — a brand-new
  station has no history yet) so mounting several history views on one page (e.g. every operator
  card) doesn't fan out N eager queries.

Both diffing paths reuse the shared `diffEntities`/`EntityDiffView` engine
(`web/src/lib/entity-diff.ts`) — the same engine used by config-transfer and Style Rules — to render
field-level before/after diffs between an entry's submitted body and the immediately older entry
for the same resource+entity.

## Known gaps (accepted, not bugs)

- **Local/dev calls under `Auth__DevFallbackRole` produce no activity entries.** That bypass
  synthesizes a dev identity with no `X-MS-CLIENT-PRINCIPAL` header at all, and
  `ActivityLoggingMiddleware` deliberately does not duplicate `AuthorizationMiddleware`'s
  dev-identity synthesis (would duplicate security-sensitive parsing across two files for a
  dev-only convenience). Real AAD-authenticated calls — test, prod, and dev once a real session is
  used — always carry the header and are logged normally.
- **Diffs show raw GUIDs, not resolved display names** (e.g. a `VoiceProfileId` change shows two
  GUIDs, not two voice names). Resolving them needs the frontend to cross-reference the
  already-fetched voice/style/dictionary lists at render time — not implemented; a candidate
  follow-up if operators find raw GUIDs unreadable in practice.
- **Only successful (2xx) mutations are logged.** A rejected/failed request is never recorded —
  by design (see "What it captures" above), not a gap, but worth knowing if a "why didn't my
  rejected PUT show up" question comes up.
- **Global-scope Guidance/Safety Terms have no single "browse all Global changes" view.** Each
  Global-scope row resolves its OWN entity id (via the response `Id` fallback), which is correct
  for "history of this one global directive/term" but means there's no single `entityId` value
  that groups "every Global change" the way a market id groups a market's changes. The unfiltered
  `/activity?resource=guidance` (or `safety-terms`) page shows everything, market and Global mixed.
