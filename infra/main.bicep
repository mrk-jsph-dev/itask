// Application resources, deployed by ci-cd.yml at resource-group scope.
targetScope = 'resourceGroup'

@description('Region for most resources.')
param location string = resourceGroup().location

@description('Static Web Apps region (the service is available in a limited set of regions).')
param staticWebAppLocation string = 'eastasia'

@description('Short name used in resource names.')
@maxLength(10)
param appName string = 'itask'

@description('Full API image reference, e.g. ghcr.io/mrk-jsph-dev/itask-api:<git-sha>.')
param apiImage string

@description('Maximum API replicas.')
@minValue(0)
@maxValue(3)
param apiMaxReplicas int = 2

@description('Name of the API identity created by the bootstrap step.')
param apiIdentityName string = 'id-itask-api'

@description('Optional custom domain for the site (e.g. app.example.com). Empty = default hostname.')
param siteOrigin string = ''

@description('Optional Entra group (object id) holding the API identity and you, used as SQL admin for manual queries.')
param sqlAdminGroupId string = ''

@description('Seed the demo accounts and reset them nightly.')
param demoEnabled bool = true

@description('True while the site and API are on different sites (no shared custom domain): the auth cookie becomes SameSite=None; Partitioned.')
param crossSiteCookies bool = true

@description('Password of the demo accounts. Empty = demo sign-in stays off.')
@secure()
param demoPassword string = ''

@description('Cloudflare Turnstile secret key. Empty = Turnstile checks are skipped.')
@secure()
param turnstileSecretKey string = ''

@description('Emails (; separated) granted Platform admin at startup and registration. Secure so it stays out of logs.')
@secure()
param platformAdminEmails string = ''

@description('Gmail address that sends iTask email (SMTP with an app password). Empty = emails are only logged.')
@secure()
param smtpUsername string = ''

@description('Gmail app password for smtpUsername.')
@secure()
param smtpPassword string = ''

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
    adminGroupId: sqlAdminGroupId
    tags: tags
  }
}

module site 'modules/staticwebapp.bicep' = {
  name: 'site'
  params: { location: staticWebAppLocation, name: 'swa-${appName}-${suffix}', tags: tags }
}

var allowedOrigin = empty(siteOrigin) ? 'https://${site.outputs.defaultHostname}' : siteOrigin

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    name: take('st${appName}${suffix}', 24)
    allowedOrigin: allowedOrigin
    tags: tags
  }
}

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
    blobEndpoint: storage.outputs.blobEndpoint
    blobContainer: storage.outputs.container
    demoEnabled: demoEnabled
    crossSiteCookies: crossSiteCookies
    demoPassword: demoPassword
    turnstileSecretKey: turnstileSecretKey
    platformAdminEmails: platformAdminEmails
    smtpUsername: smtpUsername
    smtpPassword: smtpPassword
    tags: tags
  }
}

output apiUrl string = api.outputs.url
output siteName string = site.outputs.name
output siteUrl string = allowedOrigin
output storageAccount string = storage.outputs.name
