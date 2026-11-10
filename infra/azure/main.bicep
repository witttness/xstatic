targetScope = 'resourceGroup'

@allowed(['test', 'prod'])
param stage string

param location string = resourceGroup().location

@description('Name of the shared Azure Container Registry.')
param registryName string

@description('Resource group containing the shared registry.')
param registryResourceGroup string

@description('Immutable image tag built in the shared registry, such as a Git commit SHA.')
param imageTag string

param oidcIssuerUrl string
param oidcClientId string
param oauthAllowedEmailDomains string

@secure()
param oidcClientSecret string

@secure()
param oauthCookieSecret string

@secure()
param postgresAdminPassword string

@secure()
param jwtKey string

param postgresAdminLogin string = 'extaticadmin'

var prefix = 'extatic-${stage}-${uniqueString(resourceGroup().id)}'
var appName = '${prefix}-app'
var storageName = take('extatic${stage}${uniqueString(resourceGroup().id)}', 24)
var networkOctet = stage == 'prod' ? '21' : '20'
var postgresName = '${prefix}-pg'

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
  scope: resourceGroup(registryResourceGroup)
}

resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-pull'
  location: location
}

module acrPull './acr-pull.bicep' = {
  name: '${prefix}-acr-pull'
  scope: resourceGroup(registryResourceGroup)
  params: {
    registryName: registryName
    principalId: pullIdentity.properties.principalId
  }
}

resource network 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${prefix}-vnet'
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.${networkOctet}.0.0/16'
      ]
    }
  }
}

resource appsSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' = {
  parent: network
  name: 'container-apps'
  properties: {
    addressPrefix: '10.${networkOctet}.0.0/27'
    delegations: [
      {
        name: 'container-apps'
        properties: {
          serviceName: 'Microsoft.App/environments'
        }
      }
    ]
  }
}

resource postgresSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' = {
  parent: network
  name: 'postgres'
  properties: {
    addressPrefix: '10.${networkOctet}.1.0/28'
    delegations: [
      {
        name: 'postgres'
        properties: {
          serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers'
        }
      }
    ]
  }
}

resource postgresDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: '${prefix}.postgres.database.azure.com'
  location: 'global'
}

resource postgresDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: postgresDns
  name: 'container-apps'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: network.id
    }
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2025-08-01' = {
  name: postgresName
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    version: '18'
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: stage == 'prod' ? 14 : 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      delegatedSubnetResourceId: postgresSubnet.id
      privateDnsZoneArmResourceId: postgresDns.id
      publicNetworkAccess: 'Disabled'
    }
  }
  dependsOn: [
    postgresDnsLink
  ]
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2025-08-01' = {
  parent: postgres
  name: 'extatic'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Enabled'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource attachments 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'extatic-attachments'
  properties: {
    publicAccess: 'Blob'
  }
}

resource logs 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${prefix}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: 1
    }
  }
}

resource appEnvironment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    vnetConfiguration: {
      infrastructureSubnetId: appsSubnet.id
      internal: false
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

var appUrl = 'https://${appName}.${appEnvironment.properties.defaultDomain}'
var databaseConnection = 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${database.name};Username=${postgresAdminLogin};Password=${postgresAdminPassword};SSL Mode=Require'
var storageConnection = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=core.windows.net'

resource app 'Microsoft.App/containerApps@2025-01-01' = {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${pullIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: appEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: registry.properties.loginServer
          identity: pullIdentity.id
        }
      ]
      secrets: [
        { name: 'db-connection', value: databaseConnection }
        { name: 'storage-connection', value: storageConnection }
        { name: 'jwt-key', value: jwtKey }
        { name: 'oidc-client-secret', value: oidcClientSecret }
        { name: 'oauth-cookie-secret', value: oauthCookieSecret }
      ]
      ingress: {
        external: true
        targetPort: 4180
        transport: 'http'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${registry.properties.loginServer}/extatic-api:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          probes: [
            {
              type: 'Readiness'
              tcpSocket: { port: 8080 }
              initialDelaySeconds: 5
              periodSeconds: 5
              failureThreshold: 12
            }
          ]
          env: [
            { name: 'ASPNETCORE_URLS', value: 'http://0.0.0.0:8080' }
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'Database__AutoMigrate', value: 'true' }
            { name: 'ConnectionStrings__DefaultConnection', secretRef: 'db-connection' }
            { name: 'Storage__ConnectionString', secretRef: 'storage-connection' }
            { name: 'Storage__ContainerName', value: attachments.name }
            { name: 'Jwt__Key', secretRef: 'jwt-key' }
            { name: 'Jwt__Issuer', value: 'extatic-${stage}' }
            { name: 'Jwt__Audience', value: 'extatic-client' }
          ]
        }
        {
          name: 'dashboard'
          image: '${registry.properties.loginServer}/extatic-dashboard:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          probes: [
            {
              type: 'Readiness'
              tcpSocket: { port: 8081 }
              initialDelaySeconds: 2
              periodSeconds: 5
              failureThreshold: 12
            }
          ]
        }
        {
          name: 'oauth2-proxy'
          image: 'quay.io/oauth2-proxy/oauth2-proxy:v7.15.5'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          probes: [
            {
              type: 'Readiness'
              tcpSocket: { port: 4180 }
              initialDelaySeconds: 2
              periodSeconds: 5
              failureThreshold: 12
            }
          ]
          env: [
            { name: 'OAUTH2_PROXY_PROVIDER', value: 'oidc' }
            { name: 'OAUTH2_PROXY_OIDC_ISSUER_URL', value: oidcIssuerUrl }
            { name: 'OAUTH2_PROXY_CLIENT_ID', value: oidcClientId }
            { name: 'OAUTH2_PROXY_CLIENT_SECRET', secretRef: 'oidc-client-secret' }
            { name: 'OAUTH2_PROXY_COOKIE_SECRET', secretRef: 'oauth-cookie-secret' }
            { name: 'OAUTH2_PROXY_HTTP_ADDRESS', value: '0.0.0.0:4180' }
            { name: 'OAUTH2_PROXY_UPSTREAMS', value: 'http://127.0.0.1:8081' }
            { name: 'OAUTH2_PROXY_REDIRECT_URL', value: '${appUrl}/oauth2/callback' }
            { name: 'OAUTH2_PROXY_EMAIL_DOMAINS', value: oauthAllowedEmailDomains }
            { name: 'OAUTH2_PROXY_COOKIE_SECURE', value: 'true' }
            { name: 'OAUTH2_PROXY_PASS_USER_HEADERS', value: 'true' }
            { name: 'OAUTH2_PROXY_SKIP_AUTH_STRIP_HEADERS', value: 'true' }
            { name: 'OAUTH2_PROXY_SKIP_AUTH_PREFLIGHT', value: 'true' }
            { name: 'OAUTH2_PROXY_SKIP_AUTH_ROUTES', value: '^/api/app_[0-9a-f]{16}(/|$)' }
            { name: 'OAUTH2_PROXY_SKIP_PROVIDER_BUTTON', value: 'true' }
            { name: 'OAUTH2_PROXY_CODE_CHALLENGE_METHOD', value: 'S256' }
          ]
        }
      ]
      scale: {
        minReplicas: stage == 'prod' ? 1 : 0
        maxReplicas: 1
      }
    }
  }
  dependsOn: [
    acrPull
  ]
}

output appUrl string = appUrl
output oauthCallbackUrl string = '${appUrl}/oauth2/callback'
output postgresHost string = postgres.properties.fullyQualifiedDomainName
output storageAccountName string = storage.name
