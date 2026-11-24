// ============================================================================
// Main Bicep Orchestrator — Information Bulletin Platform (IBP)
// Deploys all Azure resources for the ARN automated traffic-bulletin system.
//
// Subscription-scope deployment across ARN's confirmed resource-group layout
// (docs/reference/azure-naming-convention.md, confirmed with the ARN technology lead
// 11 Jun 2026):
//
//   IBP-AUE-{ENV}-RSG-APP01  — application (Functions, SWA, Logic App, AIN, FDY, FIQ)
//   IBP-AUE-{ENV}-RSG-NTW01  — private endpoints (VNet/subnets/NSGs are ARN-managed)
//   IBP-AUE-{ENV}-RSG-KYV01  — Key Vault
//   IBP-AUE-{ENV}-RSG-STA01  — storage
//   IBP-AUE-{ENV}-RSG-LAW01  — Log Analytics, ONLY when logging stays IBP-side
//
// Resources are named IBP-AUE-{ENV}-{TYPE}-APP01. Storage accounts are
// lowercase concatenated (ibpaue{env}staapp01); Azure AI Search requires a
// lowercase name, so FIQ is lowercased.
//
// ── OPEN ITEMS (placeholders — see deployment/README.md) ────────────────────
//   1. logAnalyticsWorkspaceResourceId: ARN's CSV points at central logging in
//      their MGT subscription, but their RG list still carries an IBP LAW RG
//      (question with the ARN technology lead). Empty → an IBP-side workspace is created in
//      RSG-LAW01. Set to the MGT workspace resource ID once confirmed.
//   2. actionGroupId: ARN's central action groups are mgt-aue-ibp-agp-{Purpose}
//      (resource IDs pending). Empty → a local IBP-AUE-{ENV}-AIA-APP01 group is
//      created from alertEmailReceivers/alertSmsReceivers.
//   3. Dedicated IBP subscription unconfirmed: this template deploys into
//      whatever subscription the deployment context targets. Fallback agreed
//      with ARN: IBP-named RGs inside the existing ARN subscription.
//   4. Subnet + private DNS zone IDs pending the ARN network team.
// ============================================================================

targetScope = 'subscription'

// ---------- Parameters ----------

@description('Environment. Maps to the {ENV} naming segment: DEV / TST / PRD / SBX.')
@allowed([
  'dev'
  'test'
  'prod'
  'sbx'
])
param environment string

@description('Primary Azure region. New IBP environments are australiaeast per ARN (the PoC was australiasoutheast).')
@allowed([
  'australiaeast'
  'australiasoutheast'
])
param location string = 'australiaeast'

@description('Azure region for AI Services (not available in australiasoutheast).')
@allowed([
  'australiaeast'
  'eastus'
  'westus'
])
param aiServicesLocation string = 'australiaeast'

@description('Azure region for Static Web App ([limited availability](https://learn.microsoft.com/azure/static-web-apps/overview#regional-availability)). The SWA keeps the AUE name segment per ARN\'s CSV even though the resource lives here.')
param swaLocation string = 'eastasia'

@description('Custom domain for the SWA operator console (e.g. bulletins-dev.arn.com.au). Requires the ARN DNS CNAME to the SWA default hostname to exist. Empty skips domain binding.')
param swaCustomDomain string = ''

@description('Enable SWA enterprise-grade edge (managed Front Door CDN, ~USD 17.52/app/month). Requires swaCustomDomain bound + validated on a PRIOR deploy — two-step rollout (edge-enabled apps reject new CNAME-validated domains).')
param swaEnterpriseEdge bool = false

@description('Deploy the second Static Web App that hosts the ARN handover documentation site (IBP-AUE-{ENV}-SWA-DOC01). Off by default: only dev and prod publish the docs. The site carries no secrets and no AAD configuration, because ARN must be able to read it after vendor identities lapse.')
param deployDocsSite bool = false

@description('Create the resource groups as part of this deployment. Set to false when ARN pre-creates them (ARN owns RG creation + access grants).')
param createResourceGroups bool = true

@description('PLACEHOLDER (open item 1): resource ID of ARN\'s central Log Analytics workspace (MGT subscription). Leave empty to create an IBP-side workspace in IBP-AUE-{ENV}-RSG-LAW01 instead. Pending ARN\'s MGT-vs-IBP answer, 11 Jun 2026.')
param logAnalyticsWorkspaceResourceId string = ''

@description('PLACEHOLDER (open item 2): resource ID of an EXTERNAL Azure Monitor action group (ARN central: mgt-aue-ibp-agp-{Purpose}) to use instead of provisioning one. Leave empty to create IBP-AUE-{ENV}-AIA-APP01 from alertEmailReceivers/alertSmsReceivers.')
param actionGroupId string = ''

@description('Email receivers for the action group provisioned by this template. Each entry: { name, emailAddress }. Ignored when actionGroupId is supplied.')
param alertEmailReceivers array = []

@description('SMS receivers for the action group provisioned by this template. Each entry: { name, countryCode, phoneNumber }. Ignored when actionGroupId is supplied.')
param alertSmsReceivers array = []

@description('Route Azure Monitor alerts into the same Teams channel the app already posts to, by adding the notification Logic App as a webhook receiver on the action group. Requires a resolvable webhook URL (deployNotifications=true, or an explicit webhookEndpointUrl). Ignored when actionGroupId points at an external ARN action group. Email/SMS receivers still fire — keep at least one, because an alert about this subscription being down should not depend on a Logic App inside it.')
param routeAlertsToTeams bool = false

@description('Webhook URL for Teams notifications. When deployNotifications is true, this is auto-populated from the Logic App trigger URL. When false, supply an external URL or leave empty to disable.')
param webhookEndpointUrl string = ''

