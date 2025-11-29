namespace ApiDuplicateDetector.Models;

/// <summary>
/// Represents the event data from API Center when an API definition is added/updated.
/// </summary>
public class ApiCenterEventData
{
    /// <summary>
    /// The title of the API definition.
    /// </summary>
    public string? Title { get; set; }
    
    /// <summary>
    /// The description of the API definition.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// The specification details.
    /// </summary>
    public SpecificationInfo? Specification { get; set; }
}

/// <summary>
/// Represents the API specification information.
/// </summary>
public class SpecificationInfo
{
    /// <summary>
    /// The specification name (e.g., "openapi", "graphql").
    /// </summary>
    public string? Name { get; set; }
    
    /// <summary>
    /// The specification version (e.g., "3.0.1").
    /// </summary>
    public string? Version { get; set; }
}
