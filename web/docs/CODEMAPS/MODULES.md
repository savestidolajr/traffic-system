# Module Map

Last verified 2026-08-07.

## Shared Library (`src/lib/`)

| File                          | Purpose                                                                                                                                                                                                                                                                         |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `auth.ts`                     | Centralized SWA `.auth/me` + VITE_API_TOKEN (dev) parsing (object + array formats), `useAuthData()` TanStack Query hook. Single cache entry shared by all callers.                                                                                                              |
| `api/client.ts`               | Axios instance. Request interceptor attaches an optional dev `VITE_API_TOKEN` only — prod auth rides the SWA-injected `X-MS-CLIENT-PRINCIPAL` (no Bearer, no `/.auth/me` fetch). Response interceptor wraps errors in `ApiError`. Exports `ApiError` for retry-logic consumers. |
| `api/use-mutation-factory.ts` | Mutation factory with simple and optimistic modes. No global toast.                                                                                                                                                                                                             |
| `api/use-query-factory.ts`    | Query factory building TanStack Query options with structured keys, + `useInfiniteQueryFactory` for cursor-based "Load older" pagination (Activity, Jobs history).                                                                                                              |
| `api/mock/`                   | Axios mock adapter for dev mode (`VITE_USE_MOCKS=true`).                                                                                                                                                                                                                        |
| `api/create-query-keys.ts`    | Helper to build structured query keys.                                                                                                                                                                                                                                          |
| `api/schemas/shared.ts`       | Shared Zod schemas (voice settings, coverage shapes, voice rotation / VoiceSelectionMode / VoiceWindow).                                                                                                                                                                        |
| `formatting.ts`               | Value formatting helpers (dates, numbers, units).                                                                                                                                                                                                                               |
| `api-utils.ts`                | Shared API utility functions.                                                                                                                                                                                                                                                   |
| `routes.ts`                   | Route path constants.                                                                                                                                                                                                                                                           |
| `toast-action.ts`             | Wraps a promise in success/error sonner toasts. Used by feature hooks, not by mutation factory.                                                                                                                                                                                 |
| `utils.ts`                    | General-purpose utilities (cn class merger).                                                                                                                                                                                                                                    |
| `layout.ts`                   | Shared grid/layout constants (CARD_GRID).                                                                                                                                                                                                                                       |
| `dom.ts`                      | DOM helpers.                                                                                                                                                                                                                                                                    |
| `table-sort.ts`               | `SortState<TKey>` + `toggleSort()` — generic column-sort state shared by every sortable data table.                                                                                                                                                                             |
| `json.ts`                     | `isRecord` / case-insensitive `getProp` / `tryParseJson` — generic JSON-shape guards.                                                                                                                                                                                           |
| `constants/timing.ts`         | Shared timing constants.                                                                                                                                                                                                                                                        |
| `design/`                     | Micro-viz primitives: BudgetBar, FreshnessDot, SourceGlyphs; icons.ts registry.                                                                                                                                                                                                 |

## Hooks (`src/hooks/`)

| File                    | Purpose                                                                                                  |
| ----------------------- | -------------------------------------------------------------------------------------------------------- |
| `use-current-user.ts`   | Consumes `useAuthData()` from `auth.ts`. Exports `useCurrentUser`, `useRoles`, `useIsAdmin`.             |
| `use-confirm-dialog.ts` | Confirmation dialog state hook.                                                                          |
| `use-view-mode.ts`      | Cards \| Table view-mode state, persisted per page in localStorage (pairs with `components/ViewToggle`). |
| `use-document-title.ts` | Sets `document.title` reactively.                                                                        |
| `use-mobile.ts`         | Mobile viewport detection.                                                                               |
| `use-global-keydown.ts` | Global keyboard handler.                                                                                 |
| `use-debounce.ts`       | Debounce hook.                                                                                           |
| `use-second-tick.ts`    | 1 s ticker for countdowns.                                                                               |

## Features

### `features/activity/`

