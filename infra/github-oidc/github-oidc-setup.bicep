@description('The location for all resources')
param location string = resourceGroup().location

@description('The name of the Azure AD application for GitHub Actions')
param appName string = 'sp-github-api-duplicate-detector'

@description('The GitHub organization/user name')
param githubOrg string

@description('The GitHub repository name')
param githubRepo string

@description('The GitHub branch for production deployments')
param githubBranch string = 'main'

// Create an Azure AD Application (requires Microsoft Graph permissions)
// Note: This needs to be done via Azure CLI as Bicep doesn't support AAD directly

// Output instructions for manual setup
output setupInstructions string = '''
To set up GitHub Actions OIDC authentication, run the following commands:

1. Create an Azure AD Application:
   az ad app create --display-name "${appName}"

2. Create a Service Principal:
   APP_ID=$(az ad app list --display-name "${appName}" --query "[0].appId" -o tsv)
   az ad sp create --id $APP_ID

3. Get the Object ID:
   OBJECT_ID=$(az ad app list --display-name "${appName}" --query "[0].id" -o tsv)

4. Add Federated Credential for GitHub:
   az ad app federated-credential create --id $OBJECT_ID --parameters '{
     "name": "github-main-branch",
     "issuer": "https://token.actions.githubusercontent.com",
     "subject": "repo:${githubOrg}/${githubRepo}:ref:refs/heads/${githubBranch}",
     "audiences": ["api://AzureADTokenExchange"]
   }'

5. Assign Contributor role on Resource Group:
   SP_ID=$(az ad sp list --display-name "${appName}" --query "[0].id" -o tsv)
   az role assignment create --assignee $SP_ID --role "Contributor" --scope "/subscriptions/{subscription-id}/resourceGroups/rg-api-duplicate-detector"

6. Add GitHub Secrets:
   - AZURE_CLIENT_ID: The Application (client) ID
   - AZURE_TENANT_ID: Your Azure AD tenant ID
   - AZURE_SUBSCRIPTION_ID: Your Azure subscription ID
   - API_CENTER_NAME: The name of your API Center instance
'''

output appName string = appName
output githubOrg string = githubOrg
output githubRepo string = githubRepo
