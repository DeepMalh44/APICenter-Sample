using System.Text.RegularExpressions;
using ApiDuplicateDetector.Models;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace ApiDuplicateDetector.Services;

/// <summary>
/// Service for comparing APIs and detecting potential duplicates using multiple similarity metrics.
/// Supports both structural analysis and AI-powered semantic similarity.
/// </summary>
public class ApiSimilarityService : IApiSimilarityService
{
    private readonly ILogger<ApiSimilarityService> _logger;
    private readonly IEmbeddingService? _embeddingService;
    private readonly IVectorStoreService? _vectorStoreService;
    private readonly bool _semanticEnabled;

    public ApiSimilarityService(
        ILogger<ApiSimilarityService> logger,
        IEmbeddingService? embeddingService = null,
        IVectorStoreService? vectorStoreService = null)
    {
        _logger = logger;
        _embeddingService = embeddingService;
        _vectorStoreService = vectorStoreService;
        _semanticEnabled = embeddingService != null && vectorStoreService != null;
        
        if (_semanticEnabled)
        {
            _logger.LogInformation("Semantic similarity analysis is ENABLED");
        }
        else
        {
            _logger.LogInformation("Semantic similarity analysis is DISABLED (structural only)");
        }
    }

    /// <inheritdoc/>
    public List<ApiSimilarityResult> FindPotentialDuplicates(ApiInfo newApi, List<ApiInfo> existingApis, double threshold)
    {
        var results = new List<ApiSimilarityResult>();
        _logger.LogWarning("=== Comparing {Api} against {Count} APIs (threshold: {T}) ===", newApi.Name, existingApis.Count, threshold);
        _logger.LogWarning("New API endpoints: {Count}", newApi.Endpoints.Count);
        _logger.LogWarning("=== Comparing {Api} against {Count} APIs (threshold: {T}) ===", newApi.Name, existingApis.Count, threshold);
        _logger.LogWarning("New API endpoints: {Count}", newApi.Endpoints.Count);

        foreach (var existingApi in existingApis)
        {
            // Skip self-comparison
            if (existingApi.Id == newApi.Id || existingApi.Name == newApi.Name)
                continue;

            var similarity = CalculateSimilarity(newApi, existingApi);
            similarity.IsPotentialDuplicate = similarity.OverallScore >= threshold;

            if (similarity.IsPotentialDuplicate)
            {
                // Add recommendations based on similarity types
                AddRecommendations(similarity);
                results.Add(similarity);
            }
        }

        return results.OrderByDescending(r => r.OverallScore).ToList();
    }

    /// <inheritdoc/>
    public async Task<List<ApiSimilarityResult>> FindPotentialDuplicatesSemanticAsync(ApiInfo newApi, double threshold)
    {
        if (!_semanticEnabled)
        {
            _logger.LogWarning("Semantic search not available, returning empty results");
            return new List<ApiSimilarityResult>();
        }

        var results = new List<ApiSimilarityResult>();

        try
        {
            // Generate embedding for the new API
            var embeddingText = _embeddingService!.CreateEmbeddingText(newApi);
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(embeddingText);

            if (queryEmbedding.Length == 0)
            {
                _logger.LogWarning("Could not generate embedding for API: {ApiName}", newApi.Name);
                return results;
            }

            // Search for similar APIs using vector search
            var semanticMatches = await _vectorStoreService!.FindSimilarApisAsync(
                queryEmbedding, topK: 20, excludeApiName: newApi.Name);

            foreach (var match in semanticMatches)
            {
                // Only include results above threshold
                if (match.SimilarityScore < threshold)
                    continue;

                // Convert ApiEmbedding back to ApiInfo for comparison
                var existingApi = new ApiInfo
                {
                    Id = match.ApiEmbedding.ApiCenterResourceId ?? match.ApiEmbedding.Id,
                    Name = match.ApiEmbedding.ApiName,
                    Title = match.ApiEmbedding.Title,
                    Description = match.ApiEmbedding.Description,
                    Kind = match.ApiEmbedding.Kind,
                    Version = match.ApiEmbedding.Version,
                    Endpoints = match.ApiEmbedding.Endpoints
                        .Select(e => new ApiEndpoint { Path = e, Method = "GET" }).ToList(),
                    Schemas = match.ApiEmbedding.Schemas
                };

                // Calculate full similarity with semantic score
                var similarity = CalculateSimilarityWithSemantic(newApi, existingApi, match.SimilarityScore);
                similarity.IsPotentialDuplicate = true;
                
                AddRecommendations(similarity);
                results.Add(similarity);
            }

            _logger.LogInformation("Semantic search found {Count} potential duplicates above threshold {Threshold}",
                results.Count, threshold);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in semantic duplicate detection");
        }

        return results.OrderByDescending(r => r.OverallScore).ToList();
    }