Activity / change audit log: read-only timeline of every authenticated, successful, mutating admin/operator request, with per-entity filtering and structural before→after diffs.

| Sub-module                     | Purpose                                                                                                                                                      |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `api/activity-hooks.ts`        | TanStack Query fetch: `useActivityLog` (embedded audit-rail widgets) + `useActivityLogHistory` (infinite "Load older" hook backing the Activity page table). |
| `api/activity-schemas.ts`      | Zod schemas (ActivityLogEntry wire → camelCase).                                                                                                             |
| `components/ActivityFeed.tsx`  | Read-only change/access-audit timeline (reuses `entity-diff` engine); powers the embedded audit-rail widgets.                                                |
| `components/ActivityTable.tsx` | Sortable data table for the Activity page: column sort (time/actor/resource), "Load older" paging, per-entity diff rows.                                     |

### `features/config-transfer/`

Config bundle export/import: multi-section config transfer between environments with diff review and second-approver workflow (admin).

| Module                                | Purpose                                               |
| ------------------------------------- | ----------------------------------------------------- |
| `components/ExportPanel.tsx`          | Section-picker bento grid → download config bundle    |
| `components/ImportPanel.tsx`          | Upload config bundle with section selection           |
| `components/ImportReviewTabs.tsx`     | Per-section diff review before applying import        |
| `components/PendingApprovalsCard.tsx` | Second-approver pending imports queue                 |
| `api/config-transfer-hooks.ts`        | TanStack Query hooks for export/import/schema/preview |
| `api/config-transfer-schemas.ts`      | Zod schemas for transfer API payloads                 |
| `lib/section-meta.ts`                 | Config section labels, icons, and ordering            |
| `lib/action-badge.ts`                 | Import-action badge variants                          |

### `features/delivery/`

Delivery-style gallery + studio: the shared library of how a station's bulletins are read aloud, a sibling of Voices. Each style defines a tone + curated ElevenLabs v3 audio-tag palette the director may use; styles are assigned per-station in schedules.

| Sub-module                                  | Purpose                                                                                        |
| ------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| `types.ts`                                  | DeliveryStyle, DeliveryStylePolicy, DeliveryStyleVersion, Create/Update/Preview request types. |
| `cues.ts`                                   | Permitted v3 audio cue catalogue.                                                              |
| `strength.ts`                               | Delivery strength range helpers.                                                               |
| `presets.ts`                                | Preset delivery-style policies.                                                                |
| `api/delivery-style-hooks.ts`               | TanStack Query CRUD + preview hooks for delivery styles.                                       |
| `api/delivery-schemas.ts`                   | Zod schemas for delivery-style API payloads.                                                   |
| `lib/assignments.ts`                        | Style ↔ schedule assignment resolution.                                                        |
| `lib/delivery-form.ts`                      | React Hook Form / Zod schema for the delivery studio.                                          |
| `components/DeliveryStyleCard.tsx`          | Gallery card (version badge, archive toggle).                                                  |
| `components/DeliveryStyleTable.tsx`         | Table view of the gallery (shadcn Table; row→studio, kebab mirrors the card).                  |
| `components/DeliveryStyleStudio.tsx`        | Create/edit studio: policy fields, cue picker, live audition.                                  |
| `components/AuditionPanel.tsx`              | Live preview of an unsaved policy via ElevenLabs.                                              |
| `components/PermittedCueSelector.tsx`       | Chip toggle for allowed audio cues.                                                            |
| `components/StrengthControl.tsx`            | Intensity slider.                                                                              |
| `components/VersionHistory.tsx`             | Version timeline + diff.                                                                       |
| `components/LiveEditConfirmationDialog.tsx` | Guard for unsaved live edits.                                                                  |

### `features/pronunciation/`

Pronunciation dictionary gallery + studio: per-station alias and phoneme-rule library for ElevenLabs TTS. Dictionaries are versioned (rule changes mint new versions) and assigned per-station in schedules.

