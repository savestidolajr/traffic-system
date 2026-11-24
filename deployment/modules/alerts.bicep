// ============================================================================
// Module:  alerts.bicep
// Purpose: Azure Monitor metric alerts for the ArnBulletinGenerator system.
// ============================================================================
//
//   1. FunctionAppErrors — HTTP 5xx responses >= 1 in 5 minutes (platform metric)
//   2. KvAccessDenied — Key Vault DATA-PLANE authorization denials (log alert on the
//      Log Analytics workspace's AzureDiagnostics AuditEvent stream)
//   3. No Executions — zero requests for 15 minutes (platform metric heartbeat)
//   4. AI Throttling — Azure OpenAI / Cognitive Services 429s (>= 5 in 15m) (log alert)
//   5. PoisonQueue — messages in a *-poison queue (log alert on broadcast.queue.depth)
//   6. SynthesisFailures — failed tts_synthesize stages (>= 3 in 1h, latched) (log alert)
//   7. BulletinFailureRate — >= 90% of bulletins failing over 30m (log alert, Sev1)
//   8. DependencyUnhealthy — a dependency is reachable but unusable (log alert, Sev1)
//   9. HealthAvailability — /api/health failing from >= 2 probe locations (webtest metric)
//
// Alerts 6-8 were shaped by the 2026-08-05 dev TTS outage. See each rule's comment
// block: 6 explains the flapping fix, 7 explains why a COUNT is not enough, and 8
// explains why detection lives on a log warning rather than on the /api/health status
// code (that path is the App Service `healthCheckPath`; a 503 recycles instances).
//
// Alerts 1, 3 and 9 are metric alerts on real platform metrics and deploy immediately.
// Alerts 2 and 4-8 are scheduledQueryRules (log-search): 2 against the Log Analytics
// workspace (Key Vault AuditEvent resource logs), 4-8 against the workspace-based
// Application Insights component, querying the dependency + custom telemetry the app
// emits (dependencies, traces, broadcast.* customMetrics/customEvents).
// Log alerts deploy cleanly whether or not data exists yet — no post-deploy ordering.
//
// The four latched rules (5-8, autoMitigate:false) carry muteActionsDuration:'PT1H'.
// Without it a sustained incident re-notifies on EVERY 5-minute evaluation: measured
// 2026-08-10 during a fault-injection test, one injected incident produced 12 alert
// firings and 12 Teams cards in 17 minutes. Latching keeps the incident open; muting
// keeps the channel readable.
//
//
// ── Action Groups ──────────────────────────────────────────────────────────────
// The `actionGroupId` parameter defaults to empty (''), which means alerts will
// fire silently. Supply a valid action group resource ID for notifications.
// ============================================================================

@description('Function App name used for alert rule naming.')
param functionAppName string

@description('Resource ID of the Function App (alert scope).')
param functionAppResourceId string

@description('Resource ID of the Key Vault whose data-plane denials are monitored. Empty (or an empty workspace ID) disables the Key Vault alert.')
param keyVaultResourceId string = ''

@description('Resource ID of the Log Analytics workspace that receives Key Vault AuditEvent resource logs. Empty disables the Key Vault access-denied alert.')
param logAnalyticsWorkspaceResourceId string = ''

@description('Resource ID of the availability web test to alert on. Empty disables the availability alert.')
param availabilityWebTestId string = ''

@description('Resource ID of the action group for notifications. Leave empty to omit actions.')
param actionGroupId string = ''

@description('Azure region for the alert rules (typically "global" for metric alerts).')
param location string

@description('Tags to apply to all alert rules.')
param tags object = {}

@description('Deploy custom-metric alerts (poison queue, synthesis failures). These require the app to be running and emitting custom metrics. Set to true only after first successful bulletin generation.')
param deployCustomMetricAlerts bool = false

@description('Resource ID of the workspace-based Application Insights component that the log-search (custom-metric) alerts query. Empty disables alerts 4-6.')
param appInsightsResourceId string = ''

// ---- Shared action block ----
var actions = !empty(actionGroupId) ? [
  {
    actionGroupId: actionGroupId
  }
] : []

