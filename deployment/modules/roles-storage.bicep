// ============================================================================
// Module:  roles-storage.bicep
// Purpose: RBAC role assignments for the Function App managed identities on
//          the storage account. Deployed at the scope of the storage RG
//          (IBP-AUE-{ENV}-RSG-STA01) — the Function App lives in APP01.
//          Takes a list of principals so the staging slot's identity gets the
//          same data-plane access as production (the slot host needs storage
//          for identity-based AzureWebJobsStorage).
// ============================================================================

@description('Principal IDs of the Function App managed identities (production slot + staging slot).')
param principalIds string[]

@description('Name of the storage account to assign roles on.')
param storageAccountName string

@description('Object ID of the AAD group granted read access to the audit evidence (blobs + tables). Empty skips the assignments. Per CC-002 (9 Jun 2026 legal review): ARN legal stakeholders.')
param auditAccessGroupObjectId string = ''

resource storageAccount 'Microsoft.Storage/storageAccounts@2024-01-01' existing = {
  name: storageAccountName
}

// Storage Blob Data Owner        – read/write/delete blobs INCLUDING blob index
//   tags. Tags are required: AzureBlobBroadcastOutput uploads with a `published`
//   index tag (CC-002 retention lifecycle). Contributor lacks blobs/tags/write,
//   so MI-only (shared-key-disabled) storage 403s the tagged upload with Owner absent.
// Storage Table Data Contributor – read/write Azure Table Storage tables
// Storage Blob Delegator         – generate user-delegation SAS tokens
// Storage Queue Data Contributor – read/write queue messages
var roleDefinitionIds = [
  'b7e6dc6d-f1e8-4753-8033-0f276bb0955b' // Storage Blob Data Owner
  '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3' // Storage Table Data Contributor
  'db58b8e5-c6ad-4a2a-8342-4190687cbf4a' // Storage Blob Delegator
  '974c5e8b-45b9-4653-ba55-5f855dd0fb88' // Storage Queue Data Contributor
]

var assignments = flatten(map(
  principalIds,
  principalId => map(roleDefinitionIds, roleId => { principalId: principalId, roleId: roleId })
))

resource storageRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for pair in assignments: {
    name: guid(storageAccount.id, pair.principalId, pair.roleId)
    scope: storageAccount
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', pair.roleId)
      principalId: pair.principalId
      principalType: 'ServicePrincipal'
    }
  }
]

// Audit-evidence readers (CC-002): the ARN audit-access AAD group gets
// read-only data-plane access to bulletin audio (blobs) and job/approval
// evidence (tables). No write, no delete — evidence review only.
var auditReaderRoleIds = [
  '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1' // Storage Blob Data Reader
  '76199698-9eea-4c19-bc75-cec21354c6b6' // Storage Table Data Reader
]

resource auditGroupRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for roleId in auditReaderRoleIds: if (!empty(auditAccessGroupObjectId)) {
    name: guid(storageAccount.id, auditAccessGroupObjectId, roleId)
    scope: storageAccount
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId)
      principalId: auditAccessGroupObjectId
      principalType: 'Group'
    }
  }
]
