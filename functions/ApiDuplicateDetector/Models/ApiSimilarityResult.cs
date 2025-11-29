namespace ApiDuplicateDetector.Models;

/// <summary>
/// Represents the result of comparing two APIs for similarity.
/// </summary>
public class ApiSimilarityResult
{
    /// <summary>
    /// The newly added/updated API.
    /// </summary>
    public ApiInfo NewApi { get; set; } = new();
    
    /// <summary>
    /// The existing API being compared against.
    /// </summary>
    public ApiInfo ExistingApi { get; set; } = new();
    
    /// <summary>
    /// Overall similarity score (0.0 to 1.0).
    /// </summary>
    public double OverallScore { get; set; }
    
    /// <summary>
    /// Similarity score based on endpoint paths.
    /// </summary>
    public double PathSimilarityScore { get; set; }
    
    /// <summary>
    /// Similarity score based on schema/model names.
    /// </summary>
    public double SchemaSimilarityScore { get; set; }
    
    /// <summary>
    /// Similarity score based on operation descriptions.
    /// </summary>
    public double DescriptionSimilarityScore { get; set; }
    
    /// <summary>
    /// Similarity score based on API title/name.
    /// </summary>
    public double NameSimilarityScore { get; set; }
    
    /// <summary>
    /// Semantic similarity score from AI embeddings (0.0 to 1.0).
    /// This captures meaning and intent, not just text matching.
    /// </summary>
    public double SemanticSimilarityScore { get; set; }
    
    /// <summary>
    /// Whether semantic analysis was used in the comparison.
    /// </summary>
    public bool UsedSemanticAnalysis { get; set; }
    
    /// <summary>
    /// Whether the similarity exceeds the configured threshold.
    /// </summary>
    public bool IsPotentialDuplicate { get; set; }
    
    /// <summary>
    /// Specific matching endpoints between the two APIs.
    /// </summary>
    public List<EndpointMatch> MatchingEndpoints { get; set; } = new();
    
    /// <summary>
    /// Recommendations for the API team.
    /// </summary>
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// Represents a matching endpoint between two APIs.
/// </summary>
public class EndpointMatch
{
    /// <summary>
    /// Endpoint from the new API.
    /// </summary>
    public ApiEndpoint NewEndpoint { get; set; } = new();
    
    /// <summary>
    /// Endpoint from the existing API.
    /// </summary>
    public ApiEndpoint ExistingEndpoint { get; set; } = new();
    
    /// <summary>
    /// Similarity score for this specific endpoint pair.
    /// </summary>
    public double SimilarityScore { get; set; }
    
    /// <summary>
    /// Reason for the match.
    /// </summary>
    public string MatchReason { get; set; } = string.Empty;
}
