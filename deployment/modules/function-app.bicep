// ──────────────────────────────────────────────
// Module: Function App (Standard App Service Plan)
// Creates an App Service Plan and Function App
// for the ArnBulletinGenerator application.
// ──────────────────────────────────────────────

// ──────────────────────────────────────────────
// Parameters
// ──────────────────────────────────────────────

@description('Azure region for the Function App.')
param location string

@description('Name of the App Service Plan.')
param planName string

@description('Name of the Function App.')
param functionAppName string

@description('Name of the storage account for deployment artifacts.')
param storageAccountName string

@description('Application Insights connection string.')
param appInsightsConnectionString string
@description('Resource ID of the Application Insights component used for generation-capacity autoscale metrics. Empty disables the declarative autoscale resource for isolated module tests.')
param appInsightsResourceId string = ''

@description('Minimum/default App Service Plan instances. Production passes 2; non-production passes 1.')
@minValue(1)
param minInstances int = 1

@description('Maximum App Service Plan instances for generation-demand autoscale.')
@minValue(1)
param maxInstances int = 6

@description('Opt in to App Insights custom-metric autoscale only after the target subscription accepts those metrics. Azure rejected the dev rule with UnsupportedMetric on 2026-09-02, so the safe default is off.')
param generationAutoscaleEnabled bool = false

@description('Feature-gated rollout controls for additive performance read models and durable queues. Keep read paths off until their backfills have been compared.')
param performanceRollout object = {
  jobWorkIndexWriteEnabled: false
  jobWorkIndexReadEnabled: false
  jobWorkIndexCompareEnabled: false
  scheduleDueIndexWriteEnabled: false
  scheduleDueIndexReadEnabled: false
  scheduleDueIndexCompareEnabled: false
  jobReadModelsWriteEnabled: false
  jobReadModelsReadEnabled: false
  jobReadModelsCompareEnabled: false
  auditQueueEnabled: false
  deliveryQueueEnabled: false
}

@description('Key Vault URI for secret references.')
param keyVaultUri string

@description('Azure AI Services endpoint URL.')
param aiServicesEndpoint string

@description('Enable the pre-approval audio review (SoW "AI-based pre-review of generated audio"): the finished bulletin audio is transcribed via Azure AI Speech fast transcription on the same AIServices account and compared against the script before WaitingApproval. Emits AiSettings__AzureAI__SpeechEndpoint (empty = review disabled).')
param enableAudioPreReview bool = true
@description('Azure AI Services model deployment name (Azure OpenAI).')
param aiModelName string = 'gpt-5.6-luna'

@description('Fallback Azure AI Services model deployment name (Azure OpenAI). Empty disables the fallback. When set, a 429, transient 5xx, or stall-exhaustion on the primary deployment is retried once immediately on this deployment with no backoff. gpt-5.6-terra measured healthy while gpt-5.6-luna\'s shared pool was rejecting. Terra is a DIFFERENT model, so a bulletin served by the fallback may read differently from one served by the primary; that is deliberate because the alternative is a failed bulletin.')
param aiFallbackModelName string = ''
@description('Process-wide cap on concurrent ElevenLabs TTS requests; size to the plan concurrent-request limit. Emits AiSettings__ElevenLabs__MaxConcurrentRequests.')
@minValue(1)
@maxValue(64)
param elevenLabsMaxConcurrentRequests int = 2

@description('Per-deployment concurrent-send bulkhead for Azure OpenAI. Emits AiSettings__AzureAI__CallBudget__BulkheadPermits.')
@minValue(1)
@maxValue(64)
param azureOpenAiBulkheadPermits int = 4

@description('Allowed CORS origins.')
param corsAllowedOrigins string[] = []

@description('Whether the webhook notifier is enabled. Driven by main.bicep based on whether a webhook URL was supplied.')
param webhookEnabled bool = false

@description('Base URL of the ops console (Static Web App) for deep-links in notifications.')
param opsConsoleBaseUrl string = ''

@description('URL of the evergreen filler-cart WAV used as the dead-air fallback of last resort. Empty disables the filler leg of the fallback chain. MUST be set via IaC, never by hand: a manual az-CLI value is silently wiped on every Bicep redeploy (QT-001, found live 2026-07-05 — the dead-air chain was dead on dev for that reason).')
param fillerCartAudioUrl string = ''

@description('Filler-cart script text, synthesized per voice and cached per (voice × settings × model). Empty disables per-voice carts (evergreen fillerCartAudioUrl only). MUST be set via IaC, never by hand (QT-001).')
param fillerCartScript string = ''

@description('Tags to apply to all resources.')
param tags object = {}

@description('Optional subnet resource ID for VNet integration.')
param virtualNetworkSubnetId string = ''

@description('Route ALL outbound traffic (incl. public internet) through the VNet integration. Default true preserves prior behavior. Set false to send only RFC1918/private traffic (PEs + on-prem via hub) through the VNet and let internet SaaS (HERE, ElevenLabs) egress directly — required when the VNet is peered to a hub whose firewall breaks/inspects outbound TLS.')
param routeAllTraffic bool = true

