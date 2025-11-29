using ApiDuplicateDetector.Models;

namespace ApiDuplicateDetector.Services;

/// <summary>
/// Service interface for sending notifications about duplicate API detections.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Sends a notification about the duplicate detection report.
    /// </summary>
    /// <param name="report">The duplicate detection report.</param>
    Task SendNotificationAsync(DuplicateDetectionReport report);
}
