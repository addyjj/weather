targetScope = 'subscription'

@description('Resource group containing the complete free-tier weather stack.')
param resourceGroupName string = 'weather'

@description('Use one region for App Service and SQL to avoid cross-region data transfer.')
param location string = 'westus3'

@description('Resource group metadata location is independent of the hosted resources.')
param resourceGroupLocation string = 'westus2'

@description('Static Web Apps supports a separate set of regions.')
param staticSiteLocation string = 'westus2'

@minLength(3)
@maxLength(12)
@description('Lowercase letters, numbers, and hyphens; globally unique suffixes are added automatically.')
param namePrefix string = 'weather'

@minLength(36)
@maxLength(36)
@description('Object ID of the existing human Entra user who will administer and migrate SQL.')
param sqlAdminObjectId string

@minLength(1)
@description('Login/display name of that existing Entra administrator. No new human user is created.')
param sqlAdminLogin string

@secure()
@minLength(1)
param ambientApiKey string

@secure()
@minLength(1)
param ambientApplicationKey string

@minLength(1)
param ambientDeviceMacAddress string

@description('Optional single public IPv4 address for manual migrations/imports. Empty creates no operator firewall rule.')
param operatorIpAddress string = ''

resource group 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: resourceGroupLocation
  tags: {
    application: 'weather'
    costProfile: 'free-only'
  }
}

module stack 'resources.bicep' = {
  name: 'weather-free-stack'
  scope: group
  params: {
    location: location
    staticSiteLocation: staticSiteLocation
    namePrefix: namePrefix
    sqlAdminObjectId: sqlAdminObjectId
    sqlAdminLogin: sqlAdminLogin
    ambientApiKey: ambientApiKey
    ambientApplicationKey: ambientApplicationKey
    ambientDeviceMacAddress: ambientDeviceMacAddress
    operatorIpAddress: operatorIpAddress
  }
}

output resourceGroupName string = group.name
output resources object = stack.outputs.resources
output urls object = stack.outputs.urls
output database object = stack.outputs.database
output identities object = stack.outputs.identities
