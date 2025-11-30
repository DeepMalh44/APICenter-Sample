using Azure.Messaging.EventGrid;
using ApiDuplicateDetector.Models;
using ApiDuplicateDetector.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ApiDuplicateDetector.Functions;

/// <summary>
/// Azure Function that handles API Center events to detect duplicate APIs.
/// Triggered when an API definition is added or updated in API Center.
/// Uses semantic similarity (Azure OpenAI) when configured, falls back to structural analysis.
/// </summary>
public class ApiDuplicateDetectorFunction
{
    private readonly IApiCenterService _apiCenterService;
    private readonly IApiSimilarityService _similarityService;
    private readonly INotificationService _notificationService;
    private readonly IVectorStoreService? _vectorStoreService;
    private readonly ILogger<ApiDuplicateDetectorFunction> _logger;
    private readonly double _similarityThreshold;
    private readonly bool _semanticEnabled;

    public ApiDuplicateDetectorFunction(
        IApiCenterService apiCenterService,
        IApiSimilarityService similarityService,
        INotificationService notificationService,
        ILogger<ApiDuplicateDetectorFunction> logger,
        IVectorStoreService? vectorStoreService = null)
    {
        _apiCenterService = apiCenterService;
        _similarityService = similarityService;
        _notificationService = notificationService;
        _vectorStoreService = vectorStoreService;
        _logger = logger;
        
        // Get threshold from configuration (default 70%)
        _similarityThreshold = double.TryParse(
            Environment.GetEnvironmentVariable("SIMILARITY_THRESHOLD"), out var threshold) 
            ? threshold : 0.7;
            
        // Check if semantic analysis is enabled
        _semanticEnabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT"));
    }

    /// <summary>
    /// Event Grid trigger function that processes API Center events.
    /// </summary>
    [Function("ApiDuplicateDetector")]
    public async Task Run(
        [EventGridTrigger] EventGridEvent eventGridEvent)
    {
        _logger.LogWarning("=== API Duplicate Detector Triggered ===");
        _logger.LogWarning("Event Type: {EventType}", eventGridEvent.EventType);
        _logger.LogWarning("Subject: {Subject}", eventGridEvent.Subject);
        _logger.LogWarning("Event Time: {EventTime}", eventGridEvent.EventTime);
        _logger.LogWarning("Event Data Raw: {Data}", eventGridEvent.Data?.ToString());
        _logger.LogWarning("Semantic Analysis: {Enabled}", _semanticEnabled ? "Enabled" : "Disabled");

        try
        {
            // Only process API definition added/updated events
            if (eventGridEvent.EventType != "Microsoft.ApiCenter.ApiDefinitionAdded" &&
                eventGridEvent.EventType != "Microsoft.ApiCenter.ApiDefinitionUpdated")
            {
                _logger.LogWarning("Ignoring event type: {EventType}", eventGridEvent.EventType);
                return;
            }

            // Parse the event data
            var eventData = eventGridEvent.Data?.ToObjectFromJson<ApiCenterEventData>();
            if (eventData == null)
            {
                _logger.LogWarning("Could not parse event data");
                return;
            }

            _logger.LogInformation("API Definition: {Title} ({Spec} {Version})",
                eventData.Title,
                eventData.Specification?.Name,
                eventData.Specification?.Version);

            // Get the newly added/updated API details
            var newApi = await _apiCenterService.GetApiFromSubjectAsync(eventGridEvent.Subject);
            if (newApi == null)
            {
                _logger.LogWarning("Could not retrieve API details from subject");
                return;
            }

            _logger.LogWarning("Processing API: {Name} with {EndpointCount} endpoints and {SchemaCount} schemas",
                newApi.Name, newApi.Endpoints.Count, newApi.Schemas.Count);

            // Get all existing APIs from API Center
            _logger.LogWarning("Retrieving all APIs from API Center for comparison...");
            var allApis = await _apiCenterService.GetAllApisAsync();
            _logger.LogWarning("Retrieved {Count} APIs from API Center for comparison", allApis.Count);

            // Find potential duplicates (use semantic analysis if available)
            List<ApiSimilarityResult> duplicates;
            
            if (_semanticEnabled && _vectorStoreService != null)
            {
                _logger.LogWarning("Using semantic + structural similarity analysis");
                duplicates = await _similarityService.FindPotentialDuplicatesSemanticAsync(
                    newApi, _similarityThreshold);
                    
                // Store the new API's embedding for future comparisons
                await _similarityService.StoreApiEmbeddingAsync(newApi);
                _logger.LogWarning("Stored API embedding in vector database");
            }
            else
            {
                _logger.LogWarning("Using structural similarity analysis only");
                duplicates = _similarityService.FindPotentialDuplicates(
                    newApi, allApis, _similarityThreshold);
            }

            // Create the detection report
            var report = new DuplicateDetectionReport
            {
                TriggeringApi = newApi,
                EventType = eventGridEvent.EventType,
                PotentialDuplicates = duplicates,
                TotalApisAnalyzed = allApis.Count - 1, // Exclude the new API itself
                SimilarityThreshold = _similarityThreshold
            };

            // Send notification
            await _notificationService.SendNotificationAsync(report);

            if (report.HasPotentialDuplicates)
            {
                _logger.LogWarning("⚠️ Found {Count} potential duplicate(s) for API '{Name}'",
                    duplicates.Count, newApi.Name);
                
                // Emit custom event for Azure Monitor Alert
                // This structured log will be picked up by Application Insights and can trigger alerts
                _logger.LogWarning(
                    "DuplicateApiDetected: API '{ApiName}' has {DuplicateCount} potential duplicates. " +
                    "Highest similarity: {HighestSimilarity:P0}. APIs: {DuplicateNames}",
                    newApi.Name,
                    duplicates.Count,
                    duplicates.Max(d => d.OverallScore),
                    string.Join(", ", duplicates.Select(d => d.ExistingApi.Name)));
            }
            else
            {
                _logger.LogWarning("✅ No duplicates found for API '{Name}'", newApi.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing API duplicate detection");
            throw;
        }
    }
}
