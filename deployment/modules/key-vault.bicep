// ============================================================================
// Key Vault Module
// ============================================================================
// Creates a Key Vault with RBAC authorization (enableRbacAuthorization=true).
// Secrets are added manually after deployment.
// ============================================================================

@description('Azure region for the Key Vault.')
param location string

@description('Name of the Key Vault. Must be 3-24 characters.')
@minLength(3)
@maxLength(24)
param keyVaultName string

@description('Tags to apply to all resources.')
param tags object = {}

@description('Webhook endpoint URL for Teams notifications. Stored as a Key Vault secret.')
param webhookEndpointUrl string = ''

@description('Resource ID of the Log Analytics workspace for diagnostic settings. Leave empty to skip.')
param logAnalyticsWorkspaceId string = ''

@description('Public network access. Set to Disabled for prod when a private endpoint is provisioned.')
@allowed(['Enabled', 'Disabled'])
param publicNetworkAccess string = 'Enabled'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenant().tenantId
    enableRbacAuthorization: true
    enabledForTemplateDeployment: true
    enableSoftDelete: true
    enablePurgeProtection: true
    softDeleteRetentionInDays: 90
    accessPolicies: []
    publicNetworkAccess: publicNetworkAccess
    networkAcls: {
      defaultAction: publicNetworkAccess == 'Disabled' ? 'Deny' : 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// Named with the `--` convention every other secret uses, because the Function App reads
// the vault at runtime via AddAzureKeyVault, which maps `--` to the config separator `:`
// (Webhook--EndpointUrl => Webhook:EndpointUrl, binding WebhookOptions.EndpointUrl). The
// old flat `webhookEndpointUrl` name could only ever be consumed through an explicit
// @Microsoft.KeyVault app-setting reference, and those are gone — they resolved over the
// site's public egress and 403'd against this vault's disabled public network access.
@description('Secret containing the webhook endpoint URL for Teams notifications.')
resource webhookSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(webhookEndpointUrl)) {
  parent: keyVault
  name: 'Webhook--EndpointUrl'
  properties: {
    value: webhookEndpointUrl
  }
}

@description('Streams Key Vault audit events and metrics to Log Analytics.')
resource keyVaultDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (!empty(logAnalyticsWorkspaceId)) {
  name: 'keyVaultDiagnostics'
  scope: keyVault
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'AuditEvent'
        enabled: true
      }
      {
        category: 'AzurePolicyEvaluationDetails'
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

@description('Name of the Key Vault.')
output name string = keyVault.name

@description('URI of the Key Vault.')
output uri string = keyVault.properties.vaultUri

@description('Resource ID of the Key Vault.')
output id string = keyVault.id