@description('Deploy the Logic App notification module (Teams Adaptive Cards). When true, provisions the Logic App and Teams API connection. The Teams connection requires one-time manual authorisation via the Portal.')
param deployNotifications bool = false

@description('Teams group (team) ID for notifications. Required when deployNotifications is true.')
param teamsGroupId string = ''

@description('Teams channel ID for notifications. Required when deployNotifications is true.')
param teamsChannelId string = ''

@description('URL of the evergreen filler-cart WAV (dead-air fallback of last resort). Empty disables the filler leg. Keep this in the env bicepparam — a hand-set app setting is wiped on redeploy (QT-001).')
param fillerCartAudioUrl string = ''

@description('Filler-cart script text, synthesized per voice and cached. Empty disables per-voice carts (evergreen fillerCartAudioUrl only). Keep in the env bicepparam — a hand-set app setting is wiped on redeploy (QT-001).')
param fillerCartScript string = ''

@description('Platform-wide gate for weather content (HereWeatherProvider). Keep this in the env bicepparam — a hand-set app setting is wiped on redeploy (QT-001, recurred 2026-07-08). Enabled everywhere since the 7 Jul 2026 live decision (supersedes the 2026-07-06 "keep false in test/prod" note).')
param weatherEnabled bool = false

@description('Platform-wide gate for fuel-price content (HereFuelPriceProvider). Same IaC-not-CLI rule as weatherEnabled.')
param fuelEnabled bool = false
// Official-feed configuration is one object so the six authority endpoints and
// corroboration switches stay in lockstep across environments. QldApiKey is
// intentionally not a template value: the Function App loads the per-environment
// `OfficialFeeds--QldApiKey` secret from Key Vault at runtime.
@description('Official authority-feed endpoints and corroboration settings. Keep QldApiKey out of Bicep; it is the OfficialFeeds--QldApiKey Key Vault secret.')
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


@description('Master gate for playout delivery on job publish (Zetta section). Same IaC-not-CLI rule as weatherEnabled (QT-001).')
param zettaEnabled bool = false

@description('Playout delivery transport: AzureFilesDrop writes to an AD DS-authenticated Azure Files share on ARN\'s drop account (the live transport everywhere); BlobDrop writes to the SFTP-enabled container on the same account. The original direct-SMB-into-Zetta transport was removed 2026-07-31, never having been enabled. IaC-not-CLI (QT-001).')
@allowed(['BlobDrop', 'AzureFilesDrop'])
param zettaTransport string = 'AzureFilesDrop'

@description('Storage account name of the delivery drop (BlobDrop/AzureFilesDrop transports only). Provisioned by ARN outside this template (ibpauetststaftp01). Empty skips the delivery-drop role assignments.')
param zettaDropAccountName string = ''

@description('Resource group holding the delivery drop account. Empty (default) means it sits in this environment\'s own storage RG. ARN built ONE drop account, in IBP-AUE-TST-RSG-STA01, and confirmed 2026-07-30 that the same share is the production playout folder — so prod must reach across RGs to it.')
param zettaDropAccountResourceGroup string = ''

@description('Container in the drop account that the playout SFTP local user pulls from (BlobDrop transport only).')
param zettaDropContainerName string = 'informationbulletins'

@description('Azure Files share name in the drop account that the playout side mounts over AD DS-authenticated SMB (AzureFilesDrop transport only).')
param zettaDropFileShareName string = 'informationbulletins'

@description('Playout file name template; {MarketId} is replaced by the market\'s configured Zetta code exactly as ARN supplied it. ARN issued the convention 2026-07-30.')
param zettaFileNamePattern string = '{MarketId}_TRAFF'

@description('JSON map of market id -> ARN playout code, e.g. {"sydney":"[SYD]KIIS90s","cairns-4ca":"[CAI]STAR1027"}. The only source of market codes; a market absent here fails delivery loudly. Azure forbids hyphens in app-setting names on Linux, which is why this is one JSON value rather than Zetta__MarketCodes__* keys.')
param zettaMarketCodesJson string = ''

@description('Allow Azure CLI management tokens as Easy Auth bearer tokens on the Function App. DEV-ONLY headless-access lever until an automation SP with app roles exists; MUST stay false in test/prod. Only the dev bicepparam sets it true.')
param allowArmAudience bool = false

// ── Foundry agent pipeline settings (promoted from live dev 2026-07-16, QT-001 class:
// keep in the env bicepparam so a Bicep redeploy can never wipe them). Empty = unset.
@description('Foundry project endpoint for prompt-agent calls. Empty = raw-model path.')
param foundryProjectEndpoint string = ''
@description('Route the four workflow drafting stages through Azure AI Foundry agents so calls appear in the Foundry Tracing tab. Disabled by default: the agent endpoint rejects request-side instructions and text fields, so the system prompt is embedded in input and the raw route\'s strict json_schema contract is downgraded to the agent\'s baked json_object format. This does not improve latency; the agent route hits the same upstream ~60s stall.')
param enableWorkflowAgents bool = false






@description('Foundry content-Safety agent name (compliance-reviewed). Empty = raw model.')
param safetyAgentName string = ''

@description('Foundry delivery-Director agent name. Empty = raw model.')
param directorAgentName string = ''

@description('Responses-API reasoning.effort override (none|minimal|low|medium|high). Empty = derive per model family.')
param reasoningEffort string = ''
@description('Minimum/default instances for the Function App App Service Plan. Set to 2 in production for high availability and 1 elsewhere.')
@minValue(1)
param appServicePlanMinInstances int = 1

