// Attachments. No account keys and no public blobs: the API hands out short-lived
// user-delegation SAS links signed with its managed identity.
param location string
@minLength(3)
@maxLength(24)
param name string
param tags object

resource account 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: name
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    defaultToOAuthAuthentication: true
  }
}

resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: account
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 7 }
  }
}

resource attachments 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobs
  name: 'attachments'
  properties: { publicAccess: 'None' }
}

output name string = account.name