| Sub-module                                     | Purpose                                                                                                     |
| ---------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| `types.ts`                                     | Dictionary, DictionaryVersion, Rule, RuleInput, Create/Update request types + the per-rule preview request. |
| `api/pronunciation-hooks.ts`                   | TanStack Query CRUD + the per-rule preview hook.                                                            |
| `api/pronunciation-schemas.ts`                 | Zod schemas for dictionary API payloads.                                                                    |
| `lib/pronunciation-form.ts`                    | React Hook Form / Zod schema for the dictionary studio.                                                     |
| `lib/assignments.ts`                           | Dictionary ↔ schedule assignment resolution.                                                                |
| `components/PronunciationDictionaryCard.tsx`   | Gallery card.                                                                                               |
| `components/PronunciationDictionaryTable.tsx`  | Table view of the gallery (shadcn Table; row→studio, kebab mirrors the card).                               |
| `components/PronunciationDictionaryStudio.tsx` | Create/edit studio: rules editor + version history.                                                         |
| `components/PronunciationRuleTable.tsx`        | Alias / phoneme rule editor (add, edit, delete rows; per-row audition in the chosen preview voice).         |
| `components/VersionHistory.tsx`                | Version timeline.                                                                                           |

### `features/guidance/`

Standing-guidance authoring and future-lesson governance in the Bulletin-rules hub. Operators maintain permitted market/station guidance; administrators inspect, promote or dismiss explicit station lessons taught during bulletin rejection.

| Sub-module                          | Purpose                                                                     |
| ----------------------------------- | --------------------------------------------------------------------------- |
| `api/guidance-hooks.ts`             | TanStack Query CRUD plus future-lesson list, promotion and dismissal hooks. |
| `api/schemas.ts`                    | Zod schemas for guidance and future-lesson payloads.                        |
| `lib/display.ts`                    | Display label constants + helpers.                                          |
| `lib/inventory.ts`                  | Filtering, grouping, and scope sentinel logic for the guidance list.        |
| `components/GuidanceComposer.tsx`   | Inline add/edit/promote composer (scope · type · directive).                |
| `components/GuidanceInventory.tsx`  | Scope-grouped guidance list with filter bar.                                |
| `components/GuidanceRow.tsx`        | Single guidance row (edit / delete / promote actions).                      |
| `components/FutureLessonsPanel.tsx` | Admin queue showing lesson provenance, expiry and promote/dismiss actions.  |
| `components/MarketDetailPanel.tsx`  | Per-market guidance detail view.                                            |

### `features/home/`

Operator landing dashboard: at-a-glance status cards for recent bulletin outcomes, next scheduled slots, and waiting approvals.

| Module                        | Purpose                                         |
| ----------------------------- | ----------------------------------------------- |
| `components/OutcomesCard.tsx` | Recent bulletin outcomes with status chips      |
| `components/UpNextCard.tsx`   | Next scheduled slots across stations            |
| `components/WaitingCard.tsx`  | Waiting-on-human bulletins with live countdowns |
| `lib/use-home-ops.ts`         | Data aggregation hook for the landing dashboard |

### `features/safety/`

Banned-words admin: the safety tab of the Bulletin-rules hub. Operators manage per-scope (global or per-market) banned terms that the pipeline must avoid.

| Sub-module                          | Purpose                                                                                                                 |
| ----------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| `api/safety-hooks.ts`               | TanStack Query CRUD hooks for safety terms.                                                                             |
| `api/schemas.ts`                    | Zod schemas (SafetyTerm, SafetyKindName, SafetyScopeName).                                                              |
| `components/SafetyTermsManager.tsx` | Scope-grouped banned-word list + on-demand composer (uses shared RuleComposer, RuleToolbar, ScopeGroupedList, RuleTab). |

### `features/jobs/`

Bulletin lifecycle: review cockpit, adjust board, broadcast history, and the full approve / reject / regenerate / publish workflow.