@description('Maximum instances for generation-demand autoscale. Scaling rules only consume broadcast/regeneration ready-queue signals.')
@minValue(1)
param appServicePlanMaxInstances int = 6

@description('Feature-gated rollout controls for additive performance read models and durable queues. Read paths must remain off until their backfills have been compared.')
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


@description('Tags applied to every resource.')
param tags object = {}

@description('Optional subnet resource ID for Function App VNet integration. Leave empty to skip. Pending ARN network team (open item 4).')
param virtualNetworkSubnetId string = ''

@description('Deploy RBAC role assignments. Requires Owner/User Access Admin. Set to false if already configured.')
param deployRoleAssignments bool = false

@description('Deploy SWA linked backend. Set to false if already linked.')
param deployLinkedBackend bool = false

@description('Enable SWA system-assigned identity + Key Vault-referenced AAD_CLIENT_ID/AAD_CLIENT_SECRET app settings for the custom-Entra-provider RBAC config (test/prod only). Dev keeps the built-in SWA provider and MUST leave this false — the AadClientSecret KV secret is never populated in dev.')
param configureSwaAadKeyVaultSettings bool = false

@description('AAD application (client) ID for Easy Auth on the Function App. Required — deploys fail-closed without it. Supply per environment via the bicepparam or a CI variable.')
param aadClientId string

@description('AAD tenant ID for Easy Auth. Required — must be the tenant that owns the Function App, not "common".')
param aadTenantId string

@description('DEV-ONLY authorization fallback. App role granted to every authenticated AAD caller so the console is usable before Entra app-role assignments exist. MUST be empty in test/prod (a non-empty value bypasses RBAC). Only the dev bicepparam sets it.')
param devAuthFallbackRole string = ''

@description('Require a SECOND admin to approve config-bundle imports before they apply (the same two-person posture as production deploys). MUST be true in prod; dev/test apply directly.')
param configImportRequireSecondApprover bool = false

@description('DEV-ONLY debugging switch. Persists the full rendered prompt and raw model response for every LLM call to a dedicated blob container, keyed by job id, bounded to the most recent debugMaxCapturedJobs jobs. MUST be false in test/prod — the captured records include the full source traffic data and the full model output verbatim. Only the dev bicepparam sets it true.')
param debugCaptureLlmCalls bool = false

@description('Rolling cap on distinct jobs kept when debugCaptureLlmCalls is true. Ignored otherwise.')
param debugMaxCapturedJobs int = 20

@description('Azure AI Services model deployment name (Azure OpenAI). Single source of truth — propagated to AI Services and the Function App app settings.')
param aiModelName string = 'gpt-5.6-luna'

@description('Fallback Azure AI Services model deployment name (Azure OpenAI). Empty disables the fallback; when set, a 429, transient 5xx, or stall-exhaustion on the primary deployment is retried once immediately on this deployment with no backoff. Terra is a different model, so fallback bulletins may read differently by deliberate design.')
param aiFallbackModelName string = ''
@description('Process-wide cap on concurrent ElevenLabs TTS requests; size to the plan concurrent-request limit. Emits AiSettings__ElevenLabs__MaxConcurrentRequests.')
@minValue(1)
@maxValue(64)
param elevenLabsMaxConcurrentRequests int = 2

@description('Per-deployment concurrent-send bulkhead for Azure OpenAI. Emits AiSettings__AzureAI__CallBudget__BulkheadPermits.')
@minValue(1)
@maxValue(64)
param azureOpenAiBulkheadPermits int = 4

@description('Azure AI Services model version. Propagated to the AI Services model deployment.')
param aiModelVersion string = '2026-07-09'

@description('Tokens-per-minute capacity for the shared model deployment.')
param aiModelCapacity int = 1000

@description('Judge model for the .foundry eval suite, deployed alongside the generator. MUST differ from aiModelName so a model never grades its own output — every evals/*/eval.yaml pins it. Empty skips the deployment.')
param judgeModelName string = 'gpt-5.6-terra'

@description('Version of the judge model deployment.')
param judgeModelVersion string = '2026-07-09'

@description('Tokens-per-minute capacity for the judge model deployment.')
param judgeModelCapacity int = 1000

@description('Provision Azure AI Search. Off by default; turn on per environment when the AzureSearch:Enabled feature flag is true.')
param deploySearch bool = false

@description('Storage account replication SKU. Use Standard_ZRS or Standard_GZRS for prod; Standard_LRS is fine for dev.')
@allowed([
  'Standard_LRS'
  'Standard_ZRS'
  'Standard_GZRS'
  'Standard_RAGRS'
])
param storageSkuName string = 'Standard_LRS'

@description('Blob soft-delete retention in days. 30 minimum for production; 90 for audit compliance.')
param blobSoftDeleteDays int = 30

@description('Enable blob point-in-time restore on the storage account.')
param enableBlobRestore bool = false

@description('Blob restore window in days (must be < blobSoftDeleteDays).')
param blobRestoreDays int = 7

// Retention figures (CC-010, self-decided 2026-07-23 — see decisions.md).
// Default here stays 0 (keep forever) for any environment that doesn't set
// these explicitly; test/prod set 90/30/1095 in their bicepparam. Floor for
// aired audio: 90 days (CC-002).
@description('Delete aired bulletin audio (blob tag published=true) after N days. 0 = keep forever. Test/prod set 90 (CC-010/CC-002).')
@minValue(0)
param publishedAudioDeleteAfterDays int = 0

@description('Delete never-aired candidate audio (blob tag published=false) after N days. 0 = keep forever. Test/prod set 30 (CC-010).')
@minValue(0)
param unpublishedAudioDeleteAfterDays int = 0

