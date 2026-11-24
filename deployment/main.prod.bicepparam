using './main.bicep'

// ==========================================================================
// IBP — Production Parameter File (IBP-AUE-PRD-*)
// ==========================================================================
// Secrets (API keys, webhook URLs) are NOT committed here. Inject via
// CI/CD pipeline variables / Key Vault references / deploy.sh env vars.
//
// SELF-SERVE POSTURE (2026-07-05): prod deploys the same way Test did on
// 29 Jun — createNetwork=true self-builds an island VNet + subnets + the 5
// privatelink DNS zones, so NOTHING here blocks on ARN network inputs.
// When ARN supplies the prod hub-connected VNet, migrate exactly as Test
// was on 3 Jul (re-carve subnets, link zones, move VNet integration,
// recreate PEs, flip createNetwork=false) — see decisions.md 2026-07-03.
//
// OPEN ITEMS — ARN-blocked, deploy proceeds without them (documented in
// docs/operations/open-items.md + task board):
//   - Prod hub VNet + firewall allowlist for HERE/ElevenLabs egress (ARN
//     technology lead / ARN network team). Island VNet has no firewall so routeAllTraffic=true works.
//   - logAnalyticsWorkspaceResourceId: MGT-vs-IBP answer (ARN technology lead). Empty
//     self-creates IBP-AUE-PRD-LAW-APP01.
//   - actionGroupId + receivers: prod DLs/on-call undefined on ARN's side.
//     Empty self-creates IBP-AUE-PRD-AIA-APP01 (no receivers yet).
//   - webhookEndpointUrl / Teams channel: ARN to nominate the prod
//     notification channel; bring-up is one Portal authorise + redeploy
//     with deployNotifications=true (proven on dev).
//   - auditAccessGroupObjectId: audit-evidence reader AAD group (CC-002).
//   - Retention delete rules stay 0 (keep forever) until ARN confirms
//     figures (CC-010, ARN legal).
// ==========================================================================

param environment = 'prod'
// Generation-capacity autoscale: two warm instances for production HA.
param appServicePlanMinInstances = 2
param appServicePlanMaxInstances = 6
param location = 'australiaeast'
param aiServicesLocation = 'australiaeast'
param swaLocation = 'eastasia'

// Custom domain + enterprise-grade edge (Option A, 2026-07-17 decision).
// ARN DNS (ARN technology lead, 31 Jul 2026) created:
//   ain.arn.com.au  CNAME  lively-glacier-054847700.7.azurestaticapps.net
// Domain bound + validated 31 Jul 2026; SWA issues/renews the managed TLS cert
// automatically, so ARN does NOT need to supply its own certificate.
// Next step (optional): set swaEnterpriseEdge = true and deploy again.
param swaCustomDomain = 'ain.arn.com.au'
param swaEnterpriseEdge = false

// Handover documentation site (IBP-AUE-<ENV>-SWA-DOC01). Published on dev and
// prod only: test is a UAT environment with no reason to host documentation.
// Keep this in step with the `deployDocsSite` pipeline variable in the
// matching variable group, which gates the deployment job.
param deployDocsSite = true

// Self-serve: we create the RGs (PIM Owner on the shared IBP sub).
param createResourceGroups = true

// PLACEHOLDER — empty creates IBP-AUE-PRD-LAW-APP01 in IBP-AUE-PRD-RSG-LAW01.
// ARN's CSV names MGT-AUE-PRD-LAW-APP01 (central logging, their management
// subscription); set that workspace resource ID once the ARN technology lead confirms.
param logAnalyticsWorkspaceResourceId = ''

// PLACEHOLDER — empty creates IBP-AUE-PRD-AIA-APP01. Swap for the
// mgt-aue-ibp-agp-PlatformAvailability resource ID once ARN supplies it
// (prod DLs still undefined in ARN's CSV).
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
// connection is OAuth-consented per environment by a named account, so it must
// be re-authorised by a lasting ARN account before the consenting account
// lapses; a lapsed consent still reports Connected and silently drops cards.
// ARN should still nominate an on-call email/SMS destination even after
// confirming Teams cards arrive.
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

