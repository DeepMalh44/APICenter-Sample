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
            _logger.LogWarning("=== GetAllApisAsync START ===");
            var armClient = new ArmClient(_credential);
            var subscription = armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{_subscriptionId}"));

            var resourceGroup = await subscription.GetResourceGroupAsync(_resourceGroup);
            var apiCenterService = await resourceGroup.Value.GetApiCenterServiceAsync(_apiCenterName);
            
            // Get the default workspace
            var workspace = await apiCenterService.Value.GetApiCenterWorkspaceAsync("default");
            
            _logger.LogWarning("Listing all APIs in workspace...");
            int apiCount = 0;
            // List all APIs in the workspace
            await foreach (var api in workspace.Value.GetApiCenterApis().GetAllAsync())
            {
                apiCount++;
                _logger.LogWarning("Processing API #{Count}: {ApiName}", apiCount, api.Data.Name);
                
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
                    _logger.LogWarning("  Version: {Version}", version.Data.Name);
                    
                    // Get definitions for this version
                    await foreach (var definition in version.GetApiCenterApiDefinitions().GetAllAsync())
                    {
                        _logger.LogWarning("  Definition: {Definition}", definition.Data.Name);
                        try
                        {
                            // Export the specification content
                            _logger.LogWarning("  Exporting spec for {Api}/{Ver}/{Def}...", 
                                api.Data.Name, version.Data.Name, definition.Data.Name);
                            var exportResult = await definition.ExportSpecificationAsync(WaitUntil.Completed);
                            
                            if (exportResult?.Value?.Value != null)
                            {
                                apiInfo.SpecificationContent = exportResult.Value.Value;
                                _logger.LogWarning("  Got spec, length: {Len}", exportResult.Value.Value.Length);
                                
                                // Parse the spec to extract endpoints and schemas
                                var parsedApi = _similarityService.ParseOpenApiSpec(
                                    exportResult.Value.Value, apiInfo.Name);
                                apiInfo.Endpoints = parsedApi.Endpoints;
                                apiInfo.Schemas = parsedApi.Schemas;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("  EXPORT FAILED for {ApiName}/{Version}/{Definition}: {Error}",
                                api.Data.Name, version.Data.Name, definition.Data.Name, ex.Message);
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
        _logger.LogWarning("Parsing subject: {Subject}", subject);
        
        // Parse subject - can be various formats:
        // Format 1: /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.ApiCenter/services/{name}/workspaces/default/apis/{apiName}/versions/{version}/definitions/{def}
        // Format 2: /workspaces/default/apis/{apiName}/versions/{version}/definitions/{def}
        // Format 3: apis/{apiName}/versions/{version}/definitions/{def}
        
        var match = Regex.Match(subject, 
            @"apis/(?<apiName>[^/]+)/versions/(?<version>[^/]+)/definitions/(?<definition>[^/]+)");
        
        if (!match.Success)
        {
            // Try alternate format - just API name without versions
            match = Regex.Match(subject, @"apis/(?<apiName>[^/]+)");
            if (!match.Success)
            {
                _logger.LogWarning("Could not parse API details from subject: {Subject}", subject);
                return null;
            }
            
            // If we only have API name, we need to find the latest version/definition
            var apiName = match.Groups["apiName"].Value;
            _logger.LogWarning("Found API name only: {ApiName}, will find latest version", apiName);
            return await GetApiWithLatestVersionAsync(apiName);
        }

        var parsedApiName = match.Groups["apiName"].Value;
        var versionName = match.Groups["version"].Value;
        var definitionName = match.Groups["definition"].Value;
        
        _logger.LogWarning("Parsed successfully: API={ApiName}, Version={Version}, Definition={Definition}",
            parsedApiName, versionName, definitionName);

        try
        {
            _logger.LogWarning("Creating ARM client with subscription: {Sub}, RG: {RG}, APIC: {APIC}",
                _subscriptionId, _resourceGroup, _apiCenterName);
                
            var armClient = new ArmClient(_credential);
            var subscription = armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{_subscriptionId}"));

            _logger.LogWarning("Getting resource group...");
            var resourceGroup = await subscription.GetResourceGroupAsync(_resourceGroup);
            
            _logger.LogWarning("Getting API Center service...");
            var apiCenterService = await resourceGroup.Value.GetApiCenterServiceAsync(_apiCenterName);
            
            _logger.LogWarning("Getting workspace...");
            var workspace = await apiCenterService.Value.GetApiCenterWorkspaceAsync("default");
            
            _logger.LogWarning("Getting API: {ApiName}", parsedApiName);
            var api = await workspace.Value.GetApiCenterApiAsync(parsedApiName);
            
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
            _logger.LogWarning("Getting API definition content for {ApiName}/{Version}/{Definition}", 
                parsedApiName, versionName, definitionName);
            var specContent = await GetApiDefinitionContentAsync(parsedApiName, versionName, definitionName);
            _logger.LogWarning("Got spec content, length: {Length}", specContent?.Length ?? 0);
            if (!string.IsNullOrEmpty(specContent))
            {
                apiInfo.SpecificationContent = specContent;
                var parsedApi = _similarityService.ParseOpenApiSpec(specContent, parsedApiName);
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

    /// <summary>
    /// Gets an API with its latest version and definition when only the API name is known.
    /// </summary>
    private async Task<ApiInfo?> GetApiWithLatestVersionAsync(string apiName)
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
            
            var apiInfo = new ApiInfo
            {
                Id = api.Value.Data.Id?.ToString() ?? "",
                Name = api.Value.Data.Name ?? "",
                Title = api.Value.Data.Name,
                Description = null,
                Kind = "rest"
            };

            // Find the latest version and its definition
            await foreach (var version in api.Value.GetApiCenterApiVersions().GetAllAsync())
            {
                apiInfo.Version = version.Data.Name;
                _logger.LogInformation("Found version: {Version}", version.Data.Name);
                
                await foreach (var definition in version.GetApiCenterApiDefinitions().GetAllAsync())
                {
                    _logger.LogInformation("Found definition: {Definition}", definition.Data.Name);
                    
                    try
                    {
                        var exportResult = await definition.ExportSpecificationAsync(WaitUntil.Completed);
                        if (exportResult?.Value?.Value != null)
                        {
                            apiInfo.SpecificationContent = exportResult.Value.Value;
                            var parsedApi = _similarityService.ParseOpenApiSpec(
                                exportResult.Value.Value, apiName);
                            apiInfo.Endpoints = parsedApi.Endpoints;
                            apiInfo.Schemas = parsedApi.Schemas;
                            _logger.LogInformation("Loaded spec with {EndpointCount} endpoints", 
                                apiInfo.Endpoints.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not export specification");
                    }
                    break; // Take first definition
                }
                break; // Take first version
            }

            return apiInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting API with latest version: {ApiName}", apiName);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<string?> GetApiDefinitionContentAsync(string apiName, string versionName, string definitionName)
    {
        _logger.LogWarning("=== GetApiDefinitionContentAsync START: {ApiName}/{Version}/{Definition} ===",
            apiName, versionName, definitionName);
        try
        {
            var armClient = new ArmClient(_credential);
            var subscription = armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{_subscriptionId}"));

            var resourceGroup = await subscription.GetResourceGroupAsync(_resourceGroup);
            var apiCenterService = await resourceGroup.Value.GetApiCenterServiceAsync(_apiCenterName);
            
            var workspace = await apiCenterService.Value.GetApiCenterWorkspaceAsync("default");
            _logger.LogWarning("Getting API resource: {ApiName}", apiName);
            var api = await workspace.Value.GetApiCenterApiAsync(apiName);
            _logger.LogWarning("Getting version resource: {Version}", versionName);
            var version = await api.Value.GetApiCenterApiVersionAsync(versionName);
            _logger.LogWarning("Getting definition resource: {Definition}", definitionName);
            var definition = await version.Value.GetApiCenterApiDefinitionAsync(definitionName);
            
            _logger.LogWarning("Calling ExportSpecificationAsync...");
            var exportResult = await definition.Value.ExportSpecificationAsync(WaitUntil.Completed);
            _logger.LogWarning("Export completed, result length: {Length}", exportResult?.Value?.Value?.Length ?? 0);
            return exportResult?.Value?.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("ERROR in GetApiDefinitionContentAsync: {Error}", ex.Message);
            _logger.LogError(ex, 
                "Error getting API definition content for {ApiName}/{Version}/{Definition}",
                apiName, versionName, definitionName);
            return null;
        }
    }
}