@description('Delete audit evidence snapshots (audit-exports/) after N days. 0 = keep forever. Test/prod set 1095 / 3yr (CC-010).')
@minValue(0)
param auditExportsDeleteAfterDays int = 0

@description('Log Analytics retention in days (IBP-side workspace only; telemetry contains operator identities). Distinct from CC-010 (audit-evidence blob retention, resolved 2026-07-23) — this covers Function/App-Insights telemetry, not audit exports.')
@minValue(30)
@maxValue(730)
param logRetentionDays int = 30

@description('Object ID of the AAD group granted read-only access to audit evidence (bulletin audio blobs + job/approval tables). Per CC-002: the audit-evidence stakeholders. Empty skips the assignment until ARN supplies the group.')
param auditAccessGroupObjectId string = ''

@description('Provision private endpoints for Storage, Key Vault, and AI Services into the network RG. Requires privateEndpointSubnetId.')
param deployPrivateEndpoints bool = false

@description('Subnet resource ID hosting the private endpoint NICs. Required when deployPrivateEndpoints=true. Pending ARN network team (open item 4).')
param privateEndpointSubnetId string = ''

@description('Public network access on data-plane services. Set to Disabled in prod once private endpoints are wired.')
@allowed(['Enabled', 'Disabled'])
param dataPlanePublicNetworkAccess string = 'Enabled'

@description('Resource ID of privatelink.blob.core.windows.net private DNS zone (ARN hub-managed). Leave empty to skip DNS wiring on the PE.')
param privateDnsZoneBlobId string = ''

@description('Resource ID of privatelink.table.core.windows.net private DNS zone (ARN hub-managed). Leave empty to skip DNS wiring on the PE.')
param privateDnsZoneTableId string = ''

@description('Resource ID of privatelink.queue.core.windows.net private DNS zone (ARN hub-managed). Leave empty to skip DNS wiring on the PE.')
param privateDnsZoneQueueId string = ''

@description('Resource ID of privatelink.file.core.windows.net private DNS zone (ARN hub-managed). Required for in-VNet resolution of the AzureFilesDrop share. Leave empty to skip DNS wiring on the PE.')
param privateDnsZoneFileId string = ''

@description('Resource ID of privatelink.vaultcore.azure.net private DNS zone (ARN hub-managed). Leave empty to skip DNS wiring on the PE.')
param privateDnsZoneKeyVaultId string = ''

@description('Resource ID of privatelink.cognitiveservices.azure.com private DNS zone (ARN hub-managed). Leave empty to skip DNS wiring on the PE.')
param privateDnsZoneCognitiveServicesId string = ''

@description('Resource ID of privatelink.openai.azure.com private DNS zone (ARN hub-managed). Leave empty to skip DNS wiring on the PE.')
param privateDnsZoneOpenAiId string = ''

@description('Resource ID of privatelink.services.ai.azure.com private DNS zone (ARN hub-managed). Required for in-VNet resolution of the Foundry agent endpoint (*.services.ai.azure.com). Leave empty to skip DNS wiring on the PE.')
param privateDnsZoneServicesAiId string = ''

@description('Build a self-contained private network (VNet + subnets + 7 privatelink DNS zones) in the network RG instead of consuming ARN hub-supplied IDs. When true the network module feeds the Function VNet-integration subnet, the PE subnet, and the DNS zones, so privateEndpointSubnetId / virtualNetworkSubnetId / privateDnsZone*Id may stay empty. Default false uses ARN hub network via those params (prod).')
param createNetwork bool = false

@description('Route ALL Function App outbound traffic (incl. public internet) through the VNet integration. Default true. Set false when the VNet is peered to a hub whose firewall breaks/inspects outbound TLS, so private traffic (PEs + on-prem via hub) uses the VNet while internet SaaS (HERE, ElevenLabs) egresses directly.')
param routeAllTraffic bool = true

// ---------- Naming (ARN convention: IBP-AUE-{ENV}-{TYPE}-APP01) ----------

var environmentCodes = {
  dev: 'DEV'
  test: 'TST'
  prod: 'PRD'
  sbx: 'SBX'
}

var regionCodes = {
  australiaeast: 'AUE'
  australiasoutheast: 'ASE'
}

var envCode = environmentCodes[environment]
var nameStem = 'IBP-${regionCodes[location]}-${envCode}'

// Resource groups
var rgAppName = '${nameStem}-RSG-APP01'
var rgNetworkName = '${nameStem}-RSG-NTW01'
var rgKeyVaultName = '${nameStem}-RSG-KYV01'
var rgStorageName = '${nameStem}-RSG-STA01'
var rgLogAnalyticsName = '${nameStem}-RSG-LAW01' // only when LAW stays IBP-side (open item 1)

// Resources
var functionAppName = '${nameStem}-FNA-APP01'
var planName = '${nameStem}-PLN-APP01'
var staticWebAppName = '${nameStem}-SWA-APP01'
// Second SWA instance, hosting the handover documentation site. DOC01 extends
// ARN's {TYPE}-{INSTANCE} convention with a new instance suffix; raise it with
// ARN rather than blocking on it (docs/reference/azure-naming-convention.md).
var docsSiteName = '${nameStem}-SWA-DOC01'
var appInsightsName = '${nameStem}-AIN-APP01'
var logAnalyticsName = '${nameStem}-LAW-APP01'
var keyVaultName = '${nameStem}-KYV-APP01'
var aiServicesName = '${nameStem}-FDY-APP01'
var logicAppName = '${nameStem}-LOA-APP01'
var actionGroupName = '${nameStem}-AIA-APP01'
var privateEndpointNamePrefix = '${nameStem}-PEP-APP' // instances 01-05 appended in the module
// Azure AI Search rejects uppercase names; everything else keeps ARN's casing.
var searchServiceName = toLower('${nameStem}-FIQ-APP01')
// Storage accounts: lowercase, no hyphens, <= 24 chars (17 here).
var storageAccountName = toLower('ibp${regionCodes[location]}${envCode}staapp01')

