using './main.bicep'

// ==========================================================================
// IBP — Test Parameter File (IBP-AUE-TST-*)
// ==========================================================================
// Staging environment meant to MIRROR PRODUCTION as closely as possible:
// ZRS storage, 90-day blob soft-delete + PITR, private endpoints with the
// data plane disabled, and full prod-style RBAC (NO dev auth fallback).
// Where a value differs from prod it is called out inline.
//
// OPEN ITEMS — values pending ARN (see deployment/README.md + the
// consolidated provisioning request raised with ARN):
//   - Subscription / RG creation + deploy access (ARN technology lead).
//   - aadClientId: TEST app registration client ID (ARN identity team).
//   - (Test SELF-BUILDS its network via createNetwork; ARN hub subnet/DNS is a prod-only item.)
//   - logAnalyticsWorkspaceResourceId: MGT-vs-IBP answer (ARN technology lead).
//   - actionGroupId / alert receivers / webhook: on-call + Teams (ARN technology lead).
//   - auditAccessGroupObjectId: audit-evidence reader group (ARN technology lead).
// Placeholders in <angle-brackets> are intentional and fail Bicep validation
// until the real value is supplied.
// ==========================================================================

param environment = 'test'
// Generation-capacity autoscale: one warm instance in non-production.
param appServicePlanMinInstances = 1
param appServicePlanMaxInstances = 6
param location = 'australiaeast'
param aiServicesLocation = 'australiaeast'
param swaLocation = 'eastasia'

// Custom domain + enterprise-grade edge (Option A, 2026-07-17 decision).
// ARN DNS (ARN technology lead, 31 Jul 2026) created:
//   ain-test.arn.com.au  CNAME  polite-flower-0a5e0ce00.7.azurestaticapps.net
// Domain bound + validated 31 Jul 2026; SWA issues/renews the managed TLS cert
// automatically, so ARN does NOT need to supply its own certificate.
// Next step (optional): set swaEnterpriseEdge = true and deploy again.
param swaCustomDomain = 'ain-test.arn.com.au'
param swaEnterpriseEdge = false

// Handover documentation site (IBP-AUE-<ENV>-SWA-DOC01). Published on dev and
// prod only: test is a UAT environment with no reason to host documentation.
// Keep this in step with the `deployDocsSite` pipeline variable in the
// matching variable group, which gates the deployment job.
param deployDocsSite = false

// Set to false once ARN pre-creates the RGs with access instead.
param createResourceGroups = true

// PLACEHOLDER — empty creates IBP-AUE-TST-LAW-APP01 in IBP-AUE-TST-RSG-LAW01.
// If ARN confirms central logging, set the MGT workspace resource ID.
param logAnalyticsWorkspaceResourceId = ''

// PLACEHOLDER — empty creates IBP-AUE-TST-AIA-APP01 from the receivers below.
// Swap for the mgt-aue-ibp-agp-* resource ID once ARN supplies it.
param actionGroupId = ''

// Alert emails: EMPTY until ARN nominates a real on-call destination.
// Source carries no receivers so vendor identities cannot receive ARN alerts,
// but the DEPLOYED action group still carries the interim vendor address
// (verified live 2026-09-03) because this file has not been applied since it
// was emptied. The next deployment of this environment removes it. ARN's
// platform owner owns supplying either an on-call distribution list here, or
// the central mgt-aue-ibp-agp-* resource ID in actionGroupId above (which
// ignores this param). Once applied, email alerts fire NOWHERE: Teams routing
// below is the only live path, and a Logic App inside this subscription cannot
// report a subscription-wide outage.
// Deploy succeeds with this empty — the local action group is still created,
// just with no email receivers.
param alertEmailReceivers = []

// Azure Monitor alerts (the 9 rules in alerts.bicep — errors, kv-access-denied,
// no-executions, poison-queue, ai-throttling, synthesis-failures,
// bulletin-failure-rate, dependency-unhealthy, health-availability — plus the
// availability test)
// post into the SAME Teams channel as the app's own cards, via the notification
// Logic App as an action-group webhook receiver — enabled 2026-08-07 on ARN's
// direction to route system alerts to Teams and defer SMS/email receivers.
// Applied live the same day (az action-group update, TeamsAlerts webhook,
// common alert schema — matching what this param deploys). Once this file is
// applied the email backstop is gone (alertEmailReceivers is empty), leaving
// Teams as the only live path — a known gap: a Logic App in this subscription
// cannot be trusted to report that this subscription is down. The Teams API
// connection is consented as the delivery representative's ARN account, so it
// must be re-authorised by a lasting ARN account before that account lapses; a
// lapsed consent still reports Connected and silently drops cards. ARN should
// still nominate an on-call email/SMS destination even after confirming Teams
// cards arrive.
param routeAlertsToTeams = true

