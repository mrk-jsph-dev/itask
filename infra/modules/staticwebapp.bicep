param location string
param name string
param tags object

resource site 'Microsoft.Web/staticSites@2024-04-01' = {
  name: name
  location: location
  tags: tags
  sku: { name: 'Free', tier: 'Free' }
  properties: {
    // Not linked to a repo; content is uploaded with the deployment token.
    provider: 'None'
    stagingEnvironmentPolicy: 'Disabled'
    allowConfigFileUpdates: true
  }
}

output name string = site.name
output defaultHostname string = site.properties.defaultHostname
