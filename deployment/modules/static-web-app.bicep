// ──────────────────────────────────────────────────────────────
// Module: Static Web App
// Deploys an Azure Static Web App for the ArnBulletinGenerator
// React/Vite frontend with configurable deployment provider.
// ──────────────────────────────────────────────────────────────

@description('Azure region for the Static Web App. Note: SWA has limited region availability.')
@allowed([
  'centralus'
  'eastasia'
  'eastus2'
  'westeurope'
  'westus2'
])
param location string

@description('Name of the Static Web App.')
param staticWebAppName string

@description('Deployment provider for the Static Web App.')
@allowed(['None', 'GitHub'])
param provider string = 'None'

@description('GitHub repository URL (required when provider is GitHub).')
param repositoryUrl string = ''

@description('GitHub branch name (required when provider is GitHub).')
param branch string = ''

@description('Application source location in the repository.')
param appLocation string = 'web'

@description('Build output directory relative to app location.')
param outputLocation string = 'dist'

@description('Base URL for API calls from the frontend.')
param apiBaseUrl string = '/api'

@description('AAD application (client) ID for the SWA custom Entra provider (test/prod RBAC). Empty (default) skips setting AAD_CLIENT_ID/AAD_CLIENT_SECRET, leaving dev on the built-in provider.')
param aadClientId string = ''

@description('AAD client secret for the SWA custom Entra provider. Resolved once via Key Vault getSecret() by the caller (main.bicep) — never stored as a live value in source. Empty (default) skips setting AAD_CLIENT_ID/AAD_CLIENT_SECRET.')
@secure()
param aadClientSecret string = ''

@description('Custom domain (e.g. bulletins-dev.arn.com.au) to bind to the SWA. Requires the CNAME to the default hostname to already exist in ARN DNS (TTL < 48h). Empty (default) skips domain binding.')
param customDomainName string = ''

@description('Enable enterprise-grade edge (managed Azure Front Door CDN). Requires the custom domain to be bound + validated on a PRIOR deploy (two-step). false leaves the live state untouched — a redeploy can never silently disable edge enabled out-of-band.')
param enableEnterpriseEdge bool = false

@description('Tags to apply to all resources.')
param tags object = {}

// ── Conditional build properties based on deployment provider ──

var buildProperties = {
  appLocation: appLocation
  outputLocation: outputLocation
}

// Idempotent by construction: every redeploy with the same params sets the same
// values (or the same "unset" no-op), so a redeploy can never silently wipe
// settings that were previously configured out-of-band (found 2 Jul 2026 —
// see docs/canonical/decisions.md — the prior full-overwrite dropped the SWA's
// AAD app settings and 404'd /.auth/login/aad until manually restored).
var configureAad = !empty(aadClientId) && !empty(aadClientSecret)

// ── Static Web App ──

resource staticWebApp 'Microsoft.Web/staticSites@2024-04-01' = {
  name: staticWebAppName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: union(
    {
      repositoryUrl: provider == 'GitHub' ? repositoryUrl : ''
      branch: provider == 'GitHub' ? branch : ''
      buildProperties: buildProperties
      provider: provider
    },
    // Only set when enabling: omitting the property leaves the live state
    // untouched (same idempotence rationale as configureAad above).
    enableEnterpriseEdge ? { enterpriseGradeCdnStatus: 'Enabled' } : {}
  )
}

// ── Custom domain ──
// CNAME-validated. MUST be bound before enterprise-grade edge is enabled:
// edge-enabled apps only accept dns-txt-token validation for new domains,
// so the rollout is two deploys (domain first, then edge).

resource customDomain 'Microsoft.Web/staticSites/customDomains@2024-04-01' = if (!empty(customDomainName)) {
  parent: staticWebApp
  name: customDomainName
  properties: {
    validationMethod: 'cname-delegation'
  }
}

// ── App Settings ──

resource staticWebAppSettings 'Microsoft.Web/staticSites/config@2024-04-01' = {
  parent: staticWebApp
  name: 'appsettings'
  properties: union(
    { VITE_API_BASE_URL: apiBaseUrl },
    configureAad
      ? {
          AAD_CLIENT_ID: aadClientId
          AAD_CLIENT_SECRET: aadClientSecret
        }
      : {}
  )
}

// ── Outputs ──

@description('Name of the Static Web App.')
output name string = staticWebApp.name

@description('Default hostname of the Static Web App.')
output defaultHostname string = staticWebApp.properties.defaultHostname

@description('Hostname the operator console should be addressed by: the custom domain when bound, else the default hostname.')
output consoleHostname string = empty(customDomainName) ? staticWebApp.properties.defaultHostname : customDomainName