@description('AAD tenant ID for Easy Auth. Required for fail-closed auth; do not use "common".')
param aadTenantId string

@description('AAD app registration client ID for Easy Auth. Required; empty value blocks deployment so the Function App is never shipped without authentication.')
param aadClientId string

@description('DEV-ONLY authorization fallback. App role (Operator or Admin) granted to every authenticated AAD caller so the operator console is usable before Entra app-role assignments exist. MUST be left empty in test/prod — a non-empty value bypasses RBAC.')
param devAuthFallbackRole string = ''

@description('DEV-ONLY debugging switch. Persists the full rendered prompt and raw model response for every LLM call to a dedicated blob container, keyed by job id, bounded to the most recent debugMaxCapturedJobs jobs. MUST be false in test/prod — the captured records include the full source traffic data and the full model output verbatim.')
param debugCaptureLlmCalls bool = false

@description('Rolling cap on distinct jobs kept when debugCaptureLlmCalls is true. Ignored otherwise.')
param debugMaxCapturedJobs int = 20

@description('Require a SECOND admin to approve config-bundle imports before they apply. MUST be true in prod.')
param configImportRequireSecondApprover bool = false

@description('Resource ID of the Log Analytics workspace for diagnostic settings. Leave empty to skip.')
param logAnalyticsWorkspaceId string = ''

@description('Platform-wide gate for HereWeatherProvider — a market only gets weather if BOTH this is true AND its own ContentSelection.Weather is true. In IaC because a hand-set app setting is wiped on every redeploy (QT-001, recurred 2026-07-08 for this exact setting — see decisions.md 2026-07-06 entry). Enabled in every environment since the 7 Jul 2026 live decision (supersedes the earlier "keep false in test/prod" note).')
param weatherEnabled bool = false

@description('Platform-wide gate for HereFuelPriceProvider — a market only gets fuel prices if BOTH this is true AND its own ContentSelection.Fuel is true. Same IaC-not-CLI rule as weatherEnabled (QT-001 pattern).')
param fuelEnabled bool = false
@description('Official authority-feed endpoints and corroboration settings. QldApiKey is loaded from the OfficialFeeds--QldApiKey Key Vault secret at runtime and is never supplied in this object.')
param officialFeeds object = {
  Enabled: true
  CorroborateIncidents: true
  InjectMissingOfficialIncidents: true
  MaxCorroborationDistanceMeters: 500
  NswHazardsBaseUrl: 'https://www.livetraffic.com/traffic/hazards'
  QldEventsUrl: 'https://api.qldtraffic.qld.gov.au/v2/events'
  VicDisruptionsUrl: 'https://opendata.transport.vic.gov.au/dataset/unplanned-disruptions-road/resource/e49bbb1b-4764-4736-a629-02c339ebaab1/download/unplanned_disruptions.json'
  WaTravelMapUrl: 'https://gisservices.mainroads.wa.gov.au/arcgis/rest/services/TravelInformation/MapServer'
  SaTrafficUrl: 'https://dit-cms-prod.geohub.sa.gov.au/arcgis/rest/services/TrafficSATime/TrafficSATime/MapServer/1/query?where=1%3D1&outFields=*&returnGeometry=false&f=pjson'
  TasAlertUrl: 'https://www.police.tas.gov.au/news-events/media-releases/feed/'
}

@description('Age in minutes below which a served stale official-feed payload is routine.')
@minValue(0)
param officialFeedFreshMinutes int = 15

@description('Hard maximum age in minutes for serving a stale official-feed payload.')
@minValue(1)
param officialFeedMaxStaleMinutes int = 60


@description('Master gate for playout delivery on job publish. IaC-not-CLI (QT-001 pattern).')
param zettaEnabled bool = false

@description('Playout delivery transport: AzureFilesDrop writes to an AD DS-authenticated Azure Files share on the drop account; BlobDrop writes to the SFTP-enabled container on the same account. Direct SMB into Zetta\'s own ingest share was removed 2026-07-31 — the drop-share model makes the playout side the SMB client, so nothing here speaks SMB.')
@allowed(['BlobDrop', 'AzureFilesDrop'])
param zettaTransport string = 'AzureFilesDrop'

@description('Storage account name of the delivery drop (BlobDrop/AzureFilesDrop transports only). The Function App MI needs Storage Blob Data Contributor (blob) and Storage File Data Privileged Contributor (files, OAuth-over-REST) on it (roles-delivery-drop.bicep).')
param zettaDropAccountName string = ''

@description('Container in the drop account that the playout SFTP local user pulls from (BlobDrop transport only).')
param zettaDropContainerName string = 'informationbulletins'

@description('Azure Files share name in the drop account that the playout side mounts over AD DS-authenticated SMB (AzureFilesDrop transport only).')
param zettaDropFileShareName string = 'informationbulletins'

