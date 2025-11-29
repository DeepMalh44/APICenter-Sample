using ApiDuplicateDetector.Models;

namespace ApiDuplicateDetector.Services;

/// <summary>
/// Service interface for interacting with Azure API Center.
/// </summary>
public interface IApiCenterService
{
    /// <summary>
    /// Gets all APIs registered in the API Center.
    /// </summary>
    Task<List<ApiInfo>> GetAllApisAsync();
    
    /// <summary>
    /// Gets a specific API by parsing the subject from the event.
    /// </summary>
    /// <param name="subject">The event subject containing the API resource path.</param>
    Task<ApiInfo?> GetApiFromSubjectAsync(string subject);
    
    /// <summary>
    /// Gets the API definition/specification content.
    /// </summary>
    /// <param name="apiName">The API name.</param>
    /// <param name="versionName">The version name.</param>
    /// <param name="definitionName">The definition name.</param>
    Task<string?> GetApiDefinitionContentAsync(string apiName, string versionName, string definitionName);
}