var lawIsExternal = !empty(logAnalyticsWorkspaceResourceId)

var defaultTags = union(tags, {
  application: 'information-bulletin-platform'
  environment: environment
})

// ==========================================================================
// Resource Groups
// ==========================================================================

resource rgApp 'Microsoft.Resources/resourceGroups@2024-03-01' = if (createResourceGroups) {
  name: rgAppName
  location: location
  tags: defaultTags
}

resource rgNetwork 'Microsoft.Resources/resourceGroups@2024-03-01' = if (createResourceGroups && (deployPrivateEndpoints || createNetwork)) {
  name: rgNetworkName
  location: location
  tags: defaultTags
}

resource rgKeyVault 'Microsoft.Resources/resourceGroups@2024-03-01' = if (createResourceGroups) {
  name: rgKeyVaultName
  location: location
  tags: defaultTags
}

resource rgStorage 'Microsoft.Resources/resourceGroups@2024-03-01' = if (createResourceGroups) {
  name: rgStorageName
  location: location
  tags: defaultTags
}

// Only exists while logging stays IBP-side (open item 1).
resource rgLogAnalytics 'Microsoft.Resources/resourceGroups@2024-03-01' = if (createResourceGroups && !lawIsExternal) {
  name: rgLogAnalyticsName
  location: location
  tags: defaultTags
}

// ==========================================================================
// Self-contained network (createNetwork=true) — private posture without ARN's
// hub. Prod sets createNetwork=false and supplies hub subnet + DNS zone IDs.
// ==========================================================================
module network './modules/network.bicep' = if (createNetwork) {
  name: 'network'
  scope: resourceGroup(rgNetworkName)
  dependsOn: [rgNetwork]
  params: {
    location: location
    vnetName: '${nameStem}-VNT-NTW01'
    tags: defaultTags
  }
}

// Effective network inputs: self-built module outputs (createNetwork) or the
// ARN hub-supplied params (default). Both feed the Function App + private endpoints.
var effectiveIntegrationSubnetId = createNetwork ? network.outputs.integrationSubnetId : virtualNetworkSubnetId
var effectivePeSubnetId = createNetwork ? network.outputs.privateEndpointSubnetId : privateEndpointSubnetId
var effectiveBlobDnsZoneId = createNetwork ? network.outputs.blobDnsZoneId : privateDnsZoneBlobId
var effectiveTableDnsZoneId = createNetwork ? network.outputs.tableDnsZoneId : privateDnsZoneTableId
var effectiveQueueDnsZoneId = createNetwork ? network.outputs.queueDnsZoneId : privateDnsZoneQueueId
var effectiveKeyVaultDnsZoneId = createNetwork ? network.outputs.keyVaultDnsZoneId : privateDnsZoneKeyVaultId
var effectiveCognitiveServicesDnsZoneId = createNetwork ? network.outputs.cognitiveServicesDnsZoneId : privateDnsZoneCognitiveServicesId
var effectiveOpenAiDnsZoneId = createNetwork ? network.outputs.openAiDnsZoneId : privateDnsZoneOpenAiId
var effectiveServicesAiDnsZoneId = createNetwork ? network.outputs.servicesAiDnsZoneId : privateDnsZoneServicesAiId
var effectiveFileDnsZoneId = createNetwork ? network.outputs.fileDnsZoneId : privateDnsZoneFileId

// The delivery drop account is ARN-provisioned outside this template. It normally
// sits in the environment's own storage RG; prod points across to the single
// account ARN built in IBP-AUE-TST-RSG-STA01 (see zettaDropAccountResourceGroup).
var effectiveDropAccountRg = empty(zettaDropAccountResourceGroup) ? rgStorageName : zettaDropAccountResourceGroup

// ==========================================================================
// Modules
// ==========================================================================

// ---- Log Analytics (IBP-side only; skipped when ARN central logging is confirmed) ----
module logAnalytics './modules/log-analytics.bicep' = if (!lawIsExternal) {
  name: 'logAnalytics'
  scope: resourceGroup(rgLogAnalyticsName)
  dependsOn: [rgLogAnalytics]
  params: {
    location: location
    logAnalyticsName: logAnalyticsName
    retentionInDays: logRetentionDays
    tags: defaultTags
  }
}

var effectiveLogAnalyticsId = lawIsExternal ? logAnalyticsWorkspaceResourceId : logAnalytics.outputs.workspaceId

// ---- Application Insights ----
module monitoring './modules/monitoring.bicep' = {
  name: 'monitoring'
  scope: resourceGroup(rgAppName)
  dependsOn: [rgApp]
  params: {
    location: location
    appInsightsName: appInsightsName
    logAnalyticsWorkspaceId: effectiveLogAnalyticsId
    tags: defaultTags
  }
}

// ---- Storage ----
module storage './modules/storage.bicep' = {
  name: 'storage'
  scope: resourceGroup(rgStorageName)
  dependsOn: [rgStorage]
  params: {
    location: location
    storageAccountName: storageAccountName
    skuName: storageSkuName
    publicNetworkAccess: dataPlanePublicNetworkAccess
    blobSoftDeleteDays: blobSoftDeleteDays
    enableBlobRestore: enableBlobRestore
    blobRestoreDays: blobRestoreDays
    publishedAudioDeleteAfterDays: publishedAudioDeleteAfterDays
    unpublishedAudioDeleteAfterDays: unpublishedAudioDeleteAfterDays
    auditExportsDeleteAfterDays: auditExportsDeleteAfterDays
    logAnalyticsWorkspaceId: effectiveLogAnalyticsId
    tags: defaultTags
  }
}