| Sub-module                            | Purpose                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| ------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `lib/incidents/coverage-grouping.ts`  | Coverage region grouping.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `lib/incidents/incident-editing.ts`   | Incident editing helpers.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `lib/incidents/incident-labels.ts`    | Incident classification labels.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `lib/incidents/road-groups.ts`        | Road grouping helpers.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `lib/job-meta/earlier-today.ts`       | "Earlier today" continuity helpers.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| `lib/job-meta/get-actor-name.ts`      | Pure function: returns `item.by` or `'System'` fallback for status-history actors.                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| `lib/job-meta/job-label.ts`           | Job display-label helpers.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `lib/job-meta/urgency.ts`             | Urgency level computation.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `lib/map/evidence-points.ts`          | Map evidence-point helpers.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `lib/map/map-links.ts`                | Google Maps traffic-view link helpers (per-incident "Open traffic view in Google Maps").                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| `lib/verification/claims.ts`          | Claim extraction.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| `lib/verification/provenance.ts`      | Provenance claim helpers.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `lib/verification/sentence-status.ts` | Per-sentence status.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| `lib/verification/spoken-numbers.ts`  | Spoken-number normalisation (cardinal 1-99 incl. compounds) — feeds the claims delay-contradiction check.                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `lib/media-url.ts`                    | Media URL helpers.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| `lib/pronunciation-matches.ts`        | Resolves the job's pinned provider dictionary version and finds model-aware, case/word-boundary-safe written-to-spoken matches without mutating canonical script text.                                                                                                                                                                                                                                                                                                                                                      |
| `lib/weather-tone.ts`                 | Weather tone helpers.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| `hooks/use-bulletin-review.ts`        | Bulletin review workflow state (playback, approve, reject, regenerate).                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `hooks/use-job-actions.ts`            | Wraps approve / reject / publish / regenerate / delete mutations with per-action `toastAction`.                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `hooks/use-script-verification.ts`    | Script verification state.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `hooks/use-waiting-badge.ts`          | Waiting-job badge polling.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `hooks/use-word-timings.ts`           | Word-timing rescale for karaoke sync.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| `api/job-hooks.ts`                    | TanStack Query mutations for job CRUD + lifecycle, + `useJobsHistory` (infinite "Load older" hook backing the History page table).                                                                                                                                                                                                                                                                                                                                                                                          |
| `components/`                         | Review cockpit (`review/`, including sentence-line native editing, previewed **Ask for a change**, exact-version pronunciation glows/popovers, and live edit mirroring), Adjust rundown (`review/adjust/` — unified incident + good-run RundownTable), broadcast log (`history/` — sortable table, day-grouped at default sort), deep-dive tabs (`canvas/`, including exact pronunciation provenance), DeliveryPanel (tagged-read disclosure), queued-change Revise decision surface, audio waveform, script renderer, map. |

### `features/schedules/`

Station schedule management: cron scheduling, coverage, priority roads, good-run roads, voice rotation (single / round-robin / time-of-day), speech settings (LLM model + delivery style + pronunciation dictionary), and alert timings.

