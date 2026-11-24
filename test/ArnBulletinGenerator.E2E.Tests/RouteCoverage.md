# Backend E2E Route Coverage

| Route | Method | Test method |
|---|---:|---|
| `/health` | GET | `HealthAndAuthE2ETests.Health_AllowsAnonymous_ReturnsReadyOrDegraded`; `QueueFailureE2ETests.MalformedQueueMessage_DoesNotBreakSubsequentBroadcast` |
| `/broadcast` | POST | `BroadcastPipelineE2ETests.Broadcast_Approve_Publish_CompletesFakePipeline`; helper-created jobs in regeneration/queue tests |
| `/jobs` | GET | `HealthAndAuthE2ETests.Jobs_WithoutPrincipal_ReturnsUnauthorized`; `ScheduleLifecycleE2ETests.Schedule_CreateUpdatePollDelete_RoundTripsAndCreatesJob`; `CalendarSchedulingE2ETests.Poller_DoesNotDispatchOnASuppressedDate_WhileTheControlStationAirs` (suppressed market absent, control present) |
| `/jobs/{jobId}` | GET | `BackendE2ETestSupport.GetJobAsync` used by pipeline/regeneration/queue tests |
| `/config/export` | GET | `CalendarSchedulingE2ETests.ConfigExportImport_CarriesTheCalendar_ThroughTheRealEndpoints` |
| `/config/import` | POST | `CalendarSchedulingE2ETests.ConfigExportImport_CarriesTheCalendar_ThroughTheRealEndpoints` (`dryRun=false`) |
| `/jobs/{jobId}` | DELETE | `BroadcastPipelineE2ETests.Broadcast_Approve_Publish_CompletesFakePipeline` |
| `/jobs/{jobId}/approve` | POST | `BroadcastPipelineE2ETests.Broadcast_Approve_Publish_CompletesFakePipeline` |
| `/jobs/{jobId}/reject` | POST | `RegenerationAndFallbackE2ETests.Reject_Regenerate_RejectToFallback_ExercisesOperatorLoop` |
| `/jobs/{jobId}/reject-to-fallback` | POST | `RegenerationAndFallbackE2ETests.Reject_Regenerate_RejectToFallback_ExercisesOperatorLoop` |
| `/jobs/{jobId}/publish` | POST | `BroadcastPipelineE2ETests.Broadcast_Approve_Publish_CompletesFakePipeline` |
| `/jobs/{jobId}/regenerate` | POST | `RegenerationAndFallbackE2ETests.Reject_Regenerate_RejectToFallback_ExercisesOperatorLoop` |
| `/schedules` | GET | `ScheduleLifecycleE2ETests.Schedule_CreateUpdatePollDelete_RoundTripsAndCreatesJob` |
| `/schedules/{marketId}` | GET | `ScheduleLifecycleE2ETests.Schedule_CreateUpdatePollDelete_RoundTripsAndCreatesJob` deletion assertion; `CalendarSchedulingE2ETests.Calendar_SurvivesHttpAndStorage_AndDrivesTheServerComputedNextRun` (calendar + server-computed NextRunAt) |
| `/schedules/{marketId}` | PUT | `ScheduleLifecycleE2ETests.Schedule_CreateUpdatePollDelete_RoundTripsAndCreatesJob`; `HealthAndAuthE2ETests.Schedules_OperatorMayWriteButNotDelete`; `CalendarSchedulingE2ETests` (full-body create with a calendar, and calendar-only partial PUT) |
| `/schedules/{marketId}` | DELETE | `ScheduleLifecycleE2ETests.Schedule_CreateUpdatePollDelete_RoundTripsAndCreatesJob`; `HealthAndAuthE2ETests.Schedules_OperatorMayWriteButNotDelete` (Operator 403) |
| `/voices` | GET | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/voices/{voiceId}/calibrate` | POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/voices/profiles` | GET/POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip`; `BackendE2ETestSupport.CreateVoiceProfileAsync` |
| `/voices/profiles/preview` | POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/voices/profiles/remix` | POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/voices/profiles/{id}` | GET/PUT | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/voices/profiles/{id}/versions/{version}/restore` | POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/delivery-styles` | GET/POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip`; `BackendE2ETestSupport.CreateDeliveryStyleAsync` |
| `/delivery-styles/preview` | POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/delivery-styles/{id}` | GET/PUT | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/delivery-styles/{id}/versions/{version}/restore` | POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/delivery-styles/{id}/archive` | POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/delivery-styles/{id}/unarchive` | POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/pronunciation-dictionaries` | GET/POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip`; `BackendE2ETestSupport.CreatePronunciationDictionaryAsync` |
| `/pronunciation-dictionaries/{id}` | GET/PUT | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/pronunciation-dictionaries/{id}/preview` | POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/pronunciation-dictionaries/{id}/versions/{version}/restore` | POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/pronunciation-dictionaries/{id}/archive` | POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/pronunciation-dictionaries/{id}/unarchive` | POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/operators/me` | GET | `GovernanceCrudE2ETests.GuidanceSafetyPriorityRoadsOperators_RoundTrip` |
| `/operators` | GET | `GovernanceCrudE2ETests.GuidanceSafetyPriorityRoadsOperators_RoundTrip` |
| `/operators/{objectId}` | PUT/DELETE | `GovernanceCrudE2ETests.GuidanceSafetyPriorityRoadsOperators_RoundTrip` |
| `/markets/{market}/priority-roads` | GET/PUT | `GovernanceCrudE2ETests.GuidanceSafetyPriorityRoadsOperators_RoundTrip` |
| `/priority-roads/inventory` | GET | `GovernanceCrudE2ETests.GuidanceSafetyPriorityRoadsOperators_RoundTrip` |
| `/guidance` | GET/PUT | `GovernanceCrudE2ETests.GuidanceSafetyPriorityRoadsOperators_RoundTrip` |
| `/guidance/{id}` | DELETE | `GovernanceCrudE2ETests.GuidanceSafetyPriorityRoadsOperators_RoundTrip` |
| `/guidance/inventory` | GET | `GovernanceCrudE2ETests.GuidanceSafetyPriorityRoadsOperators_RoundTrip` |
| `/guidance/promote` | POST | `GovernanceCrudE2ETests.GuidanceSafetyPriorityRoadsOperators_RoundTrip` |
| `/markets/{market}/guidance` | GET/PUT | `GovernanceCrudE2ETests.GuidanceSafetyPriorityRoadsOperators_RoundTrip` |
| `/markets/{market}/guidance/{id}` | DELETE | `GovernanceCrudE2ETests.GuidanceSafetyPriorityRoadsOperators_RoundTrip` |
| `/markets/{market}/guidance/composed` | GET | `GovernanceCrudE2ETests.GuidanceSafetyPriorityRoadsOperators_RoundTrip` |
| `/markets/{market}/guidance/candidates` | GET | `GovernanceCrudE2ETests.GuidanceSafetyPriorityRoadsOperators_RoundTrip` |
| `/safety/terms` | GET/PUT | `GovernanceCrudE2ETests.GuidanceSafetyPriorityRoadsOperators_RoundTrip` |
| `/safety/terms/{id}` | DELETE | `GovernanceCrudE2ETests.GuidanceSafetyPriorityRoadsOperators_RoundTrip` |
| `/synthesize` | POST | `VoiceAndContentLibraryE2ETests.VoiceDeliveryPronunciationAndSynthesis_RoundTrip` |
| `/activity` | GET | `ActivityAuditE2ETests.ScheduleVoiceChange_IsRecordedInActivityLog_NewestFirst` |

Notes:
- Backend-only routes remain backend-only; Playwright covers user-visible workflows only.
- Schedule generation is covered through the real timer poller and the real queue-trigger pipeline when the Functions host runs under `scripts/e2e-backend.sh`.
- Queue poison behavior is not asserted through private host logs; `QueueFailureE2ETests` asserts host health and valid-broadcast recovery after a malformed queue message.
