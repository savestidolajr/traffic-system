using './main.bicep'

// ==========================================================================
// IBP — Dev Parameter File (IBP-AUE-DEV-*)
// ==========================================================================
// Minimal dev environment for integration testing.
// All-private networking is the agreed target for every environment incl.
// dev, but dev deploys without PEs until ARN's network allocations land.
//
// OPEN ITEMS (see deployment/README.md):
//   - logAnalyticsWorkspaceResourceId: pending MGT-vs-IBP answer (ARN technology lead).
//   - actionGroupId: pending mgt-aue-ibp-agp-* resource IDs (ARN technology lead).
// ==========================================================================

param environment = 'dev'
// Generation-capacity autoscale: one warm instance in non-production.
param appServicePlanMinInstances = 1
param appServicePlanMaxInstances = 6
param location = 'australiaeast'
param aiServicesLocation = 'australiaeast'
param swaLocation = 'eastasia'

// Custom domain + enterprise-grade edge (Option A, 2026-07-17 decision).
// ARN DNS (ARN technology lead, 31 Jul 2026) created:
//   ain-dev.arn.com.au  CNAME  brave-desert-09eeae900.7.azurestaticapps.net
// Domain bound + validated 31 Jul 2026; SWA issues/renews the managed TLS cert
// automatically, so ARN does NOT need to supply its own certificate.
// The edge is the half that fixes latency: the custom domain alone still resolved
// to the am2 (Amsterdam) stamp. Enabling was ATTEMPTED on dev (31 Jul, and again
// via the 9 Aug supervised deploy): both times the SWA sat in 'Enabling' for an
// hour-plus, every ARM write failed with 'operation in progress', the pipeline's
// staticWebApp module timed out (runs 2735 and 2788), and Azure eventually rolled
// the status back to Disabled. False here matches the real steady state; retry
// the edge OUT OF BAND (az staticwebapp update / support case), and only commit
// true once the resource actually reports Enabled.
param swaCustomDomain = 'ain-dev.arn.com.au'
param swaEnterpriseEdge = false

// Handover documentation site (IBP-AUE-<ENV>-SWA-DOC01). Published on dev and
// prod only: test is a UAT environment with no reason to host documentation.
// Keep this in step with the `deployDocsSite` pipeline variable in the
// matching variable group, which gates the deployment job.
param deployDocsSite = true

// Set to false once ARN pre-creates the RGs with access instead.
param createResourceGroups = true

// PLACEHOLDER — empty creates IBP-AUE-DEV-LAW-APP01 in IBP-AUE-DEV-RSG-LAW01.
// If ARN confirms central logging, set the MGT workspace resource ID.
param logAnalyticsWorkspaceResourceId = ''

// PLACEHOLDER — empty creates IBP-AUE-DEV-AIA-APP01. Swap for the
// mgt-aue-ibp-agp-PlatformAvailability resource ID once ARN supplies it.
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

// Teams notifications — self-served 2026-07-05. The Logic App + Teams API
// connection deploy into IBP-AUE-DEV-RSG-APP01; the module's trigger URL is
// stored as the KV `webhookEndpointUrl` secret and the Function App reads it
// via KV reference (Webhook__Enabled=true wired by main.bicep).
// Channel: "IBP Notifications (Dev)" in the "Project B10" team. That team is
// ARN-owned (ARNTrafficNetwork@arn.com.au, verified 2026-09-03), so the channel
// itself survives handover; only the connection's consent does not. The Teams
// API connection deploys Unauthorized — authorise ONCE via Portal: API
// Connections → IBP-AUE-DEV-LOA-APP01-teams → Edit API connection → Authorize.
// It is currently consented as the delivery representative's ARN account, so it
// must be re-authorised by a lasting ARN account before that account lapses: a
// lapsed consent keeps reporting Connected and silently drops every card.
// ARN's own ops channel is a swap of the two IDs below (ARN to nominate).
param deployNotifications = true
param teamsGroupId = 'fd3865c1-25f8-4cd1-b991-3ec32ddd5f71'
param teamsChannelId = '19:ee9d2f21a7454a8daef8bbb5d1cf3b5b@thread.tacv2'
param webhookEndpointUrl = ''

// Azure Monitor alerts (the 9 rules in alerts.bicep — errors, kv-access-denied,
// no-executions, poison-queue, ai-throttling, synthesis-failures,
// bulletin-failure-rate, dependency-unhealthy, health-availability — plus the
// availability test)
// post into the SAME Teams channel as the app's own cards, via the notification
// Logic App as an action-group webhook receiver. Interim while ARN's on-call DL
// / central mgt-aue-ibp-agp-* group is outstanding: email alerting is EMPTY
// (see alertEmailReceivers above), so Teams is the only live path. That is a
// known gap, not a design — a Logic App in this subscription cannot be trusted
// to report that this subscription is down, so ARN should still nominate an
// on-call email/SMS destination even after confirming Teams cards arrive.
param routeAlertsToTeams = true

// Dead-air filler cart (synthesised 25 Jun, lives in the broadcasts container).
// In IaC because a hand-set app setting is wiped on every redeploy (QT-001).
param fillerCartAudioUrl = 'https://ibpauedevstaapp01.blob.core.windows.net/broadcasts/filler-cart.wav'

// Per-voice filler cart script (transcribed from the approved evergreen WAV, 2026-08-04):
// each voice synthesises this copy, cached per (voice × settings × model × script).
// Same audio/wording convention as the evergreen cart. Empty disables per-voice carts.
param fillerCartScript = 'You\'re listening to Your Traffic Network, keeping you moving. We\'ll have your next full update on the roads for you very shortly, so keep it locked here. In the meantime, give yourself a little extra time, take it easy through the work zones and we\'ll be back soon with the latest right across the network.'

