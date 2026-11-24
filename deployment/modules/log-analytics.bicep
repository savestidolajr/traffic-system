// ============================================================================
// Module:  log-analytics.bicep
// Purpose: Log Analytics workspace, deployed ONLY when logging stays in the
//          IBP subscription.
//
// OPEN ITEM (11 Jun 2026): ARN's naming CSV places the workspace in their
// management subscription (MGT-AUE-{ENV}-LAW-APP01 in MGT-AUE-{ENV}-RSG-LAW01,
// central logging), but the RG list still carries an IBP-side LAW RG.
// Question with the ARN technology lead. If central logging is confirmed, this module is
// never deployed: supply `logAnalyticsWorkspaceResourceId` in the bicepparam
// instead. See docs/reference/azure-naming-convention.md.
// ============================================================================

@description('Azure region for the workspace.')
param location string

@description('Name of the Log Analytics workspace.')
param logAnalyticsName string

@description('Log Analytics workspace retention in days.')
@minValue(30)
@maxValue(730)
param retentionInDays int = 30

@description('Tags to apply.')
param tags object = {}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
  }
}

@description('Resource ID of the Log Analytics workspace.')
output workspaceId string = logAnalytics.id