// Weather + fuel enabled platform-wide since the 7 Jul 2026 live decision (supersedes
// the 2026-07-06 "keep false in test/prod" note in decisions.md — the live state was
// deliberately flipped on and IaC now matches it, closing the drift found by the
// 2026-07-16 audit).
param weatherEnabled = true
param fuelEnabled = true
// Official feeds are explicit per environment. QldApiKey is intentionally
// absent from source: seed OfficialFeeds--QldApiKey in this environment's
// Key Vault and let AddAzureKeyVault provide it at runtime.
param officialFeeds = {
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
param officialFeedFreshMinutes = 15
param officialFeedMaxStaleMinutes = 60

// Playout delivery is LIVE in test as of 2026-08-20. Test is the environment that carries new
// code and production is deliberately FROZEN at the last known-good commit as the fallback, so
// test has to be able to air to a real pilot station. This intentionally gives up the guard the
// isolated informationbulletins-test share provided (decisions.md 2026-07-30).
//
// What replaces that guard is PER-STATION, not per-environment: MarketSchedule.PlayoutEnabled,
// surfaced as "Send to playout" in the console. Delivery needs BOTH gates — this setting for the
// environment and the station's own flag — so an operator arms a station in exactly one
// environment and switches it over with no deployment. Both environments write the SAME file
// name to the SAME share root, so a station armed in both is a race neither side can audit:
// Zetta returns no on-air receipt, and the drop file's disappearance identifies a station's
// latest publish rather than a specific job.
//
// informationbulletins-test still exists and the autoloader still does not watch it; test just
// no longer writes there. Any market other than the two pilot stations still cannot reach
// playout from any environment — it has no code in the map below, so delivery fails loudly.
param zettaEnabled = true
param zettaTransport = 'AzureFilesDrop'
param zettaDropAccountName = 'ibpauetststaftp01'
// The full delivery path was PROVEN against the isolated `informationbulletins-test` share on
// 2026-08-20 before this was pointed at the live folder: `[SYD]KIIS90s_TRAFF.wav` landed
// byte-identical to the approved audio (sha256 20d2c8f5…, 1,940,444 B, 22.000s), and a second
// fully-approved publish with the station HELD BACK wrote nothing at all. See decisions.md.
param zettaDropFileShareName = 'informationbulletins'

// ARN's playout naming convention, issued by ARN playout engineering 2026-07-30 and proven
// end to end the same day: one file per station, written to the share ROOT,
// same name every time — Zetta re-ingests on overwrite and stamps its own
// date/expiry. The code is ARN's own playout identifier and is written exactly
// as supplied (no case folding). Both pilot stations ingested and were
// confirmed landed in their station libraries. The other 11 markets are
// post-pilot: absent from the map, so delivery fails loudly instead of
// inventing a name. See docs/canonical/decisions.md (2026-07-30).
param zettaFileNamePattern = '{MarketId}_TRAFF'
param zettaMarketCodesJson = '{"sydney":"[SYD]KIIS90s","cairns-4ca":"[CAI]STAR1027"}'

// Teams notifications — wired 2026-07-05 to "IBP Notifications (Test)", a
// standard channel in the Project B10 team created hidden-by-default
// (not auto-shown, cards carry no @mentions, so no ARN member is notified).
// The Logic App trigger URL lands in KV as `webhookEndpointUrl`; the Teams
// API connection needs the one-time programmatic consent after deploy
// (docs/operations/notifications.md). ARN's real ops channel remains a
// prod-time swap.
param deployNotifications = true
param teamsGroupId = 'fd3865c1-25f8-4cd1-b991-3ec32ddd5f71'
param teamsChannelId = '19:b0a5adcfda4341b595da58cbc9e7a631@thread.tacv2'
param webhookEndpointUrl = ''

// Dead-air filler cart — synthesised 7 Jul 2026, same audio/wording as dev's
// approved filler-cart.wav, uploaded to this environment's broadcasts container.
param fillerCartAudioUrl = 'https://ibpauetststaapp01.blob.core.windows.net/broadcasts/filler-cart.wav'

// Per-voice filler cart script (transcribed from the approved evergreen WAV, 2026-08-04):
// each voice synthesises this copy, cached per (voice × settings × model × script).
// Same audio/wording as dev's. Empty disables per-voice carts.
param fillerCartScript = 'You\'re listening to Your Traffic Network, keeping you moving. We\'ll have your next full update on the roads for you very shortly, so keep it locked here. In the meantime, give yourself a little extra time, take it easy through the work zones and we\'ll be back soon with the latest right across the network.'

// Function App VNet-integration subnet. 2026-07-03: Test was MIGRATED off its
// self-built island VNet onto ARN's hub-connected VNet (IBP-AUE-TST-VNT-APP01-
// 10-241-91-0_24, 10.241.91.0/24, peered to ARN-AUE-HUB) so it can reach on-prem
// Zetta. createNetwork is now false; this + the PE subnet/DNS IDs below are the
// hub-VNet resources supplied explicitly. See decisions.md 2026-07-03.
param virtualNetworkSubnetId = '/subscriptions/5e5f8695-2207-4572-8e80-1065a1b0c135/resourceGroups/IBP-AUE-TST-RSG-NTW01/providers/Microsoft.Network/virtualNetworks/IBP-AUE-TST-VNT-APP01-10-241-91-0_24/subnets/IBP-AUE-TST-SNT-APP01-10-241-91-0_24'

param tags = {
  project: 'information-bulletin-platform'
  environment: 'test'
  managedBy: 'bicep'
  costCenter: 'test-team'
}

param deployRoleAssignments = true
param deployLinkedBackend = true

// SWA custom-Entra-provider RBAC. `true` is safe here because the Test Key Vault
// + AadClientSecret already exist (seeded 29 Jun), so `getSecret()` resolves and
// every redeploy is idempotent (never wipes AAD_CLIENT_ID/AAD_CLIENT_SECRET).
// For a from-scratch rebuild this is two-phase: deploy `false` first, seed the
// KV secret, then flip to `true` (see deployment/README.md, and prod's param).
param configureSwaAadKeyVaultSettings = true

// AAD Easy Auth — Test app registration `IBP-AUE-SPN-TST-Traffic` (created by ARN
// 29 Jun 2026). Client ID below; the secret lives in Key Vault (AadClientSecret),
// never in source. Tenant is ARN's (ausradio.onmicrosoft.com).
param aadClientId = 'e47e265d-60ed-4599-bbec-3b41e27f4043'
param aadTenantId = '91d5bc47-4df3-4ad0-a5ae-3d3249eceaf1'

// FULL PROD-STYLE RBAC — no dev fallback. Empty (the default) means every
// caller is authorized only by the Operator/Admin app roles forwarded from the
// custom Entra provider; unassigned callers get 403. NEVER set this in test/prod.
param devAuthFallbackRole = ''

// DEV-ONLY debugging feature (full prompt/response capture). NEVER set this in test/prod.
param debugCaptureLlmCalls = false

// Foundry agent pipeline (provisioned in the TEST Foundry project 2026-07-16;
// matches dev's live configuration — QT-001: keep in IaC so redeploys can't wipe).
param foundryProjectEndpoint = 'https://ibp-aue-tst-fdy-app01.services.ai.azure.com/api/projects/bulletin-generator'
param safetyAgentName = 'arn-safety'
param directorAgentName = 'arn-director'
param reasoningEffort = 'none'

// Match prod storage SKU so test soak exercises ZRS behaviour.
param storageSkuName = 'Standard_ZRS'

// Mirror prod blob durability: 90-day soft-delete + 30-day point-in-time restore.
param blobSoftDeleteDays = 90
param enableBlobRestore = true
param blobRestoreDays = 30

// PLACEHOLDER — empty skips the assignment. Set to the object ID of the ARN
// audit-access AAD group (the audit-evidence stakeholders, CC-002) once ARN creates it.
param auditAccessGroupObjectId = ''

// Retention figures (CC-010, self-decided 2026-07-23 — see decisions.md).
// Mirrors prod: 90d published-audio floor (CC-002: 60d post-complaint + 30d
// complaint window), 30d for never-aired candidates (debugging window only,
// not evidence), 3yr audit-evidence text (cheap, useful for a later dispute).
param publishedAudioDeleteAfterDays = 90
param unpublishedAudioDeleteAfterDays = 30
param auditExportsDeleteAfterDays = 1095

// Telemetry retention (operator identities appear in logs). Applies only while
// logging stays IBP-side; ARN central logging owns its own retention.
param logRetentionDays = 30

// Private endpoints — Test mirrors prod's private posture.
//
// 2026-07-03: Test was MIGRATED off its original self-built island VNet
// (10.61.0.0/24, createNetwork=true) ONTO ARN's hub-connected VNet
// IBP-AUE-TST-VNT-APP01-10-241-91-0_24 (10.241.91.0/24), which ARN peered to
// ARN-AUE-HUB so the platform can reach on-prem Zetta. createNetwork is now
// false and the hub-VNet PE subnet + the 5 privatelink DNS zones (which live in
// IBP-AUE-TST-RSG-NTW01) are supplied explicitly below — the same shape prod uses.
//
// routeAllTraffic=false: only RFC1918/private traffic (the PEs + on-prem Zetta via
// the hub) routes through the VNet; internet SaaS (HERE, ElevenLabs) egresses
// DIRECTLY, because ARN's hub firewall breaks outbound TLS to them (verified live
// 2026-07-03 — private deps healthy, HERE/ElevenLabs failed until route-all off).
// Prod keeps routeAllTraffic=true and instead needs an ARN hub-firewall allowlist —
// open governance item. The old 10.61.0.0/24 VNet is retained until a test redeploy
// confirms this path. See decisions.md 2026-07-03.
param createNetwork = false
param routeAllTraffic = false
param deployPrivateEndpoints = true
param dataPlanePublicNetworkAccess = 'Disabled'
param privateEndpointSubnetId = '/subscriptions/5e5f8695-2207-4572-8e80-1065a1b0c135/resourceGroups/IBP-AUE-TST-RSG-NTW01/providers/Microsoft.Network/virtualNetworks/IBP-AUE-TST-VNT-APP01-10-241-91-0_24/subnets/snet-pe'
param privateDnsZoneBlobId = '/subscriptions/5e5f8695-2207-4572-8e80-1065a1b0c135/resourceGroups/IBP-AUE-TST-RSG-NTW01/providers/Microsoft.Network/privateDnsZones/privatelink.blob.core.windows.net'
param privateDnsZoneTableId = '/subscriptions/5e5f8695-2207-4572-8e80-1065a1b0c135/resourceGroups/IBP-AUE-TST-RSG-NTW01/providers/Microsoft.Network/privateDnsZones/privatelink.table.core.windows.net'
param privateDnsZoneQueueId = '/subscriptions/5e5f8695-2207-4572-8e80-1065a1b0c135/resourceGroups/IBP-AUE-TST-RSG-NTW01/providers/Microsoft.Network/privateDnsZones/privatelink.queue.core.windows.net'
param privateDnsZoneKeyVaultId = '/subscriptions/5e5f8695-2207-4572-8e80-1065a1b0c135/resourceGroups/IBP-AUE-TST-RSG-NTW01/providers/Microsoft.Network/privateDnsZones/privatelink.vaultcore.azure.net'
param privateDnsZoneCognitiveServicesId = '/subscriptions/5e5f8695-2207-4572-8e80-1065a1b0c135/resourceGroups/IBP-AUE-TST-RSG-NTW01/providers/Microsoft.Network/privateDnsZones/privatelink.cognitiveservices.azure.com'
param privateDnsZoneOpenAiId = '/subscriptions/5e5f8695-2207-4572-8e80-1065a1b0c135/resourceGroups/IBP-AUE-TST-RSG-NTW01/providers/Microsoft.Network/privateDnsZones/privatelink.openai.azure.com'
// Foundry agent endpoint (*.services.ai.azure.com) is unresolvable in-VNet
// without this zone wired to the AI Services PE (found 2026-07-17).
param privateDnsZoneServicesAiId = '/subscriptions/5e5f8695-2207-4572-8e80-1065a1b0c135/resourceGroups/IBP-AUE-TST-RSG-NTW01/providers/Microsoft.Network/privateDnsZones/privatelink.services.ai.azure.com'

// STALE PIN — reconcile before the next deploy. This file pins 150 from the
// 2026-07-29 verification, but live model-deployment capacity observed
// 2026-09-04 is 4000 in test and production (1000 in dev). A redeploy is
// therefore NOT a no-op on this deployment: it would cut live capacity from
// 4000 back to 150. The judge is NEW here — gpt-5.6-terra was only ever
// hand-made in dev, which is why EvalGate_Test could not run at all.
param aiModelCapacity = 150
param elevenLabsMaxConcurrentRequests = 20
param azureOpenAiBulkheadPermits = 20
param judgeModelCapacity = 1000
