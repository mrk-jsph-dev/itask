// API on Container Apps (consumption profile), scaling between 0 and maxReplicas.
param location string
param environmentName string
param appName string
param image string
param maxReplicas int
param identityId string
param identityClientId string
param logAnalyticsCustomerId string
param logAnalyticsName string
@secure()
param appInsightsConnectionString string
param sqlConnectionString string
param allowedOrigin string

@description('Blob service endpoint for attachments.')
param blobEndpoint string
param blobContainer string
param demoEnabled bool
param crossSiteCookies bool
@secure()
param demoPassword string
@secure()
param turnstileSecretKey string
@secure()
param platformAdminEmails string
param tags object

// App secrets are Container Apps secrets (free), passed in on every deploy. Empty ones are left out.
var optionalSecrets = filter([
  { name: 'demo-password', value: demoPassword, env: 'Demo__Password' }
  { name: 'turnstile-secret', value: turnstileSecretKey, env: 'Turnstile__SecretKey' }
  { name: 'admin-emails', value: platformAdminEmails, env: 'Admin__Emails' }
], s => !empty(s.value))

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' existing = {
  name: logAnalyticsName
}

resource environment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: environmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]
  }
}

resource app 'Microsoft.App/containerApps@2025-01-01' = {
  name: appName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        corsPolicy: {
          allowedOrigins: [allowedOrigin]
          allowCredentials: true
          allowedMethods: ['GET', 'POST', 'PUT', 'PATCH', 'DELETE']
          allowedHeaders: ['*']
        }
      }
      secrets: concat(
        [{ name: 'appinsights-connection', value: appInsightsConnectionString }],
        map(optionalSecrets, s => { name: s.name, value: s.value })
      )
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat([
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'AZURE_CLIENT_ID', value: identityClientId }
            { name: 'Database__Provider', value: 'SqlServer' }
            { name: 'Database__AutoMigrate', value: 'true' }
            { name: 'ConnectionStrings__Default', value: sqlConnectionString }
            { name: 'Cors__AllowedOrigins__0', value: allowedOrigin }
            { name: 'Storage__Provider', value: 'Azure' }
            { name: 'Storage__BlobEndpoint', value: blobEndpoint }
            { name: 'Storage__Container', value: blobContainer }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights-connection' }
            { name: 'Demo__Enabled', value: string(demoEnabled) }
            { name: 'Auth__CookieSameSite', value: crossSiteCookies ? 'None' : 'Lax' }
            // No email sender yet, so sign-up can't wait for a confirmation link.
            { name: 'Auth__RequireConfirmedEmail', value: 'false' }
          ], map(optionalSecrets, s => { name: s.env, secretRef: s.name }))
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/api/health', port: 8080 }
              periodSeconds: 30
            }
            {
              // Generous threshold: a paused free-tier database takes about a minute to resume.
              type: 'Readiness'
              httpGet: { path: '/api/health/ready', port: 8080 }
              periodSeconds: 30
              timeoutSeconds: 10
              failureThreshold: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: maxReplicas
        rules: [{ name: 'http', http: { metadata: { concurrentRequests: '50' } } }]
      }
    }
  }
}

output url string = 'https://${app.properties.configuration.ingress.fqdn}'
