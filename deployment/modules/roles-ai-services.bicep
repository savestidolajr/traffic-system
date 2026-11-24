// ============================================================================
// Module:  roles-ai-services.bicep
// Purpose: RBAC role assignments for the Function App managed identities on
//          the AI Services account. Deployed at the scope of the application
//          RG (IBP-AUE-{ENV}-RSG-APP01) where the account lives.
//          Takes a list of principals so the staging slot's identity can call
//          Azure OpenAI the same way production does.
// ============================================================================

@description('Principal IDs of the Function App managed identities (production slot + staging slot).')
param principalIds string[]

@description('Name of the AI Services account to assign roles on.')
param aiServicesName string

@description('Principal ID of the Foundry PROJECT system-assigned identity. Distinct from the account identity. Empty skips the assignment.')
param foundryProjectPrincipalId string = ''

@description('Name of the Application Insights component the Foundry project publishes agent traces to. Empty skips the assignment.')
param appInsightsName string = ''

resource aiServices 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: aiServicesName
}

// Cognitive Services User – access Azure AI Services endpoints
resource cognitiveServicesUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in principalIds: {
    name: guid(aiServices.id, principalId, 'a97b65f3-24c7-4388-baec-2e87135dc908')
    scope: aiServices
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a97b65f3-24c7-4388-baec-2e87135dc908')
      principalId: principalId
      principalType: 'ServicePrincipal'
    }
  }
]


// ============================================================================
// Foundry project -> Application Insights: agent trace publishing
// ============================================================================

// Without this, the Foundry portal's Tracing tab is silently empty.
//
// Measured on dev 2026-08-31. The project's App Insights connection was moved from the
// portal-created `ApiKey` form to a declared `ProjectManagedIdentity` one. Every bulletin before
// that change emitted an `invoke_agent` span per agent call; the seven bulletins after it emitted
// none, because ProjectManagedIdentity authenticates as the PROJECT identity and that identity had
// no role on the component. Granting Monitoring Metrics Publisher restored the spans on the very
// next bulletin.
//
// Note the identity: this is the project's own system-assigned principal, NOT the AI Services
// account's. They are different objects and assigning the account's fixes nothing.
//
// Monitoring Metrics Publisher is the minimum. Monitoring Contributor was also granted while
// diagnosing; it was then removed and tracing continued unaffected, so it is deliberately not
// reproduced here.
resource appInsights 'Microsoft.Insights/components@2020-02-02' existing =
  if (!empty(appInsightsName)) {
    name: appInsightsName
  }

resource foundryProjectMetricsPublisher 'Microsoft.Authorization/roleAssignments@2022-04-01' =
  if (!empty(foundryProjectPrincipalId) && !empty(appInsightsName)) {
    name: guid(appInsights.id, foundryProjectPrincipalId, '3913510d-42f4-4e42-8a64-420c390055eb')
    scope: appInsights
    properties: {
      roleDefinitionId: subscriptionResourceId(
        'Microsoft.Authorization/roleDefinitions',
        '3913510d-42f4-4e42-8a64-420c390055eb'
      )
      principalId: foundryProjectPrincipalId
      principalType: 'ServicePrincipal'
    }
  }
