using ApiDuplicateDetector.Models;

namespace ApiDuplicateDetector.Services;

/// <summary>
/// Service interface for comparing APIs and detecting similarities/duplicates.
/// </summary>
public interface IApiSimilarityService
{
    /// <summary>
    /// Compares a new API against all existing APIs to find potential duplicates.
    /// Uses both structural and semantic analysis.
    /// </summary>
    /// <param name="newApi">The newly added/updated API.</param>
    /// <param name="existingApis">List of existing APIs to compare against.</param>
    /// <param name="threshold">Similarity threshold (0.0 to 1.0).</param>
    /// <returns>List of similarity results for APIs that exceed the threshold.</returns>
    List<ApiSimilarityResult> FindPotentialDuplicates(ApiInfo newApi, List<ApiInfo> existingApis, double threshold);
    
    /// <summary>
    /// Finds potential duplicates using semantic similarity (AI-powered).
    /// </summary>
    /// <param name="newApi">The newly added/updated API.</param>
    /// <param name="threshold">Similarity threshold (0.0 to 1.0).</param>
    /// <returns>List of similarity results for APIs that exceed the threshold.</returns>
    Task<List<ApiSimilarityResult>> FindPotentialDuplicatesSemanticAsync(ApiInfo newApi, double threshold);
    
    /// <summary>
    /// Calculates similarity score between two APIs.
    /// </summary>
    ApiSimilarityResult CalculateSimilarity(ApiInfo api1, ApiInfo api2);
    
    /// <summary>
    /// Calculates combined similarity including semantic score.
    /// </summary>
    /// <param name="api1">First API.</param>
    /// <param name="api2">Second API.</param>
    /// <param name="semanticScore">Pre-calculated semantic similarity score.</param>
    ApiSimilarityResult CalculateSimilarityWithSemantic(ApiInfo api1, ApiInfo api2, double semanticScore);
    
    /// <summary>
    /// Parses an OpenAPI specification to extract API information.
    /// </summary>
    ApiInfo ParseOpenApiSpec(string specContent, string apiName);
    
    /// <summary>
    /// Stores the API embedding for future semantic searches.
    /// </summary>
    Task StoreApiEmbeddingAsync(ApiInfo apiInfo);
}