// Weather + fuel enabled platform-wide on dev per the 2026-07-06 decision (decisions.md) —
// was CLI-set, silently wiped by the 2026-07-08 Bicep redeploy (same QT-001 class as
// fillerCartAudioUrl above). Now in IaC so it survives redeploys. MUST stay false in
// test/prod until ARN gives the same explicit go-ahead there.
param weatherEnabled = true
// Official feeds are explicit per environment so endpoint drift cannot hide in
// the compiled defaults. QldApiKey is intentionally absent: seed the dedicated
// OfficialFeeds--QldApiKey secret in this environment's Key Vault.
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
param fuelEnabled = true

// No VNet for dev yet — subnet IDs pending the ARN network team
param virtualNetworkSubnetId = ''

param tags = {
  project: 'information-bulletin-platform'
  environment: 'dev'
  managedBy: 'bicep'
  costCenter: 'dev-team'
}

param deployRoleAssignments = true
param deployLinkedBackend = true

// AAD Easy Auth — required. Tenant is ARN's (ausradio.onmicrosoft.com). Dev
// uses a self-created app registration (no ARN dependency: dev's
// `devAuthFallbackRole` grants Admin to any authenticated AAD caller, so no
// real app-role assignment from ARN is needed here, unlike test/prod).
param aadClientId = '4671da9f-93b0-49bd-9310-366ede758810'
param aadTenantId = '91d5bc47-4df3-4ad0-a5ae-3d3249eceaf1'

// DEV-ONLY auth bypass: until ARN assigns Operator/Admin app roles on the API app
// registration (and the SWA is wired to a custom Entra provider so the forwarded
// principal carries the roles claim), the SWA built-in provider only yields the
// anonymous/authenticated pseudo-roles. This grants every authenticated caller Admin
// so the operator console is usable. MUST stay empty in test/prod.
param devAuthFallbackRole = 'Admin'

// DEV-ONLY debugging: persist the exact prompt/response for every LLM call during bulletin
// generation, bounded to the most recent 20 jobs, so a failure can be reproduced and inspected
// exactly as the model saw it rather than reconstructed from LogsJson's narrated summary.
// MUST stay false in test/prod — captures include the full source traffic data verbatim.
param debugCaptureLlmCalls = true
param debugMaxCapturedJobs = 20

// DEV-ONLY headless access: lets Azure CLI management tokens act as Easy Auth bearer
// tokens (programmatic API access, load tests). MUST stay false in test/prod; retiring
// it behind an automation service principal remains an open ARN-owned item.
param allowArmAudience = true

// Foundry agent pipeline (values = live dev state 2026-07-16, promoted to IaC so a
// Bicep redeploy can never wipe them — QT-001 class; see
// the live dev app settings captured on 2026-07-16 as the ground truth).
param foundryProjectEndpoint = 'https://ibp-aue-dev-fdy-app01.services.ai.azure.com/api/projects/bulletin-generator'
param safetyAgentName = 'arn-safety'
param directorAgentName = 'arn-director'
param reasoningEffort = 'none'

// Model deployment capacities pinned to the LIVE dev values so a deploy is a
// no-op on them: the judge sits at 5000 TPM from a 2026-07-10 trial and the
// eval suite runs it with --repeats 3, so dropping it to the 1000 default would
// throttle EvalGate.
//
// STALE PIN — reconcile before the next deploy. Live model-deployment capacity
// observed 2026-09-04 is 1000 in dev (4000 in test and production), while this
// file pins 1986 from the 2026-08-31 out-of-band drift. A redeploy is therefore
// NOT a no-op on this deployment: it would raise live capacity to 1986. Note
// this is not a fix for the shared-pool rejections: those arrive with our own
// quota idle (429 observed at 149/150 requests and 149,936/150,000 tokens
// remaining), so capacity is not the binding limit and raising this number
// buys nothing.
param aiModelCapacity = 1986
param elevenLabsMaxConcurrentRequests = 10
param azureOpenAiBulkheadPermits = 10
param judgeModelCapacity = 5000

// Alternate deployment for the LLM fallback. On a 429, a transient 5xx, or stall
// exhaustion against the primary, one immediate no-backoff attempt is made here
// before the throttle ladder. Measured 2026-08-31: gpt-5.6-luna's shared pool
// returned 38 x 503 'no healthy upstream' and 32 stalls over seven days in dev,
// while gpt-5.6-terra took 0/29 stalls and 0 throttles in the same window.
// Terra is a DIFFERENT model, so a fallback-served bulletin may read differently
// from a primary-served one; that is accepted because the alternative is a failed
// bulletin. ARN's standing preference remains Luna for the primary.
param aiFallbackModelName = 'gpt-5.6-terra'

// Route the four workflow roles through their Foundry agents rather than straight to
// the model. This is the ONLY thing that puts a bulletin's LLM calls in the Foundry
// portal's Tracing tab: the raw model endpoint emits nothing there, so the agents can
// exist and be byte-identical to what the app sends and still show zero traces because
// they were never called. Verified on dev 2026-08-31 before enabling: arn.route on every
// captured call read 'gpt-5.6-luna' against host cognitiveservices.azure.com, i.e. the
// raw endpoint, while all ten agents sat provisioned and idle.
//
// The trade, stated plainly: the agent endpoint REJECTS a request-side `text` field, so
// the strict json_schema contract the raw route enforces degrades to the agent's baked
// json_object. A malformed reply therefore becomes a downstream parse error instead of a
// provider-side failure. It also rejects a request-side `instructions`, which is why the
// station-specific system prompt travels inside `input` on this route. It does NOT improve
// latency: the agent endpoint returned the same `no healthy upstream` as the raw one.
param enableWorkflowAgents = true
