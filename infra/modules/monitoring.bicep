param location string
param name string
param tags object

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: 'log-${name}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    // Hard cap on ingestion: stays far inside the free 5 GB a month.
    workspaceCapping: { dailyQuotaGb: json('0.1') }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${name}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    SamplingPercentage: 25
    DisableLocalAuth: true
  }
}

output customerId string = workspace.properties.customerId
output workspaceName string = workspace.name
output appInsightsConnectionString string = appInsights.properties.ConnectionString