// ---- Static Web App ----
resource kvForSwaSecret 'Microsoft.KeyVault/vaults@2023-07-01' existing = if (configureSwaAadKeyVaultSettings) {
  name: keyVaultName
  scope: resourceGroup(rgKeyVaultName)
}

module staticWebApp './modules/static-web-app.bicep' = {
  name: 'staticWebApp'
  scope: resourceGroup(rgAppName)
  dependsOn: [rgApp]
  params: {
    location: swaLocation
    staticWebAppName: staticWebAppName
    aadClientId: configureSwaAadKeyVaultSettings ? aadClientId : ''
    aadClientSecret: configureSwaAadKeyVaultSettings ? kvForSwaSecret.getSecret('AadClientSecret') : ''
    customDomainName: swaCustomDomain
    enableEnterpriseEdge: swaEnterpriseEdge
    tags: defaultTags
  }
}

// ---- Documentation site (second Static Web App) ----
// Deliberately no AAD configuration: this is the handover documentation ARN
// must still be able to read once vendor identities lapse. It is kept out of
// search engines by a noindex meta tag emitted by the site itself.
module docsSwa './modules/static-web-app.bicep' = if (deployDocsSite) {
  name: 'docsSwa'
  scope: resourceGroup(rgAppName)
  dependsOn: [rgApp]
  params: {
    location: swaLocation
    staticWebAppName: docsSiteName
    appLocation: 'docs-site'
    outputLocation: 'dist'
    tags: defaultTags
  }
}

// Operator-console base URL: the custom domain once bound, else the SWA default hostname.
var consoleBaseUrl = 'https://${staticWebApp.outputs.consoleHostname}'

// ---- Logic App Notifications (optional) ----
module notifications './modules/logic-app-notification.bicep' = if (deployNotifications) {
  name: 'notifications'
  scope: resourceGroup(rgAppName)
  dependsOn: [rgApp]
  params: {
    logicAppName: logicAppName
    location: location
    teamsGroupId: teamsGroupId
    teamsChannelId: teamsChannelId
    opsConsoleBaseUrl: consoleBaseUrl
    environmentLabel: environment
    tags: defaultTags
  }
}

// Resolve the effective webhook URL: Logic App trigger URL (if deployed) or external param
var effectiveWebhookUrl = deployNotifications ? notifications.outputs.triggerUrl : webhookEndpointUrl

// ---- Key Vault ----
module keyVault './modules/key-vault.bicep' = {
  name: 'keyVault'
  scope: resourceGroup(rgKeyVaultName)
  dependsOn: [rgKeyVault]
  params: {
    location: location
    keyVaultName: keyVaultName
    webhookEndpointUrl: effectiveWebhookUrl
    publicNetworkAccess: dataPlanePublicNetworkAccess
    logAnalyticsWorkspaceId: effectiveLogAnalyticsId
    tags: defaultTags
  }
}

// ---- AI Services & Search ----
module aiServices './modules/ai-services.bicep' = {
  name: 'aiServices'
  scope: resourceGroup(rgAppName)
  dependsOn: [rgApp]
    params: {
      location: aiServicesLocation
      searchLocation: location
      aiServicesName: aiServicesName
      searchServiceName: searchServiceName
      aiModelName: aiModelName
      aiModelVersion: aiModelVersion
      aiModelCapacity: aiModelCapacity
      judgeModelName: judgeModelName
      judgeModelVersion: judgeModelVersion
      judgeModelCapacity: judgeModelCapacity
      deploySearch: deploySearch
      publicNetworkAccess: dataPlanePublicNetworkAccess
      logAnalyticsWorkspaceId: effectiveLogAnalyticsId
      appInsightsResourceId: monitoring.outputs.appInsightsResourceId
      appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
      tags: defaultTags
    }

}