@description('Playout file name template. {MarketId} is replaced by the market\'s configured Zetta code, exactly as ARN supplied it (ARN issued the convention 2026-07-30: [SYD]KIIS90s_TRAFF, [CAI]STAR1027_TRAFF). IaC-not-CLI (QT-001): hand-set on test 30 Jul, which the next deploy would have reverted.')
param zettaFileNamePattern string = '{MarketId}_TRAFF'

@description('JSON object mapping market id -> ARN\'s playout code, e.g. {"sydney":"[SYD]KIIS90s"}. This is the ONLY source of market codes: Azure rejects app-setting names containing a hyphen on Linux, so a hyphenated market like cairns-4ca has no expressible Zetta__MarketCodes__* key. A market absent here fails delivery loudly rather than inventing a code.')
param zettaMarketCodesJson string = ''

@description('Allow Azure CLI management tokens (audience https://management.azure.com/) as Easy Auth bearer tokens. DEV-ONLY convenience for headless/programmatic access until an automation SP with app roles exists. MUST stay false in test/prod (least privilege).')
param allowArmAudience bool = false

// ── Foundry agent pipeline settings (promoted from live dev 2026-07-16 so a Bicep
// redeploy can never wipe them again — QT-001 class). Empty string = unset: the app
// treats blank agent names / endpoint / effort as "use the raw-model path".
@description('Foundry project endpoint for prompt-agent calls (AiSettings__AzureAI__FoundryProjectEndpoint). Empty = raw-model path.')
param foundryProjectEndpoint string = ''
// Enabling this without foundryProjectEndpoint is a deployment misconfiguration:
// the agent names would be present but the endpoint required to resolve them would
// be blank. Keep both values in the same environment parameter set.
@description('Route the four workflow drafting stages through Azure AI Foundry agents so calls appear in the Foundry portal\'s Tracing tab. Disabled by default: the agent endpoint rejects request-side instructions and text fields, so the system prompt travels inside input and the raw route\'s strict json_schema contract is downgraded to the agent\'s baked json_object format. This does NOT improve latency; the agent route hits the same upstream ~60s stall.')
param enableWorkflowAgents bool = false






@description('Foundry prompt-agent name for the content-Safety stage. Compliance-reviewed: changing the agent (or its baked-in instructions) is a broadcast-standards change. Empty = raw model.')
param safetyAgentName string = ''

@description('Foundry prompt-agent name for the delivery-Director stage. Empty = raw model.')
param directorAgentName string = ''

@description('Responses-API reasoning.effort override (none|minimal|low|medium|high). Empty = derive per model family in code.')
param reasoningEffort string = ''



// ──────────────────────────────────────────────
// References & Variables
// ──────────────────────────────────────────────

// Functions whose triggers must NOT run on the staging slot so the staging
// slot doesn't compete with production for queue/timer work during deploys.
// These settings are slot-sticky (see slotConfigNames below).
var slotDisabledFunctionNames = [
  'BroadcastQueueProcessor'
  'RegenerationQueueProcessor'
  'BroadcastPoisonQueue'
  'ActivityAuditQueueProcessor'
  'ActivityAuditPoisonQueue'
  'LessonLearningQueueProcessor'
  'LessonLearningPoisonQueue'
  'DeliveryQueueProcessor'
  'DeliveryPoisonQueue'
  'AuditExport'
  'JobRetention'
  'PreviewAudioCleanup'
  'QueueDepthMetrics'
  'SchedulePoller'
]

var productionTriggerEnableSettings = [
  for fn in slotDisabledFunctionNames: {
    name: 'AzureWebJobs.${fn}.Disabled'
    value: '0'
  }
]

var stagingTriggerDisableSettings = [
  for fn in slotDisabledFunctionNames: {
    name: 'AzureWebJobs.${fn}.Disabled'
    value: '1'
  }
]

var slotStickyTriggerSettingNames = [
  for fn in slotDisabledFunctionNames: 'AzureWebJobs.${fn}.Disabled'
]

