targetScope = 'resourceGroup'

@description('Name of the Key Vault in the current resource group.')
param keyVaultName string

@description('Object ID of the App Service managed identity.')
param principalId string

var secretsOfficerRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')

resource keyVault 'Microsoft.KeyVault/vaults@2026-02-01' existing = {
  name: keyVaultName
}

resource assignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, principalId, secretsOfficerRoleDefinitionId)
  scope: keyVault
  properties: {
    roleDefinitionId: secretsOfficerRoleDefinitionId
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

output roleAssignmentId string = assignment.id