// ---- Function App ----
module functionApp './modules/function-app.bicep' = {
  name: 'functionApp'
  scope: resourceGroup(rgAppName)
  params: {
    location: location
    planName: planName
    functionAppName: functionAppName
    storageAccountName: storage.outputs.name
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    appInsightsResourceId: monitoring.outputs.appInsightsResourceId
    minInstances: appServicePlanMinInstances
    maxInstances: appServicePlanMaxInstances
    performanceRollout: performanceRollout
    keyVaultUri: keyVault.outputs.uri
    aiServicesEndpoint: aiServices.outputs.aiServicesEndpoint
    aiModelName: aiModelName
    aiFallbackModelName: aiFallbackModelName
    elevenLabsMaxConcurrentRequests: elevenLabsMaxConcurrentRequests
    azureOpenAiBulkheadPermits: azureOpenAiBulkheadPermits
    webhookEnabled: deployNotifications || !empty(webhookEndpointUrl)
    opsConsoleBaseUrl: consoleBaseUrl
    fillerCartAudioUrl: fillerCartAudioUrl
    fillerCartScript: fillerCartScript
    weatherEnabled: weatherEnabled
    fuelEnabled: fuelEnabled
    officialFeeds: officialFeeds
    officialFeedFreshMinutes: officialFeedFreshMinutes
    officialFeedMaxStaleMinutes: officialFeedMaxStaleMinutes
    zettaEnabled: zettaEnabled
    zettaTransport: zettaTransport
    zettaDropAccountName: zettaDropAccountName
    zettaDropContainerName: zettaDropContainerName
    zettaDropFileShareName: zettaDropFileShareName
    zettaFileNamePattern: zettaFileNamePattern
    zettaMarketCodesJson: zettaMarketCodesJson
    corsAllowedOrigins: union(
      ['https://${staticWebApp.outputs.defaultHostname}'],
      empty(swaCustomDomain) ? [] : ['https://${swaCustomDomain}']
    )
    virtualNetworkSubnetId: effectiveIntegrationSubnetId
    routeAllTraffic: routeAllTraffic
    aadClientId: aadClientId
    aadTenantId: aadTenantId
    // Structural guard: the RBAC bypass is a non-production affordance, so a value can only
    // ever reach a dev/sbx site. Prod carried this setting (empty, therefore inert) until
    // 2026-07-28 with nothing to fail a deploy if it acquired a value; forcing it here makes
    // a mistaken test/prod bicepparam inert instead of catastrophic. The Functions host
    // additionally refuses to start if it ever sees a value on a TST/PRD site.
    devAuthFallbackRole: contains(['dev', 'sbx'], environment) ? devAuthFallbackRole : ''
    configImportRequireSecondApprover: configImportRequireSecondApprover
    debugCaptureLlmCalls: debugCaptureLlmCalls
    debugMaxCapturedJobs: debugMaxCapturedJobs
    logAnalyticsWorkspaceId: effectiveLogAnalyticsId
    allowArmAudience: allowArmAudience
    foundryProjectEndpoint: foundryProjectEndpoint
    enableWorkflowAgents: enableWorkflowAgents
    safetyAgentName: safetyAgentName
    directorAgentName: directorAgentName
    reasoningEffort: reasoningEffort
    tags: defaultTags
  }
}

// ---- Action Group (provisioned only if no external one supplied) ----
// ARN's central groups (mgt-aue-ibp-agp-{Purpose}) are the intended targets
// once their resource IDs land (open item 2). Until then this keeps each
// environment self-contained.
// Alerts-to-Teams reuses the notification Logic App rather than standing up a
// second one: its Teams API connection is already authorised per environment,
// and that interactive OAuth consent is the only step here a redeploy cannot do.
// The URL is never a source value — it is the Logic App's own trigger callback,
// which carries a `sig=` and must stay out of the repo.
var alertWebhookReceivers = routeAlertsToTeams && !empty(effectiveWebhookUrl) ? [
  {
    name: 'TeamsAlerts'
    serviceUri: effectiveWebhookUrl
    useCommonAlertSchema: true
  }
] : []

module actionGroup './modules/action-group.bicep' = if (empty(actionGroupId)) {
  name: 'actionGroup'
  scope: resourceGroup(rgAppName)
  dependsOn: [rgApp]
  params: {
    actionGroupName: actionGroupName
    shortName: take('IBP-${envCode}', 12)
    emailReceivers: alertEmailReceivers
    smsReceivers: alertSmsReceivers
    webhookReceivers: alertWebhookReceivers
    tags: defaultTags
  }
}

var effectiveActionGroupId = empty(actionGroupId) ? actionGroup.outputs.id : actionGroupId

// ---- Workbooks (ops dashboards) ----
module workbooks './modules/workbooks.bicep' = {
  name: 'workbooks'
  scope: resourceGroup(rgAppName)
  params: {
    location: location
    sourceResourceId: monitoring.outputs.appInsightsResourceId
    namePrefix: nameStem
    tags: defaultTags
  }
}

// ---- Availability Test (synthetic ping on /api/health) ----
// Declared before the alerts module because alert 9 watches this web test.
module availabilityTest './modules/availability-test.bicep' = {
  name: 'availabilityTest'
  scope: resourceGroup(rgAppName)
  params: {
    location: location
    namePrefix: nameStem
    appInsightsResourceId: monitoring.outputs.appInsightsResourceId
    healthUrl: 'https://${functionApp.outputs.defaultHostName}/api/health'
    tags: defaultTags
  }
}

// ---- Alerts ----
module alerts './modules/alerts.bicep' = {
  name: 'alerts'
  scope: resourceGroup(rgAppName)
  params: {
    functionAppName: functionApp.outputs.name
    functionAppResourceId: functionApp.outputs.id
    keyVaultResourceId: keyVault.outputs.id
    logAnalyticsWorkspaceResourceId: effectiveLogAnalyticsId
    availabilityWebTestId: availabilityTest.outputs.id
    actionGroupId: effectiveActionGroupId
    appInsightsResourceId: monitoring.outputs.appInsightsResourceId
    deployCustomMetricAlerts: true
    location: location
    tags: defaultTags
  }
}

// ---- Linked Backend (SWA → Function App proxy) ----
module linkedBackend './modules/linked-backend.bicep' = if (deployLinkedBackend) {
  name: 'linkedBackend'
  scope: resourceGroup(rgAppName)
  params: {
    staticWebAppName: staticWebApp.outputs.name
    functionAppId: functionApp.outputs.id
    functionAppRegion: location
  }
}

// SWA linked-backend creation mutates Function App authsettingsV2 by adding the
// Azure Static Web Apps provider. Reapply our pinned AAD settings afterwards so
// direct bearer-token auth keeps the intended per-env audience, while preserving
// the SWA-linked provider needed for proxied /api/* calls.
module functionAuthSettingsAfterLinkedBackend './modules/function-authsettings.bicep' = if (deployLinkedBackend) {
  name: 'functionAuthSettingsAfterLinkedBackend'
  scope: resourceGroup(rgAppName)
  dependsOn: [linkedBackend]
  params: {
    functionAppName: functionApp.outputs.name
    aadClientId: aadClientId
    aadTenantId: aadTenantId
    linkedStaticWebAppHostname: staticWebApp.outputs.defaultHostname
    allowArmAudience: allowArmAudience
  }
}

