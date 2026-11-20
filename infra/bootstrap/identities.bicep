// Deploy and API identities with role assignments at resource-group scope.
param location string
param githubRepository string
param githubOwnerId string
param githubRepositoryId string
param githubEnvironment string

// Built-in role definition ids.
var roles = {
  contributor: 'b24988ac-6180-42a0-ab88-20f7382dd24c'
  storageBlobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  cognitiveServicesOpenAiUser: '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
}

// GitHub Actions deploy identity (federated credential, no client secret).
resource deployIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-itask-github-deploy'
  location: location
}

resource githubTrust 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: deployIdentity
  name: 'github-${githubEnvironment}'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    // Matches only jobs that run in the given GitHub environment of this repository. GitHub's immutable
    // subject format names the owner and repository with their numeric ids, so a renamed or re-created
    // repository with the same name can't use this trust.
    subject: 'repo:${split(githubRepository, '/')[0]}@${githubOwnerId}/${split(githubRepository, '/')[1]}@${githubRepositoryId}:environment:${githubEnvironment}'
    audiences: ['api://AzureADTokenExchange']
  }
}

// Runtime identity of the API container (SQL, Blob Storage, Microsoft Foundry).
resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-itask-api'
  location: location
}

resource deployContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deployIdentity.id, roles.contributor)
  properties: {
    principalId: deployIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.contributor)
  }
}

resource apiRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for role in [roles.storageBlobDataContributor, roles.cognitiveServicesOpenAiUser]: {
    name: guid(resourceGroup().id, apiIdentity.id, role)
    properties: {
      principalId: apiIdentity.properties.principalId
      principalType: 'ServicePrincipal'
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
    }
  }
]

output deployClientId string = deployIdentity.properties.clientId
output apiIdentityName string = apiIdentity.name