// Shared app settings used by both the production slot and the staging slot.
// Slot-specific values (trigger enable/disable) are appended per slot below.
var commonAppSettings = [
  { name: 'AzureWebJobsStorage__accountName', value: storageAccountName }
  { name: 'AzureWebJobsStorage__blobServiceUri', value: 'https://${storageAccountName}.blob.${environment().suffixes.storage}' }
  { name: 'AzureWebJobsStorage__queueServiceUri', value: 'https://${storageAccountName}.queue.${environment().suffixes.storage}' }
  { name: 'AzureWebJobsStorage__tableServiceUri', value: 'https://${storageAccountName}.table.${environment().suffixes.storage}' }
  { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
  { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
  { name: 'WEBSITE_RUN_FROM_PACKAGE', value: '1' }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
  { name: 'OfficialFeeds__Enabled', value: officialFeeds.Enabled ? 'true' : 'false' }
  { name: 'OfficialFeeds__CorroborateIncidents', value: officialFeeds.CorroborateIncidents ? 'true' : 'false' }
  { name: 'OfficialFeeds__InjectMissingOfficialIncidents', value: officialFeeds.InjectMissingOfficialIncidents ? 'true' : 'false' }
  { name: 'OfficialFeeds__MaxCorroborationDistanceMeters', value: string(officialFeeds.MaxCorroborationDistanceMeters) }
  { name: 'OfficialFeeds__NswHazardsBaseUrl', value: officialFeeds.NswHazardsBaseUrl }
  { name: 'OfficialFeeds__QldEventsUrl', value: officialFeeds.QldEventsUrl }
  // Deliberately blank: the deployed value arrives from Key Vault secret
  // OfficialFeeds--QldApiKey through Program.cs AddAzureKeyVault.
  { name: 'OfficialFeeds__QldApiKey', value: '' }
  { name: 'OfficialFeeds__VicDisruptionsUrl', value: officialFeeds.VicDisruptionsUrl }
  { name: 'OfficialFeeds__WaTravelMapUrl', value: officialFeeds.WaTravelMapUrl }
  { name: 'OfficialFeeds__SaTrafficUrl', value: officialFeeds.SaTrafficUrl }
  { name: 'OfficialFeeds__TasAlertUrl', value: officialFeeds.TasAlertUrl }
  { name: 'KeyVaultUri', value: keyVaultUri }
  // NO @Microsoft.KeyVault(...) references in this list, deliberately. App Service
  // resolves those in the platform configuration layer, which egresses from the site's
  // PUBLIC outbound IP and does NOT honour VNet integration. Against a vault with
  // publicNetworkAccess='Disabled' every resolution therefore 403s ("Public network
  // access is disabled and request is not from a trusted service nor via an approved
  // private link") on App Service's ~24h reference-refresh cycle and on every restart,
  // which is what fired IBP-AUE-<ENV>-FNA-APP01-kv-access-denied continuously (verified
  // in prod 2026-08-12: 7 denials per cycle daily since at least 13 Jul, 100% of them
  // from the public IP, 100% of private-endpoint calls OK).
  // Program.cs already loads the whole vault at startup with AddAzureKeyVault over the
  // private endpoint, and it registers AFTER AddEnvironmentVariables so vault values take
  // precedence regardless. A secret reaches the app under its `--` name mapped to `:`
  // (Here--ApiKey => Here:ApiKey => Here__ApiKey). Do not re-add references here.
  { name: 'Providers__OutputProvider', value: 'AzureBlob' }
  { name: 'Broadcast__OfficialFeedFreshMinutes', value: string(officialFeedFreshMinutes) }
  { name: 'Broadcast__OfficialFeedMaxStaleMinutes', value: string(officialFeedMaxStaleMinutes) }
  { name: 'AiSettings__AzureAI__Endpoint', value: aiServicesEndpoint }
  { name: 'AiSettings__AzureAI__SpeechEndpoint', value: enableAudioPreReview ? aiServicesEndpoint : '' }
  { name: 'AiSettings__AzureAI__ApiKey', value: '' }
  { name: 'AiSettings__AzureAI__Model', value: aiModelName }
  { name: 'AiSettings__AzureAI__CallBudget__BulkheadPermits', value: string(azureOpenAiBulkheadPermits) }
  // Fallback settings apply only to the route in use: the raw route swaps the model,
  // while the agent route swaps the agent. These are independent settings.
  { name: 'AiSettings__AzureAI__FallbackModel', value: aiFallbackModelName }
  { name: 'AiSettings__AzureAI__WorkflowAgents__writeIncidents', value: enableWorkflowAgents ? 'arn-wf-write-incidents' : '' }
  { name: 'AiSettings__AzureAI__WorkflowAgents__writeFlow', value: enableWorkflowAgents ? 'arn-wf-write-flow' : '' }
  { name: 'AiSettings__AzureAI__WorkflowAgents__compose2', value: enableWorkflowAgents ? 'arn-wf-compose' : '' }
  { name: 'AiSettings__AzureAI__WorkflowAgents__edit', value: enableWorkflowAgents ? 'arn-wf-edit' : '' }
  { name: 'AiSettings__AzureAI__FallbackWorkflowAgents__writeIncidents', value: enableWorkflowAgents && !empty(aiFallbackModelName) ? 'arn-wf-write-incidents-terra' : '' }
  { name: 'AiSettings__AzureAI__FallbackWorkflowAgents__writeFlow', value: enableWorkflowAgents && !empty(aiFallbackModelName) ? 'arn-wf-write-flow-terra' : '' }
  { name: 'AiSettings__AzureAI__FallbackWorkflowAgents__compose2', value: enableWorkflowAgents && !empty(aiFallbackModelName) ? 'arn-wf-compose-terra' : '' }
  { name: 'AiSettings__AzureAI__FallbackWorkflowAgents__edit', value: enableWorkflowAgents && !empty(aiFallbackModelName) ? 'arn-wf-edit-terra' : '' }
  { name: 'Storage__AccountName', value: storageAccountName }
  // Every production table is provisioned by storage.bicep before the app module runs.
  // Local Azurite still forces runtime ensure on regardless of this hosted setting.
  { name: 'Storage__RuntimeEnsureEnabled', value: 'false' }
  // One busy market touches 30k+ distinct map segments across traffic, flow and continuity.
  // Retain a full multi-market working set so the persistent cache is a cold-start layer, not
  // hundreds of Table queries on every bulletin.
  { name: 'MapAttributesCache__L1SizeLimit', value: '200000' }
  { name: 'MapAttributesCache__L1ByteLimit', value: '67108864' }
  { name: 'Here__UseBatchGeocoding', value: 'true' }
  { name: 'Weather__Enabled', value: weatherEnabled ? 'true' : 'false' }
  { name: 'Fuel__Enabled', value: fuelEnabled ? 'true' : 'false' }
  { name: 'AiSettings__ElevenLabs__ModelId', value: 'eleven_v3' }
  { name: 'AiSettings__ElevenLabs__LanguageCode', value: 'en' }
  { name: 'AiSettings__ElevenLabs__BaseUrl', value: 'https://api.elevenlabs.io/v1/text-to-speech' }
  { name: 'AiSettings__ElevenLabs__MinSpeed', value: '0.7' }
  { name: 'AiSettings__ElevenLabs__MaxSpeed', value: '1.2' }
  { name: 'AiSettings__ElevenLabs__MaxConcurrentRequests', value: string(elevenLabsMaxConcurrentRequests) }
  { name: 'AiSettings__ElevenLabs__OutputFormat', value: 'pcm_44100' }
  { name: 'Webhook__Enabled', value: webhookEnabled ? 'true' : 'false' }
  { name: 'Webhook__OpsConsoleBaseUrl', value: opsConsoleBaseUrl }
  { name: 'Broadcast__FillerCartAudioUrl', value: fillerCartAudioUrl }
  { name: 'Broadcast__FillerCartScript', value: fillerCartScript }
  // conversational-v1 owns review/refine; never re-enable the retired outer LLM loop via the code default.
  { name: 'Broadcast__MaxReviewRefineRetries', value: '0' }
  { name: 'Zetta__Enabled', value: zettaEnabled ? 'true' : 'false' }
  { name: 'Zetta__Transport', value: zettaTransport }
  { name: 'Zetta__DropAccountName', value: zettaDropAccountName }
  { name: 'Zetta__DropContainerName', value: zettaDropContainerName }
  { name: 'Zetta__DropFileShareName', value: zettaDropFileShareName }
  { name: 'Zetta__FileNamePattern', value: zettaFileNamePattern }
  // Market codes live in ONE JSON setting. Azure rejects app-setting names containing a
  // hyphen on Linux, so cairns-4ca could never have a per-market key. As of 2026-08-30 the
  // per-key Zetta__MarketCodes__* fallback is GONE from the code as well: the six legacy
  // three-letter codes (SYD/MEL/BNE/ADL/PER/GLD) shipped in the package defaults were never
  // confirmed by ARN and did not match the proven convention ([SYD]KIIS90s), so a second
  // source could only ever disagree about an on-air filename. This setting is now the ONLY
  // source, and an unmapped market fails delivery rather than guessing a name.
  { name: 'Zetta__MarketCodesJson', value: zettaMarketCodesJson }
  { name: 'DurationPrediction__Mode', value: 'HistoryBased' }
  { name: 'DurationPrediction__FallbackSyllablesPerSecond', value: '5.8' }
  // Plain value, not a Key Vault reference or secret: an Entra client ID is public (it is
  // already inline in this template's Easy Auth registration and in every bicepparam), and
  // AuthorizationMiddleware only needs to compare it. Same reasoning as AadTenantId below.
  { name: 'AadClientId', value: aadClientId }
  // AadTenantId is needed at runtime to pin the issuer and signing keys when validating
  // the Static Web Apps rolesSource access token. It is NOT a secret (a tenant id is
  // public), so it is a plain value rather than a Key Vault reference.
  { name: 'AadTenantId', value: aadTenantId }
  // No AadClientSecret. The Function App is a resource server: Easy Auth runs
  // unauthenticatedClientAction Return401 and validates bearer JWTs against the OIDC JWKS,
  // and AuthorizationMiddleware reads only AadClientId. A client secret is only used in an
  // authorization-code exchange, which this app never performs — the SWA's custom Entra
  // provider does that on test/prod, and main.bicep feeds it the secret directly via
  // getSecret() at deploy time. The setting used to be an unconditional Key Vault reference
  // to a secret dev deliberately never populates, so it sat permanently unresolved.
  { name: 'Auth__DevFallbackRole', value: devAuthFallbackRole }
  { name: 'ConfigImport__RequireSecondApprover', value: string(configImportRequireSecondApprover) }
  { name: 'Debug__CaptureLlmCalls', value: string(debugCaptureLlmCalls) }
  { name: 'Debug__MaxCapturedJobs', value: string(debugMaxCapturedJobs) }
  { name: 'Broadcast__UseAudioProxy', value: 'true' }
  { name: 'Jobs__WorkIndex__WriteEnabled', value: string(performanceRollout.jobWorkIndexWriteEnabled) }
  { name: 'Jobs__WorkIndex__ReadEnabled', value: string(performanceRollout.jobWorkIndexReadEnabled) }
  { name: 'Jobs__WorkIndex__CompareEnabled', value: string(performanceRollout.jobWorkIndexCompareEnabled) }
  { name: 'Schedules__DueIndex__WriteEnabled', value: string(performanceRollout.scheduleDueIndexWriteEnabled) }
  { name: 'Schedules__DueIndex__ReadEnabled', value: string(performanceRollout.scheduleDueIndexReadEnabled) }
  { name: 'Schedules__DueIndex__CompareEnabled', value: string(performanceRollout.scheduleDueIndexCompareEnabled) }
  { name: 'Jobs__Persistence__TimelineWriteEnabled', value: string(performanceRollout.jobReadModelsWriteEnabled) }
  { name: 'Jobs__Persistence__TimelineReadEnabled', value: string(performanceRollout.jobReadModelsReadEnabled) }
  { name: 'Jobs__Persistence__PayloadWriteEnabled', value: string(performanceRollout.jobReadModelsWriteEnabled) }
  { name: 'Jobs__Persistence__PayloadReadEnabled', value: string(performanceRollout.jobReadModelsReadEnabled) }
  { name: 'Jobs__Persistence__ReadIndexWriteEnabled', value: string(performanceRollout.jobReadModelsWriteEnabled) }
  { name: 'Jobs__Persistence__ReadIndexReadEnabled', value: string(performanceRollout.jobReadModelsReadEnabled) }
  { name: 'Jobs__Persistence__LastSuccessfulWriteEnabled', value: string(performanceRollout.jobReadModelsWriteEnabled) }
  { name: 'Jobs__Persistence__LastSuccessfulReadEnabled', value: string(performanceRollout.jobReadModelsReadEnabled) }
  { name: 'Jobs__Persistence__LedgerWriteEnabled', value: string(performanceRollout.jobReadModelsWriteEnabled) }
  { name: 'Jobs__Persistence__LedgerReadEnabled', value: string(performanceRollout.jobReadModelsReadEnabled) }
  { name: 'Jobs__Persistence__CompareEnabled', value: string(performanceRollout.jobReadModelsCompareEnabled) }
  { name: 'Jobs__Ledger__WriteEnabled', value: string(performanceRollout.jobReadModelsWriteEnabled) }
  { name: 'Jobs__Ledger__ReadEnabled', value: string(performanceRollout.jobReadModelsReadEnabled) }
  { name: 'Jobs__Ledger__CompareEnabled', value: string(performanceRollout.jobReadModelsCompareEnabled) }
  { name: 'Audit__Queue__Enabled', value: string(performanceRollout.auditQueueEnabled) }
  { name: 'DeliveryQueue__Enabled', value: string(performanceRollout.deliveryQueueEnabled) }
  { name: 'AiSettings__AzureAI__ReasoningEffort', value: reasoningEffort }
  { name: 'AiSettings__AzureAI__FoundryProjectEndpoint', value: foundryProjectEndpoint }
  { name: 'AiSettings__AzureAI__SafetyAgentName', value: safetyAgentName }
  // DirectorAgentName MUST be emitted here. It was declared and threaded through main.bicep but
  // never written as an app setting, while the live dev app carried it from an earlier deploy —
  // so IaC and reality disagreed and a clean redeploy would have silently dropped the Director
  // stage back to the raw model. Empty value = raw model, which is the documented fallback in
  // AzureAIOptions.AgentNameFor.
  { name: 'AiSettings__AzureAI__DirectorAgentName', value: directorAgentName }
  // Zip-deploy plumbing previously live-only: kept here so a full Bicep appsettings
  // write can never strip them (QT-001 class).
  { name: 'SCM_DO_BUILD_DURING_DEPLOYMENT', value: 'false' }
  { name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE', value: 'true' }
  { name: 'WEBSITE_ENABLE_SYNC_UPDATE_SITE', value: 'true' }
]

// Auth settings shared by the production and staging slots.
var authSettingsProperties = {
  platform: {
    enabled: true
  }
  globalValidation: {
    requireAuthentication: true
    unauthenticatedClientAction: 'Return401'
    excludedPaths: [
      '/api/health'
      '/api/health/dependencies'
      '/api/GetRoles'
    ]
  }
  identityProviders: {
    azureActiveDirectory: {
      enabled: true
      registration: {
        // v1 issuer (no /v2.0): AAD app registrations default to v1 access
        // tokens (requestedAccessTokenVersion=null), whose iss claim is
        // https://sts.windows.net/{tenant}/. A /v2.0 suffix here breaks
        // bearer-token validation (401) — found in sandbox validation 11 Jun 2026.
        openIdIssuer: 'https://sts.windows.net/${aadTenantId}/'
        // Inline clientId, NOT clientIdSettingName: SWA backend-linking rewrites
        // authsettingsV2 and drops clientIdSettingName, leaving the AAD provider
        // with no client ID (all tokens rejected, /.auth/login returns 400).
        // Found in sandbox validation 11 Jun 2026.
        clientId: aadClientId
        // clientSecretSettingName deliberately omitted — see the AadClientSecret note above.
      }
      validation: {
        // ARM audience is DEV-ONLY (allowArmAudience): it lets Azure CLI management tokens
        // act as bearer tokens for headless/programmatic access without an app-role SP.
        // Test/prod exclude it (least privilege; SEC finding 2026-07-16).
        allowedAudiences: concat(
          ['api://${aadClientId}'],
          allowArmAudience ? ['https://management.azure.com/'] : []
        )
        // No defaultAuthorizationPolicy here. An explicit empty
        // allowedPrincipals.identities list is treated by Easy Auth as
        // deny-all (HTTP 403 for every validated principal, including
        // app-only client-credential tokens) — found in sandbox validation
        // 11 Jun 2026. Authorization (Operator/Admin roles) is enforced by
        // AuthorizationMiddleware in the Functions worker instead.
      }
    }
  }
}

// ──────────────────────────────────────────────
// Resources
// ──────────────────────────────────────────────

@description('App Service Plan (P0v3 PremiumV3 with Always On).')
resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    tier: 'PremiumV3'
    name: 'P0v3'
    capacity: minInstances
  }
  properties: {
    reserved: true
  }
}

@description('Function App (Linux, .NET 10 isolated worker).')
resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    slotConfigNames: {
      appSettingNames: union(
        [
          'AzureWebJobsStorage__accountName'
          'APPLICATIONINSIGHTS_CONNECTION_STRING'
          'KeyVaultUri'
          'AiSettings__AzureAI__Endpoint'
          'Storage__AccountName'
        ],
        slotStickyTriggerSettingNames
      )
    }
    virtualNetworkSubnetId: !empty(virtualNetworkSubnetId) ? virtualNetworkSubnetId : null
    siteConfig: {
      alwaysOn: true
      healthCheckPath: '/api/health'
      vnetRouteAllEnabled: !empty(virtualNetworkSubnetId) && routeAllTraffic
      linuxFxVersion: 'DOTNET-ISOLATED|10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      cors: {
        allowedOrigins: corsAllowedOrigins
      }
      appSettings: union(commonAppSettings, productionTriggerEnableSettings)
    }
  }
}

