# File Map

Frontend file map for the Arn operator console (`web/src`). Last verified 2026-08-07.

```
src/
├── main.tsx                              Entry point; mounts <App/>
├── App.tsx                               Providers, QueryClient (ApiError-aware retry), sidebar layout, route table
├── vite-env.d.ts                         Vite client types
│
├── pages/
│   ├── HomePage.tsx                      / — dashboard / landing
│   ├── OnAirPage.tsx                     /onair — live operator workstation
│   ├── StationsPage.tsx                  /stations — station + schedule management
│   ├── VoicesPage.tsx                    /voices — voice library
│   ├── OperatorsPage.tsx                 /operators — operator assignments (admin)
│   ├── HistoryPage.tsx                   /history — broadcast log
│   ├── DeliveryPage.tsx                  /delivery — delivery-style gallery + studio
│   ├── PronunciationPage.tsx             /pronunciation — pronunciation dictionary gallery + studio
│   ├── GuidancePage.tsx                  /guidance — bulletin-rules hub (guidance, future lessons, banned words, priority roads)
│   ├── ConfigTransferPage.tsx            /config-transfer — config-bundle export/import + pending approvals (admin)
│   ├── ActivityPage.tsx                  /activity — activity / change audit log (admin)
│   └── NotFoundPage.tsx                  * — 404
│
├── lib/
│   ├── routes.ts                         APP_ROUTES + nav labels
│   ├── layout.ts                         Shared grid/layout constants (CARD_GRID)
│   ├── formatting.ts                     Date/time/duration/relative formatters
│   ├── audio-solo.ts (+ test)            Single playback arbiter — starting one `<audio>` pauses whoever held the floor, so two voices never read over each other
│   ├── formatting.test.ts                Formatter tests
│   ├── auth.ts                           SWA/dev auth parsing + shared useAuthData cache
│   ├── toast-action.ts                   Promise → sonner toast wrapper
│   ├── transfer.ts                       File-transfer conventions: arn-<slug>-YYYY-MM-DD names, download/CSV helpers
│   ├── json.ts                           isRecord / case-insensitive getProp / tryParseJson — generic JSON-shape guards
│   ├── entity-diff.ts (+ test)           Generic client-side structural diff (objects flatten to fields, arrays diff by case-insensitive item identity, duplicates disambiguated by occurrence) — shared by config-transfer + style-rules
│   ├── table-sort.ts                     SortState<TKey> + toggleSort() — shared column-sort state for every sortable data table
│   ├── dom.ts                            DOM helpers
│   ├── utils.ts                          cn() class merger
│   ├── api-utils.ts                      Shared API utility functions
│   ├── api/
│   │   ├── client.ts                     Axios instance, ApiError, token interceptor, mock injection
│   │   ├── paths.ts                      API path constants
│   │   ├── use-query-factory.ts          Query factory + useInfiniteQueryFactory (cursor-based "Load older" pagination) (TanStack Query)
│   │   ├── use-mutation-factory.ts       Mutation factory (simple / optimistic)
│   │   ├── create-query-keys.ts          Structured query-key builder
│   │   ├── schemas/
│   │   │   └── shared.ts                 Shared Zod schemas (voice settings, coverage, voice rotation)
│   │   └── mock/                         In-memory mock backend
│   │       ├── adapter.ts                Axios mock adapter
│   │       ├── fixtures.ts               Seed data
│   │       ├── fixtures.test.ts          Fixture contract tests
│   │       ├── cursor-pagination.test.ts  Composite-cursor (`LastUpdatedAt|JobId`) paging contract
│   │       ├── index.ts                  Mock barrel
│   │       ├── live-traffic.ts           Simulated HERE traffic
│   │       ├── mock.test.ts              Mock adapter tests
│   │       └── store.ts                  Mutable in-memory store
│   ├── constants/
│   │   └── timing.ts                     Shared timing constants
│   └── design/                           Micro-viz primitives
│       ├── BudgetBar.tsx
│       ├── FreshnessDot.tsx
│       ├── icons.ts                      Icon registry (ICON.*)
│       └── SourceGlyphs.tsx
│
├── hooks/
│   ├── use-current-user.ts               useCurrentUser / useRoles / useIsAdmin
│   ├── use-global-keydown.ts             Global keyboard handler
│   ├── use-debounce.ts                   Debounce hook
│   ├── use-document-title.ts             Sets document.title reactively
│   ├── use-confirm-dialog.ts             Confirmation dialog state hook
│   ├── use-view-mode.ts                  Cards|Table view-mode state, persisted per page in localStorage
│   └── use-second-tick.ts                1 s ticker for countdowns
│
├── features/
│   ├── activity/                         Activity / change audit log
│   │   ├── api/
│   │   │   ├── activity-hooks.ts         TanStack Query fetch (useActivityLog, embedded rail) + useActivityLogHistory (infinite "Load older" hook backing the Activity page table)
│   │   │   └── activity-schemas.ts       Zod schemas (ActivityLogEntry wire → camelCase)
│   │   └── components/
│   │       ├── ActivityFeed.tsx           Read-only change/access-audit timeline (reuses entity-diff); powers the small embedded audit-rail widgets
│   │       ├── ActivityTable.tsx          Sortable data table for the Activity page: column sort (time/actor/resource), "Load older" paging, per-entity diff rows
│   │       └── ScopedActivityHistory.tsx Collapsed-by-default disclosure over ActivityFeed for one fixed resource+entityId (mirrors the Version History disclosure shape; used by 6 admin surfaces)
│   │
│   ├── delivery/                         Delivery-style library (ElevenLabs v3 audio-tag steering)
│   │   ├── types.ts                      DeliveryStyle, Policy, Version, Create/Update/Preview request types
│   │   ├── cues.ts                       Permitted v3 audio cue catalogue
│   │   ├── strength.ts                   Delivery strength range helpers
│   │   ├── presets.ts                    Preset delivery-style policies
│   │   ├── api/
│   │   │   ├── delivery-style-hooks.ts   TanStack Query CRUD + preview hooks
│   │   │   ├── delivery-schemas.ts       Zod schemas for API payloads
│   │   │   └── delivery-schemas.test.ts
│   │   ├── lib/
│   │   │   ├── assignments.ts            Style ↔ schedule assignment resolution
│   │   │   ├── delivery-form.ts          React Hook Form schema for delivery studio
│   │   │   └── delivery-form.test.ts
│   │   └── components/
│   │       ├── DeliveryStyleCard.tsx      Gallery card (version badge, archive toggle)
│   │       ├── DeliveryStyleTable.tsx     Table view of the gallery (shadcn Table; row→studio, kebab mirrors card)
│   │       ├── DeliveryStyleStudio.tsx    Create/edit studio (policy fields, cue picker, audition)
│   │       ├── AuditionPanel.tsx          Live preview of an unsaved policy
│   │       ├── PermittedCueSelector.tsx   Cue chip toggle for allowed tags
│   │       ├── StrengthControl.tsx        Intensity slider
│   │       ├── VersionHistory.tsx         Version timeline + diff
│   │       └── LiveEditConfirmationDialog.tsx  Guard for unsaved live edits
│   │
│   ├── pronunciation/                    Per-station pronunciation dictionary library
│   │   ├── types.ts                      Dictionary, Rule, RuleInput, Create/Update/Preview requests
│   │   ├── lib/
│   │   │   ├── pronunciation-form.ts     React Hook Form schema for dictionary studio
│   │   │   ├── pronunciation-target.ts   Hovered word → a rule target (script punctuation stripped)
│   │   │   └── assignments.ts            Dictionary ↔ schedule assignment resolution
│   │   ├── api/
│   │   │   ├── pronunciation-hooks.ts    TanStack Query CRUD + per-rule preview hooks
│   │   │   └── pronunciation-schemas.ts  Zod schemas for API payloads
│   │   └── components/
│   │       ├── PronunciationDictionaryCard.tsx (+ test)  Gallery card
│   │       ├── PronunciationDictionaryTable.tsx  Table view of the gallery (shadcn Table; row→studio, kebab mirrors card)
│   │       ├── PronunciationDictionaryStudio.tsx Create/edit studio (rules + version history)
│   │       ├── PronunciationRuleTable.tsx (+ test) Alias / phoneme rule editor (per-row audition)
│   │       └── VersionHistory.tsx                  Version timeline
│   │
│   ├── guidance/                         Standing guidance + future-lesson governance
│   │   ├── lib/
│   │   │   ├── display.ts                Display label helpers
│   │   │   ├── inventory.ts              Filtering + grouping logic (+ tests)
│   │   │   └── inventory.test.ts
│   │   ├── api/
│   │   │   ├── guidance-hooks.ts         TanStack Query CRUD, promotion, candidate-list and dismissal hooks
│   │   │   ├── schemas.ts                Zod schemas for guidance and future-lesson payloads
│   │   │   └── schemas.test.ts
│   │   └── components/
│   │       ├── GuidanceComposer.tsx       Inline add/edit/promote composer
│   │       ├── GuidanceInventory.tsx      Scope-grouped guidance list
│   │       ├── GuidanceRow.tsx            Single guidance row (edit/delete/promote)
│   │       ├── FutureLessonsPanel.tsx     Admin queue for promoting or dismissing active station lessons
│   │       └── MarketDetailPanel.tsx      Per-market guidance detail view
│   │
│   ├── safety/                           Banned-words admin
│   │   ├── api/
│   │   │   ├── safety-hooks.ts           TanStack Query CRUD hooks
│   │   │   ├── schemas.ts                Zod schemas (SafetyTerm, SafetyKindName, SafetyScopeName)
│   │   │   └── schemas.test.ts
│   │   └── components/
│   │       └── SafetyTermsManager.tsx     Scope-grouped banned-word list + composer
│   │
│   ├── style-rules/                      Global editorial Style Rules library (the draft prompt's Voicing block)
│   │   ├── types.ts                      StyleRule, StyleRuleSetVersion, Update/Preview request types
│   │   ├── api/
│   │   │   ├── style-rules-hooks.ts      TanStack Query CRUD + restore + preview hooks
│   │   │   └── style-rules-schemas.ts    Zod schemas for API payloads
│   │   └── components/
│   │       ├── StyleRulesManager.tsx           Rules/Version-history tabbed editor: reorder/toggle/delete, Review-changes diff, save
│   │       ├── StyleRulesChangesSummary.tsx     Collapsed "what am I about to save" disclosure — shared entity-diff engine, no backend change needed
│   │       ├── StyleRulesVersionHistory.tsx     Version timeline + restore (renders as the Version-history tab's content)
│   │       └── StyleRulesPreviewDialog.tsx      Draft-pass-only side-by-side preview on canned scenarios
│   │
│   ├── config-transfer/                  Admin config-bundle export/import ("Data transfer")
│   │   ├── api/
│   │   │   ├── config-transfer-hooks.ts  Export/schema fetchers, dry-run/apply import (tryImportConfig normalizes the 400 report), export preview query, pending approve/reject hooks
│   │   │   └── config-transfer-schemas.ts Zod schemas (import report, pending imports; bundle stays raw JSON)
│   │   ├── lib/
│   │   │   ├── section-meta.ts           Section labels/icons/key-fields/order (wire-exact to backend ConfigSections) + live entity counts + bundle entity lookup (findEntityInBundle)
│   │   │   └── action-badge.ts           Create/Update/Unchanged/Error → Badge variant (shared by the review tabs and pending-approvals table)
│   │   └── components/
│   │       ├── ExportPanel.tsx            Section picker as a bento grid of SpotlightCards (icon + live count + toggle) + Export JSON / Download schema
│   │       ├── ImportPanel.tsx            Drag-and-drop dropzone → dry-run (report + current bundle fetched in parallel) → ImportReviewTabs → Apply
│   │       ├── ImportReviewTabs.tsx (+ test) Per-touched-section tabs; each entity row expands into a real diff via the shared EntityDiffView
│   │       └── PendingApprovalsCard.tsx   Second-admin approval queue — click-to-expand row + kebab menu (Show details/Approve/Reject), MovingBorder(warning) when non-empty
│   │
│   ├── jobs/                             Bulletin lifecycle
│   │   ├── types.ts                      Job union, request types, guards
│   │   ├── config/
│   │   │   ├── status-config.ts          Status display config
│   │   │   └── status-values.ts          Status enum values
│   │   ├── api/
│   │   │   ├── job-hooks.ts              TanStack Query mutations for job CRUD + lifecycle, + useJobsHistory (infinite "Load older" hook backing the History page table)
│   │   │   ├── schemas.ts                Job Zod schemas
│   │   │   ├── schemas.test.ts
│   │   │   └── mappers.ts               DTO ↔ domain mappers
│   │   ├── hooks/
│   │   │   ├── use-bulletin-review.ts    Review workflow state (playback, approve, reject, regenerate)
│   │   │   ├── use-job-actions.ts        Approve / reject / publish / regenerate / delete mutations
│   │   │   ├── use-script-verification.ts  Script verification state
│   │   │   ├── use-waiting-badge.ts      Waiting-job badge polling
│   │   │   └── use-word-timings.ts       Word-timing rescale for karaoke sync
│   │   │       └── use-word-timings.test.ts
│   │   ├── lib/
│   │   │   ├── incidents/
│   │   │   │   ├── coverage-grouping.ts (+ test)  Coverage region grouping
│   │   │   │   ├── incident-editing.ts       Incident editing helpers
│   │   │   │   ├── incident-labels.ts (+ test)  Incident classification labels
│   │   │   │   └── road-groups.ts            Road grouping helpers
│   │   │   ├── job-meta/
│   │   │   │   ├── earlier-today.ts (+ test)  "Earlier today" continuity helpers
│   │   │   │   ├── get-actor-name.ts         Actor display name (item.by or 'System')
│   │   │   │   ├── job-label.ts (+ test)     Job display-label helpers
│   │   │   │   └── urgency.ts (+ test)       Urgency level computation
│   │   │   ├── map/
│   │   │   │   ├── evidence-points.ts        Map evidence-point helpers
│   │   │   │   └── map-links.ts (+ test)     Google Maps traffic-view link helpers
│   │   │   ├── verification/
│   │   │   │   ├── claims.ts (+ test)        Claim extraction
│   │   │   │   ├── provenance.ts (+ test)    Provenance claim helpers
│   │   │   │   ├── sentence-status.ts (+ test)  Per-sentence status
│   │   │   │   └── spoken-numbers.ts         Spoken-number normalisation (cardinal 1-99 incl. compounds)
│   │   │   ├── media-url.ts (+ test)               Media URL helpers
│   │   │   ├── pronunciation-matches.ts (+ test)   Exact historical dictionary-version matching against canonical script text
│   │   │   └── weather-tone.ts (+ test)             Weather tone helpers
│   │   └── components/
│   │       ├── index.ts                  Barrel exports
│   │       ├── AudioWaveformPlayer.tsx    Waveform playback
│   │       ├── LocationMap.tsx            Incident map
│   │       ├── ScriptRenderer.tsx         Script body renderer; hover-dwell on any word opens its respelling panel
│   │       ├── ScriptWordMark.tsx         Marked word: dictionary substitution + pre-review verdict popover
│   │       ├── ScriptRespellPopover.tsx   Hover-opened respell panel, anchored to the word under the pointer
│   │       ├── RespellingField.tsx        Shared "say it differently" field (marked-word popover + respell panel)
│   │       ├── JobActions.tsx             Per-job action buttons
│   │       ├── review/
│   │       │   ├── JobReviewCockpit.tsx   Top-level review orchestrator
│   │       │   ├── ReviewScript.tsx       Script review pane
│   │       │   ├── PronunciationScriptEditor.tsx Native textarea + caret-aligned tag/pronunciation mirror
│   │       │   ├── script-editor-text.ts  Sentence-line formatting + canonical re-voice text
│   │       │   ├── ReviewBulletin.tsx     Bulletin review card
│   │       │   ├── ReviewSources.tsx      Source list review
│   │       │   ├── ReviewContextLens.tsx  Context/metadata lens
│   │       │   ├── ReviewVitals.tsx       Vital metrics display
│   │       │   ├── ReviewDetailsTab.tsx   Detail tab content
│   │       │   ├── DecisionDock.tsx       Approve / reject / regenerate dock
│   │       │   ├── ChangeChips.tsx        Change category chips
│   │       │   ├── DeliveryPanel.tsx      Tagged-read disclosure (ElevenLabs v3 audio tags)
│   │       │   ├── ReviseDialog.tsx       Applies queued source/voice changes; fallback/reject decision surface
│   │       │   └── AsAiredCard.tsx        As-aired summary card
│   │       ├── review/adjust/
│   │       │   ├── AdjustMap.tsx          Incident-adjust map
│   │       │   ├── RankingExplainer.tsx   Ranking explanation
│   │       │   └── RundownTable.tsx       Unified incident + good-run rundown (score-ranked, drag in/out, row expand, Google Maps traffic links)
│   │       ├── history/
│   │       │   ├── BroadcastLog.tsx (+ test)  Broadcast log: sortable `<table>` (day-grouped at the default sort, flattens on any other column sort), filter rail, CSV export, "Load older" paging
│   │       │   ├── BroadcastLogRow.tsx    Sortable-table row: fixed summary cells + on-demand expand
│   │       │   ├── BroadcastLogRowDetail.tsx  Expanded row detail
│   │       │   └── history-utils.ts (+ test)  Sorting (`sortJobs`/`HistorySortKey`), day-grouping, `PIPELINE_QUERY_STATUSES`, CSV export, formatting helpers
│   │       └── canvas/
│   │           ├── ConfigurationTab.tsx   System configuration display
│   │           ├── pronunciation-display.ts  Exact/missing pronunciation-version audit labels
│   │           ├── SystemAuditTab.tsx     System audit trail
│   │           ├── FailureDetailsPanel.tsx  Failure details
│   │           ├── use-audio-seek.ts      Audio seek hook
│   │           └── constants.ts           Canvas tab constants
│   │
│   ├── schedules/                        Station schedule management
│   │   ├── types.ts                      MarketSchedule, UpsertScheduleRequest, VoiceRotation, VoiceWindow, VoiceSelectionMode
│   │   ├── form-schemas.ts (+ test)      React Hook Form / Zod schemas
│   │   ├── config/
│   │   │   └── status-config.ts          Schedule status display config
│   │   ├── api/
│   │   │   ├── schedule-hooks.ts         TanStack Query mutations for schedule CRUD
│   │   │   ├── priority-roads-hooks.ts   Priority-roads query/mutation hooks
│   │   │   └── schemas.ts               Schedule Zod schemas
│   │   ├── hooks/
│   │   │   └── use-schedule-actions.ts   Update/delete schedule actions
│   │   ├── lib/
│   │   │   ├── date-math.ts              Cron date-math helpers (CronField, TzDateParts, firstValue, …)
│   │   │   ├── cron-calendar.ts (+ test) Calendar-based cron builder
│   │   │   ├── cron-occurrences.ts (+ test)  Next-N cron occurrence computation
│   │   │   ├── cron-format.ts            cronstrue → abbreviated human-readable text
│   │   │   ├── schedule-utils.ts         Schedule display/formatting utilities
│   │   │   ├── schedule-studio-helpers.ts  Studio form helpers
│   │   │   └── voice-colors.ts           Deterministic colour palette for voice rotation UI
│   │   └── components/
│   │       ├── ScheduleStudio.tsx         Create/edit schedule studio
│   │       ├── ScheduleDetailStudio.tsx   Detail studio view
│   │       ├── ScheduleStatusBadge.tsx    Status badge
│   │       ├── ScheduleNextRun.tsx        Next-run countdown
│   │       ├── CronScheduleGlyph.tsx      Cron expression visual glyph
│   │       ├── CronHumanReadable.tsx (+ test)  Human-readable cron display
│   │       ├── VoiceProfileSelector.tsx   Voice profile picker
│   │       ├── DeliveryStyleSelector.tsx  Delivery-style picker (linked to delivery feature)
│   │       ├── PronunciationDictionarySelector.tsx  Dictionary picker (linked to pronunciation feature)
│   │       ├── VoiceRotationEditor.tsx (+ test) Voice rotation mode editor (single / rotate / time-of-day)
│   │       ├── BookendRotationEditor.tsx (+ test) Bookend rotation mode editor (single / rotate / time-of-day)
│   │       ├── VoiceTimeline.tsx          24-hour drag-to-resize voice window timeline
│   │       ├── VoiceModeGlyph.tsx         Compact voice-mode indicator for station cards
│   │       ├── PriorityRoadsManager.tsx   Priority-roads manager (used in guidance page)
│   │       └── sections/
│   │           ├── index.ts              Section barrel
│   │           ├── BasicInfoSection.tsx   Name, station, timezone
│   │           ├── SchedulingSection.tsx (+ test) Cron expression, enable toggle
│   │           ├── CoverageSection.tsx    Coverage area / map
│   │           ├── AlertTimesSection.tsx  Notify / escalation lead times
│   │           ├── ContentSection.tsx     Content-selection (traffic / flow / incidents)
│   │           ├── PriorityRoadsSection.tsx  Good-run road toggles
│   │           └── SpeechSection.tsx      LLM model + delivery style + pronunciation dictionary
│   │
│   ├── stations/                         Station profiles + manual trigger
│   │   ├── lib/
│   │   │   └── station-attention.ts      Station attention / priority helpers
│   │   └── components/
│   │       ├── StationProfileCard.tsx     Single station card
│   │       ├── StationProfileCards.tsx (+ test) Card grid
│   │       ├── StationProfileTable.tsx (+ test) Table view of the station grid (shadcn Table; in-scope/out-of-scope sections, status switch, kebab)
│   │       ├── CoverageMapPreview.tsx     Mini Leaflet coverage preview
│   │       ├── TriggerStudio.tsx (+ test) Manual-trigger studio
│   │       ├── TriggerVoicePicker.tsx     Voice picker for manual triggers
│   │       ├── StationVoicePicker.tsx     Station-level voice picker
│   │       └── trigger-studio-helpers.ts  Trigger form helpers
│   │
│   ├── voices/                           Voice library (variants / versions)
│   │   ├── types.ts                      Voice wire types
│   │   ├── profile-types.ts              VoiceProfile domain types
│   │   ├── form-schemas.ts               React Hook Form / Zod schemas
│   │   ├── api/
│   │   │   ├── voice-hooks.ts            Voice CRUD hooks
│   │   │   ├── voice-profile-hooks.ts    Profile CRUD hooks
│   │   │   ├── schemas.ts                Voice Zod schemas
│   │   │   └── profile-schemas.ts        Profile Zod schemas
│   │   ├── lib/
│   │   │   ├── model-capabilities.ts (+ test)  ElevenLabs model capability lookup
│   │   │   ├── voice-profile-utils.ts (+ test)  Profile form/diff helpers
│   │   │   ├── sample-script.ts          Sample TTS script
│   │   │   └── sort-voices.ts            Voice sort helpers
│   │   └── components/
│   │       ├── VoiceStudio.tsx            Profile studio (versioning, create/edit flow; Voiceover step, operator-only view)
│   │       ├── VoiceVariantCard.tsx       Variant card (fixed-skeleton: audition+identity · pace ▏ proven-on-air stats · assignment)
│   │       ├── VoiceProfileTable.tsx      Table view of the gallery (shadcn Table; row→studio, kebab mirrors card)
│   │       ├── VoiceVersionHistory.tsx    Version timeline
│   │       ├── VoiceoverSection.tsx       Voiceover booth: arbitrary copy → this voice's current config → downloadable WAV
│   │       ├── VoiceIdentitySection.tsx   Identity fields (name, description)
│   │       ├── VoiceCharacterSection.tsx  Character / personality fields + delivery-pace boost slider
│   │       ├── StabilitySelector.tsx      Stability slider
│   │       └── SamplePlayButton.tsx       Sample audio play button
│   │
│   ├── operators/                        Operator assignments (admin)
│   │   ├── api/
│   │   │   ├── operator-hooks.ts         Operator CRUD hooks
│   │   │   └── schemas.ts               Operator Zod schemas
│   │   └── components/
│   │       ├── OperatorAssignmentCard.tsx Assignment card
│   │       ├── OperatorAssignmentTable.tsx Table view of the gallery (shadcn Table; row→edit, kebab Edit/Remove)
│   │       └── AssignOperatorDialog.tsx   Assign/edit dialog
│   │
│   ├── onair/                            Live workstation
│   │   ├── hooks/
│   │   │   └── use-queue-keyboard.ts     Keyboard shortcuts for queue navigation
│   │   ├── lib/
│   │   │   └── slots.ts (+ test)         Queue slot logic
│   │   └── components/
│   │       ├── workstation/
│   │       │   ├── PipelineStepper.tsx    Pipeline progress stepper
│   │       │   ├── QueueBeam.tsx          Active-queue beam indicator
│   │       │   └── ReviewPane.tsx         Quick review pane
│   │       └── board/
│   │           ├── CoverageHome.tsx (+ test) Coverage board
│   │           └── lane-condition.ts      Lane condition helpers
│   │
│   └── notifications/                    Waiting / failed-job alerts
│       ├── index.ts                      Barrel exports
│       ├── constants.ts                  Notification constants
│       ├── types.ts                      Notification types
│       ├── hooks/
│       │   ├── use-notifications.ts      Notification polling
│       │   ├── use-alarm.ts              Alarm state
│       │   ├── use-alarm-prefs.ts        Alarm preferences
│       │   └── use-title-alarm.ts        Title-bar alarm
│       └── components/
│           ├── NotificationsPanel.tsx     Alert panel
│           └── AlarmEffects.tsx           Visual alarm effects
│
├── components/
│   ├── CommandPalette.tsx                Cmd+K: pages + bounded configurable entities (stations/voices/delivery/pronunciation/operators, admin-gated) — no bulletins (unbounded, non-unique)
│   ├── NavigationBar.tsx                 Top navigation bar
│   ├── DataBoundary.tsx                  Query loading / error / empty boundary
│   ├── ViewToggle.tsx                    Cards | Table segmented control for entity-list pages (pairs with hooks/use-view-mode)
│   ├── TransferButtons.tsx               Standard ExportButton / ImportButton (labels, icons, hidden file input)
│   ├── EntityDiffView.tsx                Renders lib/entity-diff's DiffEntry[] — git-style before→after fields, tinted +/− for whole array items
│   ├── LoadingSkeleton.tsx               CardGridSkeleton, ListSkeleton
│   ├── DataTableColumnHeader.tsx         Clickable `<TableHead>` with sort-direction indicator (generic over a `SortState<TKey>`)
│   ├── LoadOlderButton.tsx               Shared "Load older" footer control for cursor-paginated tables (Activity, History)
│   ├── MetricCard.tsx                    Metric display card
│   ├── Placeholder.tsx                   Empty-state placeholder
│   ├── FormInputField.tsx                Shared form field
│   ├── ConfigRow.tsx                     Key-value config row
│   ├── ConfirmDialog.tsx                 Reusable confirm dialog
│   ├── ErrorAlert.tsx                    Inline error alert
│   ├── ErrorBoundary.tsx (+ test)         React error boundary
│   ├── SectionHeader.tsx                 Section heading with optional action
│   ├── InfoHint.tsx                      Focusable `?` explainer trigger + tooltip, beside a toolbar Add button
│   ├── RuleComposer.tsx                  On-demand authoring shell for bulletin-rules hub (SpotlightCard + GlowBorder action)
│   ├── RuleToolbar.tsx                   Search + filters + right-anchored Add — the one row above every rules-hub list
│   ├── RuleTab.tsx                       Shared scaffold for rules hub tabs (icon + intro + children)
│   ├── ScopeGroupedList.tsx              Generic scope-grouped list with sticky headers
│   ├── layout/
│   │   ├── index.ts                      Barrel exports (Header, PageContainer)
│   │   ├── Header.tsx                    Page header with title + action slot
│   │   └── PageContainer.tsx             Standard page wrapper
│   ├── map-picker/                       Leaflet coverage editor
│   │   ├── index.ts                      Barrel exports
│   │   ├── MapPicker.tsx                 Root map component
│   │   ├── MapClickHandler.tsx           Map click event handler
│   │   ├── AddressSearch.tsx             Nominatim geocoding search
│   │   ├── DraggableCircle.tsx           Draggable circle marker
│   │   ├── FitToShape.tsx                Auto-fit map to shape
│   │   ├── FollowCenter.tsx              Follow-center behavior
│   │   ├── KeyboardHandler.tsx           Map keyboard shortcuts
│   │   ├── ShapeEditors.tsx              Polygon/polyline editors
│   │   ├── leaflet-defaults.ts           Leaflet config defaults
│   │   ├── coverage-shapes.ts (+ test)   Shape manipulation helpers
│   │   ├── coverage-validation.ts (+ test)  Coverage validation rules
│   │   └── types.ts                      Map-picker types
│   ├── navigation/
│   │   ├── AppSidebar.tsx                App sidebar (nav + branding)
│   │   ├── UserMenu.tsx                  User dropdown
│   │   └── MobileNav.tsx                 Mobile hamburger nav
│   ├── studio/
│   │   ├── Studio.tsx (+ test)           Shared Studio shell (dialog, rail, canvas, footer)
│   │   ├── StudioSubmitButton.tsx        Studio save button
│   │   └── use-studio-step-gate.ts       Delays invalid-step warnings until touch or submit
│   └── ui/
│       ├── aceternity/                   Motion / glow primitives
│       │   ├── aurora-hero.tsx           Aurora hero animation
│       │   ├── aurora-wash.tsx           Aurora wash background
│       │   ├── dot-grid-backdrop.tsx     Dot grid backdrop
│       │   ├── glow-border.tsx           Glow-border when-ready treatment
│       │   ├── moving-border.tsx         Animated border
│       │   ├── spotlight.tsx             Spotlight effect
│       │   └── spotlight-card.tsx        Card with spotlight hover
│       ├── alert-dialog.tsx              AlertDialog (Radix)
│       ├── alert.tsx                     Alert banner
│       ├── badge.tsx                     Badge
│       ├── button.tsx                    Button
│       ├── button-variants.ts            Button variant definitions
│       ├── card.tsx                      Card
│       ├── command.tsx                   Command (cmdk)
│       ├── container.tsx                 Container
│       ├── dialog.tsx                    Dialog (Radix)
│       ├── dropdown-menu.tsx             DropdownMenu (Radix)
│       ├── flex.tsx                      Flex layout helper
│       ├── form-context.ts               Form context provider
│       ├── form.tsx                      Form (react-hook-form integration)
│       ├── input.tsx                     Input
│       ├── label.tsx                     Label
│       ├── popover.tsx                   Popover (Radix)
│       ├── scroll-area.tsx               ScrollArea (Radix)
│       ├── select.tsx                    Select (Radix)
│       ├── separator.tsx                 Separator
│       ├── sheet.tsx                     Sheet (Radix)
│       ├── skeleton.tsx                  Skeleton
│       ├── slider.tsx                    Slider (Radix)
│       ├── spinner.tsx                   Spinner
│       ├── switch.tsx                    Switch (Radix)
│       ├── table.tsx                     Table
│       ├── tabs.tsx                      Tabs (Radix)
│       ├── textarea.tsx                  Textarea
│       ├── toggle.tsx                    Toggle (Radix)
│       ├── toggle-group.tsx              ToggleGroup (Radix)
│       ├── toggle-variants.ts            Toggle variant definitions
│       └── tooltip.tsx                   Tooltip (Radix)
│
└── test/                                 Vitest factories + helpers
    ├── factories.ts                      Shared test factories
    ├── smoke-api.test.ts                 API smoke tests
    └── utils.ts                          Test utility helpers
```
