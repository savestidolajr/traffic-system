// ============================================================================
// Module:  roles-key-vault.bicep
// Purpose: RBAC role assignments for the Function App managed identities on
//          the Key Vault. Deployed at the scope of the Key Vault RG
//          (IBP-AUE-{ENV}-RSG-KYV01) — the Function App lives in APP01.
//          Takes a list of principals so the staging slot's identity can
//          resolve the same Key Vault references as production.
// ============================================================================

@description('Principal IDs of the Function App managed identities (production slot + staging slot).')
param principalIds string[]

@description('Name of the Key Vault to assign roles on.')
param keyVaultName string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Key Vault Secrets User – read secrets from Key Vault
resource keyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in principalIds: {
    name: guid(keyVault.id, principalId, '4633458b-17de-408a-b874-0445c86b69e6')
    scope: keyVault
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
      principalId: principalId
      principalType: 'ServicePrincipal'
    }
  }
]