    /// <inheritdoc/>
    public ApiSimilarityResult CalculateSimilarity(ApiInfo api1, ApiInfo api2)
    {
        var result = new ApiSimilarityResult
        {
            NewApi = api1,
            ExistingApi = api2
        };

        // Calculate different similarity scores
        result.NameSimilarityScore = CalculateStringSimilarity(
            api1.Title ?? api1.Name, 
            api2.Title ?? api2.Name);
        
        result.DescriptionSimilarityScore = CalculateStringSimilarity(
            api1.Description ?? "", 
            api2.Description ?? "");
        
        result.PathSimilarityScore = CalculatePathSimilarity(api1.Endpoints, api2.Endpoints, result);
        result.SchemaSimilarityScore = CalculateSchemaSimilarity(api1.Schemas, api2.Schemas);

        // Calculate weighted overall score
        // Weights: Paths are most important (40%), then schemas (25%), name (20%), description (15%)
        result.OverallScore = 
            (result.PathSimilarityScore * 0.40) +
            (result.SchemaSimilarityScore * 0.25) +
            (result.NameSimilarityScore * 0.20) +
            (result.DescriptionSimilarityScore * 0.15);

        return result;
    }

    /// <inheritdoc/>
    public ApiSimilarityResult CalculateSimilarityWithSemantic(ApiInfo api1, ApiInfo api2, double semanticScore)
    {
        var result = CalculateSimilarity(api1, api2);
        
        // Add semantic score
        result.SemanticSimilarityScore = semanticScore;
        result.UsedSemanticAnalysis = true;
        
        // Recalculate overall score with semantic weight
        // New weights: Semantic (35%), Paths (25%), Schemas (20%), Name (12%), Description (8%)
        result.OverallScore = 
            (result.SemanticSimilarityScore * 0.35) +
            (result.PathSimilarityScore * 0.25) +
            (result.SchemaSimilarityScore * 0.20) +
            (result.NameSimilarityScore * 0.12) +
            (result.DescriptionSimilarityScore * 0.08);

        return result;
    }