// Log-search (scheduledQueryRules) alerts take a flat array of action-group IDs.
var logAlertActionGroups = !empty(actionGroupId) ? [actionGroupId] : []

// ============================================================================
// Alert 1: Function App Errors (HTTP 5xx)
// ============================================================================

resource functionAppErrorsAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${functionAppName}-errors-alert'
  location: 'global'
  tags: tags
  properties: {
    description: 'Function App HTTP 5xx errors >= 1 in 5 minutes'
    severity: 2
    enabled: true
    scopes: [
      functionAppResourceId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'Http5xx'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'Http5xx'
          metricNamespace: 'Microsoft.Web/sites'
          operator: 'GreaterThanOrEqual'
          threshold: 1
          timeAggregation: 'Count'
          skipMetricValidation: false
        }
      ]
    }
    actions: actions
    autoMitigate: true
  }
}

// ============================================================================
// Alert 2: Key Vault data-plane access denied (log alert on AuditEvent)
// ============================================================================
// This was an activityLogAlerts rule on `category: Security, level: Warning` until
// 2026-08-10, when a monitoring audit proved it could never fire: Key Vault raises
// NO Activity Log Security events (0 in 82 days on dev), because data-plane
// authorization outcomes are resource logs, not control-plane events. Over the same
// window the AuditEvent stream carried real denials — including a chronic, entirely
// unalerted one: the TEST and PROD Function App identities were Forbidden on SecretGet
// ("Public network access is disabled and request is not from a trusted service nor via
// an approved private link") every day, 2083 and 582 events in 90 days. That cause was
// removed on 2026-08-12 by deleting the platform `@Microsoft.KeyVault(...)` app-setting
// references, which App Service resolved over its PUBLIC egress; measured after the
// test and prod deploys, application-identity denials are ZERO in both.
//
// TWO exclusions, both deliberate, both measured:
//
// 1. `OperationName == "Authentication"` — pre-auth token rejections against the vault's
//    public endpoint (342 on dev in 90 days, all from unrelated internet IPs): internet
//    noise, not an access-control problem.
//
// 2. Interactive client app IDs (Azure CLI, Azure PowerShell, Azure Portal). A vault with
//    `publicNetworkAccess: 'Disabled'` refusing a human at a laptop is the firewall doing
//    its job, not an incident — but it is indistinguishable from a real fault in an alert
//    that counts raw denials. Measured on 2026-08-12: this rule fired on TEST and PROD
//    purely because an operator ran read-only `az keyvault secret list`/`list-versions`
//    against the locked vaults while investigating, and it fires on every deploy because
//    `deploy.sh` seeds secrets from outside the VNet. Both are expected and harmless, and
//    training an on-call operator to ignore this rule is worse than not having it.
//    Denials from an APPLICATION identity still alert — that is the failure that matters
//    (the app cannot read its own secrets), and it is what the 48 pre-fix SecretGet
//    denials on prod were. Rows with no appid claim are deliberately NOT excluded, so an
//    unrecognised caller still fails loud.

resource keyVaultAccessDeniedAlert 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = if (!empty(keyVaultResourceId) && !empty(logAnalyticsWorkspaceResourceId)) {
  name: '${functionAppName}-kv-access-denied'
  location: location
  tags: tags
  kind: 'LogAlert'
  properties: {
    displayName: '${functionAppName}-kv-access-denied'
    description: 'Key Vault refused an authenticated caller — check RBAC and private-endpoint reachability.'
    severity: 2
    enabled: true
    scopes: [
      logAnalyticsWorkspaceResourceId
    ]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: 'AzureDiagnostics | where ResourceProvider == "MICROSOFT.KEYVAULT" | where ResultSignature == "Forbidden" or (ResultSignature == "Unauthorized" and OperationName != "Authentication") | where identity_claim_appid_g !in ("04b07795-8ddb-461a-bbee-02f9e1bf7b46", "1950a258-227b-4e31-a9cf-717495945fc2", "c44b4083-3bb0-49c1-b47d-974e53cbdf3c") | summarize AggregatedValue = count()'
          timeAggregation: 'Total'
          metricMeasureColumn: 'AggregatedValue'
          operator: 'GreaterThanOrEqual'
          threshold: 1
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    // Azure rejects muteActionsDuration together with autoMitigate:true ("Auto mitigation
    // must be disabled when action suppression is set"). This rule self-resolves and its
    // dataset is quiet, so auto-mitigation is the better half of that trade.
    autoMitigate: true
    actions: {
      actionGroups: logAlertActionGroups
    }
  }
}

