using System.Text.RegularExpressions;
using ApiDuplicateDetector.Models;
using Azure;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ApiCenter;
using Microsoft.Extensions.Logging;

namespace ApiDuplicateDetector.Services;

/// <summary>
/// Service for interacting with Azure API Center using Azure SDK.
/// </summary>
public class ApiCenterService : IApiCenterService
{
    private readonly DefaultAzureCredential _credential;
    private readonly IApiSimilarityService _similarityService;
    private readonly ILogger<ApiCenterService> _logger;
    private readonly string _subscriptionId;
    private readonly string _resourceGroup;
    private readonly string _apiCenterName;

    public ApiCenterService(
        DefaultAzureCredential credential,
        IApiSimilarityService similarityService,
        ILogger<ApiCenterService> logger)
    {
        _credential = credential;
        _similarityService = similarityService;
        _logger = logger;
        _subscriptionId = Environment.GetEnvironmentVariable("API_CENTER_SUBSCRIPTION_ID")
            ?? throw new InvalidOperationException("API_CENTER_SUBSCRIPTION_ID not configured");
        _resourceGroup = Environment.GetEnvironmentVariable("API_CENTER_RESOURCE_GROUP")
            ?? throw new InvalidOperationException("API_CENTER_RESOURCE_GROUP not configured");
        _apiCenterName = Environment.GetEnvironmentVariable("API_CENTER_NAME")
            ?? throw new InvalidOperationException("API_CENTER_NAME not configured");
    }

    /// <inheritdoc/>
    public async Task<List<ApiInfo>> GetAllApisAsync()
    {
        var apis = new List<ApiInfo>();

        try
        {
            var armClient = new ArmClient(_credential);
            var subscription = armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{_subscriptionId}"));

            var resourceGroup = await subscription.GetResourceGroupAsync(_resourceGroup);
            var apiCenterService = await resourceGroup.Value.GetApiCenterServiceAsync(_apiCenterName);
            
            // Get the default workspace
            var workspace = await apiCenterService.Value.GetApiCenterWorkspaceAsync("default");
            
            // List all APIs in the workspace
            await foreach (var api in workspace.Value.GetApiCenterApis().GetAllAsync())
            {
                var apiInfo = new ApiInfo
                {
                    Id = api.Data.Id?.ToString() ?? "",
                    Name = api.Data.Name ?? "",
                    Title = api.Data.Name,
                    Description = null,
                    Kind = "rest"
                };

                // Try to get the API specification for each version
                await foreach (var version in api.GetApiCenterApiVersions().GetAllAsync())
                {
                    apiInfo.Version = version.Data.Name;
                    
                    // Get definitions for this version
                    await foreach (var definition in version.GetApiCenterApiDefinitions().GetAllAsync())
                    {
                        try
                        {
                            // Export the specification content
                            var exportResult = await definition.ExportSpecificationAsync(WaitUntil.Completed);
                            
                            if (exportResult?.Value?.Value != null)
                            {
                                apiInfo.SpecificationContent = exportResult.Value.Value;
                                
                                // Parse the spec to extract endpoints and schemas
                                var parsedApi = _similarityService.ParseOpenApiSpec(
                                    exportResult.Value.Value, apiInfo.Name);
                                apiInfo.Endpoints = parsedApi.Endpoints;
                                apiInfo.Schemas = parsedApi.Schemas;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, 
                                "Could not export specification for {ApiName}/{Version}/{Definition}",
                                api.Data.Name, version.Data.Name, definition.Data.Name);
                        }
                        
                        break; // Only process first definition
                    }
                    
                    break; // Only process first version
                }

                apis.Add(apiInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving APIs from API Center");
            throw;
        }

        return apis;
    }

    /// <inheritdoc/>
    public async Task<ApiInfo?> GetApiFromSubjectAsync(string subject)
    {
        // Parse subject like:
        // /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.ApiCenter/services/{name}/workspaces/default/apis/{apiName}/versions/{version}/definitions/{def}
        var match = Regex.Match(subject, 
            @"/apis/(?<apiName>[^/]+)/versions/(?<version>[^/]+)/definitions/(?<definition>[^/]+)$");
        
        if (!match.Success)
        {
            _logger.LogWarning("Could not parse API details from subject: {Subject}", subject);
            return null;
        }

        var apiName = match.Groups["apiName"].Value;
        var versionName = match.Groups["version"].Value;
        var definitionName = match.Groups["definition"].Value;

        try
        {
            var armClient = new ArmClient(_credential);
            var subscription = armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{_subscriptionId}"));

            var resourceGroup = await subscription.GetResourceGroupAsync(_resourceGroup);
            var apiCenterService = await resourceGroup.Value.GetApiCenterServiceAsync(_apiCenterName);
            
            var workspace = await apiCenterService.Value.GetApiCenterWorkspaceAsync("default");
            var api = await workspace.Value.GetApiCenterApiAsync(apiName);
            
            var apiInfo = new ApiInfo
            {
                Id = api.Value.Data.Id?.ToString() ?? "",
                Name = api.Value.Data.Name ?? "",
                Title = api.Value.Data.Name,
                Description = null,
                Kind = "rest",
                Version = versionName
            };

            // Get the specific definition content
            var specContent = await GetApiDefinitionContentAsync(apiName, versionName, definitionName);
            if (!string.IsNullOrEmpty(specContent))
            {
                apiInfo.SpecificationContent = specContent;
                var parsedApi = _similarityService.ParseOpenApiSpec(specContent, apiName);
                apiInfo.Endpoints = parsedApi.Endpoints;
                apiInfo.Schemas = parsedApi.Schemas;
            }

            return apiInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting API from subject: {Subject}", subject);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<string?> GetApiDefinitionContentAsync(string apiName, string versionName, string definitionName)
    {
        try
        {
            var armClient = new ArmClient(_credential);
            var subscription = armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{_subscriptionId}"));

            var resourceGroup = await subscription.GetResourceGroupAsync(_resourceGroup);
            var apiCenterService = await resourceGroup.Value.GetApiCenterServiceAsync(_apiCenterName);
            
            var workspace = await apiCenterService.Value.GetApiCenterWorkspaceAsync("default");
            var api = await workspace.Value.GetApiCenterApiAsync(apiName);
            var version = await api.Value.GetApiCenterApiVersionAsync(versionName);
            var definition = await version.Value.GetApiCenterApiDefinitionAsync(definitionName);
            
            var exportResult = await definition.Value.ExportSpecificationAsync(WaitUntil.Completed);
            return exportResult?.Value?.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error getting API definition content for {ApiName}/{Version}/{Definition}",
                apiName, versionName, definitionName);
            return null;
        }
    }
}