    /// <inheritdoc/>
    public async Task StoreApiEmbeddingAsync(ApiInfo apiInfo)
    {
        if (!_semanticEnabled)
        {
            _logger.LogDebug("Semantic storage not available, skipping embedding storage");
            return;
        }

        try
        {
            // Generate embedding text
            var embeddingText = _embeddingService!.CreateEmbeddingText(apiInfo);
            
            // Generate embedding vector
            var embedding = await _embeddingService.GenerateEmbeddingAsync(embeddingText);
            
            if (embedding.Length == 0)
            {
                _logger.LogWarning("Could not generate embedding for API: {ApiName}", apiInfo.Name);
                return;
            }

            // Create and store the embedding
            var apiEmbedding = new ApiEmbedding
            {
                Id = apiInfo.Name, // Use name as ID for upsert
                ApiName = apiInfo.Name,
                Title = apiInfo.Title,
                Description = apiInfo.Description,
                Kind = apiInfo.Kind,
                Version = apiInfo.Version,
                EmbeddingText = embeddingText,
                Embedding = embedding,
                Endpoints = apiInfo.Endpoints.Select(e => $"{e.Method} {e.Path}").ToList(),
                Schemas = apiInfo.Schemas,
                ApiCenterResourceId = apiInfo.Id
            };

            await _vectorStoreService!.UpsertApiEmbeddingAsync(apiEmbedding);
            
            _logger.LogInformation("Stored embedding for API: {ApiName} ({Dimensions} dimensions)",
                apiInfo.Name, embedding.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing API embedding: {ApiName}", apiInfo.Name);
        }
    }

    /// <inheritdoc/>
    public ApiInfo ParseOpenApiSpec(string specContent, string apiName)
    {
        var apiInfo = new ApiInfo { Name = apiName };

        try
        {
            var reader = new OpenApiStringReader();
            var openApiDoc = reader.Read(specContent, out var diagnostic);

            if (openApiDoc == null)
            {
                _logger.LogWarning("Could not parse OpenAPI spec for {ApiName}: {Errors}", 
                    apiName, string.Join(", ", diagnostic.Errors.Select(e => e.Message)));
                return apiInfo;
            }

            apiInfo.Title = openApiDoc.Info?.Title;
            apiInfo.Description = openApiDoc.Info?.Description;
            apiInfo.Version = openApiDoc.Info?.Version;

            // Extract endpoints
            if (openApiDoc.Paths != null)
            {
                foreach (var path in openApiDoc.Paths)
                {
                    foreach (var operation in path.Value.Operations)
                    {
                        var endpoint = new ApiEndpoint
                        {
                            Path = path.Key,
                            Method = operation.Key.ToString().ToUpperInvariant(),
                            OperationId = operation.Value.OperationId,
                            Summary = operation.Value.Summary,
                            Description = operation.Value.Description
                        };

                        // Extract request schema
                        if (operation.Value.RequestBody?.Content != null)
                        {
                            var content = operation.Value.RequestBody.Content
                                .FirstOrDefault(c => c.Key.Contains("json"));
                            endpoint.RequestSchema = content.Value?.Schema?.Reference?.Id;
                        }

                        // Extract response schema
                        if (operation.Value.Responses != null)
                        {
                            foreach (var resp in operation.Value.Responses.Where(r => r.Key.StartsWith("2")))
                            {
                                if (resp.Value?.Content != null)
                                {
                                    var jsonContent = resp.Value.Content.FirstOrDefault(c => c.Key.Contains("json"));
                                    endpoint.ResponseSchema = jsonContent.Value?.Schema?.Reference?.Id;
                                    break;
                                }
                            }
                        }

                        apiInfo.Endpoints.Add(endpoint);
                    }
                }
            }

            // Extract schema names
            if (openApiDoc.Components?.Schemas != null)
            {
                apiInfo.Schemas = openApiDoc.Components.Schemas.Keys.ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing OpenAPI spec for {ApiName}", apiName);
        }

        return apiInfo;
    }

    /// <summary>
    /// Calculates Levenshtein distance-based similarity between two strings.
    /// </summary>
    private double CalculateStringSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2))
            return 0;
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
            return 0;

        s1 = NormalizeString(s1);
        s2 = NormalizeString(s2);

