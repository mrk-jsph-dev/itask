// Budget kill switch: the budget's action group calls a Logic App (Consumption, billed per run,
// so $0 while idle) that stops the API Container App through ARM with its own identity.
// Restart afterwards with: az containerapp start -g rg-itask -n ca-itask-api
param location string
param apiAppName string

@description('Custom role allowing only read and stop on the API.')
param stopperRoleId string

var stopUrl = '${environment().resourceManager}subscriptions/${subscription().subscriptionId}/resourceGroups/${resourceGroup().name}/providers/Microsoft.App/containerApps/${apiAppName}/stop?api-version=2025-01-01'

resource workflow 'Microsoft.Logic/workflows@2019-05-01' = {
  name: 'logic-itask-killswitch'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    state: 'Enabled'
    definition: {
      '$schema': 'https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#'
      contentVersion: '1.0.0.0'
      triggers: {
        manual: { type: 'Request', kind: 'Http', inputs: { schema: {} } }
      }
      actions: {
        stop_api: {
          type: 'Http'
          runAfter: {}
          inputs: {
            method: 'POST'
            uri: stopUrl
            authentication: { type: 'ManagedServiceIdentity', audience: environment().resourceManager }
          }
        }
      }
    }
  }
}

resource stopperAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, workflow.id, stopperRoleId)
  properties: {
    principalId: workflow.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: stopperRoleId
  }
}

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-itask-budget'
  location: 'global'
  properties: {
    groupShortName: 'itaskbudget'
    enabled: true
    logicAppReceivers: [
      {
        name: 'stop-api'
        resourceId: workflow.id
        callbackUrl: listCallbackUrl('${workflow.id}/triggers/manual', '2019-05-01').value
        useCommonAlertSchema: true
      }
    ]
  }
}

output actionGroupId string = actionGroup.id
