// ──────────────────────────────────────────────────────────────
// Module: Linked Backend
// Links a Function App as a backend to a Static Web App,
// enabling the SWA reverse proxy for /api/* routes.
// ──────────────────────────────────────────────────────────────

@description('Name of the Static Web App.')
param staticWebAppName string

@description('Resource ID of the Function App to link as backend.')
param functionAppId string

@description('Azure region of the Function App.')
param functionAppRegion string

// ── Existing SWA reference ──

resource staticWebApp 'Microsoft.Web/staticSites@2024-04-01' existing = {
  name: staticWebAppName
}

// ── Linked Backend ──

resource linkedBackend 'Microsoft.Web/staticSites/linkedBackends@2024-04-01' = {
  parent: staticWebApp
  name: 'backend'
  properties: {
    backendResourceId: functionAppId
    region: functionAppRegion
  }
}