// Teams notifications — Logic App + Teams API connection (same module proven
// on dev 2026-07-05). INTERIM channel: "IBP Notifications (Prod)" in the
// "Project B10" team, created self-serve pending ARN's ops-channel nomination
// (documented blocker) — swapping is a 2-param change + one connection
// re-authorise. The team itself is ARN-owned (ARNTrafficNetwork@arn.com.au,
// verified 2026-09-03), so it survives handover; the connection's consent does
// not. The connection deploys Unauthorized; authorise via listConsentLinks +
// confirmConsentCode (docs/operations/notifications.md). It is consented today
// as the delivery representative's ARN account and must be re-authorised by a
// lasting ARN account before that account lapses.
param deployNotifications = true
param teamsGroupId = 'fd3865c1-25f8-4cd1-b991-3ec32ddd5f71'
param teamsChannelId = '19:9ecbf7f0e088465d80a6a39a3e2227f3@thread.tacv2'
param webhookEndpointUrl = ''

// Dead-air filler cart — synthesised 7 Jul 2026, same audio/wording as dev's
// approved filler-cart.wav, uploaded to this environment's broadcasts container.
param fillerCartAudioUrl = 'https://ibpaueprdstaapp01.blob.core.windows.net/broadcasts/filler-cart.wav'

// Per-voice filler cart script (transcribed from the approved evergreen WAV, 2026-08-04):
// each voice synthesises this copy, cached per (voice × settings × model × script).
// Same audio/wording as dev's. Empty disables per-voice carts.
param fillerCartScript = 'You\'re listening to Your Traffic Network, keeping you moving. We\'ll have your next full update on the roads for you very shortly, so keep it locked here. In the meantime, give yourself a little extra time, take it easy through the work zones and we\'ll be back soon with the latest right across the network.'

// ── Playout delivery (Zetta) ────────────────────────────────────────────────
// RE-ENABLED 2026-08-12 by explicit attended decision, closing the emergency-off applied
// earlier the same day after a restart sweep walked 15 expired slots and wrote stale audio
// to the live share.
//
// Two independent guards now stand between a stale bulletin and the share, which is what
// makes this different from the state that produced the incident:
//   1. The deadline checks in JobWatchdog / SchedulePollerService / Job (commit d5acbcab) —
//      the sweep can no longer pick up a slot whose moment has passed.
//   2. A refusal at the delivery boundary itself: ZettaFileDeliveryService.DeliverAsync takes
//      the slot deadline and refuses before it downloads anything, deliberately in a different
//      layer from the orchestrator and watchdog, so one bug cannot defeat both.
// Delivery still has exactly one caller (PublishJobAsync), which requires an Approved job and
// a matching ETag, so nothing reaches the share without a human approving it.
param zettaEnabled = true
param zettaTransport = 'AzureFilesDrop'
param zettaDropAccountName = 'ibpauetststaftp01'
param zettaDropAccountResourceGroup = 'IBP-AUE-TST-RSG-STA01'
param zettaDropFileShareName = 'informationbulletins'
param zettaFileNamePattern = '{MarketId}_TRAFF'
param zettaMarketCodesJson = '{"sydney":"[SYD]KIIS90s","cairns-4ca":"[CAI]STAR1027"}'

// createNetwork=true supplies this from the network module.
param virtualNetworkSubnetId = ''

param tags = {
  project: 'information-bulletin-platform'
  environment: environment
  managedBy: 'bicep'
  costCenter: 'production'
}

param deployRoleAssignments = true
param deployLinkedBackend = true

