namespace ApiDuplicateDetector.Models;

/// <summary>
/// Represents a duplicate detection report to be sent as notification.
/// </summary>
public class DuplicateDetectionReport
{
    /// <summary>
    /// The timestamp when the detection was performed.
    /// </summary>
    public DateTime DetectionTime { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// The newly registered API that triggered the detection.
    /// </summary>
    public ApiInfo TriggeringApi { get; set; } = new();
    
    /// <summary>
    /// Event type that triggered the detection.
    /// </summary>
    public string EventType { get; set; } = string.Empty;
    
    /// <summary>
    /// List of potential duplicate APIs found.
    /// </summary>
    public List<ApiSimilarityResult> PotentialDuplicates { get; set; } = new();
    
    /// <summary>
    /// Whether any potential duplicates were found.
    /// </summary>
    public bool HasPotentialDuplicates => PotentialDuplicates.Any();
    
    /// <summary>
    /// Total number of APIs analyzed.
    /// </summary>
    public int TotalApisAnalyzed { get; set; }
    
    /// <summary>
    /// The similarity threshold used.
    /// </summary>
    public double SimilarityThreshold { get; set; }
    
    /// <summary>
    /// Summary message for the report.
    /// </summary>
    public string Summary => HasPotentialDuplicates
        ? $"⚠️ ALERT: Found {PotentialDuplicates.Count} potential duplicate API(s) for '{TriggeringApi.Name}'"
        : $"✅ No duplicates found for '{TriggeringApi.Name}' after analyzing {TotalApisAnalyzed} APIs";
}
