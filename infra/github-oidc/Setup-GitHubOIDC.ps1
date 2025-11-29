<#
.SYNOPSIS
    Sets up GitHub Actions OIDC authentication for Azure deployments.

.DESCRIPTION
    This script creates an Azure AD application, service principal, and federated credentials
    for GitHub Actions to deploy to Azure using OIDC (no secrets needed).

.PARAMETER GitHubOrg
    The GitHub organization or username.

.PARAMETER GitHubRepo
    The GitHub repository name.

.PARAMETER ResourceGroupName
    The Azure resource group name for the deployment.

.PARAMETER SubscriptionId
    The Azure subscription ID.

.PARAMETER AppName
    The name of the Azure AD application (default: sp-github-api-duplicate-detector).

.EXAMPLE
    .\Setup-GitHubOIDC.ps1 -GitHubOrg "myorg" -GitHubRepo "myrepo" -ResourceGroupName "rg-api-duplicate-detector" -SubscriptionId "12345678-1234-1234-1234-123456789012"
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$GitHubOrg,

    [Parameter(Mandatory = $true)]
    [string]$GitHubRepo,

    [Parameter(Mandatory = $false)]
    [string]$ResourceGroupName = "rg-api-duplicate-detector",

    [Parameter(Mandatory = $false)]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $false)]
    [string]$AppName = "sp-github-api-duplicate-detector"
)

$ErrorActionPreference = "Stop"

Write-Host "🔐 Setting up GitHub Actions OIDC authentication..." -ForegroundColor Cyan

# Get current subscription if not provided
if (-not $SubscriptionId) {
    $SubscriptionId = az account show --query id -o tsv
    Write-Host "Using current subscription: $SubscriptionId" -ForegroundColor Yellow
}

# Get tenant ID
$TenantId = az account show --query tenantId -o tsv
Write-Host "Tenant ID: $TenantId" -ForegroundColor Gray

# Step 1: Check if app already exists
Write-Host "`n📋 Step 1: Checking for existing Azure AD application..." -ForegroundColor Cyan
$existingApp = az ad app list --display-name $AppName --query "[0]" -o json 2>$null | ConvertFrom-Json

if ($existingApp) {
    Write-Host "Found existing application: $($existingApp.displayName)" -ForegroundColor Yellow
    $AppId = $existingApp.appId
    $ObjectId = $existingApp.id
} else {
    # Create Azure AD Application
    Write-Host "`n📋 Step 2: Creating Azure AD application..." -ForegroundColor Cyan
    $app = az ad app create --display-name $AppName --query "{appId:appId, id:id}" -o json | ConvertFrom-Json
    $AppId = $app.appId
    $ObjectId = $app.id
    Write-Host "Created application: $AppName (App ID: $AppId)" -ForegroundColor Green
}

# Step 2: Create or get Service Principal
Write-Host "`n📋 Step 3: Creating/getting Service Principal..." -ForegroundColor Cyan
$existingSp = az ad sp list --filter "appId eq '$AppId'" --query "[0]" -o json 2>$null | ConvertFrom-Json

if (-not $existingSp) {
    $sp = az ad sp create --id $AppId --query "{id:id}" -o json | ConvertFrom-Json
    $SpId = $sp.id
    Write-Host "Created Service Principal" -ForegroundColor Green
} else {
    $SpId = $existingSp.id
    Write-Host "Using existing Service Principal" -ForegroundColor Yellow
}

# Step 3: Add Federated Credential for GitHub main branch
Write-Host "`n📋 Step 4: Adding federated credential for GitHub..." -ForegroundColor Cyan

$credentialName = "github-main-branch"
$subject = "repo:${GitHubOrg}/${GitHubRepo}:ref:refs/heads/main"

# Check if credential already exists
$existingCred = az ad app federated-credential list --id $ObjectId --query "[?name=='$credentialName']" -o json 2>$null | ConvertFrom-Json

if ($existingCred -and $existingCred.Count -gt 0) {
    Write-Host "Federated credential already exists, updating..." -ForegroundColor Yellow
    az ad app federated-credential delete --id $ObjectId --federated-credential-id $credentialName 2>$null
}

$credentialParams = @{
    name = $credentialName
    issuer = "https://token.actions.githubusercontent.com"
    subject = $subject
    audiences = @("api://AzureADTokenExchange")
} | ConvertTo-Json -Compress

