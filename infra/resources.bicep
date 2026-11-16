targetScope = 'resourceGroup'

param location string
param staticSiteLocation string
param namePrefix string
param sqlAdminObjectId string
param sqlAdminLogin string
@secure()
param ambientApiKey string
@secure()
param ambientApplicationKey string
param ambientDeviceMacAddress string
param operatorIpAddress string = ''

var suffix = uniqueString(subscription().subscriptionId, resourceGroup().name)
var stem = '${toLower(namePrefix)}-${suffix}'
var tags = {
  application: 'weather'
  costProfile: 'free-only'
}

// These SKUs and SQL exhaustion behavior are deliberately not parameters.
// A failed/unsupported free deployment must never fall back to a paid tier.
resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${namePrefix}-free-plan'
  location: location
  kind: 'linux'
  tags: tags
  sku: {
    name: 'F1'
    tier: 'Free'
  }
  properties: {
    reserved: true
  }
}

resource ui 'Microsoft.Web/staticSites@2024-04-01' = {
  name: '${stem}-ui'
  location: staticSiteLocation
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    // No linked backend: that requires a paid SWA plan. The browser calls the
    // API's HTTPS origin directly, using the app's existing CORS support.
    stagingEnvironmentPolicy: 'Disabled'
  }
}

resource sql 'Microsoft.Sql/servers@2023-08-01' = {
  name: '${stem}-sql-${location}'
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      principalType: 'User'
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
    }
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sql
  name: 'weather-db'
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    createMode: 'Default'
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: 'Local'
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    zoneRedundant: false
  }
}

resource backupRetention 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2023-08-01' = {
  parent: database
  name: 'default'
  properties: {
    retentionDays: 7
    diffBackupIntervalInHours: 24
  }
}

// F1 has no VNet integration. This rule allows Azure-hosted clients through
// the SQL firewall; Entra authentication and per-database grants still apply.
resource azureFirewall 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sql
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource operatorFirewall 'Microsoft.Sql/servers/firewallRules@2023-08-01' = if (!empty(operatorIpAddress)) {
  parent: sql
  name: 'OperatorIp'
  properties: {
    startIpAddress: operatorIpAddress
    endIpAddress: operatorIpAddress
  }
}

resource api 'Microsoft.Web/sites@2024-04-01' = {
  name: '${stem}-api'
  location: location
  kind: 'app,linux'
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      appCommandLine: 'dotnet Weather.WebApi.dll'
      alwaysOn: false
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      http20Enabled: true
    }
  }
}

resource mcp 'Microsoft.Web/sites@2024-04-01' = {
  name: '${stem}-mcp'
  location: location
  kind: 'app,linux'
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      appCommandLine: 'dotnet Weather.Mcp.dll'
      alwaysOn: false
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      http20Enabled: true
    }
  }
}

// Pooling=False releases idle database sessions rather than preventing SQL's
// serverless auto-pause. Runtime identities receive no schema/write permissions.
var connectionString = 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=${database.name};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;Pooling=False'
var commonSettings = {
  ASPNETCORE_ENVIRONMENT: 'Production'
  ASPNETCORE_URLS: 'http://0.0.0.0:8080'
  Https__RedirectEnabled: 'false'
  Ambient__ApiKey: ambientApiKey
  Ambient__ApplicationKey: ambientApplicationKey
  Ambient__DeviceMacAddress: ambientDeviceMacAddress
  Ambient__ApiUrl: 'https://api.ambientweather.net/v1/'
  Ambient__SocketUrl: 'https://rt2.ambientweather.net'
  ConnectionStrings__sql: connectionString
  SCM_DO_BUILD_DURING_DEPLOYMENT: 'false'
}

resource apiSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: api
  name: 'appsettings'
  properties: union(commonSettings, {
    AllowedHosts: api.properties.defaultHostName
    Cors__AllowedOrigins__0: 'https://${ui.properties.defaultHostname}'
  })
}

resource mcpSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: mcp
  name: 'appsettings'
  properties: union(commonSettings, {
    AllowedHosts: mcp.properties.defaultHostName
  })
}

resource apiScmPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: api
  name: 'scm'
  properties: {
    allow: false
  }
}
resource apiFtpPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: api
  name: 'ftp'
  properties: {
    allow: false
  }
}
resource mcpScmPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: mcp
  name: 'scm'
  properties: {
    allow: false
  }
}
resource mcpFtpPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: mcp
  name: 'ftp'
  properties: {
    allow: false
  }
}

output resources object = {
  plan: plan.name
  ui: ui.name
  api: api.name
  mcp: mcp.name
  sqlServer: sql.name
  database: database.name
}
output urls object = {
  ui: 'https://${ui.properties.defaultHostname}'
  api: 'https://${api.properties.defaultHostName}'
  mcp: 'https://${mcp.properties.defaultHostName}/mcp'
}
output database object = {
  server: sql.properties.fullyQualifiedDomainName
  name: database.name
  adminConnectionString: 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=${database.name};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;Pooling=False'
}
output identities object = {
  apiPrincipalId: api.identity.principalId
  mcpPrincipalId: mcp.identity.principalId
}
