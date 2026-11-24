// ============================================================================
// Module:  monitoring.bicep
// Purpose: Application Insights (workspace-based) for observability.
//          The backing Log Analytics workspace is supplied by resource ID:
//          either the IBP-side workspace from log-analytics.bicep or ARN's
//          central MGT-subscription workspace (cross-subscription linkage is
//          supported). See docs/reference/azure-naming-convention.md.
// ============================================================================

@description('Azure region for the Application Insights component.')
param location string

@description('Name of the Application Insights resource.')
param appInsightsName string

@description('Resource ID of the Log Analytics workspace backing this component. May live in another resource group or subscription (ARN central logging).')
param logAnalyticsWorkspaceId string

@description('Tags to apply.')
param tags object = {}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspaceId
  }
}

@description('Application Insights connection string.')
output appInsightsConnectionString string = appInsights.properties.ConnectionString

@description('Resource ID of the Application Insights component (used by workbooks).')
output appInsightsResourceId string = appInsights.id
