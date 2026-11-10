targetScope = 'resourceGroup'

@description('Azure region for the shared container registry.')
param location string = resourceGroup().location

@description('Globally unique, lower-case registry name.')
param registryName string = 'extatic${uniqueString(subscription().id)}'

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

output registryName string = registry.name
output loginServer string = registry.properties.loginServer
