// Holds the few secrets that remain (demo password, GitHub App key, webhook
// secrets). Access is by Azure RBAC only; the API reads with its identity.
param location string
param name string
param tags object

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: take(name, 24)
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

output name string = vault.name