// ──────────────────────────────────────────────
// VNet Regional Integration
// ──────────────────────────────────────────────

@description('VNet regional integration when virtualNetworkSubnetId is provided.')
resource vnetIntegration 'Microsoft.Web/sites/networkConfig@2024-04-01' = if (!empty(virtualNetworkSubnetId)) {
  parent: functionApp
  name: 'virtualNetwork'
  properties: {
    subnetResourceId: virtualNetworkSubnetId
    swiftSupported: true
  }
}

// ──────────────────────────────────────────────
// Easy Auth (AAD Authentication)
// ──────────────────────────────────────────────

@description('AAD authentication settings for the Function App. Fail-closed: requireAuthentication=true, Return401 for unauthenticated callers, /api/health and /api/health/dependencies excluded.')
resource authsettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: functionApp
  name: 'authsettingsV2'
  properties: authSettingsProperties
}
// ──────────────────────────────────────────────────────────────────────
// Generation-demand autoscale
// ──────────────────────────────────────────────────────────────────────
// QueueDepthMetricsTrigger emits generation-only custom metrics, but Azure rejected these
// metrics as an autoscale source in dev on 2026-09-02 (UnsupportedMetric). Keep the resource
// opt-in so the rest of the additive performance infrastructure remains deployable. The
// existing capacity policy stays untouched until the provider accepts the metric contract.
resource planAutoscaleSettings 'Microsoft.Insights/autoscaleSettings@2022-10-01' = if (generationAutoscaleEnabled && !empty(appInsightsResourceId)) {
  name: '${planName}-autoscale'
  location: location
  tags: tags
  properties: {
    name: '${planName}-autoscale'
    enabled: true
    targetResourceUri: plan.id
    targetResourceLocation: location
    profiles: [
      {
        name: 'generation-demand'
        capacity: {
          minimum: string(minInstances)
          default: string(minInstances)
          maximum: string(maxInstances)
        }
        rules: [
          {
            metricTrigger: {
              metricName: 'broadcast.queue.ready.depth'
              metricNamespace: 'Microsoft.Insights/components'
              metricResourceUri: appInsightsResourceId
              metricResourceLocation: location
              dimensions: [
                {
                  DimensionName: 'queue'
                  Operator: 'Equals'
                  Values: [
                    'broadcast-queue'
                  ]
                }
              ]
              dividePerInstance: false
              statistic: 'Average'
              timeAggregation: 'Average'
              timeGrain: 'PT1M'
              timeWindow: 'PT2M'
              operator: 'GreaterThan'
              threshold: 4
            }
            scaleAction: {
              direction: 'Increase'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT5M'
            }
          }
          {
            metricTrigger: {
              metricName: 'broadcast.queue.ready.depth'
              metricNamespace: 'Microsoft.Insights/components'
              metricResourceUri: appInsightsResourceId
              metricResourceLocation: location
              dimensions: [
                {
                  DimensionName: 'queue'
                  Operator: 'Equals'
                  Values: [
                    'regeneration-queue'
                  ]
                }
              ]
              dividePerInstance: false
              statistic: 'Average'
              timeAggregation: 'Average'
              timeGrain: 'PT1M'
              timeWindow: 'PT2M'
              operator: 'GreaterThan'
              threshold: 4
            }
            scaleAction: {
              direction: 'Increase'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT5M'
            }
          }
          {
            metricTrigger: {
              metricName: 'broadcast.queue.oldest_age_seconds'
              metricNamespace: 'Microsoft.Insights/components'
              metricResourceUri: appInsightsResourceId
              metricResourceLocation: location
              dimensions: [
                {
                  DimensionName: 'queue'
                  Operator: 'Equals'
                  Values: [
                    'broadcast-queue'
                  ]
                }
              ]
              dividePerInstance: false
              statistic: 'Average'
              timeAggregation: 'Average'
              timeGrain: 'PT1M'
              timeWindow: 'PT2M'
              operator: 'GreaterThan'
              threshold: 60
            }
            scaleAction: {
              direction: 'Increase'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT5M'
            }
          }
          {
            metricTrigger: {
              metricName: 'broadcast.queue.oldest_age_seconds'
              metricNamespace: 'Microsoft.Insights/components'
              metricResourceUri: appInsightsResourceId
              metricResourceLocation: location
              dimensions: [
                {
                  DimensionName: 'queue'
                  Operator: 'Equals'
                  Values: [
                    'regeneration-queue'
                  ]
                }
              ]
              dividePerInstance: false
              statistic: 'Average'
              timeAggregation: 'Average'
              timeGrain: 'PT1M'
              timeWindow: 'PT2M'
              operator: 'GreaterThan'
              threshold: 60
            }
            scaleAction: {
              direction: 'Increase'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT5M'
            }
          }
          {
            metricTrigger: {
              metricName: 'broadcast.queue.scale_in.eligible'
              metricNamespace: 'Microsoft.Insights/components'
              metricResourceUri: appInsightsResourceId
              metricResourceLocation: location
              dimensions: []
              dividePerInstance: false
              statistic: 'Average'
              timeAggregation: 'Average'
              timeGrain: 'PT1M'
              timeWindow: 'PT15M'
              operator: 'GreaterThan'
              threshold: 0
            }
            scaleAction: {
              direction: 'Decrease'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT15M'
            }
          }
        ]
      }
    ]
  }
}
// ──────────────────────────────────────────────────────────────────────