// ---- Private Endpoints (network RG) ----
module privateEndpoints './modules/private-endpoints.bicep' = if (deployPrivateEndpoints) {
  name: 'privateEndpoints'
  scope: resourceGroup(rgNetworkName)
  // PEs attach to the storage/KV/AI accounts; the MI role-assignment modules also
  // write those accounts. Serialise PEs AFTER them so the Cognitive Services PE
  // doesn't race the account ETag (IfMatchPreconditionFailed) — hit on first
  // test deploy 29 Jun 2026; would otherwise recur on prod's first deploy.
  dependsOn: [rgNetwork, rolesStorage, rolesKeyVault, rolesAiServices]
  params: {
    location: location
    namePrefix: privateEndpointNamePrefix
    subnetId: effectivePeSubnetId
    storageAccountId: storage.outputs.id
    keyVaultId: keyVault.outputs.id
    aiServicesId: aiServices.outputs.id
    deliveryDropStorageAccountId: empty(zettaDropAccountName)
      ? ''
      : resourceId(
          subscription().subscriptionId,
          effectiveDropAccountRg,
          'Microsoft.Storage/storageAccounts',
          zettaDropAccountName
        )
    deliveryDropFileDnsZoneId: effectiveFileDnsZoneId
    blobDnsZoneId: effectiveBlobDnsZoneId
    tableDnsZoneId: effectiveTableDnsZoneId
    queueDnsZoneId: effectiveQueueDnsZoneId
    keyVaultDnsZoneId: effectiveKeyVaultDnsZoneId
    cognitiveServicesDnsZoneId: effectiveCognitiveServicesDnsZoneId
    openAiDnsZoneId: effectiveOpenAiDnsZoneId
    servicesAiDnsZoneId: effectiveServicesAiDnsZoneId
    tags: defaultTags
  }
}

// ---- Role Assignments (RBAC for Function App MSI, per target RG) ----
module rolesStorage './modules/roles-storage.bicep' = if (deployRoleAssignments) {
  name: 'rolesStorage'
  scope: resourceGroup(rgStorageName)
  params: {
    principalIds: [functionApp.outputs.principalId, functionApp.outputs.stagingPrincipalId]
    storageAccountName: storage.outputs.name
    auditAccessGroupObjectId: auditAccessGroupObjectId
  }
}

// The SFTP-enabled delivery drop account is provisioned by ARN outside this
// template (test: ibpauetststaftp01, local user svctraffictst). The Function
// App MI writes bulletins into it natively as blobs; the playout side pulls
// the same files over SFTP.
module rolesDeliveryDrop './modules/roles-delivery-drop.bicep' = if (deployRoleAssignments && !empty(zettaDropAccountName)) {
  name: 'rolesDeliveryDrop'
  scope: resourceGroup(effectiveDropAccountRg)
  params: {
    principalIds: [functionApp.outputs.principalId, functionApp.outputs.stagingPrincipalId]
    storageAccountName: zettaDropAccountName
  }
}

module rolesKeyVault './modules/roles-key-vault.bicep' = if (deployRoleAssignments) {
  name: 'rolesKeyVault'
  scope: resourceGroup(rgKeyVaultName)
  params: {
    principalIds: [functionApp.outputs.principalId, functionApp.outputs.stagingPrincipalId]
    keyVaultName: keyVault.outputs.name
  }
}

module rolesAiServices './modules/roles-ai-services.bicep' = if (deployRoleAssignments) {
  name: 'rolesAiServices'
  scope: resourceGroup(rgAppName)
  params: {
    principalIds: [functionApp.outputs.principalId, functionApp.outputs.stagingPrincipalId]
    aiServicesName: aiServices.outputs.name
    // Lets the Foundry project publish agent traces to App Insights. Without it the portal's
    // Tracing tab is silently empty — see the module for the measured dev evidence.
    foundryProjectPrincipalId: aiServices.outputs.projectPrincipalId
    appInsightsName: appInsightsName
  }
}

// ---------- Outputs ----------

@description('Application resource group name.')
output resourceGroupApp string = rgAppName

@description('Network resource group name (private endpoints).')
output resourceGroupNetwork string = rgNetworkName

@description('Key Vault resource group name.')
output resourceGroupKeyVault string = rgKeyVaultName

@description('Storage resource group name.')
output resourceGroupStorage string = rgStorageName

@description('Function App name.')
output functionAppName string = functionApp.outputs.name

@description('Function App default hostname.')
output functionAppHostName string = functionApp.outputs.defaultHostName

@description('Static Web App default hostname.')
output staticWebAppHostName string = staticWebApp.outputs.defaultHostname

@description('Documentation site hostname. Empty when deployDocsSite is false.')
output docsSiteHostName string = deployDocsSite ? docsSwa.outputs.defaultHostname : ''

@description('Operator console base URL (custom domain when configured).')
output consoleBaseUrl string = consoleBaseUrl

@description('Key Vault URI.')
output keyVaultUri string = keyVault.outputs.uri

@description('Key Vault name.')
output keyVaultName string = keyVault.outputs.name

@description('AI Services endpoint.')
output aiServicesEndpoint string = aiServices.outputs.aiServicesEndpoint

@description('Storage account name.')
output storageAccountName string = storage.outputs.name

@description('Resource ID of the action group used for alert notifications.')
output actionGroupId string = effectiveActionGroupId

@description('Resource ID of the Log Analytics workspace in use (IBP-side or ARN central).')
output logAnalyticsWorkspaceId string = effectiveLogAnalyticsId