| Sub-module                                       | Purpose                                                                                                                                                                                                                                                |
| ------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `lib/date-math.ts`                               | Pure cron date-math helpers: `CronField`, `TzDateParts`, `firstValue`, `daysInMonth`, `toLocalMinutes`, `tzDateFrom`.                                                                                                                                  |
| `lib/cron-calendar.ts`                           | Calendar-based cron expression builder.                                                                                                                                                                                                                |
| `lib/cron-occurrences.ts`                        | Computes next N cron occurrences using `date-math` helpers.                                                                                                                                                                                            |
| `lib/cron-format.ts`                             | cronstrue → abbreviated human-readable text for narrow card rows.                                                                                                                                                                                      |
| `lib/schedule-utils.ts`                          | Schedule display/formatting utilities.                                                                                                                                                                                                                 |
| `lib/schedule-studio-helpers.ts`                 | Studio form helpers.                                                                                                                                                                                                                                   |
| `lib/voice-colors.ts`                            | Deterministic colour palette for voice rotation UI (timeline, legend, glyph).                                                                                                                                                                          |
| `hooks/use-schedule-actions.ts`                  | Update / delete schedule actions with per-action `toastAction`.                                                                                                                                                                                        |
| `api/schedule-hooks.ts`                          | TanStack Query mutations for schedule CRUD.                                                                                                                                                                                                            |
| `api/priority-roads-hooks.ts`                    | Priority-roads query / mutation hooks.                                                                                                                                                                                                                 |
| `components/VoiceRotationEditor.tsx`             | Per-station voice selection mode: Single (one voice), Rotate (cycle ordered set), By-time-of-day (local-hour windows → voices).                                                                                                                        |
| `components/VoiceTimeline.tsx`                   | 24-hour drag-to-resize voice window timeline (coloured segments, midnight-wrap, gap defaults).                                                                                                                                                         |
| `components/VoiceModeGlyph.tsx`                  | Compact at-a-glance voice indicator for station cards (mic / repeat + count / clock + mini day-bar).                                                                                                                                                   |
| `components/DeliveryStyleSelector.tsx`           | Delivery-style picker (linked to delivery feature).                                                                                                                                                                                                    |
| `components/PronunciationDictionarySelector.tsx` | Dictionary picker (linked to pronunciation feature).                                                                                                                                                                                                   |
| `components/ScheduleStudio.tsx`                  | Create / edit schedule studio (sections: BasicInfo, Scheduling, Coverage, AlertTimes, Content, PriorityRoads, Speech).                                                                                                                                 |
| `components/sections/SpeechSection.tsx`          | How the bulletin is written and performed: delivery style + pronunciation dictionary.                                                                                                                                                                  |
| `components/`                                    | ScheduleStudio, ScheduleDetailStudio, CronScheduleGlyph, CronHumanReadable, ScheduleNextRun, ScheduleStatusBadge, VoiceProfileSelector, PriorityRoadsManager, sections/ (BasicInfo, Scheduling, Coverage, AlertTimes, Content, PriorityRoads, Speech). |

### `features/notifications/`

Waiting / failed-job alerts: polling, audible alarm, panel display.

| Module                              | Purpose                         |
| ----------------------------------- | ------------------------------- |
| `hooks/use-notifications.ts`        | Notification polling.           |
| `hooks/use-alarm.ts`                | Alarm state management.         |
| `hooks/use-alarm-prefs.ts`          | Alarm preferences persistence.  |
| `hooks/use-title-alarm.ts`          | Title-bar alarm indicator.      |
| `components/NotificationsPanel.tsx` | Alert panel UI.                 |
| `components/AlarmEffects.tsx`       | Visual / audible alarm effects. |

### `features/voices/`

Voice library: workshop / studio, version history, calibration display, model capabilities, selector dropdown.

| Module                            | Purpose                                                                                                                                                                                                                                                                                                                         |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `lib/model-capabilities.ts`       | ElevenLabs model capability lookup.                                                                                                                                                                                                                                                                                             |
| `lib/voice-profile-utils.ts`      | Profile form / diff helpers.                                                                                                                                                                                                                                                                                                    |
| `lib/sample-script.ts`            | Sample TTS script for audition.                                                                                                                                                                                                                                                                                                 |
| `lib/sort-voices.ts`              | Voice sort helpers.                                                                                                                                                                                                                                                                                                             |
| `api/voice-hooks.ts`              | Voice CRUD hooks.                                                                                                                                                                                                                                                                                                               |
| `api/voice-profile-hooks.ts`      | Profile CRUD hooks.                                                                                                                                                                                                                                                                                                             |
| `components/VoiceStudio.tsx`      | Profile studio — versioning, create/edit flow (the only voice create/edit surface). An operator sees ONLY its Voiceover step, with no submit.                                                                                                                                                                                   |
| `components/VoiceoverSection.tsx` | The voiceover booth: arbitrary copy read on the profile's saved current version, played back and downloaded as a WAV. Operator-reachable; deliberately skips the bulletin pace boost.                                                                                                                                           |
| `components/`                     | VoiceVariantCard (pace ▏ proven-on-air stats; `canManage=false` swaps the kebab for a focusable Record button), VoiceProfileTable (table view of the gallery, same `canManage` rule), VoiceVersionHistory, VoiceIdentitySection, VoiceCharacterSection (incl. delivery-pace boost slider), StabilitySelector, SamplePlayButton. |

