namespace ApiDuplicateDetector.Models;

/// <summary>
/// Represents an API registered in API Center with its key characteristics for comparison.
/// </summary>
public class ApiInfo
{
    /// <summary>
    /// The unique identifier of the API.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// The name of the API.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// The title of the API.
    /// </summary>
    public string? Title { get; set; }
    
    /// <summary>
    /// The description of the API.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// The API kind (rest, graphql, grpc, etc.).
    /// </summary>
    public string? Kind { get; set; }
    
    /// <summary>
    /// The OpenAPI specification content (if available).
    /// </summary>
    public string? SpecificationContent { get; set; }
    
    /// <summary>
    /// List of API endpoints/paths.
    /// </summary>
    public List<ApiEndpoint> Endpoints { get; set; } = new();
    
    /// <summary>
    /// List of schemas/models defined in the API.
    /// </summary>
    public List<string> Schemas { get; set; } = new();
    
    /// <summary>
    /// The version of the API.
    /// </summary>
    public string? Version { get; set; }
}

/// <summary>
/// Represents a single API endpoint.
/// </summary>
public class ApiEndpoint
{
    /// <summary>
    /// The HTTP method (GET, POST, PUT, DELETE, etc.).
    /// </summary>
    public string Method { get; set; } = string.Empty;
    
    /// <summary>
    /// The path of the endpoint.
    /// </summary>
    public string Path { get; set; } = string.Empty;
    
    /// <summary>
    /// The operation ID.
    /// </summary>
    public string? OperationId { get; set; }
    
    /// <summary>
    /// The operation summary.
    /// </summary>
    public string? Summary { get; set; }
    
    /// <summary>
    /// The operation description.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Request body schema name.
    /// </summary>
    public string? RequestSchema { get; set; }
    
    /// <summary>
    /// Response schema name.
    /// </summary>
    public string? ResponseSchema { get; set; }
}
