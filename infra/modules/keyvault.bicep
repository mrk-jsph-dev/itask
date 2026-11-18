// Application secrets. RBAC authorization only (no access policies).
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