### `features/stations/`

Station profiles + manual trigger: station cards, coverage preview, manual-trigger studio, voice picker.

| Module                                 | Purpose                                                                                              |
| -------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| `lib/station-attention.ts`             | Station attention / priority helpers.                                                                |
| `components/StationProfileCard(s).tsx` | Station profile cards.                                                                               |
| `components/StationProfileTable.tsx`   | Table view of the station grid (shadcn Table; in-scope/out-of-scope sections, status switch, kebab). |
| `components/TriggerStudio.tsx`         | Manual-trigger studio (ad-hoc bulletin generation).                                                  |
| `components/CoverageMapPreview.tsx`    | Mini Leaflet coverage preview.                                                                       |
| `components/StationVoicePicker.tsx`    | Station-level voice picker.                                                                          |
| `components/TriggerVoicePicker.tsx`    | Voice picker for manual triggers.                                                                    |

### `features/style-rules/`

Global editorial style rules library: ordered, versioned voicing rules rendered into every draft prompt, with busy/quiet scenario preview.

| Module                                    | Purpose                                               |
| ----------------------------------------- | ----------------------------------------------------- |
| `components/StyleRulesManager.tsx`        | Drag-to-reorder style rules editor with search/filter |
| `components/StyleRuleRow.tsx`             | Single rule row (text, example, enabled toggle)       |
| `components/StyleRulesPreviewDialog.tsx`  | Draft-pass preview against busy/quiet scenarios       |
| `components/StyleRulesVersionHistory.tsx` | Version timeline with per-version changes summary     |
| `components/StyleRulesChangesSummary.tsx` | Uncommitted changes diff summary                      |
| `components/useStyleRulesEditor.ts`       | Editor state + undo manager for the rule list         |
| `api/style-rules-hooks.ts`                | TanStack Query CRUD + preview hooks                   |
| `api/style-rules-schemas.ts`              | Zod schemas for style-rules API payloads              |

### `features/operators/`

Operator market / station assignment cards + dialog + hooks (admin).

| Module                                   | Purpose                                                                |
| ---------------------------------------- | ---------------------------------------------------------------------- |
| `api/operator-hooks.ts`                  | Operator CRUD hooks.                                                   |
| `api/schemas.ts`                         | Operator Zod schemas.                                                  |
| `components/OperatorAssignmentCard.tsx`  | Assignment card.                                                       |
| `components/OperatorAssignmentTable.tsx` | Table view of the gallery (shadcn Table; row→edit, kebab Edit/Remove). |
| `components/AssignOperatorDialog.tsx`    | Assign / edit dialog.                                                  |

### `features/onair/`

Live workstation: queue beam, pipeline stepper, review pane; coverage board; slot logic; keyboard shortcuts.

| Module                                       | Purpose                                  |
| -------------------------------------------- | ---------------------------------------- |
| `hooks/use-queue-keyboard.ts`                | Keyboard shortcuts for queue navigation. |
| `lib/slots.ts`                               | Queue slot logic.                        |
| `components/workstation/PipelineStepper.tsx` | Pipeline progress stepper.               |
| `components/workstation/QueueBeam.tsx`       | Active-queue beam indicator.             |
| `components/workstation/ReviewPane.tsx`      | Quick review pane.                       |
| `components/board/CoverageHome.tsx`          | Coverage board.                          |
| `components/board/lane-condition.ts`         | Lane condition helpers.                  |

## Shared Components (`src/components/`)

