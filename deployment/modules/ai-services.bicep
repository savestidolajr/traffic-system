// ============================================================================
// AI Services Module - Azure AI Services and Search
// ============================================================================
// Creates Azure AI Services account and AI Search service with model deployment.
// ============================================================================

targetScope = 'resourceGroup'

// ============================================================================
// Parameters
// ============================================================================

@description('Azure region for AI Services account.')
param location string

@description('Azure region for AI Search service.')
param searchLocation string

@description('Name of the Azure AI Services account.')
param aiServicesName string

@description('Name of the Azure AI Search service.')
param searchServiceName string

@description('SKU for the AI Search service.')
@allowed(['free', 'basic', 'standard'])
param searchSku string = 'free'

@description('Shared Azure OpenAI model deployment: the model every bulletin agent runs on. Deployment name equals the model name, which is what AiSettings:AzureAI:Model resolves against.')
param aiModelName string

@description('Version of the shared model deployment.')
param aiModelVersion string

@description('Tokens-per-minute capacity for the shared model deployment.')
param aiModelCapacity int = 1000

@description('Judge model deployment for the .foundry eval suite. Every evals/*/eval.yaml pins judge_model, and it MUST be independent of the generator so a model does not grade its own output. Empty skips it.')
param judgeModelName string = ''

@description('Version of the judge model deployment.')
param judgeModelVersion string = ''

@description('Tokens-per-minute capacity for the judge model deployment.')
param judgeModelCapacity int = 1000

@description('Tags to apply to all resources.')
param tags object = {}

@description('Enable public network access to AI Services and Search. Set to Disabled for production with private endpoints.')
@allowed(['Enabled', 'Disabled'])
param publicNetworkAccess string = 'Enabled'

@description('Resource ID of the Log Analytics workspace for diagnostic settings. Leave empty to skip.')
param logAnalyticsWorkspaceId string = ''

@description('Deploy Azure AI Search. StyleExamplesService consumes it for RAG-based style examples; set false for environments where the feature flag AzureSearch:Enabled is off.')
param deploySearch bool = false

@description('Name of the Foundry project (sub-resource of the AI Services account). Enables the new Microsoft Foundry portal experience — see docs/reference for the allowProjectManagement investigation.')
param projectName string = 'bulletin-generator'

@description('Resource ID of the Application Insights component connected to the Foundry project. The Tracing tab in the Foundry portal reads gen_ai telemetry from this connected component; without it, the Tracing tab is empty. deployment/main.bicep must pass this value when tracing is enabled.')
param appInsightsResourceId string = ''

@description('Connection string of the Application Insights component connected to the Foundry project. Required as connection metadata by the resource provider even when authType is ProjectManagedIdentity. Sourced from the monitoring module output; empty disables the connection alongside appInsightsResourceId.')
@secure()
param appInsightsConnectionString string = ''

// ============================================================================
// 1. Azure AI Search (optional)
// ============================================================================
// StyleExamplesService reads from this Search service when AzureSearch:Enabled=true.
// Default off so non-prod environments aren't billed for an unused resource.

resource searchService 'Microsoft.Search/searchServices@2024-06-01-preview' = if (deploySearch) {
  name: searchServiceName
  location: searchLocation
  tags: tags
  sku: {
    name: searchSku
  }
  properties: {
    hostingMode: 'default'
    publicNetworkAccess: toLower(publicNetworkAccess)
    partitionCount: 1
    replicaCount: 1
    semanticSearch: searchSku == 'free' ? 'disabled' : 'free'
  }
}

// ============================================================================
// 2. Azure AI Services (Foundry resource)
// ============================================================================

resource aiServices 'Microsoft.CognitiveServices/accounts@2026-05-01' = {
  name: aiServicesName
  location: location
  tags: tags
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    // Custom subdomain is a DNS label — must be lowercase even though the
    // resource name follows ARN's uppercase IBP convention.
    customSubDomainName: toLower(aiServicesName)
    publicNetworkAccess: publicNetworkAccess
    // Without this, the account never gets a `projects` sub-resource and the
    // Foundry portal falls back to the classic (hub-era) experience — the new
    // portal then shows "no projects" and prompts you to create one.
    allowProjectManagement: true
  }
}

// ============================================================================
// 2a. Foundry project
// ============================================================================
// Subresource that makes the account addressable in the new Foundry portal
// (ai.azure.com) and via project-scoped endpoints
// (https://{account}.ai.azure.com/api/projects/{projectName}). Not consumed
// by LlmScriptGenerator today (it calls the account's /openai/v1/responses
// endpoint directly), but required for Agent Service / Evaluations / Tracing
// and for deployment/scripts/provision-foundry-agents.sh's PROJECT_ENDPOINT.

resource project 'Microsoft.CognitiveServices/accounts/projects@2026-05-01' = {
  parent: aiServices
  name: projectName
  location: location
  // Declared explicitly rather than left to the provider's default. The project identity is what
  // a ProjectManagedIdentity connection authenticates as when Foundry publishes agent traces, so
  // projectPrincipalId below has to be a value the template can be relied on to produce.
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    displayName: 'Arn Bulletin Generator'
    description: 'Foundry project for the Arn traffic bulletin script generation pipeline (draft/review/refine agents, evaluations, tracing).'
  }
}