// ──────────────────────────────────────────────
// Deployment Slots
// ──────────────────────────────────────────────

@description('Staging deployment slot for the Function App. Queue/timer triggers are disabled here via slot-sticky AzureWebJobs.<Function>.Disabled=1 so staging does not compete with production for messages.')
resource stagingSlot 'Microsoft.Web/sites/slots@2024-04-01' = {
  parent: functionApp
  name: 'staging'
  location: location
  tags: tags
  kind: 'functionapp,linux'
  // The slot needs its own system identity: slots do NOT inherit the parent
  // site's identity, and the app settings point at identity-based storage
  // (AzureWebJobsStorage__accountName). Without this the staging host cannot
  // reach storage at all ("Unable to access AzureWebJobsStorage" — observed
  // in sandbox validation 11 Jun 2026). Role assignments for this principal
  // are granted alongside production's in the roles-* modules.
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      alwaysOn: true
      healthCheckPath: '/api/health'
      linuxFxVersion: 'DOTNET-ISOLATED|10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      cors: {
        allowedOrigins: corsAllowedOrigins
      }
      appSettings: union(commonAppSettings, stagingTriggerDisableSettings)
    }
  }
}

@description('AAD authentication settings for the staging slot. Same fail-closed policy as production.')
resource stagingAuthSettings 'Microsoft.Web/sites/slots/config@2024-04-01' = {
  parent: stagingSlot
  name: 'authsettingsV2'
  properties: authSettingsProperties
}

// ──────────────────────────────────────────────
// Diagnostic Settings
// ──────────────────────────────────────────────

@description('Streams Function App logs and metrics to Log Analytics.')
resource functionAppDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (!empty(logAnalyticsWorkspaceId)) {
  name: 'functionAppDiagnostics'
  scope: functionApp
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'FunctionAppLogs'
        enabled: true
      }
      {
        category: 'AppServiceAuthenticationLogs'
        enabled: true
      }
      {
        category: 'AppServiceAuditLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

// ──────────────────────────────────────────────
// Outputs
// ──────────────────────────────────────────────

@description('Principal ID of the Function App managed identity.')
output principalId string = functionApp.identity.principalId

@description('Principal ID of the staging slot managed identity.')
output stagingPrincipalId string = stagingSlot.identity.principalId

@description('Name of the Function App.')
output name string = functionApp.name

@description('Default hostname of the Function App.')
output defaultHostName string = functionApp.properties.defaultHostName

@description('Resource ID of the Function App.')
output id string = functionApp.id
