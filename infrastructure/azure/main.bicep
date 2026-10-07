targetScope = 'resourceGroup'

@description('Base name used for generated Azure resource names.')
param appName string = 'booking'

@allowed([
  'test'
  'prod'
])
@description('Deployment environment. Only test and prod are supported by this template.')
param env string

@description('Expected Azure subscription ID. Resources are created only in this subscription and the expected environment resource group.')
param expectedSubscriptionId string

@description('Exact resource group expected for this deployment. Its name must also end with the environment suffix.')
param expectedResourceGroupName string = 'rg-${appName}-${env}'

@description('Optional service principal object ID for shared local development access to the test vault. Never assigned access to the production vault.')
param sharedDevelopmentPrincipalId string = ''

@description('Azure region for regional resources other than the web app, which uses the existing App Service plan location.')
param location string = resourceGroup().location

@description('SQL admin login for SQL authentication.')
param sqlAdminLogin string

@secure()
@description('SQL admin password for SQL authentication.')
param sqlAdminPassword string

@description('SQL database name.')
param sqlDatabaseName string = appName

@description('Additional resource tags to apply.')
param tags object = {}

@minValue(30)
@maxValue(730)
@description('Log Analytics and Application Insights retention in days.')
param appInsightsRetentionInDays int = 30

@description('Linux App Service runtime stack.')
param linuxFxVersion string = 'DOTNETCORE|10.0'

@description('Existing App Service plan name.')
param webPlanName string = env == 'prod' ? 'Nibbler' : 'DefaultPlan2'

@description('Resource group containing the existing App Service plan.')
param webPlanResourceGroup string = env == 'prod' ? 'service-plans-linux' : 'Default-Web-WestUS'

@description('SQL database SKU name.')
param sqlSkuName string = env == 'prod' ? 'S0' : 'Basic'

@description('SQL database SKU tier.')
param sqlSkuTier string = env == 'prod' ? 'Standard' : 'Basic'

@description('Whether to allow Azure services and resources to access SQL server.')
param sqlAllowAzureServices bool = env == 'test'

@allowed([
  'Enabled'
  'Disabled'
])
@description('Public network access for SQL server.')
param sqlPublicNetworkAccess string = 'Enabled'

var appNameSafe = toLower(replace(replace(appName, ' ', ''), '_', ''))
var nameToken = substring(uniqueString(resourceGroup().id, appName, env), 0, 6)
var normalizedExpectedSubscriptionId = toLower(expectedSubscriptionId)
var normalizedCurrentSubscriptionId = toLower(subscription().subscriptionId)
var expectedResourceGroupSuffix = '-${env}'
var deploymentGuardPassed = !empty(expectedSubscriptionId) && normalizedCurrentSubscriptionId == normalizedExpectedSubscriptionId && toLower(resourceGroup().name) == toLower(expectedResourceGroupName) && endsWith(toLower(resourceGroup().name), expectedResourceGroupSuffix)

var sqlServerName = toLower('sql-${appNameSafe}-${env}-${nameToken}')
var webAppName = toLower('web-${appNameSafe}-${env}-${nameToken}')
var appInsightsName = toLower('appi-${appNameSafe}-${env}-${nameToken}')
var logAnalyticsWorkspaceName = toLower('log-${appNameSafe}-${env}-${nameToken}')
var keyVaultName = 'kv-${env}-${uniqueString(resourceGroup().id, appName, env)}'

var resourceTags = union(tags, {
  application: appName
  environment: env
})

resource webPlan 'Microsoft.Web/serverfarms@2023-12-01' existing = {
  name: webPlanName
  scope: resourceGroup(webPlanResourceGroup)
}

resource keyVault 'Microsoft.KeyVault/vaults@2026-02-01' = if (deploymentGuardPassed) {
  name: keyVaultName
  location: location
  tags: resourceTags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    accessPolicies: []
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
  }
}

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = if (deploymentGuardPassed) {
  name: logAnalyticsWorkspaceName
  location: location
  tags: resourceTags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: appInsightsRetentionInDays
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = if (deploymentGuardPassed) {
  name: appInsightsName
  location: location
  kind: 'web'
  tags: resourceTags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace!.id
  }
}

module sql 'modules/sql.bicep' = if (deploymentGuardPassed) {
  name: 'sql-${env}'
  params: {
    name: sqlServerName
    location: location
    tags: resourceTags
    adminLogin: sqlAdminLogin
    adminPassword: sqlAdminPassword
    databaseName: sqlDatabaseName
    skuName: sqlSkuName
    skuTier: sqlSkuTier
    allowAzureServices: sqlAllowAzureServices
    publicNetworkAccess: sqlPublicNetworkAccess
  }
}

var sqlServerHostnameSuffix = environment().suffixes.sqlServerHostname
var sqlServerFqdn = '${sqlServerName}${startsWith(sqlServerHostnameSuffix, '.') ? '' : '.'}${sqlServerHostnameSuffix}'
var sqlConnectionString = 'Server=tcp:${sqlServerFqdn},1433;Initial Catalog=${sqlDatabaseName};Persist Security Info=False;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

module compute 'modules/compute.bicep' = if (deploymentGuardPassed) {
  name: 'compute-${env}'
  dependsOn: [
    sql
  ]
  params: {
    webAppLocation: webPlan.location
    tags: resourceTags
    webPlanId: webPlan.id
    webAppName: webAppName
    linuxFxVersion: linuxFxVersion
    sqlConnectionString: sqlConnectionString
    environmentName: env
    appInsightsConnectionString: appInsights!.properties.ConnectionString
    appInsightsInstrumentationKey: appInsights!.properties.InstrumentationKey
    keyVaultUrl: keyVault!.properties.vaultUri
  }
}

module appVaultAccess 'modules/key-vault-role-assignment.bicep' = if (deploymentGuardPassed) {
  name: 'app-vault-access-${env}'
  params: {
    keyVaultName: keyVaultName
    principalId: compute!.outputs.principalId
  }
}

module sharedDevelopmentVaultAccess 'modules/key-vault-role-assignment.bicep' = if (deploymentGuardPassed && env == 'test' && !empty(sharedDevelopmentPrincipalId)) {
  name: 'shared-development-vault-access-${env}'
  dependsOn: [
    keyVault
  ]
  params: {
    keyVaultName: keyVaultName
    principalId: sharedDevelopmentPrincipalId
  }
}

output appInsightsConnectionString string = deploymentGuardPassed ? appInsights!.properties.ConnectionString : ''
output appInsightsName string = deploymentGuardPassed ? appInsights!.name : ''
output appServiceDefaultHostName string = deploymentGuardPassed ? compute!.outputs.defaultHostName : ''
output appServicePrincipalId string = deploymentGuardPassed ? compute!.outputs.principalId : ''
output deploymentGuardPassed bool = deploymentGuardPassed
output keyVaultName string = deploymentGuardPassed ? keyVault!.name : ''
output keyVaultUrl string = deploymentGuardPassed ? keyVault!.properties.vaultUri : ''
output logAnalyticsWorkspaceName string = deploymentGuardPassed ? logAnalyticsWorkspace!.name : ''
output sqlDatabaseName string = sqlDatabaseName
output sqlServerName string = deploymentGuardPassed ? sql!.outputs.serverName : ''
output webAppName string = deploymentGuardPassed ? compute!.outputs.webAppName : ''
