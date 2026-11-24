// ──────────────────────────────────────────────────────────────
// Module: Function App Auth Settings
// Reapplies Easy Auth after SWA linked-backend creation mutates authsettingsV2.
// ──────────────────────────────────────────────────────────────

@description('Name of the Function App.')
param functionAppName string

@description('AAD app registration client ID for Easy Auth. Required; auth fails closed without it.')
param aadClientId string

@description('AAD tenant ID that owns the Function App AAD app registration.')
param aadTenantId string

@description('Default hostname of the linked Static Web App, e.g. polite-flower-*.azurestaticapps.net. Empty skips the SWA-linked provider.')
param linkedStaticWebAppHostname string = ''

@description('Allow Azure CLI management tokens (audience https://management.azure.com/) as Easy Auth bearer tokens. DEV-ONLY; MUST stay false in test/prod.')
param allowArmAudience bool = false

resource functionApp 'Microsoft.Web/sites@2024-04-01' existing = {
  name: functionAppName
}

var staticWebAppsIdentityProvider = empty(linkedStaticWebAppHostname) ? {} : {
  azureStaticWebApps: {
    enabled: true
    registration: {
      clientId: linkedStaticWebAppHostname
    }
  }
}

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
  identityProviders: union({
    azureActiveDirectory: {
      enabled: true
      registration: {
        openIdIssuer: 'https://sts.windows.net/${aadTenantId}/'
        clientId: aadClientId
        // No clientSecretSettingName: Return401 means this provider only validates bearer
        // tokens against the OIDC JWKS and never exchanges an authorization code, so no
        // client secret is involved. See the note in function-app.bicep's commonAppSettings.
      }
      validation: {
        allowedAudiences: concat(
          ['api://${aadClientId}'],
          allowArmAudience ? ['https://management.azure.com/'] : []
        )
      }
    }
  }, staticWebAppsIdentityProvider)
}

@description('AAD + SWA-linked Easy Auth settings for the Function App. Applied after linked-backend mutation so the pinned AAD audience is restored without losing the SWA provider.')
resource authsettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: functionApp
  name: 'authsettingsV2'
  properties: authSettingsProperties
}
