// ============================================================================
// Module:  roles-delivery-drop.bicep
// Purpose: RBAC role assignments for the Function App managed identities on
//          the delivery drop storage account (SFTP-enabled blob container +
//          AD DS-authenticated Azure Files share). The drop account is
//          provisioned by ARN outside the main template (test: ibpauetststaftp01)
//          but lives in the same storage RG (IBP-AUE-{ENV}-RSG-STA01), so this
//          module deploys at that RG's scope against the existing account.
//          Storage Blob Data Contributor covers the BlobDrop transport. For
//          AzureFilesDrop, the app calls the Files REST API over OAuth — this
//          does NOT go through SMB/Kerberos, so the "Storage File Data SMB
//          Share *" roles (used for on-prem AD DS mounts, e.g. Zetta's own
//          access) do not apply. OAuth-over-REST needs the read/writeFile
//          BackupSemantics data actions, only present on "Storage File Data
//          Privileged Reader/Contributor" — see decisions.md, 2026-07-24.
//          Both roles are granted unconditionally whenever a drop account is
//          configured so flipping zettaTransport never needs a fresh role
//          deploy (QT-001 class: config not backed by IaC is a footgun here
//          too — grant once, let the app setting pick the transport).
// ============================================================================

@description('Principal IDs of the Function App managed identities (production slot + staging slot).')
param principalIds string[]

@description('Name of the existing delivery drop storage account.')
param storageAccountName string

resource dropAccount 'Microsoft.Storage/storageAccounts@2024-01-01' existing = {
  name: storageAccountName
}

var blobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe' // Storage Blob Data Contributor
var fileDataPrivilegedContributorRoleId = '69566ab7-960f-475b-8e7c-b3118f30c6bd' // Storage File Data Privileged Contributor (OAuth-over-REST; bypasses NTFS/share ACLs)

resource dropRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in principalIds: {
    name: guid(dropAccount.id, principalId, blobDataContributorRoleId)
    scope: dropAccount
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributorRoleId)
      principalId: principalId
      principalType: 'ServicePrincipal'
    }
  }
]

resource dropFileRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in principalIds: {
    name: guid(dropAccount.id, principalId, fileDataPrivilegedContributorRoleId)
    scope: dropAccount
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', fileDataPrivilegedContributorRoleId)
      principalId: principalId
      principalType: 'ServicePrincipal'
    }
  }
]