$credentialParams | Out-File -FilePath "fed-cred-temp.json" -Encoding utf8
az ad app federated-credential create --id $ObjectId --parameters "@fed-cred-temp.json"
Remove-Item -Path "fed-cred-temp.json" -Force
Write-Host "Added federated credential for: $subject" -ForegroundColor Green

# Step 4: Add Federated Credential for pull requests (optional)
Write-Host "`n📋 Step 5: Adding federated credential for pull requests..." -ForegroundColor Cyan
$prCredentialName = "github-pull-request"
$prSubject = "repo:${GitHubOrg}/${GitHubRepo}:pull_request"

$existingPrCred = az ad app federated-credential list --id $ObjectId --query "[?name=='$prCredentialName']" -o json 2>$null | ConvertFrom-Json

if ($existingPrCred -and $existingPrCred.Count -gt 0) {
    Write-Host "PR credential already exists, skipping..." -ForegroundColor Yellow
} else {
    $prCredentialParams = @{
        name = $prCredentialName
        issuer = "https://token.actions.githubusercontent.com"
        subject = $prSubject
        audiences = @("api://AzureADTokenExchange")
    } | ConvertTo-Json -Compress

    $prCredentialParams | Out-File -FilePath "fed-cred-pr-temp.json" -Encoding utf8
    az ad app federated-credential create --id $ObjectId --parameters "@fed-cred-pr-temp.json"
    Remove-Item -Path "fed-cred-pr-temp.json" -Force
    Write-Host "Added federated credential for pull requests" -ForegroundColor Green
}

# Step 5: Assign Contributor role on Resource Group
Write-Host "`n📋 Step 6: Assigning Contributor role on resource group..." -ForegroundColor Cyan

# Create resource group if it doesn't exist
az group create --name $ResourceGroupName --location eastus --output none 2>$null

$scope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroupName"

# Check if role assignment exists
$existingRole = az role assignment list --assignee $SpId --scope $scope --role "Contributor" --query "[0]" -o json 2>$null | ConvertFrom-Json

if (-not $existingRole) {
    az role assignment create --assignee $SpId --role "Contributor" --scope $scope --output none
    Write-Host "Assigned Contributor role on: $ResourceGroupName" -ForegroundColor Green
} else {
    Write-Host "Contributor role already assigned" -ForegroundColor Yellow
}

# Step 6: Assign additional roles for API Center
Write-Host "`n📋 Step 7: Assigning API Center Data Reader role..." -ForegroundColor Cyan
$apiCenterRoleId = "c7244dfb-f447-457d-b2ba-3999044d1706"  # Azure API Center Data Reader
$existingApiCenterRole = az role assignment list --assignee $SpId --scope $scope --role $apiCenterRoleId --query "[0]" -o json 2>$null | ConvertFrom-Json

if (-not $existingApiCenterRole) {
    az role assignment create --assignee $SpId --role $apiCenterRoleId --scope $scope --output none 2>$null
    Write-Host "Assigned API Center Data Reader role" -ForegroundColor Green
} else {
    Write-Host "API Center Data Reader role already assigned" -ForegroundColor Yellow
}

# Output summary
Write-Host "`n" + "="*60 -ForegroundColor Cyan
Write-Host "✅ GitHub Actions OIDC Setup Complete!" -ForegroundColor Green
Write-Host "="*60 -ForegroundColor Cyan

Write-Host "`n📝 Add these secrets to your GitHub repository:" -ForegroundColor Yellow
Write-Host "   Settings -> Secrets and variables -> Actions -> New repository secret" -ForegroundColor Gray
Write-Host ""
Write-Host "   AZURE_CLIENT_ID:       $AppId" -ForegroundColor White
Write-Host "   AZURE_TENANT_ID:       $TenantId" -ForegroundColor White
Write-Host "   AZURE_SUBSCRIPTION_ID: $SubscriptionId" -ForegroundColor White
Write-Host "   API_CENTER_NAME:       <your-api-center-name>" -ForegroundColor White
Write-Host ""
Write-Host "🚀 After adding secrets, push to the 'main' branch to trigger deployment!" -ForegroundColor Cyan

# Output for easy copy-paste
Write-Host "`n📋 Copy-paste these values:" -ForegroundColor Yellow
Write-Host "AZURE_CLIENT_ID=$AppId"
Write-Host "AZURE_TENANT_ID=$TenantId"
Write-Host "AZURE_SUBSCRIPTION_ID=$SubscriptionId"
