// One-time, subscription-scope setup. Requires Owner (or User Access Administrator):
//
//   az login
//   az deployment sub create --location southeastasia \
//     --template-file infra/bootstrap/bootstrap.bicep \
//     --parameters infra/bootstrap/bootstrap.bicepparam
//
// Creates the resource group, identities, GitHub OIDC federated credential, role
// assignments and budget. The deploy identity receives Contributor on the resource
// group only, so CI cannot create role assignments.
targetScope = 'subscription'

@description('Azure region for the resource group and identities.')
param location string = 'southeastasia'

@description('Resource group that holds every iTask resource.')
param resourceGroupName string = 'rg-itask'

@description('GitHub repository allowed to deploy, as owner/name.')
param githubRepository string = 'mrk-jsph-dev/itask'

@description('GitHub environment whose jobs may sign in to Azure.')
param githubEnvironment string = 'production'

@description('Email address for budget alerts.')
param alertEmail string

@description('Monthly subscription budget in your billing currency.')
param monthlyBudget int = 5

@description('First day of the budget period, yyyy-MM-01. Must be this month or later.')
param budgetStartDate string = '${utcNow('yyyy-MM')}-01'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: { app: 'itask' }
}

module identities 'identities.bicep' = {
  scope: rg
  name: 'itask-identities'
  params: {
    location: location
    githubRepository: githubRepository
    githubEnvironment: githubEnvironment
  }
}

resource budget 'Microsoft.Consumption/budgets@2024-08-01' = {
  name: 'itask-monthly'
  properties: {
    category: 'Cost'
    amount: monthlyBudget
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStartDate }
    notifications: {
      actual20: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 20
        contactEmails: [alertEmail]
        thresholdType: 'Actual'
      }
      actual100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        contactEmails: [alertEmail]
        thresholdType: 'Actual'
      }
      forecast100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        contactEmails: [alertEmail]
        thresholdType: 'Forecasted'
      }
    }
  }
}

output resourceGroup string = rg.name
output deployClientId string = identities.outputs.deployClientId
output apiIdentityName string = identities.outputs.apiIdentityName
