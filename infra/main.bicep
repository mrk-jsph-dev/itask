// Everything iTask runs on, deployed by the ci-cd pipeline into rg-itask
// (after your approval). Every resource is on a free tier or a hard-capped plan.
targetScope = 'resourceGroup'

@description('Region for most resources.')
param location string = resourceGroup().location

@description('Static Web Apps is only offered in a few regions; East Asia is closest to Manila.')
param staticWebAppLocation string = 'eastasia'

@description('Short name used in resource names.')
@maxLength(10)
param appName string = 'itask'

@description('Full API image reference, e.g. ghcr.io/mrk-jsph-dev/itask-api:<git-sha>.')
param apiImage string

@description('Max API replicas. Keeps compute inside the Container Apps free grant.')
@minValue(0)
@maxValue(3)
param apiMaxReplicas int = 2

@description('Name of the API identity created by the bootstrap step.')
param apiIdentityName string = 'id-itask-api'

@description('Optional custom domain for the site (e.g. app.example.com). Empty = default hostname.')
param siteOrigin string = ''

var suffix = uniqueString(resourceGroup().id)
var tags = { app: appName }

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: apiIdentityName
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: { location: location, name: '${appName}-${suffix}', tags: tags }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    serverName: 'sql-${appName}-${suffix}'
    databaseName: appName
    adminIdentityName: apiIdentity.name
    adminIdentityClientId: apiIdentity.properties.clientId
    tags: tags
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: { location: location, name: take('st${appName}${suffix}', 24), tags: tags }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: { location: location, name: 'kv-${appName}-${suffix}', tags: tags }
}

module site 'modules/staticwebapp.bicep' = {
  name: 'site'
  params: { location: staticWebAppLocation, name: 'swa-${appName}-${suffix}', tags: tags }
}

var allowedOrigin = empty(siteOrigin) ? 'https://${site.outputs.defaultHostname}' : siteOrigin

module api 'modules/containerapp.bicep' = {
  name: 'api'
  params: {
    location: location
    environmentName: 'cae-${appName}-${suffix}'
    appName: 'ca-${appName}-api'
    image: apiImage
    maxReplicas: apiMaxReplicas
    identityId: apiIdentity.id
    identityClientId: apiIdentity.properties.clientId
    logAnalyticsCustomerId: monitoring.outputs.customerId
    logAnalyticsName: monitoring.outputs.workspaceName
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    sqlConnectionString: sql.outputs.connectionString
    allowedOrigin: allowedOrigin
    tags: tags
  }
}

output apiUrl string = api.outputs.url
output siteName string = site.outputs.name
output siteUrl string = allowedOrigin
output storageAccount string = storage.outputs.name
output keyVaultName string = keyVault.outputs.name
