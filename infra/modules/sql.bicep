// Azure SQL Database free offer (useFreeLimit). freeLimitExhaustionBehavior AutoPause
// pauses the database until next month instead of billing past the free allowance.
param location string
param serverName string
param databaseName string
param adminIdentityName string
param adminIdentityClientId string
param tags object

resource server 'Microsoft.Sql/servers@2023-08-01' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      // Entra-only authentication: SQL logins are disabled.
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: adminIdentityName
      sid: adminIdentityClientId
      principalType: 'Application'
      tenantId: tenant().tenantId
    }
  }
}

// Container Apps (consumption) has no fixed outbound IP, so allow Azure services.
// Authentication remains Entra-only.
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: server
  name: 'AllowAllWindowsAzureIps'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: { name: 'GP_S_Gen5', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: 'Local'
  }
}

output connectionString string = 'Server=tcp:${server.properties.fullyQualifiedDomainName},1433;Database=${database.name};Authentication=Active Directory Managed Identity;User Id=${adminIdentityClientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'
