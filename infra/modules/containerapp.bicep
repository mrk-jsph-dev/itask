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
param tags object

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
      secrets: [
        { name: 'appinsights-connection', value: appInsightsConnectionString }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'AZURE_CLIENT_ID', value: identityClientId }
            { name: 'Database__Provider', value: 'SqlServer' }
            { name: 'Database__AutoMigrate', value: 'true' }
            { name: 'ConnectionStrings__Default', value: sqlConnectionString }
            { name: 'Cors__AllowedOrigins__0', value: allowedOrigin }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights-connection' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/api/health', port: 8080 }
              periodSeconds: 30
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
