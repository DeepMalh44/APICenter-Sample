using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApiDuplicateDetector.Models;
using Microsoft.Extensions.Logging;

namespace ApiDuplicateDetector.Services;

/// <summary>
/// Service for sending notifications about duplicate API detections.
/// Supports Teams, Slack, and generic webhooks.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string? _webhookUrl;
    private readonly bool _sendEmailNotifications;
    private readonly string? _notificationEmail;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _webhookUrl = Environment.GetEnvironmentVariable("NOTIFICATION_WEBHOOK_URL");
        _sendEmailNotifications = bool.TryParse(
            Environment.GetEnvironmentVariable("SEND_EMAIL_NOTIFICATIONS"), out var send) && send;
        _notificationEmail = Environment.GetEnvironmentVariable("NOTIFICATION_EMAIL");
    }

    /// <inheritdoc/>
    public async Task SendNotificationAsync(DuplicateDetectionReport report)
    {
        _logger.LogInformation("Sending notification for API: {ApiName}, Duplicates found: {Count}",
            report.TriggeringApi.Name, report.PotentialDuplicates.Count);

        var tasks = new List<Task>();

        // Send webhook notification (Teams/Slack compatible)
        if (!string.IsNullOrEmpty(_webhookUrl))
        {
            tasks.Add(SendWebhookNotificationAsync(report));
        }

        // Log the report details
        LogReport(report);

        await Task.WhenAll(tasks);
    }

    private async Task SendWebhookNotificationAsync(DuplicateDetectionReport report)
    {
        try
        {
            // Create an Adaptive Card for Teams (also works with Slack incoming webhooks)
            var card = CreateTeamsAdaptiveCard(report);
            
            var content = new StringContent(
                JsonSerializer.Serialize(card),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(_webhookUrl, content);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Webhook notification failed: {StatusCode} - {Reason}",
                    response.StatusCode, response.ReasonPhrase);
            }
            else
            {
                _logger.LogInformation("Webhook notification sent successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending webhook notification");
        }
    }

    private object CreateTeamsAdaptiveCard(DuplicateDetectionReport report)
    {
        var facts = new List<object>
        {
            new { title = "API Name", value = report.TriggeringApi.Name },
            new { title = "API Title", value = report.TriggeringApi.Title ?? "N/A" },
            new { title = "Event Type", value = report.EventType },
            new { title = "Detection Time", value = report.DetectionTime.ToString("u") },
            new { title = "APIs Analyzed", value = report.TotalApisAnalyzed.ToString() },
            new { title = "Similarity Threshold", value = $"{report.SimilarityThreshold:P0}" }
        };

        var duplicateDetails = new List<object>();
        foreach (var duplicate in report.PotentialDuplicates.Take(5)) // Limit to top 5
        {
            duplicateDetails.Add(new
            {
                type = "Container",
                items = new object[]
                {
                    new
                    {
                        type = "TextBlock",
                        text = $"**{duplicate.ExistingApi.Name}** - {duplicate.OverallScore:P0} match",
                        wrap = true,
                        color = duplicate.OverallScore >= 0.9 ? "attention" : "warning"
                    },
                    new
                    {
                        type = "FactSet",
                        facts = new object[]
                        {
                            new { title = "Path Match", value = $"{duplicate.PathSimilarityScore:P0}" },
                            new { title = "Schema Match", value = $"{duplicate.SchemaSimilarityScore:P0}" },
                            new { title = "Matching Endpoints", value = duplicate.MatchingEndpoints.Count.ToString() }
                        }
                    },
                    new
                    {
                        type = "TextBlock",
                        text = string.Join("\n", duplicate.Recommendations.Take(2)),
                        wrap = true,
                        size = "small"
                    }
                }
            });
        }

        // Teams Adaptive Card format
        return new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new
                    {
                        type = "AdaptiveCard",
                        version = "1.4",
                        body = new object[]
                        {
                            new
                            {
                                type = "TextBlock",
                                text = report.HasPotentialDuplicates 
                                    ? "⚠️ API Duplicate Detection Alert" 
                                    : "✅ API Duplicate Check Complete",
                                weight = "bolder",
                                size = "large",
                                color = report.HasPotentialDuplicates ? "attention" : "good"
                            },
                            new
                            {
                                type = "TextBlock",
                                text = report.Summary,
                                wrap = true
                            },
                            new
                            {
                                type = "FactSet",
                                facts = facts
                            },
                            new
                            {
                                type = "Container",
                                items = report.HasPotentialDuplicates ? new object[]
                                {
                                    new
                                    {
                                        type = "TextBlock",
                                        text = "**Potential Duplicates Found:**",
                                        weight = "bolder"
                                    }
                                } : Array.Empty<object>()
                            }
                        }.Concat(duplicateDetails).ToArray()
                    }
                }
            }
        };
    }

    private void LogReport(DuplicateDetectionReport report)
    {
        _logger.LogInformation("=== API Duplicate Detection Report ===");
        _logger.LogInformation("API: {Name} ({Title})", report.TriggeringApi.Name, report.TriggeringApi.Title);
        _logger.LogInformation("Event: {EventType}", report.EventType);
        _logger.LogInformation("Time: {Time}", report.DetectionTime);
        _logger.LogInformation("APIs Analyzed: {Count}", report.TotalApisAnalyzed);
        _logger.LogInformation("Threshold: {Threshold:P0}", report.SimilarityThreshold);
        _logger.LogInformation("Duplicates Found: {Count}", report.PotentialDuplicates.Count);

        foreach (var duplicate in report.PotentialDuplicates)
        {
            _logger.LogWarning("  ⚠️ Potential duplicate: {Name} (Score: {Score:P0})",
                duplicate.ExistingApi.Name, duplicate.OverallScore);
            _logger.LogWarning("    - Path Similarity: {Score:P0}", duplicate.PathSimilarityScore);
            _logger.LogWarning("    - Schema Similarity: {Score:P0}", duplicate.SchemaSimilarityScore);
            _logger.LogWarning("    - Matching Endpoints: {Count}", duplicate.MatchingEndpoints.Count);
            
            foreach (var recommendation in duplicate.Recommendations)
            {
                _logger.LogWarning("    {Recommendation}", recommendation);
            }
        }

        _logger.LogInformation("=====================================");
    }
}