// SWA custom-Entra-provider RBAC. TWO-PHASE (see deployment/README.md
// "Production SWA auth bring-up"): this MUST stay `false` for the FIRST-EVER
// prod deploy — `getSecret('AadClientSecret')` resolves at ARM parameter-binding
// (before any resource deploys), so it fails unless the Key Vault + the
// AadClientSecret secret already exist. Phase 1 deploys infra (false) + seeds
// the KV secret; Phase 2 flips this to `true` and redeploys, after which every
// redeploy is idempotent (never wipes the SWA AAD_CLIENT_ID/AAD_CLIENT_SECRET).
// LEAVE `true` once auth is live — reverting to `false` re-drops those settings.
// PHASE 2 LIVE (7 Jul 2026): ARN technology lead delivered IBP-AUE-SPN-PRD-Traffic
// (c3b659e4-23f2-4522-b3f0-b0e48b73fa96); AadClientId/AadClientSecret seeded
// into IBP-AUE-PRD-KYV-APP01 via a temporary firewall allow + re-lock.
param configureSwaAadKeyVaultSettings = true

// AAD Easy Auth — real prod app registration (ARN technology lead, 7 Jul 2026).
// Tenant is ARN's — pinned, never "common".
param aadClientId = 'c3b659e4-23f2-4522-b3f0-b0e48b73fa96'

param aadTenantId = '91d5bc47-4df3-4ad0-a5ae-3d3249eceaf1'

// FULL PROD RBAC — no dev fallback. Empty means every caller is authorized
// only by the Operator/Admin app roles forwarded from the custom Entra
// provider; unassigned callers get 403. NEVER set this in test/prod.
param devAuthFallbackRole = ''

// DEV-ONLY debugging feature (full prompt/response capture). NEVER set this in test/prod.
param debugCaptureLlmCalls = false

// Foundry agent pipeline (provisioned in the PROD Foundry project 2026-07-16;
// matches dev's live configuration — QT-001: keep in IaC so redeploys can't wipe).
param foundryProjectEndpoint = 'https://ibp-aue-prd-fdy-app01.services.ai.azure.com/api/projects/bulletin-generator'
param safetyAgentName = 'arn-safety'
param directorAgentName = 'arn-director'
param reasoningEffort = 'none'

// Config-bundle imports (admin export/import surface) require a SECOND admin's
// approval in prod — same two-person posture as production deploys.
param configImportRequireSecondApprover = true

// Production storage must survive a single-zone failure. ZRS replicates synchronously
// across three AZs in the same region. Upgrade to GZRS if cross-region durability is needed.
param storageSkuName = 'Standard_ZRS'

// Prod retains deleted blobs for 90 days (audit compliance) and supports
// point-in-time restore within the first 30 days.
param blobSoftDeleteDays = 90
param enableBlobRestore = true
param blobRestoreDays = 30

// PLACEHOLDER — empty skips the assignment. Set to the object ID of the ARN
// audit-access AAD group (the audit-evidence stakeholders, CC-002) once ARN creates it.
param auditAccessGroupObjectId = ''

// Retention figures (CC-010, self-decided 2026-07-23 — see decisions.md).
// 90d published-audio floor (CC-002: 60d post-complaint + 30d complaint
// window), 30d for never-aired candidates (debugging window only, not
// evidence), 3yr audit-evidence text (cheap, useful for a later dispute).
param publishedAudioDeleteAfterDays = 90
param unpublishedAudioDeleteAfterDays = 30
param auditExportsDeleteAfterDays = 1095

// Telemetry retention (operator identities appear in logs). Only applies while
// logging stays IBP-side; ARN central logging owns its own retention.
param logRetentionDays = 30

// Private endpoints — production data plane is private only.
// SELF-BUILT network (island VNet, 10.61.0.0/24 defaults from network.bicep —
// never peered, so no CIDR coordination needed until the hub migration).
// PEs land in IBP-AUE-PRD-RSG-NTW01 named IBP-AUE-PRD-PEP-APP01..05.
// routeAllTraffic=true is CORRECT here (no hub firewall on the island path);
// after the hub migration it needs the ARN firewall allowlist for HERE/
// ElevenLabs or routeAllTraffic=false like Test — open governance item.
param createNetwork = true
param routeAllTraffic = true
param deployPrivateEndpoints = true
param dataPlanePublicNetworkAccess = 'Disabled'

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