| File                                                      | Purpose                                                                                                                                                                                                                                                                                                                       |
| --------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `studio/`                                                 | Shared Studio shell, submit button and touched/submit validation gate used by Schedule / Voice / Delivery / Pronunciation / Bookend editors.                                                                                                                                                                                  |
| `navigation/`                                             | AppSidebar, UserMenu, MobileNav.                                                                                                                                                                                                                                                                                              |
| `layout/`                                                 | Header (page header with title + action slot), PageContainer (standard page wrapper).                                                                                                                                                                                                                                         |
| `map-picker/`                                             | Leaflet coverage editor: MapPicker, ShapeEditors, DraggableCircle, AddressSearch, FollowCenter, FitToShape, KeyboardHandler, coverage-shapes/validation, use-geocoding (+ tests).                                                                                                                                             |
| `ui/aceternity/`                                          | Motion / glow primitives: aurora-hero, aurora-wash, dot-grid-backdrop, glow-border, moving-border, spotlight, spotlight-card.                                                                                                                                                                                                 |
| `CommandPalette.tsx`                                      | Cmd+K: pages + unique/bounded configurable entities (stations, voice profiles, delivery styles, pronunciation dictionaries, operators; admin-gated), each deep-linking via `?open=<id>`. Bulletins are deliberately excluded (unbounded, not unique by name).                                                                 |
| `DataBoundary.tsx`                                        | Query loading / error / empty boundary.                                                                                                                                                                                                                                                                                       |
| `ViewToggle.tsx`                                          | Cards \| Table segmented control for entity-list pages (pairs with `hooks/use-view-mode`).                                                                                                                                                                                                                                    |
| `LoadingSkeleton.tsx`                                     | Shared loading placeholders: `CardGridSkeleton` (card pages), `ListSkeleton` (bordered list containers).                                                                                                                                                                                                                      |
| `DataTableColumnHeader.tsx`                               | Clickable `<TableHead>` with sort-direction indicator, generic over a `SortState<TKey>`.                                                                                                                                                                                                                                      |
| `LoadOlderButton.tsx`                                     | Shared "Load older" footer control for cursor-paginated tables (Activity, History).                                                                                                                                                                                                                                           |
| `MetricCard.tsx`, `Placeholder.tsx`, `FormInputField.tsx` | Shared display / form primitives.                                                                                                                                                                                                                                                                                             |
| `ConfigRow.tsx`                                           | Key-value config row.                                                                                                                                                                                                                                                                                                         |
| `ConfirmDialog.tsx`                                       | Reusable confirm dialog.                                                                                                                                                                                                                                                                                                      |
| `ErrorAlert.tsx`                                          | Inline error alert.                                                                                                                                                                                                                                                                                                           |
| `ErrorBoundary.tsx`                                       | React error boundary.                                                                                                                                                                                                                                                                                                         |
| `SectionHeader.tsx`                                       | Section heading with optional action slot.                                                                                                                                                                                                                                                                                    |
| `InfoHint.tsx`                                            | The `?` explainer beside a control — a focusable button + tooltip. Used by `RuleToolbar` (every bulletin-rules tab) and `StyleRulesManager`.                                                                                                                                                                                  |
| `RuleComposer.tsx`                                        | Shared authoring shell for the bulletin-rules hub (SpotlightCard + GlowBorder action). Mounted on demand from `RuleToolbar`'s Add button, never permanently. Used by guidance, banned words, priority roads and filler statements.                                                                                            |
| `RuleToolbar.tsx`                                         | The one row of chrome above every rules-hub list: search, the tab's filter selects, then the outline Add (plus its `?`) pinned right. No export slot — Data transfer is the one door for config.                                                                                                                              |
| `RuleTab.tsx`                                             | Shared scaffold for rules hub tabs (icon + intro banner + children).                                                                                                                                                                                                                                                          |
| `ScopeGroupedList.tsx`                                    | Generic scope-grouped list with sticky per-scope headers and dense rows.                                                                                                                                                                                                                                                      |
| `ui/`                                                     | shadcn/ui primitives: alert-dialog, alert, badge, button, button-variants, card, command, container, dialog, dropdown-menu, flex, form-context, form, input, label, popover, scroll-area, select, separator, sheet, skeleton, slider, spinner, switch, table, tabs, textarea, toggle, toggle-group, toggle-variants, tooltip. |