// ============================================================================
// Alert 3: Function App requests dropped to zero (heartbeat)
// ============================================================================
// If the scheduled poller is healthy we should see at least one execution per
// 5-minute window. Zero requests for 15 minutes = host stalled.

resource functionAppHeartbeatAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${functionAppName}-no-executions'
  location: 'global'
  tags: tags
  properties: {
    description: 'No Function App requests for 15 minutes — scheduled poller may be stuck'
    severity: 1
    enabled: true
    scopes: [
      functionAppResourceId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'Requests'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'Requests'
          metricNamespace: 'Microsoft.Web/sites'
          operator: 'LessThanOrEqual'
          threshold: 0
          timeAggregation: 'Total'
          skipMetricValidation: false
        }
      ]
    }
    actions: actions
    autoMitigate: true
  }
}

// ============================================================================
// Alert 5: Poison-queue depth (log alert on broadcast.queue.depth)
// ============================================================================

resource poisonQueueAlert 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = if (deployCustomMetricAlerts && !empty(appInsightsResourceId)) {
  name: '${functionAppName}-poison-queue'
  location: location
  tags: tags
  kind: 'LogAlert'
  properties: {
    displayName: '${functionAppName}-poison-queue'
    description: 'A message landed in a *-poison queue — investigate failed processing.'
    severity: 1
    enabled: true
    scopes: [
      appInsightsResourceId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    criteria: {
      allOf: [
        {
          query: 'customMetrics | where name == "broadcast.queue.depth" | where tostring(customDimensions.queue) endswith "poison" | summarize AggregatedValue = max(value)'
          timeAggregation: 'Maximum'
          metricMeasureColumn: 'AggregatedValue'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: false
    muteActionsDuration: 'PT1H'
    actions: {
      actionGroups: logAlertActionGroups
    }
  }
}

// ============================================================================
// Alert 4: AI throttling (log alert on Azure OpenAI dependency 429s)
// ============================================================================

resource aiThrottlingAlert 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = if (deployCustomMetricAlerts && !empty(appInsightsResourceId)) {
  name: '${functionAppName}-ai-throttling'
  location: location
  tags: tags
  kind: 'LogAlert'
  properties: {
    displayName: '${functionAppName}-ai-throttling'
    description: 'Azure OpenAI / Cognitive Services returning 429 throttling responses (>= 5 in 15m).'
    severity: 2
    enabled: true
    scopes: [
      appInsightsResourceId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: 'dependencies | where target has_any ("openai", "cognitiveservices") | where resultCode == "429" | summarize AggregatedValue = count()'
          timeAggregation: 'Total'
          metricMeasureColumn: 'AggregatedValue'
          operator: 'GreaterThanOrEqual'
          threshold: 5
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
    actions: {
      actionGroups: logAlertActionGroups
    }
  }
}

// ============================================================================
// Alert 6: ElevenLabs synthesis failure COUNT (log alert on tts_synthesize stage)
// ============================================================================
// Window sizing is deliberate. This fired correctly during the 2026-08-05 dev TTS
// outage (ElevenLabs began refusing the legacy API key at ~22:20Z) but with the
// original PT15M window + autoMitigate it FLAPPED: the poller only generates every
// 10-30 minutes, so between runs the window emptied, the alert auto-resolved, and a
// continuous 100% outage arrived as a handful of fire/resolve emails that read like
// intermittent noise. A 1-hour window spans the quiet gap between poller runs, and
// leaving the incident latched (autoMitigate: false, matching the poison-queue rule)
// means a sustained outage stays ONE open incident until a human closes it.

resource synthesisFailureAlert 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = if (deployCustomMetricAlerts && !empty(appInsightsResourceId)) {
  name: '${functionAppName}-synthesis-failures'
  location: location
  tags: tags
  kind: 'LogAlert'
  properties: {
    displayName: '${functionAppName}-synthesis-failures'
    description: 'Multiple ElevenLabs synthesis failures — audio generation is degraded.'
    severity: 2
    enabled: true
    scopes: [
      appInsightsResourceId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT1H'
    criteria: {
      allOf: [
        {
          query: 'customMetrics | where name == "broadcast.stage.tts_synthesize.duration_seconds" | where tostring(customDimensions.success) == "false" | summarize AggregatedValue = count()'
          timeAggregation: 'Total'
          metricMeasureColumn: 'AggregatedValue'
          operator: 'GreaterThanOrEqual'
          threshold: 3
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: false
    muteActionsDuration: 'PT1H'
    actions: {
      actionGroups: logAlertActionGroups
    }
  }
}

// ============================================================================
// Alert 7: Bulletin failure RATE (log alert) — "is the product working at all?"
// ============================================================================
// A COUNT cannot distinguish "3 bulletins failed" from "every bulletin is failing".
// During the 2026-08-05 outage the count alert reported `Metric value 3, Threshold 3`
// at Sev2 — indistinguishable from a minor blip, while in fact 100% of bulletins were
// falling back to a generic filler cart with no traffic content. This rate alert is
// the "total outage" signal: Sev1 when nearly every bulletin emitted in the last
// half-hour failed. `broadcast.bulletin.emitted` carries customDimensions.success and
// is emitted per bulletin, so it is the honest denominator (the sibling
// broadcast.fallback.activated event is emitted too sparsely to alert on).
// A window with zero bulletins yields no row and cannot alert — the no-executions
// heartbeat covers a silent host.
//
// MINIMUM VOLUME (2026-09-03). A rate needs a denominator worth dividing by. Measured
// on the live platform: production emitted 4 bulletins in 24 hours with schedules off,
// one of them failed, and the rule fired Sev1 "nearly every bulletin is failing" six
// times on a 1-of-1 window; test fired 17 times the same day. A single failure is not
// a total outage, and a Sev1 that says it is teaches an on-call reader to ignore Sev1.
// The rate is therefore computed only once the window holds at least 3 bulletins, which
// every armed station clears (a 20-minute cron emits ~90 per 30-minute window across
// two stations) and a quiet or parked environment does not. Single and repeated
// failures are still covered: the synthesis-failure rule alerts at 3 in an hour, the
// dependency rule catches an unusable provider, and every failed bulletin is visible
// in the console and the workbook.

resource bulletinFailureRateAlert 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = if (deployCustomMetricAlerts && !empty(appInsightsResourceId)) {
  name: '${functionAppName}-bulletin-failure-rate'
  location: location
  tags: tags
  kind: 'LogAlert'
  properties: {
    displayName: '${functionAppName}-bulletin-failure-rate'
    description: 'Nearly every bulletin is failing — listeners are getting fallback content, not traffic.'
    severity: 1
    enabled: true
    scopes: [
      appInsightsResourceId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT30M'
    criteria: {
      allOf: [
        {
          query: 'customMetrics | where name == "broadcast.bulletin.emitted" | summarize total = count(), failed = countif(tostring(customDimensions.success) == "false") | extend AggregatedValue = iff(total >= 3, 100.0 * failed / total, 0.0) | project AggregatedValue'
          timeAggregation: 'Maximum'
          metricMeasureColumn: 'AggregatedValue'
          operator: 'GreaterThanOrEqual'
          threshold: 90
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: false
    muteActionsDuration: 'PT1H'
    actions: {
      actionGroups: logAlertActionGroups
    }
  }
}

// ============================================================================
// Alert 8: Health-check dependency unhealthy (log alert on the health warning)
// ============================================================================
// Closes the gap the 2026-08-05 outage exposed. The /api/health availability webtest
// ran 36 times through a total TTS outage and reported ZERO failures, because the
// ElevenLabs probe sent no credential and accepted any HTTP response as proof of life.
//
// The probe now sends the real credential and reports a rejected key as unhealthy, but
// /api/health MUST NOT start returning 503 for it: that path is also the Function App's
// `healthCheckPath`, so a non-200 makes App Service recycle instances — turning a
// third-party credential problem into a self-inflicted outage. ElevenLabs therefore
// stays a NON-critical check (health still returns 200) and detection happens here
// instead, on the warning the health endpoint logs.
//
// Threshold and window are sized against SAMPLING, measured not assumed. host.json enables
// adaptive sampling with `excludedTypes: "Request"` — so requests survive intact but TRACES
// are sampled: /api/health was measured taking 24 requests per 15 min while only 4 of the
// resulting warning traces reached App Insights (~17% survival). A "persistence filter" of
// >=3 could therefore miss a genuine outage in an unlucky window, which is the exact failure
// this rule exists to prevent. So: >=1 over a 30-minute window (~48 health calls, so the
// probability of zero surviving traces is negligible), and `autoMitigate: false` latches the
// incident so a single survivor is enough to raise it and it stays raised until a human
// closes it. A blip cannot cause noise here anyway — the health endpoint only emits this
// warning when a dependency is genuinely unusable, which is not a transient condition.

resource dependencyUnhealthyAlert 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = if (deployCustomMetricAlerts && !empty(appInsightsResourceId)) {
  name: '${functionAppName}-dependency-unhealthy'
  location: location
  tags: tags
  kind: 'LogAlert'
  properties: {
    displayName: '${functionAppName}-dependency-unhealthy'
    description: 'A downstream dependency is reachable but unusable (e.g. an API key the provider rejects).'
    severity: 1
    enabled: true
    scopes: [
      appInsightsResourceId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT30M'
    criteria: {
      allOf: [
        {
          query: 'traces | where message has "Health check dependencies unhealthy" | summarize AggregatedValue = count()'
          timeAggregation: 'Total'
          metricMeasureColumn: 'AggregatedValue'
          operator: 'GreaterThanOrEqual'
          threshold: 1
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: false
    muteActionsDuration: 'PT1H'
    actions: {
      actionGroups: logAlertActionGroups
    }
  }
}

// ============================================================================
// Alert 9: Health availability (metric alert on the /api/health web test)
// ============================================================================
// availability-test.bicep has pinged /api/health from three APAC locations since
// bring-up, but until 2026-08-10 no rule watched the result — a monitoring audit
// found zero availability alert rules in all three environments, so a failing
// probe (app stopped, DNS/TLS failure, expiring certificate, /api/health 503)
// notified nobody. `failedLocationCount: 2` of three locations is the standard
// guard against a single flaky probe point.
//
// This overlaps alert 1 for the 503 case only (a 503 from /api/health is also an
// Http5xx on the site). Everything else the web test sees — TLS, DNS, the site
// being stopped, regional unreachability — has no other detector.

resource healthAvailabilityAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = if (!empty(availabilityWebTestId) && !empty(appInsightsResourceId)) {
  name: '${functionAppName}-health-availability'
  location: 'global'
  tags: union(tags, {
    'hidden-link:${availabilityWebTestId}': 'Resource'
    'hidden-link:${appInsightsResourceId}': 'Resource'
  })
  properties: {
    description: '/api/health is failing from multiple probe locations — the platform is unreachable or a critical dependency is down.'
    severity: 1
    enabled: true
    scopes: [
      availabilityWebTestId
      appInsightsResourceId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.WebtestLocationAvailabilityCriteria'
      webTestId: availabilityWebTestId
      componentId: appInsightsResourceId
      failedLocationCount: 2
    }
    actions: actions
    autoMitigate: true
  }
}

// ============================================================================
// Outputs
// ============================================================================

@description('Resource IDs of all created alert rules.')
output alertRuleIds array = [
  functionAppErrorsAlert.id
  functionAppHeartbeatAlert.id
  aiThrottlingAlert.id
  poisonQueueAlert.id
  synthesisFailureAlert.id
  bulletinFailureRateAlert.id
  dependencyUnhealthyAlert.id
]