// ============================================================================
// 2b. Foundry project App Insights connection
// ============================================================================

// The -2644 suffix on the portal-created dev connection was portal-generated, so this
// declaration derives a stable connection name from the App Insights resource instead.
//
// ORDER MATTERS: the resource provider allows exactly ONE connection per category. A deploy
// on 2026-08-31 failed with "Multiple connection with same category (AppInsights) created, we
// only allow to have 1 connection for category (AppInsights), existing connection
// (IBP-AUE-DEV-AIN-APP01-2644), new connection (IBP-AUE-DEV-AIN-APP01)". Any portal-created
// connection must therefore be DELETED BEFORE the first deploy of this resource into an
// environment, not cleaned up afterwards.
//
// authType is NOT free choice here. A deploy on 2026-08-31 failed with
// "AuthType for AppInsights Connection can only be ProjectManagedIdentity, ApiKey", so the
// 'AAD' value used elsewhere in this file for data-plane connections is rejected for this
// category. ProjectManagedIdentity is chosen over ApiKey because ApiKey would require the
// App Insights connection string as a credential in the template — a secret this deployment
// does not need to hold. The portal-created dev connection uses ApiKey; this one deliberately
// does not.
resource appInsightsConnection 'Microsoft.CognitiveServices/accounts/projects/connections@2026-05-01' = if (!empty(appInsightsResourceId) && !empty(appInsightsConnectionString)) {
  parent: project
  name: last(split(appInsightsResourceId, '/'))
  properties: {
    category: 'AppInsights'
    target: appInsightsResourceId
    authType: 'ProjectManagedIdentity'
    // Required by the resource provider even under ProjectManagedIdentity: the second deploy
    // attempt failed with "Required metadata property ApplicationInsightsConnectionString is
    // missing". The connection string carries the instrumentation key, so it is passed in from
    // the monitoring module's existing output rather than duplicated or hardcoded here.
    metadata: {
      ApplicationInsightsConnectionString: appInsightsConnectionString
    }
  }
}

// ============================================================================
// 3. Model Deployments
// ============================================================================
// Two deployments: the shared generator every agent runs on, and the eval
// suite's independent judge.
//
// Both were previously created BY HAND in all three environments and existed in
// no IaC at all — the old roleModelDeployments loop only ever made
// {model}-{role} deployments, never the shared model. So a from-scratch
// environment came up with a Function App configured for a model that did not
// exist, and `EvalGate_Test` could not run at all because gpt-5.6-terra was
// only ever hand-made in dev. Found 2026-07-29 when the first test/prod
// promotion attempt reached the eval gate.
//
// Per-role deployments stay deleted: that parameter created a bucket per role
// and emitted a role→deployment map function-app.bicep never read, so no role
// was ever pinned. AzureAIOptions still supports a per-role override
// (DraftModel and siblings) if a real need appears — it needs a deployment and
// an app setting, not standing IaC.
//
// @batchSize(1) equivalent: Cognitive Services rejects concurrent writes to
// sibling deployments under one account (409 RequestConflict), so the judge
// explicitly dependsOn the shared model rather than racing it.

resource sharedModelDeploy 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: aiServices
  name: aiModelName
  sku: {
    name: 'GlobalStandard'
    capacity: aiModelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: aiModelName
      version: aiModelVersion
    }
  }
}

resource judgeModelDeploy 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = if (!empty(judgeModelName)) {
  parent: aiServices
  name: judgeModelName
  dependsOn: [sharedModelDeploy]
  sku: {
    name: 'GlobalStandard'
    capacity: judgeModelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: judgeModelName
      version: judgeModelVersion
    }
  }
}

// ============================================================================
// 4. Diagnostic Settings
// ============================================================================

@description('Streams AI Services audit logs and metrics to Log Analytics.')
resource aiServicesDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (!empty(logAnalyticsWorkspaceId)) {
  name: 'aiServicesDiagnostics'
  scope: aiServices
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'Audit'
        enabled: true
      }
      {
        category: 'RequestResponse'
        enabled: true
      }
      {
        category: 'Trace'
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

// ============================================================================
// Outputs
// ============================================================================

@description('Endpoint URL of the AI Services account.')
output aiServicesEndpoint string = aiServices.properties.endpoint

@description('Name of the AI Services account (short alias).')
output name string = aiServices.name

@description('Resource ID of the AI Services account.')
output id string = aiServices.id

@description('Name of the Foundry project (sub-resource of the AI Services account).')
output projectName string = project.name

@description('Project-scoped Foundry endpoint (PROJECT_ENDPOINT for deployment/scripts/provision-foundry-agents.sh and Agents/Evaluations/Tracing SDKs).')
output projectEndpoint string = '${aiServices.properties.endpoint}api/projects/${project.name}'

@description('Principal ID of the Foundry project system-assigned identity. Distinct from the AI Services account identity; this is the one a ProjectManagedIdentity App Insights connection authenticates as.')
output projectPrincipalId string = project.identity.principalId