        // Use Jaccard similarity for word overlap
        var words1 = s1.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var words2 = s2.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (words1.Count == 0 && words2.Count == 0)
            return 0;

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union > 0 ? (double)intersection / union : 0;
    }

    /// <summary>
    /// Calculates similarity between endpoint paths.
    /// </summary>
    private double CalculatePathSimilarity(List<ApiEndpoint> endpoints1, List<ApiEndpoint> endpoints2, ApiSimilarityResult result)
    {
        if (!endpoints1.Any() || !endpoints2.Any())
            return 0;

        var matches = 0;
        var totalComparisons = Math.Max(endpoints1.Count, endpoints2.Count);

        foreach (var ep1 in endpoints1)
        {
            foreach (var ep2 in endpoints2)
            {
                var pathScore = CompareEndpointPaths(ep1.Path, ep2.Path);
                var methodMatch = ep1.Method == ep2.Method;

                // High similarity if same method and similar path
                if (methodMatch && pathScore > 0.7)
                {
                    matches++;
                    result.MatchingEndpoints.Add(new EndpointMatch
                    {
                        NewEndpoint = ep1,
                        ExistingEndpoint = ep2,
                        SimilarityScore = pathScore,
                        MatchReason = $"{ep1.Method} {ep1.Path} ≈ {ep2.Method} {ep2.Path}"
                    });
                    break; // Only count each endpoint once
                }
            }
        }

        return totalComparisons > 0 ? (double)matches / totalComparisons : 0;
    }

    /// <summary>
    /// Compares two endpoint paths, accounting for path parameters.
    /// </summary>
    private double CompareEndpointPaths(string path1, string path2)
    {
        // Normalize path parameters (e.g., {id}, :id, [id])
        var normalizedPath1 = NormalizePath(path1);
        var normalizedPath2 = NormalizePath(path2);

        if (normalizedPath1 == normalizedPath2)
            return 1.0;

        // Compare path segments
        var segments1 = normalizedPath1.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var segments2 = normalizedPath2.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments1.Length != segments2.Length)
            return 0.3; // Different depth, but might still be similar

        var matchingSegments = 0;
        for (int i = 0; i < segments1.Length; i++)
        {
            if (segments1[i] == segments2[i] || 
                segments1[i] == "{param}" || 
                segments2[i] == "{param}")
            {
                matchingSegments++;
            }
        }

        return (double)matchingSegments / segments1.Length;
    }

    /// <summary>
    /// Normalizes a path by replacing path parameters with a standard placeholder.
    /// </summary>
    private string NormalizePath(string path)
    {
        // Replace various path parameter formats with standard {param}
        var normalized = Regex.Replace(path, @"\{[^}]+\}", "{param}");
        normalized = Regex.Replace(normalized, @":[a-zA-Z_]+", "{param}");
        normalized = Regex.Replace(normalized, @"\[[a-zA-Z_]+\]", "{param}");
        return normalized.ToLowerInvariant().TrimEnd('/');
    }

    /// <summary>
    /// Calculates similarity between schema/model names.
    /// </summary>
    private double CalculateSchemaSimilarity(List<string> schemas1, List<string> schemas2)
    {
        if (!schemas1.Any() || !schemas2.Any())
            return 0;

        var normalizedSchemas1 = schemas1.Select(NormalizeSchemaName).ToHashSet();
        var normalizedSchemas2 = schemas2.Select(NormalizeSchemaName).ToHashSet();

        var intersection = normalizedSchemas1.Intersect(normalizedSchemas2).Count();
        var union = normalizedSchemas1.Union(normalizedSchemas2).Count();

        return union > 0 ? (double)intersection / union : 0;
    }

    /// <summary>
    /// Normalizes a schema name for comparison.
    /// </summary>
    private string NormalizeSchemaName(string name)
    {
        // Remove common suffixes like DTO, Model, Entity, etc.
        var normalized = Regex.Replace(name, @"(DTO|Dto|Model|Entity|Request|Response|VM|ViewModel)$", "");
        return normalized.ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes a string for comparison.
    /// </summary>
    private string NormalizeString(string s)
    {
        return Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9\s]", " ");
    }

    /// <summary>
    /// Adds recommendations based on similarity analysis.
    /// </summary>
    private void AddRecommendations(ApiSimilarityResult result)
    {
        if (result.OverallScore >= 0.9)
        {
            result.Recommendations.Add("🔴 HIGH CONFIDENCE: This API appears to be a duplicate. Consider using the existing API instead.");
        }
        else if (result.OverallScore >= 0.7)
        {
            result.Recommendations.Add("🟡 MEDIUM CONFIDENCE: This API has significant overlap with an existing API. Review for potential consolidation.");
        }

        // Semantic-specific recommendations
        if (result.UsedSemanticAnalysis && result.SemanticSimilarityScore > 0.85)
        {
            result.Recommendations.Add("🧠 AI ANALYSIS: The APIs appear to serve the same purpose based on semantic understanding. Consider consolidation.");
        }
        else if (result.UsedSemanticAnalysis && result.SemanticSimilarityScore > 0.7)
        {
            result.Recommendations.Add("🧠 AI ANALYSIS: These APIs have similar functionality. Review for potential overlap.");
        }

        if (result.PathSimilarityScore > 0.8)
        {
            result.Recommendations.Add($"📍 {result.MatchingEndpoints.Count} endpoint(s) have very similar paths. Consider reusing the existing endpoints.");
        }

        if (result.SchemaSimilarityScore > 0.7)
        {
            result.Recommendations.Add("📦 Similar data schemas detected. Consider using shared models to avoid duplication.");
        }

        if (result.NameSimilarityScore > 0.8)
        {
            result.Recommendations.Add("📛 API names are very similar. Ensure this is intentional and not a naming conflict.");
        }

        result.Recommendations.Add($"💡 Contact the owner of '{result.ExistingApi.Name}' to discuss potential collaboration.");
    }
}






