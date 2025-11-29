@description('The location for all resources')
param location string = resourceGroup().location

@description('The name of the API Center instance')
param apiCenterName string

@description('The name of the Function App')
param functionAppName string = 'func-api-duplicate-detector-${uniqueString(resourceGroup().id)}'

@description('The name of the Storage Account')
param storageAccountName string = 'stfunc${uniqueString(resourceGroup().id)}'

@description('The name of the Application Insights instance')
param appInsightsName string = 'appi-api-duplicate-detector-${uniqueString(resourceGroup().id)}'

@description('The similarity threshold for duplicate detection (0.0 to 1.0)')
param similarityThreshold string = '0.7'

@description('The webhook URL for notifications (Teams/Slack)')
@secure()
param notificationWebhookUrl string = ''

@description('Enable semantic similarity analysis using Azure OpenAI')
param enableSemanticAnalysis bool = false

@description('The name of the Azure OpenAI resource (required if enableSemanticAnalysis is true)')
param openAiName string = 'openai-api-detector-${uniqueString(resourceGroup().id)}'

@description('The name of the Cosmos DB account (required if enableSemanticAnalysis is true)')
param cosmosDbName string = 'cosmos-api-detector-${uniqueString(resourceGroup().id)}'

@description('The location for Azure OpenAI (may differ from main location due to availability)')
param openAiLocation string = 'eastus'

// Storage Account for Function App - Using Managed Identity
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

// Application Insights for monitoring
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    Request_Source: 'rest'
  }
}

// App Service Plan (Premium for better cold start)
resource hostingPlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: '${functionAppName}-plan'
  location: location
  sku: {
    name: 'EP1'
    tier: 'ElasticPremium'
    family: 'EP'
  }
  kind: 'elastic'
  properties: {
    maximumElasticWorkerCount: 20
    reserved: true
  }
}

// Function App with Managed Identity for storage
resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: hostingPlan.id
    reserved: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|8.0'
      appSettings: [
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccount.name
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsights.properties.InstrumentationKey
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'API_CENTER_SUBSCRIPTION_ID'
          value: subscription().subscriptionId
        }
        {
          name: 'API_CENTER_RESOURCE_GROUP'
          value: resourceGroup().name
        }
        {
          name: 'API_CENTER_NAME'
          value: apiCenterName
        }
        {
          name: 'SIMILARITY_THRESHOLD'
          value: similarityThreshold
        }
        {
          name: 'NOTIFICATION_WEBHOOK_URL'
          value: notificationWebhookUrl
        }
        {
          name: 'ENABLE_SEMANTIC_ANALYSIS'
          value: string(enableSemanticAnalysis)
        }
      ]
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
    }
    httpsOnly: true
  }
}

// Storage Blob Data Owner role for Function App
resource storageBlobOwnerRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, 'Storage Blob Data Owner')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Storage Queue Data Contributor role for Function App
resource storageQueueRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, 'Storage Queue Data Contributor')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Storage Table Data Contributor role for Function App
resource storageTableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionApp.id, 'Storage Table Data Contributor')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Reference to existing API Center
resource apiCenter 'Microsoft.ApiCenter/services@2024-03-01' existing = {
  name: apiCenterName
}

// Role assignment for Function App to read from API Center
resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(apiCenter.id, functionApp.id, 'Azure API Center Data Reader')
  scope: apiCenter
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'c7244dfb-f447-457d-b2ba-3999044d1706')
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Event Grid System Topic for API Center
resource eventGridTopic 'Microsoft.EventGrid/systemTopics@2023-12-15-preview' = {
  name: '${apiCenterName}-events'
  location: location
  properties: {
    source: apiCenter.id
    topicType: 'Microsoft.ApiCenter.Services'
  }
}


// Email address for notifications
@description('Email address for duplicate API detection alerts')
param alertEmailAddress string = 'ketaanhshah@microsoft.com'

// Action Group for email notifications
resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-api-duplicate-alerts'
  location: 'global'
  properties: {
    groupShortName: 'APIDupAlert'
    enabled: true
    emailReceivers: [
      {
        name: 'API Admin Email'
        emailAddress: alertEmailAddress
        useCommonAlertSchema: true
      }
    ]
  }
}

// Scheduled Query Rule Alert for Duplicate API Detection
resource duplicateApiAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-duplicate-api-detected'
  location: location
  properties: {
    displayName: 'Duplicate API Detected Alert'
    description: 'Triggers when a potential duplicate API is detected in API Center'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT5M'
    scopes: [
      appInsights.id
    ]
    targetResourceTypes: [
      'microsoft.insights/components'
    ]
    windowSize: 'PT5M'
    criteria: {
      allOf: [
        {
          query: 'traces | where message contains "DuplicateApiDetected"'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: false
    actions: {
      actionGroups: [
        actionGroup.id
      ]
      customProperties: {
        AlertType: 'DuplicateAPIDetection'
        Source: 'API Center Duplicate Detector'
      }
    }
  }
}
// Outputs
output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
output functionAppPrincipalId string = functionApp.identity.principalId
output eventGridTopicName string = eventGridTopic.name
output appInsightsName string = appInsights.name
output storageAccountName string = storageAccount.name
