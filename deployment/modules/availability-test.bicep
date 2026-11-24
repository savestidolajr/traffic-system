// ============================================================================
// Module:  availability-test.bicep
// Purpose: Standard Application Insights availability (web) test that pings the
//          Function App health endpoint from multiple regions. /api/health is an
//          Easy Auth excluded path (function-app.bicep globalValidation), so it is
//          reachable anonymously and returns 200 only when all CRITICAL downstream
//          checks pass (503 otherwise) — exactly the signal we want to alert on.
// ============================================================================

@description('Azure region for the web test resource (co-located with the App Insights component).')
param location string

@description('Prefix applied to the test name (e.g. "IBP-AUE-PRD").')
param namePrefix string

@description('Resource ID of the Application Insights component the test links to (hidden-link tag).')
param appInsightsResourceId string

@description('Absolute URL of the anonymously-reachable health endpoint to ping.')
param healthUrl string

@description('Tags to apply.')
param tags object = {}

resource availabilityTest 'Microsoft.Insights/webtests@2022-06-15' = {
  name: '${namePrefix}-health'
  location: location
  // The hidden-link tag binds the web test to its App Insights component so results
  // surface in the Availability blade and can be queried (availabilityResults table).
  tags: union(tags, {
    'hidden-link:${appInsightsResourceId}': 'Resource'
  })
  kind: 'standard'
  properties: {
    SyntheticMonitorId: '${namePrefix}-health'
    Name: '${namePrefix} — Health Availability'
    Description: 'Pings /api/health from multiple regions; fails when a critical dependency is down.'
    Enabled: true
    Frequency: 300
    Timeout: 30
    Kind: 'standard'
    RetryEnabled: true
    // Australia East + two nearby APAC points (>= 3 locations avoids single-probe false alarms).
    Locations: [
      {
        Id: 'emea-au-syd-edge'
      }
      {
        Id: 'apac-sg-sin-azr'
      }
      {
        Id: 'apac-hk-hkn-azr'
      }
    ]
    Request: {
      RequestUrl: healthUrl
      HttpVerb: 'GET'
      ParseDependentRequests: false
      FollowRedirects: false
    }
    ValidationRules: {
      ExpectedHttpStatusCode: 200
      SSLCheck: true
      SSLCertRemainingLifetimeCheck: 7
    }
  }
}

@description('Resource ID of the availability web test.')
output id string = availabilityTest.id
